using System.Text.Json.Serialization;

namespace EcoSim.Core.Data;

public sealed class SpeciesGenome
{
    public double Size { get; set; }
    public double Speed { get; set; }
    public double Sense { get; set; }
    public double Metab { get; set; }
    public double Litter { get; set; }
    public double Lifespan { get; set; }
    public double Temp { get; set; }
    public double Tol { get; set; }
    public double Hue { get; set; }
    public double Agg { get; set; }
}

public sealed class SpeciesDefinition
{
    public string Behavior { get; set; } = "";
    public string Label { get; set; } = "";
    public string Emoji { get; set; } = "";
    public string Blurb { get; set; } = "";
    public int Diet { get; set; }
    public string Shape { get; set; } = "small";
    public int[] Col { get; set; } = [128, 128, 128];
    public string[]? Hunts { get; set; }
    public string[]? PreyOf { get; set; }
    public bool CanSwim { get; set; }
    public bool SpawnNearWater { get; set; }
    public SpeciesGenome Base { get; set; } = new();
    public double[] GestationSec { get; set; } = [3, 5];
    public double[] MateCooldownSec { get; set; } = [3, 5];
    public double StockWeight { get; set; }

    [JsonIgnore]
    public uint HuntsMask { get; set; }

    [JsonIgnore]
    public uint PreyMask { get; set; }

    [JsonIgnore]
    public Behavior.BehaviorConfig? BehaviorConfig { get; set; }
}

public sealed class SpeciesFileRoot
{
    public string[] GeneKeys { get; set; } = [];
    public Dictionary<string, double[]> GeneRange { get; set; } = new();
    public Dictionary<string, string> GeneLabel { get; set; } = new();
    public Dictionary<string, SpeciesDefinition> Species { get; set; } = new();
}
