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
    private SpeedSliderControl _speedControl = null!;
    private PanelContainer _terrainTip = null!;
    private PanelContainer _terrainTipDot = null!;
    private Label _terrainTipLabel = null!;
    private PanelContainer _creatureTip = null!;
    private PanelContainer _creatureTipDot = null!;
    private Label _creatureTipLabel = null!;
    private SubViewportContainer _worldViewportContainer = null!;
    private SubViewport _worldViewport = null!;
    private int _lastTerrainTipBiome = -1;
    private string _lastCreatureTipKey = "";

    private TimelineDbPanel? _timelineDbPanel;
    private ProfilerDetailPanel? _profilerDetail;
    private BtEditorPanel? _btObserve;
    private DeathNoticePanel? _deathNotice;
    private PauseMenuPanel? _pauseMenu;
    private Button? _cpuGpuBtn;
    private Button? _btObserveBtn;
    private int? _watchFollowDeathId;
    private TimelineDb? _timelineDb;
    private TimeScrubController? _scrub;
    private double _heartbeatIntervalSec = 5;
    private long _lastHeartbeatBucket = -1;
    private double _scrubStripLastRefreshMs;
    private bool _timelineDragging;
    private bool _seekDeferred;
    private double? _pendingSeekT;
    private DraggablePanel[] _panels = [];
    private PopHistoryTracker _popHistory = new();

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
        _speedControl = GetNode<SpeedSliderControl>("%SpeedControl");
        _speedControl.Theme = uiTheme;
        _terrainTip = GetNode<PanelContainer>("%TerrainTip");
        _terrainTipDot = GetNode<PanelContainer>("%TerrainTipDot");
        _terrainTipLabel = GetNode<Label>("%TerrainTipLabel");
        _creatureTip = GetNode<PanelContainer>("%CreatureTip");
        _creatureTipDot = GetNode<PanelContainer>("%CreatureTipDot");
        _creatureTipLabel = GetNode<Label>("%CreatureTipLabel");
        _terrainTip.Theme = uiTheme;
        _terrainTip.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());
        _creatureTip.Theme = uiTheme;
        _creatureTip.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());
        _creatureTip.TopLevel = true;
        _creatureTip.ZIndex = 50;

        EcoSimFonts.ApplyFont(_dayIcon, EcoSimFonts.DayIcon);
        EcoSimFonts.ApplyFont(_clockLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        EcoSimFonts.ApplyFont(_dayLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Dim);
        EcoSimFonts.ApplyFont(_popLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        EcoSimFonts.ApplyFont(_genLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        EcoSimFonts.ApplyFont(_vegLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        EcoSimFonts.ApplyFont(_simFpsLabel, EcoSimFonts.Small, EcoSimThemeBuilder.Dim);
        EcoSimFonts.ApplyFont(_terrainTipLabel, EcoSimFonts.Scaled7, EcoSimThemeBuilder.Text);
        EcoSimFonts.ApplyFont(_creatureTipLabel, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Text);
        EcoSimFonts.StylePanelTitle(GetNode<Label>("%GodMenuTitle"));

        _worldViewportContainer = GetNode<SubViewportContainer>("../WorldViewportContainer");

        _worldViewport = GetNode<SubViewport>("%WorldViewport");
        _worldViewport.TransparentBg = true;
        _world = _worldViewport.GetNode<WorldRenderer>("WorldRoot");
        _camera = _worldViewport.GetNode<WorldCamera>("WorldRoot/Camera2D");
        Callable.From(SyncWorldViewportSize).CallDeferred();

        _profilerDetail = new ProfilerDetailPanel();
        _profilerDetail.Theme = uiTheme;
        _profilerDetail.PanelClosed += OnProfilerDetailClosed;
        AddChild(_profilerDetail);
        _btObserve = new BtEditorPanel();
        _btObserve.Theme = uiTheme;
        AddChild(_btObserve);
        _timelineDbPanel = new TimelineDbPanel();
        AddChild(_timelineDbPanel);
        _deathNotice = new DeathNoticePanel();
        _deathNotice.Theme = uiTheme;
        AddChild(_deathNotice);
        _pauseMenu = new PauseMenuPanel();
        _pauseMenu.Theme = uiTheme;
        _pauseMenu.Resumed += OnPauseMenuResume;
        _pauseMenu.QuitRequested += OnPauseMenuQuit;
        AddChild(_pauseMenu);

        _panels = [_gen, _ecosystem, _inspector, GetNode<StoryPanel>("%StoryPanel"), _speciesStats, _profiler, _timelineDbPanel, _btObserve!];

        _gen.GenerateRequested += OnGenerate;
        _gen.RestockRequested += OnRestock;
        _speedControl.SpeedChanged += OnSpeedChanged;
        _ecosystem.SpeciesLocked += OnSpeciesLocked;
        _ecosystem.SpeciesHovered += OnSpeciesHovered;
        _ecosystem.SpeciesFollow += OnSpeciesFollow;
        _ecosystem.SpeciesGodMenu += OnSpeciesGodMenu;
        _godMenu.KillAllRequested += OnKillAll;
        _gameApp.SimTicked += OnSimTicked;
        _timeline.SeekRequested += OnTimelineSeek;
        _timeline.PresentRequested += OnTimelinePresent;
        _timeline.ScrubDragStarted += OnTimelineScrubDragStarted;
        _timeline.ScrubDragEnded += OnTimelineScrubDragEnded;

        GetNode<Button>("%WorldGenBtn").Pressed += OnWorldGenToggled;
        GetNode<Button>("%FollowBtn").Pressed += OnFollowToggled;
        GetNode<Button>("%ProfilerBtn").Pressed += OnProfilerToggled;
        GetNode<Button>("%BtObserveBtn").Pressed += OnBtObserveToggled;
        GetNode<Button>("%TestRunnerBtn").Disabled = false;
        GetNode<Button>("%TestRunnerBtn").TooltipText = "Open batch test runner";
        GetNode<Button>("%TestRunnerBtn").Pressed += () => GetTree().ChangeSceneToFile("res://scenes/BatchTest.tscn");
        GetNode<Button>("%PresentBtn").Pressed += OnTimelinePresent;

        var cpuGpuBtn = new Button { Text = "CPU/GPU", FocusMode = Control.FocusModeEnum.None };
        cpuGpuBtn.Theme = uiTheme;
        cpuGpuBtn.Pressed += OnCpuGpuToggled;
        _cpuGpuBtn = cpuGpuBtn;
        GetNode<HBoxContainer>("TopBar/VBox/Row1/Actions").AddChild(cpuGpuBtn);

        var gpuThrottle = new OptionButton();
        gpuThrottle.AddItem("GPU throttle: Off");
        gpuThrottle.AddItem("Light");
        gpuThrottle.AddItem("Medium");
        gpuThrottle.AddItem("Heavy");
        gpuThrottle.AddItem("Eco");
        gpuThrottle.ItemSelected += idx => WildlandsEcoSim.Gpu.GpuThrottle.Preset = (WildlandsEcoSim.Gpu.GpuThrottlePreset)idx;
        GetNode<HBoxContainer>("TopBar/VBox/Row1/Actions").AddChild(gpuThrottle);

        _world.BindInput(() => _tools.ActiveTool, OnToolApply);

        _host.BootstrapIfNeeded();
        _popHistory.Bind(_host.Species!);
        _ecosystem.Bind(_host.Species!, _popHistory);
        _tools.Bind(_host.Species!);
        _inspector.Bind(_host.Species!);
        _inspector.CreatureLinkPressed += OnCreatureLinkPressed;
        _speciesStats.Bind(_host.Species!, _popHistory);
        _story.CreatureLifeEvent += OnCreatureLifeEvent;

        if (!Engine.IsEditorHint())
        {
            InitTimeline();
            OnGenerate();
        }

        GetViewport().SizeChanged += OnViewportSizeChanged;
    }

    private void OnViewportSizeChanged()
    {
        SyncWorldViewportSize();
        PanelLayoutService.ClampAll(_panels);
        if (_profilerDetail != null && _profilerDetail.Visible)
        {
            PanelLayoutService.ClampToViewport(_profilerDetail);
        }
    }

    private void SyncWorldViewportSize()
    {
        Vector2 size = _worldViewportContainer.Size;
        if (size.X < 1f || size.Y < 1f)
        {
            return;
        }

        var next = new Vector2I(
            Math.Max(1, (int)MathF.Round(size.X)),
            Math.Max(1, (int)MathF.Round(size.Y)));
        if (_worldViewport.Size != next)
        {
            _worldViewport.Size = next;
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
            SaveBeforeQuit();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Space)
            {
                _gameApp.TogglePause();
                SyncSpeedSliderFromSession();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.F2)
            {
                OnProfilerToggled();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Escape)
            {
                if (_deathNotice != null && _deathNotice.Visible)
                {
                    _deathNotice.HideNotice();
                }
                else if (_pauseMenu != null && _pauseMenu.IsOpen)
                {
                    OnPauseMenuResume();
                }
                else
                {
                    OpenPauseMenu();
                }
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.F)
            {
                OnFollowToggled();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (!string.IsNullOrEmpty(_ecosystem.LockedSpecies) && !IsPointerOnHud(mb.GlobalPosition))
            {
                DeselectFromPanel();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Process(double delta)
    {
        var profiler = PerfProfiler.Instance;
        profiler.Timed("frame.ui", () =>
        {
            profiler.Timed("ui", () =>
            {
                UpdateTooltips();
                if (_scrub != null && _scrub.ScrubActive)
                {
                    RefreshScrubChrome();
                }
                else
                {
                    RefreshHud();
                }
            });
            _profiler.Refresh();
            if (_profilerDetail.PanelOpen)
            {
                _profilerDetail.Refresh();
            }
            RefreshTimelineStripIfDue();
        });
    }

    private void RefreshTimelineStripIfDue()
    {
        var session = _host.Session;
        if (session == null || _scrub == null) return;

        double nowMs = Time.GetTicksMsec();
        bool force = !_timelineDragging && _scrub.ScrubActive;
        if (!force && nowMs - _scrubStripLastRefreshMs < PerfPolicy.EffectiveScrubTickRefreshMs())
        {
            if (_timelineDragging)
            {
                _timeline.SetPlayheadPreview(_scrub.ScrubTargetT);
            }
            return;
        }

        _scrubStripLastRefreshMs = nowMs;
        RefreshTimelineStrip();
    }

    private void OnCpuGpuToggled()
    {
        bool open = !_profilerDetail!.PanelOpen;
        _profilerDetail.PanelOpen = open;
        PerfProfiler.Instance.DetailEnabled = open;
        EcoSimThemeBuilder.StyleActiveButton(_cpuGpuBtn!, open);
        if (open)
        {
            _profilerDetail.EnsureLayout(_profiler.PanelOpen ? _profiler : null);
            _profilerDetail.Refresh();
        }
    }

    private void OnProfilerDetailClosed()
    {
        _profilerDetail!.SaveLayout();
        _profilerDetail.PanelOpen = false;
        PerfProfiler.Instance.DetailEnabled = false;
        EcoSimThemeBuilder.StyleActiveButton(_cpuGpuBtn!, false);
    }

    private void OnGenerate()
    {
        var cfg = _gen.BuildConfig();
        uint seed = _gen.Seed;
        _host.GenerateWorld(cfg, seed);
        var session = _host.Session!;
        session.State.Speed = _speedControl.Value;
        session.State.AutoMigrationEnabled = _gen.AutoMigrationEnabled;
        _gameApp.Paused = false;

        _scrub?.ResetBaseline();
        _timelineDb?.TruncateFuture(0);
        string runId = $"run-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _timelineDb?.BeginRun(runId);
        var initialSnap = SnapshotService.Capture(session.State);
        _timelineDb?.SaveSnapshot(initialSnap, TimeScrubController.DefaultSnapshotIntervalSec);
        _scrub?.RegisterSnapshot(initialSnap);

        _world.BindWorld(session, _host.Species!);
        _camera.CenterOnWorld();
        _story.Reset();
        _ecosystem.Reset();
        _deathNotice?.HideNotice();
        _pauseMenu?.HideMenu();
        _watchFollowDeathId = null;
        _story.LogGodAction($"Day 0: World generated ({cfg.Size}, seed {seed})", session);
        RefreshHud();
    }

    private void OnCreatureLifeEvent(string speciesKey, string kind)
    {
        _ecosystem.FlashSpeciesPop(speciesKey, kind);
    }

    private bool IsPointerOnHud(Vector2 globalPos)
    {
        var topBar = GetNode<PanelContainer>("TopBar");
        if (topBar.GetGlobalRect().HasPoint(globalPos)) return true;

        foreach (var panel in _panels)
        {
            if (!panel.Visible) continue;
            if (panel.GetGlobalRect().HasPoint(globalPos)) return true;
        }

        if (_profilerDetail != null && _profilerDetail.Visible
            && _profilerDetail.GetGlobalRect().HasPoint(globalPos))
        {
            return true;
        }

        if (_godMenu.Visible)
        {
            return true;
        }

        return false;
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
        }
    }

    private void OnSpeciesLocked(string speciesKey)
    {
        _world.SetLockedSpecies(string.IsNullOrEmpty(speciesKey) ? null : speciesKey);
        _speciesStats.ShowSpecies(
            string.IsNullOrEmpty(speciesKey) ? null : speciesKey,
            _host.Session!,
            _host.Session!.SpeciesStats);
        if (_btObserve is { Visible: true } && _host.Session != null)
        {
            _btObserve.Refresh(_host.Session, speciesKey);
        }
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

        Vector2 center = _camera.Position;
        Creature? nearest = session.Creatures.FindNearestOfSpecies(speciesKey, center.X, center.Y);
        if (nearest == null) return;

        session.State.Selected = nearest;
        _camera.FollowEnabled = true;
        _watchFollowDeathId = nearest.Id;
        SyncFollowButton();
        _camera.FocusCreature(nearest);
        _inspector.Refresh(nearest, session.Creatures, session.State);
    }

    private void DeselectFromPanel()
    {
        var session = _host.Session;
        if (session != null)
        {
            session.State.Selected = null;
            _speciesStats.ShowSpecies(null, session, session.SpeciesStats);
        }

        _ecosystem.ClearSpeciesLock();
        OnSpeciesLocked("");
        OnSpeciesHovered("");
        _camera.FollowEnabled = false;
        _watchFollowDeathId = null;
        SyncFollowButton();
        _inspector.Refresh(null);
    }

    private void OnCreatureLinkPressed(int creatureId)
    {
        var session = _host.Session;
        if (session == null) return;

        var creature = session.Creatures.GetById(creatureId);
        if (creature == null) return;

        session.State.Selected = creature;
        if (!creature.Dead)
        {
            _camera.FocusCreature(creature);
        }

        _inspector.Refresh(creature, session.Creatures, session.State);
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

    private void OnTimelineScrubDragStarted()
    {
        PauseSimulationForTimeline();
        _timelineDragging = true;
        _scrub?.SetDragging(true);
        _scrub?.PrewarmCache();
    }

    private void OnTimelineScrubDragEnded()
    {
        _timelineDragging = false;
        _scrub?.SetDragging(false);
        if (_pendingSeekT != null)
        {
            RunPendingSeek(full: true);
        }
        else
        {
            FinalizeScrubSeek();
        }
    }

    private void FinalizeScrubSeek()
    {
        if (_scrub == null) return;
        var session = _host.Session;
        if (session == null || _host.Species == null) return;

        bool applied = false;
        PerfProfiler.Instance.Timed("scrub.seek", () =>
        {
            applied = _scrub.SeekTo(_scrub.ScrubTargetT, light: false);
        });
        if (!applied && !_scrub.IsViewingPast()) return;

        _world.ApplyScrubState(session, _host.Species, light: false);
        RefreshHud();
        RefreshTimelineStrip();
    }

    private void OnTimelineSeek(double targetT)
    {
        PauseSimulationForTimeline();
        _pendingSeekT = targetT;
        _timeline.SetPlayheadPreview(targetT);
        if (!_seekDeferred)
        {
            _seekDeferred = true;
            CallDeferred(MethodName.ProcessPendingSeek);
        }
    }

    private void ProcessPendingSeek()
    {
        _seekDeferred = false;
        RunPendingSeek(full: !_timelineDragging);
        if (_pendingSeekT != null)
        {
            _seekDeferred = true;
            CallDeferred(MethodName.ProcessPendingSeek);
        }
    }

    private void RunPendingSeek(bool full)
    {
        if (_pendingSeekT == null || _scrub == null) return;
        double targetT = _pendingSeekT.Value;
        _pendingSeekT = null;

        var profiler = PerfProfiler.Instance;
        bool applied = false;
        profiler.Timed("scrub.seek", () =>
        {
            applied = _scrub.SeekTo(targetT, light: !full);
        });
        if (!applied) return;

        var session = _host.Session;
        if (session == null || _host.Species == null) return;
        _world.ApplyScrubState(session, _host.Species, light: !full);

        if (full)
        {
            RefreshHud();
            RefreshTimelineStrip();
        }
        else
        {
            RefreshScrubChrome();
        }
    }

    private void PauseSimulationForTimeline()
    {
        _gameApp.PauseForTimeline();
        _speedControl.SetValueNoSignal(0);
    }

    private void SyncSpeedSliderFromSession()
    {
        var session = _host.Session;
        if (session == null) return;
        double speed = session.State.Speed;
        _speedControl.SetValueNoSignal(speed);
    }

    private void OpenPauseMenu()
    {
        if (!_gameApp.Paused)
        {
            _gameApp.TogglePause();
            SyncSpeedSliderFromSession();
        }

        _pauseMenu?.ShowMenu();
    }

    private void OnPauseMenuResume()
    {
        _pauseMenu?.HideMenu();
        if (_gameApp.Paused)
        {
            _gameApp.TogglePause();
            SyncSpeedSliderFromSession();
        }
    }

    private void OnPauseMenuQuit()
    {
        SaveBeforeQuit();
        GetTree().Quit();
    }

    private void SaveBeforeQuit()
    {
        PanelLayoutService.SaveAll(_panels);
        _profilerDetail?.SaveLayout();
        _timelineDb?.Dispose();
        _timelineDb = null;
    }

    private void OnTimelinePresent()
    {
        PerfProfiler.Instance.Timed("scrub.seek", () => _scrub?.GoToPresent());
        var session = _host.Session;
        if (session == null || _host.Species == null) return;
        _world.ApplyScrubState(session, _host.Species, light: false);
        RefreshHud();
        RefreshTimelineStrip();
    }

    private void OnSimTicked()
    {
        var session = _host.Session;
        if (session == null) return;
        CheckFollowedCreatureDeath(session);
        CaptureHeartbeatIfDue(session);
        _story.OnSimTicked(session);
        RefreshHud();
    }

    private void OnFollowToggled()
    {
        _camera.FollowEnabled = !_camera.FollowEnabled;
        SyncFollowButton();
        if (!_camera.FollowEnabled)
        {
            _watchFollowDeathId = null;
        }
    }

    private void SyncFollowButton()
    {
        var btn = GetNode<Button>("%FollowBtn");
        bool following = _camera.FollowEnabled;
        btn.Text = following ? "Following" : "Follow";
        EcoSimThemeBuilder.StyleActiveButton(btn, following);
    }

    private void CheckFollowedCreatureDeath(SimSession session)
    {
        if (_deathNotice == null || _host.Species == null) return;

        var selected = session.State.Selected;
        if (_camera.FollowEnabled && selected is { Dead: false })
        {
            _watchFollowDeathId = selected.Id;
            return;
        }

        if (_deathNotice.Visible || _watchFollowDeathId == null) return;

        Creature? dead = null;
        foreach (var c in session.State.Creatures)
        {
            if (c.Id == _watchFollowDeathId && c.Dead)
            {
                dead = c;
                break;
            }
        }

        if (dead == null) return;

        _deathNotice.ShowNotice(dead, _host.Species, session.State);
        _camera.FollowEnabled = false;
        _watchFollowDeathId = null;
        SyncFollowButton();
        _inspector.Refresh(dead, session.Creatures, session.State);
    }

    private void OnProfilerToggled()
    {
        _profiler.PanelOpen = !_profiler.PanelOpen;
        PerfProfiler.Instance.Enabled = _profiler.PanelOpen;
        _tools.Visible = _profiler.PanelOpen;
        EcoSimThemeBuilder.StyleActiveButton(GetNode<Button>("%ProfilerBtn"), _profiler.PanelOpen);
        if (_profilerDetail!.PanelOpen)
        {
            _profilerDetail.EnsureLayout(_profiler.PanelOpen ? _profiler : null);
            _profilerDetail.Refresh();
        }
    }

    private void OnBtObserveToggled()
    {
        if (_btObserve == null) return;
        bool open = !_btObserve.Visible;
        _btObserve.Visible = open;
        EcoSimThemeBuilder.StyleActiveButton(GetNode<Button>("%BtObserveBtn"), open);
        if (open)
        {
            _btObserve.Refresh(_host.Session, _ecosystem.LockedSpecies);
        }
    }

    private void OnWorldGenToggled()
    {
        bool open = !_gen.Visible;
        _gen.Visible = open;
        EcoSimThemeBuilder.StyleActiveButton(GetNode<Button>("%WorldGenBtn"), open);
        if (open)
        {
            _gen.SetCollapsed(false);
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

    private void RefreshScrubChrome()
    {
        var session = _host.Session;
        if (session == null) return;

        var phase = SimMath.DayPhaseFromTimeOfDay(session.State.TimeOfDay);
        _dayIcon.Text = phase.Icon;
        _clockLabel.Text = SimMath.FormatTimeOfDay12(session.State.TimeOfDay);
        _dayLabel.Text = $"Day {session.State.Day}";

        double fps = PerfProfiler.Instance.FrameMsAvg > 0 ? 1000.0 / PerfProfiler.Instance.FrameMsAvg : 0;
        double frameMs = PerfProfiler.Instance.FrameMsAvg;
        _simFpsLabel.Text = $"⚙ {fps:F0} FPS · {frameMs:F1}ms";
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

        _popHistory.SampleIfDue(session, _gameApp.Paused);
        _ecosystem.Refresh(session);
        _inspector.Refresh(session.State.Selected, session.Creatures, session.State);
        if (_btObserve != null && _btObserve.Visible)
        {
            _btObserve.Refresh(session, _ecosystem.LockedSpecies);
        }
        if (!string.IsNullOrEmpty(_ecosystem.LockedSpecies))
        {
            _speciesStats.ShowSpecies(_ecosystem.LockedSpecies, session, session.SpeciesStats);
        }
    }

    private void RefreshTimelineStrip()
    {
        var session = _host.Session;
        if (session == null || _scrub == null) return;
        double currentT = _scrub.ScrubActive ? _scrub.ScrubTargetT : session.State.TGlobal;
        _timeline.SetSnapshots(
            _scrub.SnapshotTimes(),
            currentT,
            _scrub.BaselineT,
            _gameApp.Paused,
            0.3);
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
            _lastTerrainTipBiome = -1;
            _lastCreatureTipKey = "";
            return;
        }

        Vector2 mouse = _world.GetViewport().GetMousePosition();
        Vector2 worldPos = _camera.GetCanvasTransform().AffineInverse() * mouse;
        Vector2 tile = _world.WorldToTile(worldPos);
        int tx = (int)Math.Floor(tile.X);
        int ty = (int)Math.Floor(tile.Y);

        _terrainTip.Visible = true;
        if (tx >= 0 && ty >= 0 && tx < session.State.W && ty < session.State.H)
        {
            int i = ty * session.State.W + tx;
            byte biome = session.State.Biome[i];
            var info = BiomeData.Info[(Biome)biome];
            if (biome != _lastTerrainTipBiome)
            {
                _lastTerrainTipBiome = biome;
                SetTerrainTipDot(info);
            }

            _terrainTipLabel.Text = $"{info.Name}  veg {(session.State.Veg[i] / Math.Max(0.001f, session.State.VegCap[i]) * 100):F0}%";
        }
        else if (_lastTerrainTipBiome != -2)
        {
            _lastTerrainTipBiome = -2;
            _terrainTipDot.AddThemeStyleboxOverride("panel",
                EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.Dim, EcoSimThemeBuilder.Edge, 2));
            _terrainTipLabel.Text = "Off map";
        }

        UpdateCreatureTooltip(session, tile);
    }

    private void UpdateCreatureTooltip(SimSession session, Vector2 tile)
    {
        var sel = session.State.Selected;
        Creature? target = null;

        if (_camera.FollowEnabled && sel is { Dead: false })
        {
            target = sel;
        }
        else if (IsMouseOverWorldViewport())
        {
            target = PickHoverCreature(session, tile);
        }
        else if (sel is { Dead: false })
        {
            target = sel;
        }

        if (target == null || target.Dead || _host.Species == null)
        {
            _creatureTip.Visible = false;
            _lastCreatureTipKey = "";
            return;
        }

        var def = _host.Species.Get(target.Sp);
        string key = CreatureBehaviorLabels.GetTooltipCacheKey(target, session.State);
        if (key != _lastCreatureTipKey)
        {
            _lastCreatureTipKey = key;
            _creatureTipDot.AddThemeStyleboxOverride("panel",
                EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.SpeciesColor(def), EcoSimThemeBuilder.Edge, 1));
            _creatureTipLabel.Text =
                $"{def.Emoji} {def.Label} · {CreatureBehaviorLabels.GetDisplayLabel(target, session.State)}";
        }

        Vector2 screen = WorldTileToHudScreen(CreatureDrawUtil.DisplayPos(target));
        float lift = ComputeCreatureTipLift(session, target);

        _creatureTip.ResetSize();
        Vector2 tipSize = _creatureTip.GetMinimumSize();
        if (tipSize.X < 8f || tipSize.Y < 8f)
        {
            tipSize = new Vector2(140, 22);
        }

        _creatureTip.GlobalPosition = new Vector2(
            screen.X - tipSize.X * 0.5f,
            screen.Y - tipSize.Y - lift);
        _creatureTip.Visible = true;
    }

    private bool IsMouseOverWorldViewport()
    {
        return _worldViewportContainer.GetGlobalRect().HasPoint(GetViewport().GetMousePosition());
    }

    private Vector2 WorldTileToHudScreen(Vector2 tilePos)
    {
        var subVp = (SubViewport)_world.GetViewport();
        Vector2 worldPixel = tilePos * WorldRenderer.TilePixels;
        Vector2 vpPos = _camera.GetCanvasTransform() * worldPixel;
        Vector2 vpSize = subVp.Size;
        if (vpSize.X < 1f || vpSize.Y < 1f)
        {
            vpSize = subVp.GetVisibleRect().Size;
        }

        Rect2 containerRect = _worldViewportContainer.GetGlobalRect();
        Vector2 norm = new Vector2(vpPos.X / vpSize.X, vpPos.Y / vpSize.Y);
        return containerRect.Position + norm * containerRect.Size;
    }

    private float ComputeCreatureTipLift(SimSession session, Creature creature)
    {
        float eSize = (float)session.Creatures.ESize(creature);
        float s = Math.Max(2.5f, _camera.Zoom.X * 0.9f * eSize);
        return Math.Max(8f, s * 0.75f + 5f);
    }

    private void SetTerrainTipDot(BiomeInfo info)
    {
        byte[] rgb = info.ColorRgb;
        var col = new Color(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f);
        _terrainTipDot.AddThemeStyleboxOverride("panel",
            EcoSimThemeBuilder.MakeFlat(col, EcoSimThemeBuilder.Edge, 2));
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
