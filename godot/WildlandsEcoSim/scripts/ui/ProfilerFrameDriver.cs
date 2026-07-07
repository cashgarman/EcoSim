using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Opens/closes the per-frame <c>frame</c> scope and records wall-clock frame time after draw.</summary>
public partial class ProfilerFrameDriver : Node
{
    private double _frameT0Ms;
    private bool _frameOpen;

    public override void _Ready()
    {
        ProcessPriority = -128;
    }

    public override void _Process(double delta)
    {
        _frameT0Ms = Godot.Time.GetTicksMsec();
        var p = PerfProfiler.Instance;
        if (p.IsInstrumentationActive)
        {
            p.BeginFrame();
            p.EnterScope("frame");
            _frameOpen = true;
        }

        Callable.From(FinishFrame).CallDeferred();
    }

    private void FinishFrame()
    {
        double frameMs = Godot.Time.GetTicksMsec() - _frameT0Ms;
        var p = PerfProfiler.Instance;
        if (_frameOpen)
        {
            p.ExitScope();
            _frameOpen = false;
        }

        p.EndFrame(frameMs);
    }
}
