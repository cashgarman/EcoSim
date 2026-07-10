namespace EcoSim.Core.Sim;

/// <summary>
/// Per-run player meta progression: generation points earned by breeding and
/// evolution nodes purchased, both keyed by species. Not persisted across runs.
/// </summary>
public sealed class PlayerProgress
{
    public Dictionary<string, int> PointsBySpecies { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HashSet<string>> PurchasedNodes { get; } = new(StringComparer.Ordinal);

    public int TotalPointsEarned { get; private set; }
    public int BodiesInhabited { get; set; }
    public int TimesKilled { get; set; }
    public int NaturalDeaths { get; set; }

    public int Points(string sp) => PointsBySpecies.GetValueOrDefault(sp);

    public void AddPoint(string sp)
    {
        PointsBySpecies[sp] = PointsBySpecies.GetValueOrDefault(sp) + 1;
        TotalPointsEarned++;
    }

    public bool SpendPoints(string sp, int cost)
    {
        int have = PointsBySpecies.GetValueOrDefault(sp);
        if (have < cost) return false;
        PointsBySpecies[sp] = have - cost;
        return true;
    }

    public HashSet<string> Purchased(string sp)
    {
        if (!PurchasedNodes.TryGetValue(sp, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            PurchasedNodes[sp] = set;
        }
        return set;
    }

    public bool HasPurchased(string sp, string nodeId) =>
        PurchasedNodes.TryGetValue(sp, out var set) && set.Contains(nodeId);

    public void Reset()
    {
        PointsBySpecies.Clear();
        PurchasedNodes.Clear();
        TotalPointsEarned = 0;
        BodiesInhabited = 0;
        TimesKilled = 0;
        NaturalDeaths = 0;
    }
}
