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

        PerfProfiler.Instance.Timed("render", () =>
        PerfProfiler.Instance.Timed("render.creatures", () =>
        {
        var profiler = PerfProfiler.Instance;
        int detail = profiler.DetailTier;
        var selected = _session.State.Selected;
        double light = _session.State.LightLevel;
        var creatures = _session.Creatures;

        int drawn = 0;
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
            float baseRadius = 0.35f + (float)c.Genome.Size * 0.12f;
            bool useMapCircles = _camZoom < 3.5f || detail <= 0;

            if (useMapCircles)
            {
                float r = CreatureDrawUtil.MapCircleRadiusTiles(_camZoom, (float)c.Genome.Size, eSize);
                var mapColor = CreatureDrawUtil.SpeciesMapColor(c.Sp, def);
                float mapBright = CreatureDrawUtil.MapMarkerBrightness(light);
                CreatureDrawUtil.DrawMapCircle(this, pos, r, mapColor, mapBright, _camZoom);
                continue;
            }

            float s = baseRadius;
            if (detail >= 2 && _camZoom > 4.2f)
            {
                s = Math.Max(baseRadius, _camZoom * 0.22f * eSize);
            }

            if (detail >= 2 && _camZoom > 4.2f)
            {
                bool moving = Math.Sqrt(c.Vx * c.Vx + c.Vy * c.Vy) > 0.02;
                CreatureDrawUtil.DrawSprite(this, pos, s, c.Dir, def.Shape, rgb, dk, moving, c.Walk,
                    !creatures.IsAdult(c), bright);
            }
            else
            {
                CreatureDrawUtil.DrawBodyRect(this, pos, s, rgb, bright);
            }

            if (_camZoom >= 3.5f && c.State != "wander")
            {
                CreatureDrawUtil.DrawStateIcon(this, pos, c.State, eSize, _camZoom, WorldRenderer.TilePixels, s);
            }

            drawn++;
        }

        if (drawn > 0)
        {
            PerfProfiler.Instance.RecordGpuDraw(drawn);
        }
        }));
    }

    private bool IsVisible(Creature c)
    {
        var camera = GetViewport()?.GetCamera2D();
        if (camera == null) return true;

        // Use camera position in tile space (same as creature coords), not global screen center.
        Vector2 center = camera.Position;
        Vector2 vp = camera.GetViewportRect().Size;
        float tileScale = (camera.GetParent() as Node2D)?.Scale.X ?? 1f;
        float hw = vp.X / (camera.Zoom.X * tileScale * 2f);
        float hh = vp.Y / (camera.Zoom.Y * tileScale * 2f);
        const float pad = 4f;
        float x = (float)c.Rx;
        float y = (float)c.Ry;
        return x >= center.X - hw - pad && x <= center.X + hw + pad
            && y >= center.Y - hh - pad && y <= center.Y + hh + pad;
    }
}
