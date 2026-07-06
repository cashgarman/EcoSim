using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.Render;

public partial class WorldRenderer : Node2D
{
    public const float TilePixels = 4f;

    private Sprite2D _terrain = null!;
    private Sprite2D _veg = null!;
    private CreatureRenderer _creatures = null!;
    private CanvasModulate _dayNight = null!;
    private SimSession? _session;
    private int _vegRefreshCounter;

    public override void _Ready()
    {
        _dayNight = new CanvasModulate();
        var layer = new Node2D();
        AddChild(_dayNight);
        _dayNight.AddChild(layer);

        _terrain = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _veg = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _creatures = new CreatureRenderer();

        layer.AddChild(_terrain);
        layer.AddChild(_veg);
        layer.AddChild(_creatures);

        var host = GetNode<EcoSimHost>("/root/EcoSimHost");
        var gameApp = GetNode<GameApp>("/root/GameApp");
        gameApp.SimTicked += OnSimTicked;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (_session == null) return;

        var camera = GetNode<Camera2D>("Camera2D");
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 worldPos = camera.GetCanvasTransform().AffineInverse() * mouse;
        Vector2 tilePos = WorldToTile(worldPos);

        Creature? best = null;
        double bestDist = 2.5;
        foreach (var c in _session.State.Creatures)
        {
            if (c.Dead) continue;
            double dx = c.X - tilePos.X;
            double dy = c.Y - tilePos.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        _session.State.Selected = best;
    }

    public void BindWorld(SimSession session, SpeciesCatalog catalog)
    {
        _session = session;
        Scale = new Vector2(TilePixels, TilePixels);

        var biomeImg = TerrainBaker.BakeBiomeImage(session.State);
        _terrain.Texture = ImageTexture.CreateFromImage(biomeImg);

        var vegImg = TerrainBaker.BakeVegImage(session.State);
        _veg.Texture = ImageTexture.CreateFromImage(vegImg);

        _creatures.Bind(session, catalog);
        _creatures.Refresh();
        UpdateDayNight();
    }

    public void RefreshVegIfDirty()
    {
        if (_session == null || !_session.State.VegDirty) return;
        _vegRefreshCounter++;
        if (_vegRefreshCounter < 10) return;
        _vegRefreshCounter = 0;
        _session.State.VegDirty = false;
        var vegImg = TerrainBaker.BakeVegImage(_session.State);
        _veg.Texture = ImageTexture.CreateFromImage(vegImg);
    }

    private void OnSimTicked()
    {
        _creatures.Refresh();
        RefreshVegIfDirty();
        UpdateDayNight();
    }

    private void UpdateDayNight()
    {
        if (_session == null) return;
        float light = (float)_session.State.LightLevel;
        _dayNight.Color = new Color(light, light, light * 0.95f);
    }

    public Vector2 WorldToTile(Vector2 worldPos)
    {
        return worldPos / TilePixels;
    }
}
