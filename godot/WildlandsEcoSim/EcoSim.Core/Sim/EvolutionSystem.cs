using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using EcoSim.Core.Numerics;

namespace EcoSim.Core.Sim;

/// <summary>
/// Spends generation points on evolution-tree nodes. Purchases mutate the live species
/// definition in place (base genome + ability flags), so future spawns inherit via
/// Genetics.NewGenome, and additionally patch every living creature's genome once —
/// required because BreedGenome averages parents and never reads the species base,
/// so live patching is the only way evolutions reach bred offspring.
/// </summary>
public sealed class EvolutionSystem
{
    private readonly SimState _state;
    private readonly SpeciesCatalog _species;
    private readonly BehaviorLibrary _behaviors;
    private readonly PlayerProgress _progress;

    public EvolutionCatalog Catalog { get; }

    public EvolutionSystem(SimState state, SpeciesCatalog species, BehaviorLibrary behaviors,
        EvolutionCatalog catalog, PlayerProgress progress)
    {
        _state = state;
        _species = species;
        _behaviors = behaviors;
        Catalog = catalog;
        _progress = progress;
    }

    public bool CanPurchase(string sp, string nodeId, out string reason)
    {
        var node = Catalog.NodeFor(sp, nodeId);
        if (node == null) { reason = "Unknown evolution"; return false; }
        if (_progress.HasPurchased(sp, nodeId)) { reason = "Already evolved"; return false; }
        foreach (string req in node.Requires)
        {
            if (!_progress.HasPurchased(sp, req))
            {
                var reqNode = Catalog.NodeFor(sp, req);
                reason = $"Requires {reqNode?.Label ?? req}";
                return false;
            }
        }
        if (_progress.Points(sp) < node.Cost) { reason = $"Needs {node.Cost} points"; return false; }
        reason = "";
        return true;
    }

    public bool Purchase(string sp, string nodeId)
    {
        if (!CanPurchase(sp, nodeId, out _)) return false;
        var node = Catalog.NodeFor(sp, nodeId)!;
        if (!_progress.SpendPoints(sp, node.Cost)) return false;
        _progress.Purchased(sp).Add(nodeId);

        var def = _species.Get(sp);
        ApplyToSpeciesDef(def, node);
        foreach (var c in _state.Creatures)
        {
            if (c.Dead || c.Sp != sp) continue;
            ApplyToGenome(c.Genome, node);
        }
        return true;
    }

    /// <summary>Restores pristine species defs and clears progress (game restart).</summary>
    public void ResetAll()
    {
        _progress.Reset();
        // Re-cloning defs detaches every BehaviorConfig ([JsonIgnore]); recompile is mandatory.
        _species.ApplyOverrides(null);
        _behaviors.RecompileAll(_species);
    }

    private void ApplyToSpeciesDef(SpeciesDefinition def, EvolutionNode node)
    {
        if (node.Effects.Genes != null)
        {
            foreach (var (key, op) in node.Effects.Genes)
            {
                SetGene(def.Base, key, ClampGene(key, op.Apply(GetGene(def.Base, key))));
            }
        }
        if (node.Effects.Abilities != null)
        {
            foreach (var (key, value) in node.Effects.Abilities)
            {
                if (key == "canSwim") def.CanSwim = value;
            }
        }
    }

    private void ApplyToGenome(Genome g, EvolutionNode node)
    {
        if (node.Effects.Genes == null) return;
        foreach (var (key, op) in node.Effects.Genes)
        {
            SetGenomeGene(g, key, ClampGene(key, op.Apply(g[key])));
        }
    }

    private double ClampGene(string key, double value)
    {
        if (!_species.GeneRange.TryGetValue(key, out var range)) return value;
        return SimMath.Clamp(value, range[0], range[1]);
    }

    private static double GetGene(SpeciesGenome b, string key) => key switch
    {
        "size" => b.Size,
        "speed" => b.Speed,
        "sense" => b.Sense,
        "metab" => b.Metab,
        "litter" => b.Litter,
        "lifespan" => b.Lifespan,
        "temp" => b.Temp,
        "tol" => b.Tol,
        "hue" => b.Hue,
        "agg" => b.Agg,
        _ => 0,
    };

    private static void SetGene(SpeciesGenome b, string key, double v)
    {
        switch (key)
        {
            case "size": b.Size = v; break;
            case "speed": b.Speed = v; break;
            case "sense": b.Sense = v; break;
            case "metab": b.Metab = v; break;
            case "litter": b.Litter = v; break;
            case "lifespan": b.Lifespan = v; break;
            case "temp": b.Temp = v; break;
            case "tol": b.Tol = v; break;
            case "hue": b.Hue = v; break;
            case "agg": b.Agg = v; break;
        }
    }

    private static void SetGenomeGene(Genome g, string key, double v)
    {
        switch (key)
        {
            case "size": g.Size = v; break;
            case "speed": g.Speed = v; break;
            case "sense": g.Sense = v; break;
            case "metab": g.Metab = v; break;
            case "litter": g.Litter = v; break;
            case "lifespan": g.Lifespan = v; break;
            case "temp": g.Temp = v; break;
            case "tol": g.Tol = v; break;
            case "hue": g.Hue = v; break;
            case "agg": g.Agg = v; break;
        }
    }
}
