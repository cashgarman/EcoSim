using Godot;

namespace WildlandsEcoSim.UI;

public sealed class PerfProfiler
{
    public static PerfProfiler Instance { get; } = new();

    public double FrameMsAvg { get; private set; }
    public double SimMsAvg { get; private set; }
    public double RenderMsAvg { get; private set; }
    public double UiMsAvg { get; private set; }
    public int QualityTier { get; set; }
    public string QualityName => QualityTier switch
    {
        0 => "high",
        1 => "medium",
        2 => "low",
        _ => "emergency",
    };

    private const double Alpha = 0.12;

    public void RecordFrame(double frameMs, double simMs, double renderMs, double uiMs)
    {
        FrameMsAvg = Lerp(FrameMsAvg, frameMs, Alpha);
        SimMsAvg = Lerp(SimMsAvg, simMs, Alpha);
        RenderMsAvg = Lerp(RenderMsAvg, renderMs, Alpha);
        UiMsAvg = Lerp(UiMsAvg, uiMs, Alpha);
        QualityTier = FrameMsAvg switch
        {
            <= 16.8 => 0,
            <= 22 => 1,
            <= 30 => 2,
            _ => 3,
        };
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
