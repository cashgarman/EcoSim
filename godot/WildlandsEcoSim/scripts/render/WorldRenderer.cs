using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim.Render;

public partial class WorldRenderer : Node2D
{
    public const float TilePixels = 4f;

    private ColorRect _oceanBg = null!;
    private Sprite2D _terrain = null!;
    private Sprite2D _veg = null!;
    private Sprite2D _water = null!;
    private CreatureRenderer _creatures = null!;
    private CanvasModulate _dayNight = null!;
    private SimSession? _session;
    private Func<string>? _activeTool;
    private Action<double, double>? _toolApply;
    private int _vegRefreshCounter;
    private double _waterAnim;

    public override void _Ready()
    {
        _oceanBg = new ColorRect
        {
            Color = EcoSimThemeBuilder.PageBg,
            Size = new Vector2(10000, 10000),
            Position = new Vector2(-5000, -5000),
        };
        AddChild(_oceanBg);

        _dayNight = new CanvasModulate();
        var layer = new Node2D();
        AddChild(_dayNight);
        _dayNight.AddChild(layer);

        _terrain = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _veg = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _water = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _creatures = new CreatureRenderer();

        layer.AddChild(_terrain);
        layer.AddChild(_veg);
        layer.AddChild(_water);
        layer.AddChild(_creatures);

        var gameApp = GetNode<GameApp>("/root/GameApp");
        gameApp.SimTicked += OnSimTicked;
    }

    public override void _Process(double delta)
    {
        _waterAnim += delta * 2.5;
        if (_session != null && _session.State.Ready)
        {
            RefreshWater();
        }
    }

    public void BindInput(Func<string> activeTool, Action<double, double> toolApply)
    {
        _activeTool = activeTool;
        _toolApply = toolApply;
    }

    public override void _Input(InputEvent @event)
    {
        var camera = GetNode<WorldCamera>("Camera2D");
        if (camera.HandleWorldInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (_session == null) return;

        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 worldPos = camera.GetCanvasTransform().AffineInverse() * mouse;
        Vector2 tilePos = WorldToTile(worldPos);

        string tool = _activeTool?.Invoke() ?? "inspect";
        if (tool != "inspect")
        {
            _toolApply?.Invoke(tilePos.X, tilePos.Y);
            GetViewport().SetInputAsHandled();
            return;
        }

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
        GetViewport().SetInputAsHandled();
    }

    public void BindWorld(SimSession session, SpeciesCatalog catalog)
    {
        _session = session;
        Scale = new Vector2(TilePixels, TilePixels);

        var terrainImg = TerrainBaker.BakeTerrainImage(session.State);
        _terrain.Texture = ImageTexture.CreateFromImage(terrainImg);
        _terrain.Scale = new Vector2(1f / TerrainBaker.Tx, 1f / TerrainBaker.Tx);

        var vegImg = TerrainBaker.BakeVegImage(session.State);
        _veg.Texture = ImageTexture.CreateFromImage(vegImg);

        RefreshWater(force: true);
        _creatures.Bind(session, catalog);
        _creatures.Refresh();
        UpdateDayNight();
    }

    public void SetLockedSpecies(string? speciesKey)
    {
        _creatures.SetLockedSpecies(speciesKey);
        _creatures.Refresh();
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

    private void RefreshWater(bool force = false)
    {
        if (_session == null) return;
        if (!force && (int)(_waterAnim * 4) % 4 != 0) return;
        var waterImg = TerrainBaker.BakeWaterImage(_session.State, _waterAnim);
        _water.Texture = ImageTexture.CreateFromImage(waterImg);
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
