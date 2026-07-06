using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim;

/// <summary>Autoload — advances simulation each frame when not paused.</summary>
public partial class GameApp : Node
{
    [Signal]
    public delegate void SimTickedEventHandler();

    private EcoSimHost _host = null!;
    private TimeScrubController? _scrub;

    public bool Paused { get; set; }
    public double LastSpeedBeforePause { get; set; } = 1;
    public TimeScrubController? Scrub => _scrub;

    public void SetScrubController(TimeScrubController scrub) => _scrub = scrub;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            return;
        }

        _host = GetNode<EcoSimHost>("/root/EcoSimHost");
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) return;
        var session = _host.Session;
        if (session == null || !session.State.Ready) return;

        double frameStart = Time.GetTicksMsec();
        double simMs = 0;
        double speed = Paused ? 0 : session.State.Speed;
        if (_scrub != null && _scrub.ScrubActive)
        {
            speed = 0;
        }

        if (speed > 0)
        {
            double simStart = Time.GetTicksMsec();
            double sdt = delta * speed;
            int steps = Math.Min(6, (int)Math.Ceiling(speed));
            double stepDt = sdt / steps;
            for (int i = 0; i < steps; i++)
            {
                session.State.TGlobal += stepDt;
                session.Simulation.Tick(stepDt);
            }
            simMs = Time.GetTicksMsec() - simStart;
            _scrub?.CaptureIfDue();
        }

        EmitSignal(SignalName.SimTicked);

        double frameMs = Time.GetTicksMsec() - frameStart;
        UI.PerfProfiler.Instance.RecordFrame(frameMs, simMs, frameMs * 0.35, frameMs * 0.15);
    }

    public void TogglePause()
    {
        var session = _host.Session;
        if (session == null) return;

        if (Paused)
        {
            Paused = false;
            session.State.Speed = LastSpeedBeforePause > 0 ? LastSpeedBeforePause : 1;
        }
        else
        {
            LastSpeedBeforePause = session.State.Speed;
            Paused = true;
            session.State.Speed = 0;
        }
    }
}
