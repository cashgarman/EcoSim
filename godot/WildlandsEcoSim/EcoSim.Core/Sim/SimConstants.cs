namespace EcoSim.Core.Sim;

public static class WorldSizePresets
{
    public sealed record Preset(double AreaKm2, double SideKm, int TilesPerKm);

    public static readonly Dictionary<string, Preset> All = new(StringComparer.Ordinal)
    {
        ["s"] = new(25, 5, 32),
        ["m"] = new(64, 8, 32),
        ["l"] = new(100, 10, 32),
        ["xl"] = new(400, 20, 32),
        ["xxl"] = new(900, 30, 24),
    };

    public static Preset Get(string key) => All.TryGetValue(key, out var p) ? p : All["m"];
}

public static class SimConstants
{
    public const int MaxPop = 6000;
    public const int Cell = 6;
    public const int PassGroundBlocked = 1;
    public const double DirectPursuitRadius = 4;
    public const double WaterDistUnreachable = 1e9;
    public const int WaterSeekRadiusMin = 48;
    public const double SimDaySeconds = 40;

    /// <summary>Global multiplier on hunger/thirst/energy drain (1 = original pacing).</summary>
    public const double NeedsDrainScale = 0.5;

    /// <summary>Player sprint: speed multiplier while Shift is held, and its extra energy cost per second.</summary>
    public const double SprintSpeedMult = 1.6;
    public const double SprintEnergyPerSec = 2.2;
    public const double SprintMinEnergy = 5;
}
