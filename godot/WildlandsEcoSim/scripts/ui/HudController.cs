using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
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
    private SpeciesStatsPanel _speciesStats = null!;
    private WorldStoryTracker _story = null!;
    private ProfilerPanel _profiler = null!;
    private ToolsController _tools = null!;
    private SpeciesGodMenu _godMenu = null!;
    private TimelineStrip _timeline = null!;

    private Label _dayIcon = null!;
    private Label _clockLabel = null!;
    private Label _dayLabel = null!;
    private Label _popLabel = null!;
    private Label _genLabel = null!;
    private Label _vegLabel = null!;
    private Label _simFpsLabel = null!;
    private Label _speedValueLabel = null!;
    private HSlider _speedSlider = null!;
    private Label _terrainTip = null!;
    private Label _creatureTip = null!;

    private TimelineDb? _timelineDb;
    private TimeScrubController? _scrub;
    private DraggablePanel[] _panels = [];

    public override void _Ready()
    {
        var uiTheme = EcoSimThemeBuilder.Build();

        _host = GetNode<EcoSimHost>("/root/EcoSimHost");
        _gameApp = GetNode<GameApp>("/root/GameApp");

        _gen = GetNode<GenPanel>("%GenPanel");
        _gen.Theme = uiTheme;
        _ecosystem = GetNode<EcosystemPanel>("%EcosystemPanel");
        _ecosystem.Theme = uiTheme;
        _inspector = GetNode<InspectorPanel>("%InspectorPanel");
        _inspector.Theme = uiTheme;
        _speciesStats = GetNode<SpeciesStatsPanel>("%SpeciesStatsPanel");
        _speciesStats.Theme = uiTheme;
        _story = GetNode<WorldStoryTracker>("%WorldStory");
        _profiler = GetNode<ProfilerPanel>("%ProfilerPanel");
        _profiler.Theme = uiTheme;
        _tools = GetNode<ToolsController>("%Toolbar");
        _tools.Theme = uiTheme;
        _godMenu = GetNode<SpeciesGodMenu>("%SpeciesGodMenu");
        _godMenu.Theme = uiTheme;
        _timeline = GetNode<TimelineStrip>("%TimelineStrip");
        GetNode<PanelContainer>("TopBar").Theme = uiTheme;
        GetNode<StoryPanel>("%StoryPanel").Theme = uiTheme;

        _dayIcon = GetNode<Label>("%DayIcon");
        _clockLabel = GetNode<Label>("%ClockLabel");
        _dayLabel = GetNode<Label>("%DayNumLabel");
        _popLabel = GetNode<Label>("%PopLabel");
        _genLabel = GetNode<Label>("%GenLabel");
        _vegLabel = GetNode<Label>("%VegLabel");
        _simFpsLabel = GetNode<Label>("%SimFpsLabel");
        _speedValueLabel = GetNode<Label>("%SpeedValueLabel");
        _speedSlider = GetNode<HSlider>("%SpeedSlider");
        _terrainTip = GetNode<Label>("%TerrainTip");
        _creatureTip = GetNode<Label>("%CreatureTip");

        var viewport = GetNode<SubViewport>("%WorldViewport");
        viewport.TransparentBg = true;
        _world = viewport.GetNode<WorldRenderer>("WorldRoot");
        _camera = viewport.GetNode<WorldCamera>("WorldRoot/Camera2D");

        _panels = [_gen, _ecosystem, _inspector, GetNode<StoryPanel>("%StoryPanel"), _speciesStats, _profiler];

        _gen.GenerateRequested += OnGenerate;
        _gen.RestockRequested += OnRestock;
        _speedSlider.ValueChanged += OnSpeedChanged;
        _ecosystem.SpeciesLocked += OnSpeciesLocked;
        _ecosystem.SpeciesGodMenu += OnSpeciesGodMenu;
        _godMenu.KillAllRequested += OnKillAll;
        _gameApp.SimTicked += OnSimTicked;
        _timeline.SeekRequested += OnTimelineSeek;
        _timeline.PresentRequested += OnTimelinePresent;

        GetNode<Button>("%FollowBtn").Pressed += () => _camera.FollowEnabled = !_camera.FollowEnabled;
        GetNode<Button>("%ProfilerBtn").Pressed += () => _profiler.PanelOpen = !_profiler.PanelOpen;
        GetNode<Button>("%TestRunnerBtn").Disabled = true;
        GetNode<Button>("%TestRunnerBtn").TooltipText = "Coming in Phase 6";
        GetNode<Button>("%PresentBtn").Pressed += OnTimelinePresent;

        _world.BindInput(() => _tools.ActiveTool, OnToolApply);

        _host.BootstrapIfNeeded();
        _ecosystem.Bind(_host.Species!);
        _inspector.Bind(_host.Species!);
        _speciesStats.Bind(_host.Species!);
        _tools.Bind(_host.Species!);

        if (!Engine.IsEditorHint())
        {
            InitTimeline();
            OnGenerate();
        }
    }

    private void InitTimeline()
    {
        string dbPath = ProjectSettings.GlobalizePath("user://timeline.db");
        _timelineDb = new TimelineDb();
        _timelineDb.Open(dbPath);
        string runId = $"run-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _timelineDb.BeginRun(runId);

        var session = _host.EnsureSession();
        _scrub = new TimeScrubController(session, _timelineDb);
        _gameApp.SetScrubController(_scrub);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            PanelLayoutService.SaveAll(_panels);
            _timelineDb?.Dispose();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Space)
            {
                _gameApp.TogglePause();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.F2)
            {
                _profiler.PanelOpen = !_profiler.PanelOpen;
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.F)
            {
                _camera.FollowEnabled = !_camera.FollowEnabled;
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Process(double delta)
    {
        UpdateTooltips();
        _profiler.Refresh();
        RefreshTimelineStrip();
    }

    private void OnGenerate()
    {
        var cfg = _gen.BuildConfig();
        uint seed = _gen.Seed;
        _host.GenerateWorld(cfg, seed);
        var session = _host.Session!;
        session.State.Speed = _speedSlider.Value;
        _gameApp.Paused = false;

        _scrub?.ResetBaseline();
        _timelineDb?.TruncateFuture(0);
        string runId = $"run-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _timelineDb?.BeginRun(runId);
        var initialSnap = SnapshotService.Capture(session.State);
        _timelineDb?.SaveSnapshot(initialSnap, TimeScrubController.DefaultSnapshotIntervalSec);

        _world.BindWorld(session, _host.Species!);
        _camera.CenterOnWorld();
        _story.Reset();
        _story.LogGodAction($"Day 0: World generated ({cfg.Size}, seed {seed})");
        RefreshHud();
    }

    private void OnRestock()
    {
        var session = _host.Session;
        if (session == null) return;
        _scrub?.OnMutatingAction();
        session.Creatures.StockLife();
        _story.LogGodAction($"Day {session.State.Day}: Restocked life");
        RefreshHud();
    }

    private void OnSpeedChanged(double v)
    {
        if (_host.Session != null && !_gameApp.Paused)
        {
            _host.Session.State.Speed = v;
            _speedValueLabel.Text = $"{v:0}×";
        }
    }

    private void OnSpeciesLocked(string speciesKey)
    {
        _world.SetLockedSpecies(string.IsNullOrEmpty(speciesKey) ? null : speciesKey);
        _speciesStats.ShowSpecies(
            string.IsNullOrEmpty(speciesKey) ? null : speciesKey,
            _host.Session!,
            _host.Session!.SpeciesStats);
    }

    private void OnSpeciesGodMenu(string speciesKey, Vector2 globalPos)
    {
        _godMenu.OpenFor(speciesKey, _host.Species!, globalPos);
    }

    private void OnKillAll(string speciesKey)
    {
        var session = _host.Session;
        if (session == null) return;
        _scrub?.OnMutatingAction();
        int n = session.Creatures.KillAllBySpecies(speciesKey);
        var def = _host.Species!.Get(speciesKey);
        _story.LogGodAction($"Day {session.State.Day}: Killed all {def.Label} ({n})");
        RefreshHud();
    }

    private void OnToolApply(double wx, double wy)
    {
        var session = _host.Session;
        if (session == null) return;
        _scrub?.OnMutatingAction();
        _tools.ApplyAt(session, wx, wy);
        _world.BindWorld(session, _host.Species!);
        RefreshHud();
    }

    private void OnTimelineSeek(double targetT)
    {
        _scrub?.SeekTo(targetT);
        var session = _host.Session;
        if (session == null) return;
        _world.BindWorld(session, _host.Species!);
        RefreshHud();
    }

    private void OnTimelinePresent()
    {
        _scrub?.GoToPresent();
        var session = _host.Session;
        if (session == null) return;
        _world.BindWorld(session, _host.Species!);
        RefreshHud();
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

        var phase = SimMath.DayPhaseFromTimeOfDay(session.State.TimeOfDay);
        _dayIcon.Text = phase.Icon;
        _clockLabel.Text = SimMath.FormatTimeOfDay12(session.State.TimeOfDay);
        _dayLabel.Text = $"Day {session.State.Day}";
        _popLabel.Text = session.Creatures.AliveCount().ToString();

        int maxGen = 1;
        foreach (var c in session.State.Creatures)
        {
            if (!c.Dead && c.Gen > maxGen) maxGen = c.Gen;
        }
        _genLabel.Text = $"Gen {maxGen}";
        _vegLabel.Text = $"{ComputeVegPercent(session.State):F0}%";

        double fps = PerfProfiler.Instance.FrameMsAvg > 0 ? 1000.0 / PerfProfiler.Instance.FrameMsAvg : 0;
        _simFpsLabel.Text = $"{fps:F0}";

        _speedValueLabel.Text = $"{session.State.Speed:0}×";
        _ecosystem.Refresh(session);
        _inspector.Refresh(session.State.Selected);
        if (!string.IsNullOrEmpty(_ecosystem.LockedSpecies))
        {
            _speciesStats.ShowSpecies(_ecosystem.LockedSpecies, session, session.SpeciesStats);
        }
    }

    private void RefreshTimelineStrip()
    {
        var session = _host.Session;
        if (session == null || _scrub == null) return;
        _timeline.SetSnapshots(_scrub.SnapshotTimes(), session.State.TGlobal, _scrub.BaselineT, _gameApp.Paused);
    }

    private static double ComputeVegPercent(SimState state)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < state.Veg.Length; i++)
        {
            if (state.VegCap[i] <= 0.001f) continue;
            sum += state.Veg[i] / state.VegCap[i];
            n++;
        }
        return n > 0 ? sum / n * 100 : 0;
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
            var info = BiomeData.Info[(Biome)biome];
            _terrainTip.Text = $"{info.Name}  veg {(session.State.Veg[i] / Math.Max(0.001f, session.State.VegCap[i]) * 100):F0}%";
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
