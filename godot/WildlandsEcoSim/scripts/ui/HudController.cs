using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.Render;

namespace WildlandsEcoSim.UI;

public partial class HudController : CanvasLayer
{
    private EcoSimHost _host = null!;
    private GameApp _gameApp = null!;
    private WorldRenderer _world = null!;
    private WorldCamera _camera = null!;

    private GenPanel _gen = null!;
    private EcosystemPanel _ecosystem = null!;
    private InspectorPanel _inspector = null!;
    private WorldStoryTracker _story = null!;
    private Label _dayLabel = null!;
    private Label _popLabel = null!;
    private HSlider _speedSlider = null!;
    private Label _terrainTip = null!;
    private Label _creatureTip = null!;

    private DraggablePanel[] _panels = [];

    public override void _Ready()
    {
        _host = GetNode<EcoSimHost>("/root/EcoSimHost");
        _gameApp = GetNode<GameApp>("/root/GameApp");

        _gen = GetNode<GenPanel>("%GenPanel");
        _ecosystem = GetNode<EcosystemPanel>("%EcosystemPanel");
        _inspector = GetNode<InspectorPanel>("%InspectorPanel");
        _story = GetNode<WorldStoryTracker>("%WorldStory");
        _dayLabel = GetNode<Label>("%DayLabel");
        _popLabel = GetNode<Label>("%PopLabel");
        _speedSlider = GetNode<HSlider>("%SpeedSlider");
        _terrainTip = GetNode<Label>("%TerrainTip");
        _creatureTip = GetNode<Label>("%CreatureTip");

        var viewport = GetNode<SubViewport>("%WorldViewport");
        _world = viewport.GetNode<WorldRenderer>("WorldRoot");
        _camera = viewport.GetNode<WorldCamera>("WorldRoot/Camera2D");

        _panels = [_gen, _ecosystem, _inspector, GetNode<StoryPanel>("%StoryPanel")];

        GetNode<Button>("%GenerateButton").Pressed += OnGenerate;
        _speedSlider.ValueChanged += v =>
        {
            if (_host.Session != null && !_gameApp.Paused)
            {
                _host.Session.State.Speed = v;
            }
        };
        _ecosystem.SpeciesLocked += OnSpeciesLocked;
        _gameApp.SimTicked += OnSimTicked;

        _host.BootstrapIfNeeded();
        _ecosystem.Bind(_host.Species!);
        _inspector.Bind(_host.Species!);

        if (!Engine.IsEditorHint())
        {
            OnGenerate();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            PanelLayoutService.SaveAll(_panels);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Space)
        {
            _gameApp.TogglePause();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        UpdateTooltips();
    }

    private void OnGenerate()
    {
        var cfg = _gen.BuildConfig();
        uint seed = _gen.Seed;
        _host.GenerateWorld(cfg, seed);
        var session = _host.Session!;
        session.State.Speed = _speedSlider.Value;
        _gameApp.Paused = false;

        _world.BindWorld(session, _host.Species!);
        _camera.CenterOnWorld();
        _story.Reset();
        _story.LogGodAction($"Day 0: World generated ({cfg.Size}, seed {seed})");
        RefreshHud();
    }

    private void OnSpeciesLocked(string speciesKey)
    {
        _world.SetLockedSpecies(string.IsNullOrEmpty(speciesKey) ? null : speciesKey);
    }

    private void OnSimTicked()
    {
        var session = _host.Session;
        if (session == null) return;
        _story.OnSimTicked(session);
        RefreshHud();
    }

    private void RefreshHud()
    {
        var session = _host.Session;
        if (session == null) return;

        int alive = session.Creatures.AliveCount();
        _dayLabel.Text = $"Day {session.State.Day}";
        _popLabel.Text = $"Pop {alive}";
        _ecosystem.Refresh(session);
        _inspector.Refresh(session.State.Selected);
    }

    private void UpdateTooltips()
    {
        var session = _host.Session;
        if (session == null || !session.State.Ready)
        {
            _terrainTip.Visible = false;
            _creatureTip.Visible = false;
            return;
        }

        Vector2 mouse = _world.GetViewport().GetMousePosition();
        Vector2 worldPos = _camera.GetCanvasTransform().AffineInverse() * mouse;
        Vector2 tile = _world.WorldToTile(worldPos);
        int tx = (int)Math.Floor(tile.X);
        int ty = (int)Math.Floor(tile.Y);

        if (tx >= 0 && ty >= 0 && tx < session.State.W && ty < session.State.H)
        {
            int i = ty * session.State.W + tx;
            byte biome = session.State.Biome[i];
            _terrainTip.Text = $"Biome {biome}  veg {(session.State.Veg[i] / Math.Max(0.001f, session.State.VegCap[i]) * 100):F0}%";
            _terrainTip.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, 16);
            _terrainTip.Visible = true;
        }
        else
        {
            _terrainTip.Visible = false;
        }

        var sel = session.State.Selected;
        if (sel != null && !sel.Dead)
        {
            _creatureTip.Text = $"{sel.Sp} · {sel.State}";
            _creatureTip.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, -28);
            _creatureTip.Visible = true;
        }
        else
        {
            _creatureTip.Visible = false;
        }
    }
}
