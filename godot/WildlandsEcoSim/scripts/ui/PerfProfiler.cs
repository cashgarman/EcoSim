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

    public int DetailTier => QualityTier switch
    {
        0 => 2,
        1 => 1,
        _ => 0,
    };

    public int HighlightTier => QualityTier switch
    {
        0 => 2,
        1 => 1,
        _ => 0,
    };

    public int RenderDecimation => QualityTier >= 3 ? 2 : 1;

    private readonly double[] _frameRing = new double[64];
    private int _frameRingIdx;
    private readonly Dictionary<string, double> _scopeTotals = new(StringComparer.Ordinal);
    private bool _detailEnabled;

    public bool DetailEnabled
    {
        get => _detailEnabled;
        set => _detailEnabled = value;
    }

    public int EffectiveHighlight(string? lockedSpecies, string? hoveredSpecies, bool hasLiveSelection)
    {
        if (!string.IsNullOrEmpty(lockedSpecies))
        {
            return Math.Max(HighlightTier, 2);
        }

        if (!string.IsNullOrEmpty(hoveredSpecies) || hasLiveSelection)
        {
            return Math.Max(HighlightTier, 1);
        }

        return HighlightTier;
    }

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
        QualityTier = Math.Max(QualityTier, Gpu.GpuThrottle.MinQualityTier);
        _frameRing[_frameRingIdx++ % _frameRing.Length] = frameMs;
    }

    public void EnterScope(string name)
    {
        if (!_detailEnabled) return;
        _scopeTotals.TryGetValue(name, out double v);
        _scopeTotals[name] = v;
    }

    public void AddScopeMs(string name, double ms)
    {
        if (!_detailEnabled) return;
        _scopeTotals.TryGetValue(name, out double v);
        _scopeTotals[name] = v + ms;
    }

    public string GetCpuTreeText()
    {
        if (_scopeTotals.Count == 0)
        {
            return $"frame {FrameMsAvg:F1}ms\n  sim {SimMsAvg:F1}ms\n  render {RenderMsAvg:F1}ms\n  ui {UiMsAvg:F1}ms";
        }

        return string.Join("\n", _scopeTotals.OrderByDescending(kv => kv.Value)
            .Take(20)
            .Select(kv => $"{kv.Key} {kv.Value:F2}ms"));
    }

    public string GetGpuStatsText()
    {
        return "GPU sim: cpu fallback\nDraw calls: —\nCompute dispatches: —\nVRAM est: —";
    }

    public IReadOnlyList<double> FrameRing => _frameRing;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
