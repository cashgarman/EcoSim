using System.Diagnostics;
using System.Text.Json;
using EcoSim.Core.Data;
using EcoSim.Core.Rng;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Batch;

public sealed class BatchHarness
{
    private readonly SimSession _session;
    private BatchMetricsCollector? _metrics;
    private BatchRunConfig? _runConfig;
    private bool _abort;

    public BatchHarness(SimSession session)
    {
        _session = session;
    }

    public void Abort() => _abort = true;

    public void Init(BatchRunConfig config)
    {
        _runConfig = config;
        _abort = false;
        _session.State.BatchMode = true;
        _session.State.BatchConfig = new BatchConfig
        {
            AutoMigration = config.AutoMigration,
            SimBackend = "cpu",
        };
        _session.State.Cfg = new WorldGenConfig
        {
            Sea = config.Sea,
            Temp = config.Temp,
            Moist = config.Moist,
            Relief = config.Relief,
            Animals = config.Animals,
            Size = config.Size,
        };
        _session.State.Seed = config.Seed ?? (uint)(Random.Shared.NextDouble() * 1e9);
        GlobalRng.SetSeed(_session.State.Seed);
    }

    public int GenerateWorld()
    {
        return _session.GenerateWorld();
    }

    public BatchRunResult Run(BatchRunOptions? options = null)
    {
        options ??= new BatchRunOptions();
        int targetDays = options.TargetDays ?? _runConfig?.TargetDays ?? 200;
        int sampleEvery = options.SampleEveryDays ?? _runConfig?.SampleEveryDays ?? 10;
        double tickDt = options.TickDt ?? 0.5;
        long maxWallTicks = (long)((options.MaxWallMs ?? 120_000) / 1000.0 * Stopwatch.Frequency);

        int initialPop = _session.Creatures.AliveCount();
        _metrics = new BatchMetricsCollector(_session.State, _session.Species, sampleEvery, targetDays, options.SparseMode);
        _metrics.Begin(initialPop);

        long wallStart = Stopwatch.GetTimestamp();
        while (_session.State.Day < targetDays && !_abort)
        {
            _session.State.TGlobal += tickDt;
            _session.Simulation.Tick(tickDt);
            _session.State.Day = (int)Math.Floor(_session.State.TGlobal / SimConstants.SimDaySeconds);

            if (options.SparseMode)
            {
                if (_metrics.ShouldSampleSparse(_session.State.Day, targetDays))
                {
                    _metrics.CaptureSample(true);
                }
            }
            else
            {
                _metrics.CaptureSample(false);
            }

            var (_, totalAlive) = _metrics.AliveCounts();
            if (options.EarlyStopDay != null && _session.State.Day >= options.EarlyStopDay && totalAlive == 0)
            {
                break;
            }

            if (Stopwatch.GetTimestamp() - wallStart > maxWallTicks) break;
        }

        _metrics.CaptureSample(true);
        bool earlyStop = options.EarlyStopDay != null && _session.State.Day < targetDays && _metrics.AliveCounts().TotalAlive == 0;
        bool timedOut = Stopwatch.GetTimestamp() - wallStart > maxWallTicks && _session.State.Day < targetDays;
        string outcome = _metrics.ClassifyOutcome(earlyStop);
        if (timedOut && outcome != "total_extinction") outcome = "timeout";

        return new BatchRunResult { Outcome = outcome, EarlyStop = earlyStop, TimedOut = timedOut };
    }

    public BatchReport GetReport(string runId, BatchRunResult result)
    {
        var cfg = new Dictionary<string, object?>
        {
            ["seed"] = _session.State.Seed,
            ["size"] = _session.State.Cfg.Size,
            ["cfg"] = _session.State.Cfg,
            ["targetDays"] = _runConfig?.TargetDays,
            ["sampleEveryDays"] = _runConfig?.SampleEveryDays,
            ["simBackend"] = "cpu",
            ["autoMigration"] = _runConfig?.AutoMigration ?? false,
            ["outcome"] = result.Outcome,
            ["earlyStop"] = result.EarlyStop,
        };
        return _metrics!.BuildReport(cfg, null, runId, result.Outcome, result.EarlyStop);
    }

    public void Teardown()
    {
        _session.State.BatchMode = false;
        _session.State.BatchConfig = null;
        _session.State.Ready = false;
    }
}

public sealed class BatchRunConfig
{
    public uint? Seed { get; set; }
    public string Size { get; set; } = "s";
    public double Sea { get; set; } = 0.46;
    public double Temp { get; set; } = 0.5;
    public double Moist { get; set; } = 0.5;
    public double Relief { get; set; } = 0.6;
    public double Animals { get; set; } = 0.45;
    public int TargetDays { get; set; } = 100;
    public int SampleEveryDays { get; set; } = 10;
    public bool AutoMigration { get; set; }
}

public sealed class BatchRunOptions
{
    public int? TargetDays { get; set; }
    public int? SampleEveryDays { get; set; }
    public double? TickDt { get; set; }
    public double? MaxWallMs { get; set; }
    public int? EarlyStopDay { get; set; }
    public bool SparseMode { get; set; }
}

public sealed class BatchRunResult
{
    public string Outcome { get; set; } = "";
    public bool EarlyStop { get; set; }
    public bool TimedOut { get; set; }
}

public static class BatchMetricsCollectorExtensions
{
    public static bool ShouldSampleSparse(this BatchMetricsCollector m, int day, int targetDays)
    {
        if (!m.SparseMode) return false;
        int[] checkpoints = [0, targetDays / 2, targetDays];
        return checkpoints.Contains(day);
    }
}
