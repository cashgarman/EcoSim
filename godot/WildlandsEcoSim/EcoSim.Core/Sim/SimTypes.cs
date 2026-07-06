namespace EcoSim.Core.Sim;

public enum SimulationFlags
{
    None = 0,
    Batch = 1,
    Scrubbing = 2,
}

public sealed class WorldGenConfig
{
    public double Sea { get; set; } = 0.46;
    public double Temp { get; set; } = 0.5;
    public double Moist { get; set; } = 0.5;
    public double Relief { get; set; } = 0.6;
    public double Animals { get; set; } = 0.45;
    public string Size { get; set; } = "m";
}

public sealed class LandBounds
{
    public int MinX { get; set; }
    public int MinY { get; set; }
    public int MaxX { get; set; }
    public int MaxY { get; set; }
}

public sealed class BatchConfig
{
    public bool AutoMigration { get; set; }
    public string SimBackend { get; set; } = "cpu";
}
