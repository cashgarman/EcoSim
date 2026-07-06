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

    private TimelineDbPanel? _timelineDbPanel;
    private ProfilerDetailPanel? _profilerDetail;
    private TimelineDb? _timelineDb;
    private TimeScrubController? _scrub;
    private double _heartbeatIntervalSec = 5;
    private long _lastHeartbeatBucket = -1;
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

        EcoSimFonts.ApplyFont(_dayIcon, EcoSimFonts.DayIcon);
        EcoSimFonts.ApplyFont(_clockLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        EcoSimFonts.ApplyFont(_dayLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Dim);
        EcoSimFonts.ApplyFont(_popLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        EcoSimFonts.ApplyFont(_genLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        EcoSimFonts.ApplyFont(_vegLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        EcoSimFonts.ApplyFont(_simFpsLabel, EcoSimFonts.Small, EcoSimThemeBuilder.Dim);
        EcoSimFonts.ApplyFont(_speedValueLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        EcoSimFonts.ApplyFont(_terrainTip, EcoSimFonts.Medium, EcoSimThemeBuilder.Text, textShadow: true);
        EcoSimFonts.ApplyFont(_creatureTip, EcoSimFonts.Small, EcoSimThemeBuilder.Text, textShadow: true);
        EcoSimFonts.StylePanelTitle(GetNode<Label>("%GodMenuTitle"));

        var viewport = GetNode<SubViewport>("%WorldViewport");
        viewport.TransparentBg = true;
        _world = viewport.GetNode<WorldRenderer>("WorldRoot");
        _camera = viewport.GetNode<WorldCamera>("WorldRoot/Camera2D");

        _profilerDetail = new ProfilerDetailPanel();
        AddChild(_profilerDetail);
        _timelineDbPanel = new TimelineDbPanel();
        AddChild(_timelineDbPanel);

        _panels = [_gen, _ecosystem, _inspector, GetNode<StoryPanel>("%StoryPanel"), _speciesStats, _profiler, _timelineDbPanel];

        _gen.GenerateRequested += OnGenerate;
        _gen.RestockRequested += OnRestock;
        _speedSlider.ValueChanged += OnSpeedChanged;
        _ecosystem.SpeciesLocked += OnSpeciesLocked;
        _ecosystem.SpeciesHovered += OnSpeciesHovered;
        _ecosystem.SpeciesFollow += OnSpeciesFollow;
        _ecosystem.SpeciesGodMenu += OnSpeciesGodMenu;
        _godMenu.KillAllRequested += OnKillAll;
        _gameApp.SimTicked += OnSimTicked;
        _timeline.SeekRequested += OnTimelineSeek;
        _timeline.PresentRequested += OnTimelinePresent;

        GetNode<Button>("%FollowBtn").Pressed += () => _camera.FollowEnabled = !_camera.FollowEnabled;
        GetNode<Button>("%ProfilerBtn").Pressed += OnProfilerToggled;
        GetNode<Button>("%TestRunnerBtn").Disabled = false;
        GetNode<Button>("%TestRunnerBtn").TooltipText = "Open batch test runner";
        GetNode<Button>("%TestRunnerBtn").Pressed += () => GetTree().ChangeSceneToFile("res://scenes/BatchTest.tscn");
        GetNode<Button>("%PresentBtn").Pressed += OnTimelinePresent;

        var cpuGpuBtn = new Button { Text = "CPU/GPU" };
        cpuGpuBtn.Pressed += () =>
        {
            _profilerDetail.PanelOpen = !_profilerDetail.PanelOpen;
            PerfProfiler.Instance.DetailEnabled = _profilerDetail.PanelOpen;
            _profilerDetail.Refresh();
        };
        GetNode<HBoxContainer>("TopBar/VBox/Row1").AddChild(cpuGpuBtn);

        var gpuThrottle = new OptionButton();
        gpuThrottle.AddItem("GPU throttle: Off");
        gpuThrottle.AddItem("Light");
        gpuThrottle.AddItem("Medium");
        gpuThrottle.AddItem("Heavy");
        gpuThrottle.AddItem("Eco");
        gpuThrottle.ItemSelected += idx => WildlandsEcoSim.Gpu.GpuThrottle.Preset = (WildlandsEcoSim.Gpu.GpuThrottlePreset)idx;
        GetNode<HBoxContainer>("TopBar/VBox/Row1").AddChild(gpuThrottle);

        _world.BindInput(() => _tools.ActiveTool, OnToolApply);

        _host.BootstrapIfNeeded();
        _ecosystem.Bind(_host.Species!);
        _tools.Bind(_host.Species!);
        _inspector.Bind(_host.Species!);
        _speciesStats.Bind(_host.Species!);

        if (!Engine.IsEditorHint())
        {
            InitTimeline();
            OnGenerate();
        }

        GetViewport().SizeChanged += OnViewportSizeChanged;
    }

    private void OnViewportSizeChanged()
    {
        PanelLayoutService.ClampAll(_panels);
    }

    private void InitTimeline()
    {
        string dbPath = ProjectSettings.GlobalizePath("user://timeline.db");
        _timelineDb = new TimelineDb();
        _timelineDb.Open(dbPath);
        string runId = $"run-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _timelineDb.BeginRun(runId);

        var session = _host.EnsureSession();
        _story.Bind(_host.Species!, _timelineDb);
        _timelineDbPanel.Bind(_timelineDb);
        var timelineCfg = TimelineConfigLoader.Load(DataPaths.RepoRoot);
        _heartbeatIntervalSec = timelineCfg.HeartbeatIntervalSec;
        _scrub = new TimeScrubController(session, _timelineDb)
        {
            SnapshotIntervalSec = PerfPolicy.EffectiveSnapshotIntervalSec(timelineCfg.SnapshotIntervalSec),
        };
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
                OnProfilerToggled();
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
        _profilerDetail.Refresh();
        RefreshTimelineStrip();
    }

    private void OnGenerate()
    {
        var cfg = _gen.BuildConfig();
        uint seed = _gen.Seed;
        _host.GenerateWorld(cfg, seed);
        var session = _host.Session!;
        session.State.Speed = _speedSlider.Value;
        session.State.AutoMigrationEnabled = _gen.AutoMigrationEnabled;
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
        _story.LogGodAction($"Day 0: World generated ({cfg.Size}, seed {seed})", session);
        RefreshHud();
    }

    private void OnRestock()
    {
        var session = _host.Session;
        if (session == null) return;
        _scrub?.OnMutatingAction();
        session.Creatures.StockLife();
        _story.LogGodAction($"Day {session.State.Day}: Restocked life", session);
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

    private void OnSpeciesHovered(string speciesKey)
    {
        _world.SetHoveredSpecies(string.IsNullOrEmpty(speciesKey) ? null : speciesKey);
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
        _story.LogGodAction($"Day {session.State.Day}: Killed all {def.Label} ({n})", session);
        RefreshHud();
    }

    private void OnSpeciesFollow(string speciesKey)
    {
        var session = _host.Session;
        if (session == null || string.IsNullOrEmpty(speciesKey)) return;

        Creature? nearest = null;
        double best = double.MaxValue;
        Vector2 center = _camera.Position;
        foreach (var c in session.State.Creatures)
        {
            if (c.Dead || c.Sp != speciesKey) continue;
            double d = (c.X - center.X) * (c.X - center.X) + (c.Y - center.Y) * (c.Y - center.Y);
            if (d < best)
            {
                best = d;
                nearest = c;
            }
        }

        if (nearest == null) return;
        session.State.Selected = nearest;
        _camera.FollowEnabled = true;
        _camera.Position = new Vector2((float)nearest.X, (float)nearest.Y);
        if (_camera.Zoom.X < 3f)
        {
            _camera.Zoom = new Vector2(3f, 3f);
        }
    }

    private void OnToolApply(double wx, double wy)
    {
        var session = _host.Session;
        if (session == null || _host.Species == null) return;
        _scrub?.OnMutatingAction();
        _tools.ApplyAt(session, _host.Species, wx, wy);
        if (session.State.VegDirty)
        {
            _world.RefreshVegIfDirty();
        }

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
        CaptureHeartbeatIfDue(session);
        _story.OnSimTicked(session);
        RefreshHud();
    }

    private void OnProfilerToggled()
    {
        _profiler.PanelOpen = !_profiler.PanelOpen;
        EcoSimThemeBuilder.StyleActiveButton(GetNode<Button>("%ProfilerBtn"), _profiler.PanelOpen);
        if (_profiler.PanelOpen)
        {
            _profilerDetail.Refresh();
        }
    }

    private void CaptureHeartbeatIfDue(SimSession session)
    {
        if (_timelineDb == null) return;
        double interval = PerfPolicy.EffectiveHeartbeatIntervalSec(_heartbeatIntervalSec, session.State.Speed);
        long bucket = (long)Math.Floor(session.State.TGlobal / interval);
        if (bucket == _lastHeartbeatBucket) return;
        _lastHeartbeatBucket = bucket;
        string json = $"{{\"pop\":{session.Creatures.AliveCount()},\"day\":{session.State.Day}}}";
        _timelineDb.AppendHeartbeat(session.State.TGlobal, session.State.Day, json);
    }

    private void RefreshHud()
    {
        var session = _host.Session;
        if (session == null) return;

        var phase = SimMath.DayPhaseFromTimeOfDay(session.State.TimeOfDay);
        _dayIcon.Text = phase.Icon;
        _clockLabel.Text = SimMath.FormatTimeOfDay12(session.State.TimeOfDay);
        _dayLabel.Text = $"Day {session.State.Day}";
        _popLabel.Text = $"🐾 {session.Creatures.AliveCount()}";

        int maxGen = 1;
        foreach (var c in session.State.Creatures)
        {
            if (!c.Dead && c.Gen > maxGen) maxGen = c.Gen;
        }
        _genLabel.Text = $"🧬 Gen {maxGen}";
        _vegLabel.Text = $"🌱 {ComputeVegPercent(session.State):F0}%";

        double fps = PerfProfiler.Instance.FrameMsAvg > 0 ? 1000.0 / PerfProfiler.Instance.FrameMsAvg : 0;
        double frameMs = PerfProfiler.Instance.FrameMsAvg;
        _simFpsLabel.Text = $"⚙ {fps:F0} FPS · {frameMs:F1}ms";

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
        _timeline.SetSnapshots(_scrub.SnapshotTimes(), session.State.TGlobal, _scrub.BaselineT, _gameApp.Paused, 0.3);
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
        Creature? hover = sel;
        if (hover == null || hover.Dead)
        {
            hover = PickHoverCreature(session, tile);
        }

        if (hover != null && !hover.Dead && _host.Species != null)
        {
            var def = _host.Species.Get(hover.Sp);
            _creatureTip.Text = $"{def.Emoji} {def.Label} · {hover.State}";
            _creatureTip.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, -28);
            _creatureTip.Visible = true;
        }
        else
        {
            _creatureTip.Visible = false;
        }
    }

    private static Creature? PickHoverCreature(SimSession session, Vector2 tile)
    {
        Creature? best = null;
        double bestDist = 2.5;
        foreach (var c in session.State.Creatures)
        {
            if (c.Dead) continue;
            double dx = c.Rx - tile.X;
            double dy = c.Ry - tile.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        return best;
    }
}
