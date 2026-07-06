namespace WildlandsEcoSim.Gpu;

public enum GpuThrottlePreset
{
    Off,
    Light,
    Medium,
    Heavy,
    Eco,
}

public static class GpuThrottle
{
    public static GpuThrottlePreset Preset { get; set; } = GpuThrottlePreset.Off;

    public static double ReadbackMultiplier => Preset switch
    {
        GpuThrottlePreset.Light => 1.25,
        GpuThrottlePreset.Medium => 1.6,
        GpuThrottlePreset.Heavy => 2.2,
        GpuThrottlePreset.Eco => 3.0,
        _ => 1.0,
    };

    public static int MinQualityTier => Preset switch
    {
        GpuThrottlePreset.Heavy => 2,
        GpuThrottlePreset.Eco => 3,
        _ => 0,
    };
}
