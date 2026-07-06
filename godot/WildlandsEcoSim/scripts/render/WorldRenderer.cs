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
    private CreaturePedigreeOverlay _pedigree = null!;
    private CreatureRenderer _creatures = null!;
    private CreatureHighlightOverlay _highlights = null!;
    private ToolFxOverlay _toolFx = null!;
    private CanvasModulate _dayNight = null!;
    private SimSession? _session;
    private Func<string>? _activeTool;
    private Action<double, double>? _toolApply;
    private bool _painting;
    private int _vegRefreshCounter;
    private double _waterAnim;
    private string? _lockedSpecies;
    private string? _hoveredSpecies;
    private double _lastClickTime;
    private Vector2 _lastClickTile;

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
        var terrainLayer = new Node2D();
        AddChild(_dayNight);
        _dayNight.AddChild(terrainLayer);

        _terrain = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _veg = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _water = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        terrainLayer.AddChild(_terrain);
        terrainLayer.AddChild(_veg);
        terrainLayer.AddChild(_water);

        _pedigree = new CreaturePedigreeOverlay();
        _creatures = new CreatureRenderer();
        _highlights = new CreatureHighlightOverlay();
        _toolFx = new ToolFxOverlay();
        AddChild(_pedigree);
        AddChild(_creatures);
        AddChild(_highlights);
        AddChild(_toolFx);

        var gameApp = GetNode<GameApp>("/root/GameApp");
        gameApp.SimTicked += OnSimTicked;
    }

    public override void _Process(double delta)
    {
        _waterAnim += delta * 2.5;
        if (_session != null && _session.State.Ready)
        {
            RefreshWater();
            UpdateRenderContext();
            UpdateToolFx();
            if (_painting && Input.IsMouseButtonPressed(MouseButton.Left))
            {
                ApplyToolAtMouse();
            }
        }
    }

    private void UpdateToolFx()
    {
        string tool = _activeTool?.Invoke() ?? "inspect";
        var camera = GetNode<WorldCamera>("Camera2D");
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 worldPos = camera.GetCanvasTransform().AffineInverse() * mouse;
        Vector2 tilePos = WorldToTile(worldPos);
        bool show = GetViewport().GetMousePosition() != Vector2.Zero;
        _toolFx.SetTool(tool, show, tilePos);
    }

    private void ApplyToolAtMouse()
    {
        var camera = GetNode<WorldCamera>("Camera2D");
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 worldPos = camera.GetCanvasTransform().AffineInverse() * mouse;
        Vector2 tilePos = WorldToTile(worldPos);
        _toolApply?.Invoke(tilePos.X, tilePos.Y);
    }

    private void UpdateRenderContext()
    {
        var camera = GetNode<WorldCamera>("Camera2D");
        float zoom = camera.Zoom.X;
        _creatures.SetCameraZoom(zoom);
        _highlights.SetCameraZoom(zoom);
        _pedigree.SetCameraZoom(zoom);
        _highlights.SetSpeciesFocus(_lockedSpecies, _hoveredSpecies);
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

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (_session == null) return;

            Vector2 mouse = GetViewport().GetMousePosition();
            Vector2 worldPos = camera.GetCanvasTransform().AffineInverse() * mouse;
            Vector2 tilePos = WorldToTile(worldPos);
            string tool = _activeTool?.Invoke() ?? "inspect";

            if (mb.Pressed)
            {
                double now = Time.GetTicksMsec() * 0.001;
                bool dblClick = now - _lastClickTime < 0.35
                    && tilePos.DistanceTo(_lastClickTile) < 0.5;
                _lastClickTime = now;
                _lastClickTile = tilePos;

                if (dblClick && tool == "inspect")
                {
                    Creature? best = PickCreature(tilePos);
                    if (best != null)
                    {
                        _session.State.Selected = best;
                        camera.FollowEnabled = true;
                        float minZoom = 3f;
                        if (camera.Zoom.X < minZoom)
                        {
                            camera.Zoom = new Vector2(minZoom, minZoom);
                        }
                    }

                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (tool != "inspect")
                {
                    _painting = true;
                    _toolApply?.Invoke(tilePos.X, tilePos.Y);
                    GetViewport().SetInputAsHandled();
                    return;
                }

                _session.State.Selected = PickCreature(tilePos);
                GetViewport().SetInputAsHandled();
            }
            else
            {
                _painting = false;
            }

            return;
        }
    }

    private Creature? PickCreature(Vector2 tilePos)
    {
        if (_session == null) return null;
        Creature? best = null;
        double bestDist = 2.5;
        foreach (var c in _session.State.Creatures)
        {
            if (c.Dead) continue;
            double dx = c.Rx - tilePos.X;
            double dy = c.Ry - tilePos.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        return best;
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
        _pedigree.Bind(session);
        _highlights.Bind(session);
        session.Creatures.SnapAllDisplayPositions();
        UpdateDayNight();
        UpdateRenderContext();
    }

    public void SetLockedSpecies(string? speciesKey)
    {
        _lockedSpecies = speciesKey;
        _creatures.SetLockedSpecies(speciesKey);
        UpdateRenderContext();
    }

    public void SetHoveredSpecies(string? speciesKey)
    {
        _hoveredSpecies = speciesKey;
        UpdateRenderContext();
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
        RefreshVegIfDirty();
        UpdateDayNight();
        UpdateRenderContext();
    }

    private void UpdateDayNight()
    {
        if (_session == null) return;
        float light = (float)_session.State.LightLevel;
        _dayNight.Color = new Color(light, light, light * 0.95f);
    }

    public Vector2 WorldToTile(Vector2 worldPos) => worldPos / TilePixels;
}
