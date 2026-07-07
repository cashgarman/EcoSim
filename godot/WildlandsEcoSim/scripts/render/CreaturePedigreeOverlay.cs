using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim.Render;

public partial class CreaturePedigreeOverlay : Node2D
{
    private const float LineWidthScreenPx = 2.2f;
    private const float DashLengthScreenPx = 4f;
    private const float GapLengthScreenPx = 3f;

    private SimSession? _session;
    private float _camZoom = 1f;

    private float PixelsPerTile => WorldRenderer.TilePixels * _camZoom;

    private float LineWidthTiles() => LineWidthScreenPx / PixelsPerTile;

    public override void _Ready()
    {
        ZIndex = 3;
    }

    public void Bind(SimSession session) => _session = session;

    public void SetCameraZoom(float zoom) => _camZoom = Math.Max(0.25f, zoom);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_session == null || !_session.State.Ready) return;

        PerfProfiler.Instance.Timed("render", () =>
        PerfProfiler.Instance.Timed("render.pedigree", () =>
        {
        var focus = _session.State.Selected;
        if (focus == null || focus.Dead) return;

        DrawTargetLine(focus);

        Vector2 from = CreatureDrawUtil.DisplayPos(focus);
        foreach (int pid in focus.ParentIds)
        {
            var parent = _session.Creatures.GetById(pid);
            if (parent == null) continue;
            DrawAnimatedPedigreeLine(
                from,
                CreatureDrawUtil.DisplayPos(parent),
                "255,220,60",
                focus.Id * 17 + pid * 3);
        }

        foreach (int oid in focus.OffspringIds)
        {
            var child = _session.Creatures.GetById(oid);
            if (child == null) continue;
            DrawAnimatedPedigreeLine(
                from,
                CreatureDrawUtil.DisplayPos(child),
                "87,184,232",
                focus.Id * 23 + oid * 5);
        }
        }));
    }

    private void DrawTargetLine(Creature focus)
    {
        if (focus.State is "rest" or "wander") return;

        Vector2 from = CreatureDrawUtil.DisplayPos(focus);
        Vector2 to = new((float)focus.Tx, (float)focus.Ty);
        if (focus.Target != null)
        {
            var target = _session!.Creatures.GetById(focus.Target.Value);
            if (target != null && !target.Dead)
            {
                to = CreatureDrawUtil.DisplayPos(target);
            }
        }

        if (from.DistanceTo(to) < 0.15f) return;

        DrawAnimatedDashedLine(
            from,
            to,
            ParseRgb(TargetLineColor(focus.State), 0.98f),
            focus.Id * 31 + 7);
    }

    private void DrawAnimatedPedigreeLine(Vector2 from, Vector2 to, string rgb, int phaseSeed)
    {
        DrawAnimatedDashedLine(from, to, ParseRgb(rgb, 0.88f), phaseSeed);
    }

    private void DrawAnimatedDashedLine(Vector2 from, Vector2 to, Color color, int phaseSeed)
    {
        Vector2 seg = to - from;
        float len = seg.Length();
        if (len < 0.001f) return;

        Vector2 dir = seg / len;
        float dashLen = DashLengthScreenPx / PixelsPerTile;
        float gapLen = GapLengthScreenPx / PixelsPerTile;
        float pattern = dashLen + gapLen;
        float offset = (float)((_session!.State.TGlobal * 42.0 + phaseSeed) % pattern);
        float width = LineWidthTiles();

        float pos = -offset;
        while (pos < len)
        {
            float dashStart = Math.Max(0f, pos);
            float dashEnd = Math.Min(len, pos + dashLen);
            if (dashEnd > dashStart)
            {
                DrawLine(from + dir * dashStart, from + dir * dashEnd, color, width);
            }

            pos += pattern;
        }
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
