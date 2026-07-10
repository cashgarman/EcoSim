namespace EcoSim.Core.Data;

/// <summary>A multiply/add operation on one gene of the species base genome.</summary>
public sealed class GeneOp
{
    public double? Mul { get; set; }
    public double? Add { get; set; }

    public double Apply(double value)
    {
        if (Mul.HasValue) value *= Mul.Value;
        if (Add.HasValue) value += Add.Value;
        return value;
    }
}

public sealed class EvolutionEffects
{
    /// <summary>Gene key (species.json geneKeys) → operation.</summary>
    public Dictionary<string, GeneOp>? Genes { get; set; }

    /// <summary>Ability flag → value. Supported: "canSwim".</summary>
    public Dictionary<string, bool>? Abilities { get; set; }
}

public sealed class EvolutionNode
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Desc { get; set; } = "";
    public int Cost { get; set; } = 1;
    public string[] Requires { get; set; } = [];
    public EvolutionEffects Effects { get; set; } = new();
}

public sealed class EvolutionTreeFile
{
    public string Species { get; set; } = "";
    public List<EvolutionNode> Nodes { get; set; } = [];
}
