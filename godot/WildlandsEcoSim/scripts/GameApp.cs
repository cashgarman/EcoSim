using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim;

/// <summary>Autoload — advances simulation each frame when not paused.</summary>
public partial class GameApp : Node
{
    [Signal]
    public delegate void SimTickedEventHandler();

    private EcoSimHost _host = null!;

    public bool Paused { get; set; }
    public double LastSpeedBeforePause { get; set; } = 1;

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

        double speed = Paused ? 0 : session.State.Speed;
        if (speed <= 0) return;

        double sdt = delta * speed;
        int steps = Math.Min(6, (int)Math.Ceiling(speed));
        double stepDt = sdt / steps;
        for (int i = 0; i < steps; i++)
        {
            session.State.TGlobal += stepDt;
            session.Simulation.Tick(stepDt);
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
