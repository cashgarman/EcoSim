namespace EcoSim.Core.Sim;

using System.Text.Json.Nodes;

public sealed class Creature
{
    public int Id { get; set; }
    public string Sp { get; set; } = "";
    public string Sex { get; set; } = "female";
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
    public int Dir { get; set; } = 1;
    public Genome Genome { get; set; } = new();
    public int Gen { get; set; } = 1;
    public double Age { get; set; }
    public double Hp { get; set; } = 100;
    public double Hunger { get; set; } = 70;
    public double Thirst { get; set; } = 70;
    public double Energy { get; set; } = 80;
    public string State { get; set; } = "wander";
    public double Tx { get; set; }
    public double Ty { get; set; }
    public int? Target { get; set; }
    public double MateCd { get; set; }
    public double Pregnant { get; set; }
    public int LitterQ { get; set; }
    public double Walk { get; set; }
    public bool Dead { get; set; }
    public string Cause { get; set; } = "";
    public int? KilledById { get; set; }
    public List<int> ParentIds { get; } = [];
    public List<int> OffspringIds { get; } = [];
    public Genome? MatePartner { get; set; }
    public int? MatePartnerId { get; set; }
    public string? BtNodeId { get; set; }
    public string? BtBranchUid { get; set; }
    public JsonObject? BtAction { get; set; }
    public double StateCommittedSince { get; set; }
    public double BtSpeedMult { get; set; } = 1;
    public double Rx { get; set; }
    public double Ry { get; set; }
    public long? GridKey { get; set; }
    public double NavGoalX { get; set; } = double.NaN;
    public double NavGoalY { get; set; } = double.NaN;
    public double NavWpX { get; set; } = double.NaN;
    public double NavWpY { get; set; } = double.NaN;
    public LifeStoryData? LifeStory { get; set; }
}

public sealed class Genome
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

    public double this[string key] => key switch
    {
        "size" => Size,
        "speed" => Speed,
        "sense" => Sense,
        "metab" => Metab,
        "litter" => Litter,
        "lifespan" => Lifespan,
        "temp" => Temp,
        "tol" => Tol,
        "hue" => Hue,
        "agg" => Agg,
        _ => 0,
    };
}
