using EcoSim.Core.Data;

namespace EcoSim.Core.Sim;

public sealed class SimState
{
    public uint Seed { get; set; }
    public int W { get; set; }
    public int H { get; set; }

    public float[] Elev { get; set; } = [];
    public float[] Temp { get; set; } = [];
    public float[] Moist { get; set; } = [];
    public byte[] Biome { get; set; } = [];
    public float[] Veg { get; set; } = [];
    public float[] VegCap { get; set; } = [];
    public byte[] PassMask { get; set; } = [];
    public float[] WaterDist { get; set; } = [];

    public WorldGenConfig Cfg { get; set; } = new();
    public double WorldAreaKm2 { get; set; }
    public double WorldKmPerTile { get; set; }
    public int GrowStride { get; set; } = 8;
    public int GrowRow { get; set; }
    public bool VegDirty { get; set; } = true;

    public LandBounds LandBounds { get; set; } = new();

    public List<Creature> Creatures { get; } = [];
    public Dictionary<long, List<Creature>> Grid { get; } = new();
    public int NextId { get; set; } = 1;
    public int GenerationMax { get; set; } = 1;

    public int Day { get; set; }
    public double TGlobal { get; set; }
    public double TimeOfDay { get; set; } = 0.3;
    public double LightLevel { get; set; } = 1;
    public bool IsNight { get; set; }
    public double MigrantTimer { get; set; }

    public bool AutoMigrationEnabled { get; set; }
    public bool BatchMode { get; set; }
    public BatchConfig? BatchConfig { get; set; }
    public bool Ready { get; set; }
    public double Speed { get; set; } = 1;

    public Creature? Selected { get; set; }
}
