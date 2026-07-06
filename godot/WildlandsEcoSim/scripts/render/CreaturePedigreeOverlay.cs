using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.Render;

public partial class CreaturePedigreeOverlay : Node2D
{
    private SimSession? _session;
    private float _camZoom = 1f;

    public override void _Ready()
    {
        ZIndex = 1;
    }

    public void Bind(SimSession session) => _session = session;

    public void SetCameraZoom(float zoom) => _camZoom = Math.Max(0.25f, zoom);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_session == null || !_session.State.Ready) return;

        var focus = _session.State.Selected;
        if (focus == null || focus.Dead) return;

        DrawTargetLine(focus);

        Vector2 from = CreatureDrawUtil.DisplayPos(focus);
        foreach (int pid in focus.ParentIds)
        {
            var parent = _session.Creatures.GetById(pid);
            if (parent == null || parent.Dead) continue;
            DrawPedigreeLine(from, CreatureDrawUtil.DisplayPos(parent), "255,210,120", pid);
        }

        foreach (int oid in focus.OffspringIds)
        {
            var child = _session.Creatures.GetById(oid);
            if (child == null || child.Dead) continue;
            DrawPedigreeLine(from, CreatureDrawUtil.DisplayPos(child), "87,184,232", oid + 100);
        }
    }

    private void DrawTargetLine(Creature focus)
    {
        if (focus.State is "rest" or "wander") return;

        double wx = focus.Tx;
        double wy = focus.Ty;
        if (focus.Target != null)
        {
            var target = _session!.Creatures.GetById(focus.Target.Value);
            if (target != null && !target.Dead)
            {
                wx = target.X;
                wy = target.Y;
            }
        }

        string rgb = TargetLineColor(focus.State);
        Vector2 from = CreatureDrawUtil.DisplayPos(focus);
        var to = new Vector2((float)wx, (float)wy);
        DrawLine(from, to, ParseRgb(rgb, 0.9f), Math.Max(0.03f, _camZoom * 0.07f));
    }

    private void DrawPedigreeLine(Vector2 from, Vector2 to, string rgb, int phaseSeed)
    {
        DrawLine(from, to, ParseRgb(rgb, 0.88f), Math.Max(0.03f, _camZoom * 0.07f));
    }

    private static string TargetLineColor(string state) => state switch
    {
        "hunt" or "huntSearch" => "245,102,72",
        "flee" => "255,220,108",
        "mate" => "236,124,214",
        "thirst" => "120,180,255",
        "graze" => "130,200,120",
        _ => "220,220,220",
    };

    private static Color ParseRgb(string rgb, float alpha)
    {
        string[] parts = rgb.Split(',');
        return new Color(
            int.Parse(parts[0]) / 255f,
            int.Parse(parts[1]) / 255f,
            int.Parse(parts[2]) / 255f,
            alpha);
    }
}
