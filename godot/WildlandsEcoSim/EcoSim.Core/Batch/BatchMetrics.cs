using System.Diagnostics;
using System.Text.Json;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Batch;

public sealed class BatchMetricsCollector
{
    private readonly SpeciesCatalog _catalog;
    private readonly SimState _state;

    public List<BatchSample> Samples { get; } = [];
    public int SampleEveryDays { get; }
    public bool SparseMode { get; }
    public int TargetDays { get; }
    public string? StartedAt { get; private set; }
    public long WallStartTicks { get; private set; }
    public int InitialPop { get; private set; }
    public int PeakPop { get; private set; }
    public int MinPop { get; private set; } = int.MaxValue;
    public Dictionary<string, int> ExtinctAtDay { get; } = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lastSeenSpecies = new(StringComparer.Ordinal);

    public BatchMetricsCollector(SimState state, SpeciesCatalog catalog, int sampleEveryDays = 10, int targetDays = 200, bool sparseMode = false)
    {
        _state = state;
        _catalog = catalog;
        SampleEveryDays = sampleEveryDays;
        TargetDays = targetDays;
        SparseMode = sparseMode;
        foreach (string sp in catalog.SpeciesKeys) _lastSeenSpecies.Add(sp);
    }

    public void Begin(int initialPop, long? wallStartTicks = null)
    {
        StartedAt = DateTime.UtcNow.ToString("o");
        WallStartTicks = wallStartTicks ?? Stopwatch.GetTimestamp();
        InitialPop = initialPop;
        PeakPop = initialPop;
        MinPop = initialPop;
        Samples.Clear();
        ExtinctAtDay.Clear();
        _lastSeenSpecies.Clear();
        foreach (string sp in _catalog.SpeciesKeys) _lastSeenSpecies.Add(sp);
        CaptureSample(true);
    }

    public (Dictionary<string, int> Counts, int TotalAlive) AliveCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string sp in _catalog.SpeciesKeys) counts[sp] = 0;
        int total = 0;
        foreach (var c in _state.Creatures)
        {
            if (c.Dead) continue;
            counts[c.Sp] = counts.GetValueOrDefault(c.Sp) + 1;
            total++;
        }
        return (counts, total);
    }

    public double AvgVegPct()
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < _state.Veg.Length; i++)
        {
            float cap = _state.VegCap[i];
            if (cap <= 0.02f) continue;
            if (BiomeData.IsWater(_state.Biome[i])) continue;
            sum += _state.Veg[i] / Math.Max(0.001, cap);
            n++;
        }
        return n > 0 ? sum / n : 0;
    }

    public void CaptureSample(bool force = false)
    {
        int day = _state.Day;
        if (!force && Samples.Count > 0)
        {
            var last = Samples[^1];
            if (day - last.Day < SampleEveryDays) return;
        }

        var (counts, totalAlive) = AliveCounts();
        PeakPop = Math.Max(PeakPop, totalAlive);
        MinPop = Math.Min(MinPop, totalAlive);

        foreach (string sp in _catalog.SpeciesKeys)
        {
            if (counts[sp] > 0) _lastSeenSpecies.Add(sp);
            else if (_lastSeenSpecies.Contains(sp) && !ExtinctAtDay.ContainsKey(sp))
            {
                ExtinctAtDay[sp] = day;
                _lastSeenSpecies.Remove(sp);
            }
        }

        var avgNeeds = new NeedsAvg();
        int denom = Math.Max(1, totalAlive);
        foreach (var c in _state.Creatures)
        {
            if (c.Dead) continue;
            avgNeeds.Hp += c.Hp;
            avgNeeds.Hunger += c.Hunger;
            avgNeeds.Thirst += c.Thirst;
            avgNeeds.Energy += c.Energy;
        }
        avgNeeds.Hp /= denom;
        avgNeeds.Hunger /= denom;
        avgNeeds.Thirst /= denom;
        avgNeeds.Energy /= denom;

        Samples.Add(new BatchSample
        {
            Day = day,
            T = _state.TGlobal,
            Counts = new Dictionary<string, int>(counts),
            TotalAlive = totalAlive,
            GenerationMax = _state.GenerationMax,
            AvgNeeds = avgNeeds,
            AvgVegPct = AvgVegPct(),
            ExtinctSpecies = _catalog.SpeciesKeys.Where(sp => counts[sp] == 0).ToList(),
        });
    }

    public string ClassifyOutcome(bool earlyStop = false)
    {
        var (counts, totalAlive) = AliveCounts();
        var extinctSpecies = _catalog.SpeciesKeys.Where(sp => counts[sp] == 0).ToList();
        bool reachedTarget = _state.Day >= TargetDays - 0.001;

        if (earlyStop) return "total_extinction";
        if (totalAlive == 0) return "total_extinction";
        if (!reachedTarget) return "timeout";
        if (extinctSpecies.Count == 0) return "stable";
        if (extinctSpecies.Count < _catalog.SpeciesKeys.Count) return "partial_collapse";
        return "total_extinction";
    }

    public double StabilityScore(string outcome)
    {
        var (_, totalAlive) = AliveCounts();
        return BatchScoring.ScoreFromOutcome(outcome, totalAlive, InitialPop);
    }

    public BatchReport BuildReport(Dictionary<string, object?> runConfig, object? balanceConfig, string runId, string? outcome = null, bool earlyStop = false)
    {
        outcome ??= ClassifyOutcome(earlyStop);
        double wallMs = (Stopwatch.GetTimestamp() - WallStartTicks) * 1000.0 / Stopwatch.Frequency;
        var (counts, totalAlive) = AliveCounts();

        string? dominant = null;
        int dominantCount = 0;
        foreach (var (sp, n) in counts)
        {
            if (n > dominantCount) { dominantCount = n; dominant = sp; }
        }

        int? collapseDay = null;
        if (outcome == "total_extinction") collapseDay = _state.Day;
        else if (ExtinctAtDay.Count > 0) collapseDay = ExtinctAtDay.Values.Min();

        return new BatchReport
        {
            RunId = runId,
            StartedAt = StartedAt,
            FinishedAt = DateTime.UtcNow.ToString("o"),
            WallMs = wallMs,
            Config = runConfig,
            BalanceConfig = balanceConfig,
            Outcome = outcome,
            Score = StabilityScore(outcome),
            Summary = new BatchSummary
            {
                FinalDay = _state.Day,
                InitialPop = InitialPop,
                TargetDays = TargetDays,
                PeakPop = PeakPop,
                MinPop = MinPop == int.MaxValue ? 0 : MinPop,
                FinalPop = totalAlive,
                GenerationMax = _state.GenerationMax,
                ExtinctAtDay = new Dictionary<string, int>(ExtinctAtDay),
                DominantSpecies = dominant,
                CollapseDay = collapseDay,
                FinalCounts = counts,
            },
            Samples = Samples,
        };
    }
}

