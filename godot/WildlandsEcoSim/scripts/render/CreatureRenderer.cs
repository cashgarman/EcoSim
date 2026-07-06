using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim.Render;

public partial class CreatureRenderer : Node2D
{
    private SimSession? _session;
    private SpeciesCatalog? _catalog;
    private string? _lockedSpecies;
    private float _camZoom = 1f;
    private int _frameCounter;

    public override void _Ready()
    {
        ZIndex = 2;
    }

    public void Bind(SimSession session, SpeciesCatalog catalog)
    {
        _session = session;
        _catalog = catalog;
    }

    public void SetLockedSpecies(string? speciesKey) => _lockedSpecies = speciesKey;

    public void SetCameraZoom(float zoom) => _camZoom = Math.Max(0.25f, zoom);

    public override void _Process(double delta)
    {
        int decimation = PerfProfiler.Instance.RenderDecimation;
        if (decimation > 1)
        {
            _frameCounter++;
            if (_frameCounter % decimation != 0) return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_session == null || _catalog == null || !_session.State.Ready) return;

        var profiler = PerfProfiler.Instance;
        int detail = profiler.DetailTier;
        var selected = _session.State.Selected;
        double light = _session.State.LightLevel;
        var creatures = _session.Creatures;

        foreach (var c in _session.State.Creatures)
        {
            if (c.Dead) continue;
            if (!IsVisible(c)) continue;

            var def = _catalog.Get(c.Sp);
            var rgb = CreatureDrawUtil.CreatureColor(def, c.Genome);
            var dk = rgb.Darkened(0.4f);
            float bright = CreatureDrawUtil.CreatureBrightness(c, selected, light);
            Vector2 pos = CreatureDrawUtil.DisplayPos(c);
            float eSize = CreatureDrawUtil.EffectiveSize(creatures, c);
            float s = Math.Max(0.15f, _camZoom * 0.9f * eSize);

            if (detail <= 0 || _camZoom < 1.8f)
            {
                CreatureDrawUtil.DrawMarker(this, pos, s, rgb, bright);
            }
            else if (detail == 1 && _camZoom < 3.5f)
            {
                CreatureDrawUtil.DrawBodyRect(this, pos, s, rgb, bright);
            }
            else if (detail >= 2 && _camZoom > 4.2f)
            {
                bool moving = Math.Sqrt(c.Vx * c.Vx + c.Vy * c.Vy) > 0.02;
                CreatureDrawUtil.DrawSprite(this, pos, s, c.Dir, def.Shape, rgb, dk, moving, c.Walk,
                    !creatures.IsAdult(c), bright);
                if (_camZoom > 6)
                {
                    string? em = CreatureDrawUtil.StateEmoji(c.State);
                    if (em != null && c.State != "wander")
                    {
                        DrawString(ThemeDB.FallbackFont, pos + new Vector2(-s * 0.4f, -s * 0.9f), em,
                            HorizontalAlignment.Left, -1, Math.Max(8, (int)(s * 9)));
                    }
                }
            }
            else
            {
                CreatureDrawUtil.DrawBodyRect(this, pos, s, rgb, bright);
            }
        }
    }

    private bool IsVisible(Creature c)
    {
        var camera = GetViewport()?.GetCamera2D();
        if (camera == null) return true;

        Rect2 view = camera.GetViewportRect();
        Vector2 center = camera.GetScreenCenterPosition();
        float scale = GetParent<Node2D>()?.Scale.X ?? 1f;
        float pad = Math.Max(4f, _camZoom * 4f) / scale;
        float hw = view.Size.X / (camera.Zoom.X * scale * 2f) + pad;
        float hh = view.Size.Y / (camera.Zoom.Y * scale * 2f) + pad;
        float x = (float)c.Rx;
        float y = (float)c.Ry;
        return x >= center.X - hw && x <= center.X + hw && y >= center.Y - hh && y <= center.Y + hh;
    }
}
