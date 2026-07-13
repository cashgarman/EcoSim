using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.UI;
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

    /// <summary>True when sim time and creature logic should advance.</summary>
    public bool IsSimAdvancing(SimSession session) => GetEffectiveSpeed(session) > 0;

    public double GetEffectiveSpeed(SimSession session)
    {
        if (Paused || session.State.Speed <= 0) return 0;
        if (_scrub != null && _scrub.ScrubActive) return 0;

        double speed = session.State.Speed;
        if (speed > 1 && session.Player.IsControlling)
        {
            speed = 1;
        }

        return speed;
    }

    /// <summary>Resume live sim after timeline/modal pause when speed &gt; 0.</summary>
    public void ResumeLiveSim(double speed)
    {
        var session = _host.Session;
        if (session == null) return;

        Paused = false;
        session.State.Speed = Math.Max(0, speed);
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            return;
        }

        _host = GetNode<EcoSimHost>("/root/EcoSimHost");
        AddChild(new ProfilerFrameDriver());
        ProcessPriority = -64;
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) return;
        var session = _host.Session;
        if (session == null || !session.State.Ready) return;

        var profiler = PerfProfiler.Instance;
        double speed = GetEffectiveSpeed(session);

        if (speed > 0)
        {
            profiler.Timed("frame.sim", () =>
            {
                profiler.Timed("sim", () =>
                {
                    double sdt = delta * speed;
                    int steps = Math.Min(6, (int)Math.Ceiling(speed));
                    double stepDt = sdt / steps;
                    for (int i = 0; i < steps; i++)
                    {
                        session.State.TGlobal += stepDt;
                        profiler.Timed("sim.tick", () => session.Simulation.Tick(stepDt));
                    }
                });

                profiler.Timed("snapshot", () => _scrub?.CaptureIfDue());
                _scrub?.NoteLiveAdvance();
            });
        }

        bool scrubbing = _scrub?.ScrubActive ?? false;
        if (IsSimAdvancing(session) || scrubbing)
        {
            profiler.Timed("displaySmooth", () =>
                session.Creatures.AdvanceDisplayPositions(delta, scrubbing));
        }

        EmitSignal(SignalName.SimTicked);
    }

    public void TogglePause()
    {
        var session = _host.Session;
        if (session == null) return;

        if (Paused)
        {
            Paused = false;
            session.State.Speed = LastSpeedBeforePause;
        }
        else
        {
            LastSpeedBeforePause = session.State.Speed;
            Paused = true;
            session.State.Speed = 0;
        }
    }

    public void PauseForTimeline()
    {
        var session = _host.Session;
        if (session == null) return;

        if (!Paused && session.State.Speed > 0)
        {
            LastSpeedBeforePause = session.State.Speed;
        }

        Paused = true;
        session.State.Speed = 0;
    }
}
