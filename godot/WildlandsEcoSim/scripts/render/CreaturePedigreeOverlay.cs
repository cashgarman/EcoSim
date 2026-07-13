using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim.Render;

public partial class CreaturePedigreeOverlay : Node2D
{
    private const float LineWidthScreenPx = 2.2f;
    private const float DashLengthScreenPx = 4f;
    private const float GapLengthScreenPx = 3f;
    private const float LabelOffsetScreenPx = 6f;
    private const float LabelFontScreenPx = 9f;
    private const float LabelAlongLineT = 0.38f;
    private const float MinLabelLineTiles = 1.2f;

    private SimSession? _session;
    private float _camZoom = 1f;
    private readonly List<Creature> _mateScratch = [];

    private const string MateHintRgb = "62,207,106";
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
        var controlled = _session.Player.Controlled;
        if (controlled != null)
        {
            DrawMateHintLines(controlled);
        }

        var focus = _session.State.Selected;
        if (focus == null || focus.Dead) return;

        DrawTargetLine(focus);

        Vector2 from = CreatureLineAnchor(focus);
        foreach (int pid in focus.ParentIds)
        {
            var parent = _session.Creatures.GetById(pid);
            if (parent == null) continue;
            DrawAnimatedPedigreeLine(
                from,
                CreatureLineAnchor(parent),
                "255,220,60",
                focus.Id * 17 + pid * 3,
                "Parent");
        }

        foreach (int oid in focus.OffspringIds)
        {
            var child = _session.Creatures.GetById(oid);
            if (child == null) continue;
            DrawAnimatedPedigreeLine(
                from,
                CreatureLineAnchor(child),
                "87,184,232",
                focus.Id * 23 + oid * 5,
                "Offspring");
        }
        }));
    }

    private void DrawMateHintLines(Creature controlled)
    {
        _session!.Creatures.CollectEligibleMatesInRange(controlled, _mateScratch);
        if (_mateScratch.Count == 0) return;

        Vector2 from = CreatureLineAnchor(controlled);
        foreach (var mate in _mateScratch)
        {
            DrawAnimatedDashedLine(
                from,
                CreatureLineAnchor(mate),
                ParseRgb(MateHintRgb, 0.92f),
                controlled.Id * 41 + mate.Id * 11,
                "Mate");
        }
    }

    private void DrawTargetLine(Creature focus)
    {
        if (focus.State is "rest" or "wander") return;

        Vector2 from = CreatureLineAnchor(focus);
        Vector2 to = new((float)focus.Tx, (float)focus.Ty);
        if (focus.Target != null)
        {
            var target = _session!.Creatures.GetById(focus.Target.Value);
            if (target != null && !target.Dead)
            {
                to = CreatureLineAnchor(target);
            }
        }

        if (from.DistanceTo(to) < 0.15f) return;

        DrawAnimatedDashedLine(
            from,
            to,
            ParseRgb(TargetLineColor(focus.State), 0.98f),
            focus.Id * 31 + 7,
            TargetLineLabel(focus));
    }

    private string TargetLineLabel(Creature focus)
    {
        if (_session!.Player.Controls(focus))
        {
            string? order = _session.Player.OrderDescription();
            if (!string.IsNullOrEmpty(order))
            {
                return ShortOrderLabel(order);
            }
        }

        return focus.State switch
        {
            "hunt" or "huntSearch" => "Hunt",
            "flee" => "Flee",
            "mate" => "Mate",
            "thirst" => "Water",
            "graze" => "Food",
            _ => CreatureBehaviorLabels.GetDisplayLabel(focus, _session.State),
        };
    }

    private static string ShortOrderLabel(string order) => order switch
    {
        "Moving" => "Move",
        "Heading to water" or "Drinking" => "Water",
        "Grazing" or "Heading to graze" => "Food",
        "Courting" => "Mate",
        var s when s.StartsWith("Hunting", StringComparison.Ordinal) => "Hunt",
        var s when s.StartsWith("Fleeing", StringComparison.Ordinal) => "Flee",
        _ => order,
    };

    private void DrawAnimatedPedigreeLine(Vector2 from, Vector2 to, string rgb, int phaseSeed, string label)
    {
        DrawAnimatedDashedLine(from, to, ParseRgb(rgb, 0.88f), phaseSeed, label);
    }

    private void DrawAnimatedDashedLine(Vector2 from, Vector2 to, Color color, int phaseSeed, string? label = null)
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

        if (!string.IsNullOrEmpty(label))
        {
            DrawLineLabel(from, to, label, color, onLine: true);
        }
    }

    private Vector2 CreatureLineAnchor(Creature c)
    {
        var def = _session!.Species.Get(c.Sp);
        int detailTier = PerfProfiler.Instance.DetailTier;
        CreatureSpriteCatalog.TryGetSpeciesSprite(c.Sp, out var spriteDef);
        return CreatureDrawUtil.GetVisualCenter(c, _session.Creatures, _camZoom, detailTier, def, spriteDef);
    }

    private void DrawLineLabel(Vector2 from, Vector2 to, string label, Color lineColor, bool onLine)
    {
        Vector2 seg = to - from;
        float len = seg.Length();
        if (len < MinLabelLineTiles) return;

        const int refFontSize = EcoSimFonts.Scaled6;
        // Scale canvas so refFontSize text renders at LabelFontScreenPx on screen.
        float textScale = LabelFontScreenPx / (refFontSize * PixelsPerTile);
        float screenPx = refFontSize / LabelFontScreenPx;

        Vector2 dir = seg / len;
        Vector2 anchor = onLine
            ? from + dir * (len * LabelAlongLineT)
            : (from + to) * 0.5f + new Vector2(-dir.Y, dir.X) * (LabelOffsetScreenPx / PixelsPerTile);

        var font = EcoSimFonts.GetFont();
        Vector2 textSize = font.GetStringSize(label, HorizontalAlignment.Left, -1, refFontSize);
        float padX = 4f * screenPx;
        float padY = 3f * screenPx;
        float accentW = 2.5f * screenPx;
        Vector2 boxSize = textSize + new Vector2(padX * 2f + accentW, padY * 2f);

        DrawSetTransformMatrix(new Transform2D(textScale, 0, 0, textScale, anchor.X, anchor.Y));

        var bg = new Rect2(-boxSize * 0.5f, boxSize);
        DrawStyleBox(UiSliceCatalog.MakeStonePanel(), bg);

        DrawRect(new Rect2(bg.Position.X, bg.Position.Y, accentW, bg.Size.Y), lineColor with { A = 0.9f });

        float textX = bg.Position.X + accentW + padX + (boxSize.X - accentW - padX * 2f - textSize.X) * 0.5f;
        Vector2 textPos = new Vector2(textX, bg.Position.Y + padY + textSize.Y * 0.85f);
        DrawString(font, textPos, label, HorizontalAlignment.Left, -1, refFontSize, EcoSimThemeBuilder.Text);

        DrawSetTransformMatrix(Transform2D.Identity);
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