public static class BatchScoring
{
    public static double ScoreFromOutcome(string outcome, int finalPop, int initialPop = 0)
    {
        if (outcome == "stable" && finalPop >= initialPop * 0.3) return 1.0;
        if (outcome == "partial_collapse") return 0.5;
        if (outcome == "timeout" && finalPop > 0) return 0.35;
        return 0.0;
    }
}

public sealed class BatchSample
{
    public int Day { get; set; }
    public double T { get; set; }
    public Dictionary<string, int> Counts { get; set; } = new();
    public int TotalAlive { get; set; }
    public int GenerationMax { get; set; }
    public NeedsAvg AvgNeeds { get; set; } = new();
    public double AvgVegPct { get; set; }
    public List<string> ExtinctSpecies { get; set; } = [];
}

public sealed class NeedsAvg
{
    public double Hp { get; set; }
    public double Hunger { get; set; }
    public double Thirst { get; set; }
    public double Energy { get; set; }
}

public sealed class BatchSummary
{
    public int FinalDay { get; set; }
    public int InitialPop { get; set; }
    public int TargetDays { get; set; }
    public int PeakPop { get; set; }
    public int MinPop { get; set; }
    public int FinalPop { get; set; }
    public int GenerationMax { get; set; }
    public Dictionary<string, int> ExtinctAtDay { get; set; } = new();
    public string? DominantSpecies { get; set; }
    public int? CollapseDay { get; set; }
    public Dictionary<string, int> FinalCounts { get; set; } = new();
}

public sealed class BatchReport
{
    public string RunId { get; set; } = "";
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public double WallMs { get; set; }
    public Dictionary<string, object?> Config { get; set; } = new();
    public object? BalanceConfig { get; set; }
    public string Outcome { get; set; } = "";
    public double Score { get; set; }
    public BatchSummary Summary { get; set; } = new();
    public List<BatchSample> Samples { get; set; } = [];
}
