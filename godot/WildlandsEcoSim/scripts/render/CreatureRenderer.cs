using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim.Render;

public partial class CreatureRenderer : Node2D
{
    private enum CreatureLod
    {
        MapCircle,
        Body,
        Sprite,
    }

    // Thresholds are in web-style effective zoom (camera zoom × tile pixels).
    private const float MapCircleEnterZoom = 3.3f;
    private const float MapCircleExitZoom = 3.6f;
    private const float SpriteEnterZoom = 4.45f;
    private const float SpriteExitZoom = MapCircleExitZoom;

    private static float EffectiveLodZoom(float camZoom) =>
        camZoom * WorldRenderer.TilePixels;

    private SimSession? _session;
    private SpeciesCatalog? _catalog;
    private string? _lockedSpecies;
    private float _camZoom = 1f;
    private CreatureLod _creatureLod = CreatureLod.Body;
    private readonly HashSet<long> _matingPairKeys = [];

    public override void _Ready()
    {
        ZIndex = 2;
        CreatureSpriteCatalog.EnsureLoaded();
    }

    public void Bind(SimSession session, SpeciesCatalog catalog)
    {
        _session = session;
        _catalog = catalog;
    }

    public void SetLockedSpecies(string? speciesKey) => _lockedSpecies = speciesKey;

    public void Invalidate() => QueueRedraw();

    public void SetCameraZoom(float zoom)
    {
        _camZoom = Math.Max(0.25f, zoom);
        UpdateCreatureLod(_camZoom);
    }

    public override void _Draw()
    {
        if (_session == null || _catalog == null || !_session.State.Ready) return;

        PerfProfiler.Instance.Timed("render", () =>
        PerfProfiler.Instance.Timed("render.creatures", () =>
        {
        var profiler = PerfProfiler.Instance;
        int detail = profiler.DetailTier;
        UpdateCreatureLod(_camZoom);

        var selected = _session.State.Selected;
        double light = _session.State.LightLevel;
        var creatures = _session.Creatures;
        double animTime = Time.GetTicksMsec() * 0.001;

        int drawn = 0;
        foreach (var c in _session.State.Creatures)
        {
            if (c.Dead) continue;
            bool forceDraw = selected?.Id == c.Id;
            if (!forceDraw && !IsVisible(c)) continue;

            var def = _catalog.Get(c.Sp);
            var rgb = CreatureDrawUtil.CreatureColor(def, c.Genome);
            var dk = rgb.Darkened(0.4f);
            float bright = CreatureDrawUtil.CreatureBrightness(c, selected, light);
            Vector2 pos = CreatureDrawUtil.DisplayPos(c);
            float eSize = CreatureDrawUtil.EffectiveSize(creatures, c);
            float baseRadius = 0.35f + (float)c.Genome.Size * 0.12f;

            if (_creatureLod == CreatureLod.MapCircle)
            {
                float r = CreatureDrawUtil.MapCircleRadiusTiles(_camZoom, (float)c.Genome.Size, eSize);
                var mapColor = CreatureDrawUtil.SpeciesMapColor(c.Sp, def);
                float mapBright = CreatureDrawUtil.MapMarkerBrightness(light);
                CreatureDrawUtil.DrawMapCircle(this, pos, r, mapColor, mapBright, _camZoom);

                drawn++;
                continue;
            }

            float s = baseRadius;
            if (_creatureLod == CreatureLod.Sprite)
            {
                s = Math.Max(baseRadius, _camZoom * 0.22f * eSize);
            }
            else if (EffectiveLodZoom(_camZoom) >= SpriteExitZoom)
            {
                // Keep body rects readable if we briefly leave sprite mode at high zoom.
                s = Math.Max(baseRadius, _camZoom * 0.18f * eSize);
            }

            if (_creatureLod == CreatureLod.Sprite)
            {
                bool moving = Math.Sqrt(c.Vx * c.Vx + c.Vy * c.Vy) > 0.02;
                bool juvenile = !creatures.IsAdult(c);
                if (CreatureSpriteCatalog.TryGetSpeciesSprite(c.Sp, out var spriteDef))
                {
                    CreatureDrawUtil.DrawTexturedSprite(this, spriteDef.Texture, pos, s, c.Dir, moving, c.Walk,
                        bright, spriteDef.Anchor, spriteDef.Scale, spriteDef.ContentRegion);
                    if (juvenile)
                    {
                        CreatureDrawUtil.DrawJuvenileCap(this, pos, s, c.Dir, moving, c.Walk, bright);
                    }
                }
                else
                {
                    CreatureDrawUtil.DrawSprite(this, pos, s, c.Dir, def.Shape, rgb, dk, moving, c.Walk,
                        juvenile, bright);
                }
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

        DrawMatingPairHearts(animTime, detail, creatures);

        if (drawn > 0)
        {
            PerfProfiler.Instance.RecordGpuDraw(drawn);
        }
        }));
    }

    private void UpdateCreatureLod(float camZoom)
    {
        float lodZoom = EffectiveLodZoom(camZoom);

        switch (_creatureLod)
        {
            case CreatureLod.MapCircle:
                if (lodZoom >= MapCircleExitZoom)
                {
                    _creatureLod = CreatureLod.Body;
                }

                break;
            case CreatureLod.Body:
                if (lodZoom < MapCircleEnterZoom)
                {
                    _creatureLod = CreatureLod.MapCircle;
                }
                else if (lodZoom >= SpriteEnterZoom)
                {
                    _creatureLod = CreatureLod.Sprite;
                }

                break;
            case CreatureLod.Sprite:
                if (lodZoom < SpriteExitZoom)
                {
                    _creatureLod = CreatureLod.Body;
                }

                break;
        }
    }

    private void DrawMatingPairHearts(double animTime, int detail, CreatureSystem creatures)
    {
        if (_session == null || _catalog == null || _camZoom < 3.5f) return;

        _matingPairKeys.Clear();
        foreach (var c in _session.State.Creatures)
        {
            if (c.Dead || c.State != "mate" || c.Target is not { } mateId) continue;

            var mate = creatures.GetById(mateId);
            if (mate is not { Dead: false, State: "mate" }) continue;

            int aId = Math.Min(c.Id, mateId);
            int bId = Math.Max(c.Id, mateId);
            long pairKey = ((long)aId << 32) | (uint)bId;
            if (!_matingPairKeys.Add(pairKey)) continue;

            var creatureA = aId == c.Id ? c : mate;
            var creatureB = bId == mateId ? mate : c;

            var defA = _catalog.Get(creatureA.Sp);
            var defB = _catalog.Get(creatureB.Sp);
            CreatureSpriteCatalog.TryGetSpeciesSprite(creatureA.Sp, out var spriteA);
            CreatureSpriteCatalog.TryGetSpeciesSprite(creatureB.Sp, out var spriteB);

            Vector2 anchorA = CreatureDrawUtil.GetVisualCenter(
                creatureA, creatures, _camZoom, detail, defA, spriteA);
            Vector2 anchorB = CreatureDrawUtil.GetVisualCenter(
                creatureB, creatures, _camZoom, detail, defB, spriteB);
            Vector2 mid = (anchorA + anchorB) * 0.5f;

            CreatureDrawUtil.DrawMatingHeartFx(this, mid, aId ^ bId, animTime, _camZoom);
        }
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
