using EcoSim.Core.Data;

namespace EcoSim.Core.Sim;

public sealed class SpeciesStatsEntry
{
    public int TotalBorn { get; set; }
    public int TotalDied { get; set; }
    public Dictionary<string, int> DeathsByKey { get; } = new(StringComparer.Ordinal);
    public List<double> BirthTimes { get; } = [];
}

public sealed class SpeciesStatsTracker
{
    private const double BirthWindowSec = 40;
    private readonly Dictionary<string, SpeciesStatsEntry> _stats = new(StringComparer.Ordinal);

    public void Init(SpeciesCatalog catalog)
    {
        _stats.Clear();
        foreach (string key in catalog.SpeciesKeys)
        {
            _stats[key] = new SpeciesStatsEntry();
        }
    }

    public SpeciesStatsEntry Get(string sp) => _stats[sp];

    public void RecordBirth(string sp, double tGlobal)
    {
        if (!_stats.TryGetValue(sp, out var entry)) return;
        entry.TotalBorn++;
        entry.BirthTimes.Add(tGlobal);
        PruneBirthTimes(entry, tGlobal);
    }

    public void RecordDeath(string sp, string deathKey)
    {
        if (!_stats.TryGetValue(sp, out var entry)) return;
        entry.TotalDied++;
        entry.DeathsByKey.TryGetValue(deathKey, out int n);
        entry.DeathsByKey[deathKey] = n + 1;
    }

    public int BirthRateLastDay(string sp, double tGlobal)
    {
        if (!_stats.TryGetValue(sp, out var entry)) return 0;
        PruneBirthTimes(entry, tGlobal);
        return entry.BirthTimes.Count;
    }

    private static void PruneBirthTimes(SpeciesStatsEntry entry, double tGlobal)
    {
        double cutoff = tGlobal - BirthWindowSec;
        int i = 0;
        while (i < entry.BirthTimes.Count && entry.BirthTimes[i] < cutoff) i++;
        if (i > 0) entry.BirthTimes.RemoveRange(0, i);
    }
}
