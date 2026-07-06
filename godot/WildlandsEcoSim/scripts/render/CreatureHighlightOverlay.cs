using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim.Render;

public partial class CreatureHighlightOverlay : Node2D
{
    private SimSession? _session;
    private string? _lockedSpecies;
    private string? _hoveredSpecies;
    private float _camZoom = 1f;

    public override void _Ready()
    {
        ZIndex = 3;
    }

    public void Bind(SimSession session) => _session = session;

    public void SetSpeciesFocus(string? locked, string? hovered)
    {
        _lockedSpecies = locked;
        _hoveredSpecies = hovered;
    }

    public void SetCameraZoom(float zoom) => _camZoom = Math.Max(0.25f, zoom);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_session == null || !_session.State.Ready) return;

        int highlightTier = PerfProfiler.Instance.EffectiveHighlight(
            _lockedSpecies,
            _hoveredSpecies,
            _session.State.Selected is { Dead: false });
        if (highlightTier <= 0) return;

        var selected = _session.State.Selected;
        double now = Time.GetTicksMsec() * 0.001;

        foreach (var c in _session.State.Creatures)
        {
            if (c.Dead) continue;
            Vector2 pos = CreatureDrawUtil.DisplayPos(c);
            float size = 0.35f + (float)c.Genome.Size * 0.12f;

            bool isLocked = c.Sp == _lockedSpecies;
            bool isHovered = c.Sp == _hoveredSpecies;
            if ((isLocked || isHovered) && selected?.Id != c.Id)
            {
                string rgb = isLocked ? "242,181,62" : "87,184,232";
                DrawGlow(pos, size, strong: false, now, c.Id + 13, rgb, highlightTier);
            }

            if (selected?.Id == c.Id)
            {
                DrawGlow(pos, size, strong: true, now, c.Id + 37, "242,181,62", highlightTier);
            }
        }
    }

    private void DrawGlow(Vector2 pos, float size, bool strong, double nowSec, int phaseSeed, string rgb, int tier)
    {
        float pulse = 0.68f + 0.32f * (float)Math.Sin(nowSec * 4.8 + phaseSeed * 0.4);
        float spin = (float)(nowSec * (strong ? 2.8 : 2.1) + phaseSeed * 0.3);
        float radius = Math.Max(0.4f, size * (strong ? 1.28f : 1.18f)) * (0.96f + pulse * 0.08f);
        Color ring = ParseRgb(rgb, strong ? 0.72f : 0.52f);

        if (tier >= 2)
        {
            DrawArc(pos, radius, 0, Mathf.Tau, 48, ring, Math.Max(0.04f, strong ? 0.12f : 0.09f));
            float arcLen = Mathf.Pi * 0.82f;
            Color arcCol = ParseRgb(rgb, strong ? 0.95f : 0.74f);
            DrawArc(pos, radius * 1.26f, spin, spin + arcLen, 24, arcCol, Math.Max(0.03f, strong ? 0.1f : 0.08f));
            DrawArc(pos, radius * 1.26f, spin + Mathf.Pi, spin + Mathf.Pi + arcLen, 24, arcCol,
                Math.Max(0.03f, strong ? 0.1f : 0.08f));
        }
        else
        {
            DrawArc(pos, radius, 0, Mathf.Tau, 32, ring, Math.Max(0.03f, 0.06f));
        }
    }

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
