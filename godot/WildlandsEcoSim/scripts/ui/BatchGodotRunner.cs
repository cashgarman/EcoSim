using System.Text.Json;
using EcoSim.Core;
using EcoSim.Core.Data;
using EcoSim.Core.Batch;
using EcoSim.Core.Sim;

namespace WildlandsEcoSim.UI;

public sealed class BatchFormParams
{
    public uint Seed { get; set; } = 42;
    public string Size { get; set; } = "m";
    public int Days { get; set; } = 200;
    public int SampleEvery { get; set; } = 10;
    public double Animals { get; set; } = 0.45;
    public bool AutoMigration { get; set; }
    public string SimBackend { get; set; } = "cpu";
    public int Runs { get; set; } = 1;
    public bool Fuzz { get; set; }
    public int FuzzTrials { get; set; } = 50;
    public uint FuzzSeed { get; set; } = 12345;
    public double FuzzIntensity { get; set; } = 0.15;
    public string FuzzScope { get; set; } = "all";
    public string FuzzProfile { get; set; } = "fast";
}

public sealed class BatchUiProgress
{
    public string Mode { get; set; } = "single";
    public int RunIndex { get; set; }
    public int RunTotal { get; set; } = 1;
    public int TrialIndex { get; set; }
    public int TrialTotal { get; set; } = 1;
    public int Day { get; set; }
    public int TargetDays { get; set; }
    public int TotalAlive { get; set; }
    public int GenerationMax { get; set; }
    public double WallMs { get; set; }
    public string SimBackend { get; set; } = "cpu";
}

public sealed class BatchGodotRunner
{
    private readonly string _dataRoot;
    private BatchHarness? _activeHarness;
    private volatile bool _abort;

    public BatchGodotRunner(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    public bool IsRunning { get; private set; }

    public void Abort()
    {
        _abort = true;
        _activeHarness?.Abort();
    }

    public async Task<IReadOnlyList<BatchReport>> RunAsync(
        BatchFormParams form,
        Action<BatchUiProgress> onProgress,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Array.Empty<BatchReport>();
        }

        IsRunning = true;
        _abort = false;

        try
        {
            var resolved = ApplyFuzzProfile(form);
            if (resolved.Fuzz)
            {
                return await Task.Run(() => RunFuzzCampaign(resolved, onProgress, cancellationToken), cancellationToken);
            }

            if (resolved.Runs > 1)
            {
                return await Task.Run(() => RunSequential(resolved, onProgress, cancellationToken), cancellationToken);
            }

            var report = await Task.Run(() => RunSingle(resolved, resolved.Seed, onProgress, 0, 1, 0, 1, cancellationToken),
                cancellationToken);
            return report != null ? [report] : [];
        }
        finally
        {
            IsRunning = false;
            _activeHarness = null;
        }
    }

    private List<BatchReport> RunSequential(BatchFormParams form, Action<BatchUiProgress> onProgress, CancellationToken ct)
    {
        var reports = new List<BatchReport>();
        for (int i = 0; i < form.Runs; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (_abort) break;

            uint seed = form.Seed + (uint)i;
            var report = RunSingle(form, seed, onProgress, i, form.Runs, 0, 1, ct);
            if (report != null)
            {
                reports.Add(report);
                SaveReport(report);
            }
        }

        return reports;
    }

    private List<BatchReport> RunFuzzCampaign(BatchFormParams form, Action<BatchUiProgress> onProgress, CancellationToken ct)
    {
        var trials = new List<BatchReport>();
        for (int i = 0; i < form.FuzzTrials; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (_abort) break;

            uint seed = form.Seed + (uint)i;
            var report = RunSingle(form, seed, onProgress, 0, 1, i, form.FuzzTrials, ct);
            if (report != null)
            {
                trials.Add(report);
                SaveReport(report);
            }
        }

        trials.Sort((a, b) => b.Score.CompareTo(a.Score));
        return trials;
    }

    private BatchReport? RunSingle(
        BatchFormParams form,
        uint seed,
        Action<BatchUiProgress> onProgress,
        int runIndex,
        int runTotal,
        int trialIndex,
        int trialTotal,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_abort) return null;

        var session = SimSession.Create(_dataRoot, seed);
        var harness = new BatchHarness(session);
        _activeHarness = harness;

        bool sparse = form.Fuzz && (form.FuzzProfile is "fast" or "fast-gpu");
        int? earlyStop = sparse ? 20 : null;

        harness.Init(new BatchRunConfig
        {
            Seed = seed,
            Size = form.Size,
            Animals = form.Animals,
            TargetDays = form.Days,
            SampleEveryDays = form.SampleEvery,
            AutoMigration = form.AutoMigration,
        });

        harness.GenerateWorld();

        string mode = form.Fuzz ? "fuzz" : runTotal > 1 ? "sequential" : "single";
        var result = harness.Run(new BatchRunOptions
        {
            TargetDays = form.Days,
            SampleEveryDays = form.SampleEvery,
            SparseMode = sparse,
            EarlyStopDay = earlyStop,
            MaxWallMs = 120_000,
            OnProgress = p => onProgress(new BatchUiProgress
            {
                Mode = mode,
                RunIndex = runIndex,
                RunTotal = runTotal,
                TrialIndex = trialIndex,
                TrialTotal = trialTotal,
                Day = p.Day,
                TargetDays = p.TargetDays,
                TotalAlive = p.TotalAlive,
                GenerationMax = p.GenerationMax,
                WallMs = p.WallMs,
                SimBackend = form.SimBackend,
            }),
        });

        ct.ThrowIfCancellationRequested();
        if (_abort) return null;

        string runId = form.Fuzz
            ? $"fuzz-{trialIndex}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            : $"batch-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(100000, 999999)}";
        var report = harness.GetReport(runId, result);
        harness.Teardown();
        return report;
    }

    private static BatchFormParams ApplyFuzzProfile(BatchFormParams form)
    {
        if (!form.Fuzz)
        {
            return form;
        }

        var p = new BatchFormParams
        {
            Seed = form.Seed,
            Size = form.Size == "m" ? "s" : form.Size,
            Days = form.Days == 200 ? 80 : form.Days,
            SampleEvery = form.SampleEvery == 10 ? 20 : form.SampleEvery,
            Animals = form.Animals,
            AutoMigration = form.AutoMigration,
            SimBackend = form.FuzzProfile.EndsWith("-gpu") ? "gpu" : "cpu",
            Runs = form.Runs,
            Fuzz = true,
            FuzzTrials = form.FuzzTrials,
            FuzzSeed = form.FuzzSeed,
            FuzzIntensity = form.FuzzIntensity,
            FuzzScope = form.FuzzScope,
            FuzzProfile = form.FuzzProfile,
        };

        return p;
    }

    private static void SaveReport(BatchReport report)
    {
        try
        {
            string dir = Path.Combine(DataPaths.RepoRoot, "reports");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{report.RunId}.json");
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort persistence for the batch UI.
        }
    }
}
