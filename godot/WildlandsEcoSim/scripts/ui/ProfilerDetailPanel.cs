using Godot;

namespace WildlandsEcoSim.UI;

public partial class ProfilerDetailPanel : PanelContainer
{
    public const string LayoutKey = "profiler-detail";

    public event Action? PanelClosed;

    private ProfilerCpuTreeView _cpuTree = null!;
    private VBoxContainer _gpuGrid = null!;
    private Button _cpuTab = null!;
    private Button _gpuTab = null!;
    private Control _cpuPage = null!;
    private Control _gpuPage = null!;
    private Label _hint = null!;
    private Control _dragHeader = null!;
    private Control _resizeGrip = null!;
    private bool _open;
    private bool _hasSavedLayout;
    private bool _dragging;
    private bool _resizing;
    private Vector2 _dragOffset;
    private Vector2 _resizeStartMouse;
    private Vector2 _resizeStartSize;
    private int _tab;

    private static readonly Vector2 DefaultSize = new(520, 480);
    private static readonly Vector2 MinSize = new(360, 280);

    public bool PanelOpen
    {
        get => _open;
        set
        {
            _open = value;
            Visible = value;
            if (value)
            {
                EnsureLayout();
            }
        }
    }

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 60;
        CustomMinimumSize = MinSize;
        ClipContents = false;
        AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());

        var shell = new Control();
        shell.SetAnchorsPreset(LayoutPreset.FullRect);
        shell.SetOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shell);

        var root = new MarginContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.SetOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("margin_right", 4);
        root.AddThemeConstantOverride("margin_bottom", 4);
        shell.AddChild(root);

        var outer = new VBoxContainer();
        outer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        outer.SizeFlagsVertical = SizeFlags.ExpandFill;
        outer.AddThemeConstantOverride("separation", 4);
        root.AddChild(outer);

        _dragHeader = new PanelHeaderStrip();
        _dragHeader.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        outer.AddChild(_dragHeader);

        var head = new HBoxContainer { Name = "PanelHead" };
        head.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _dragHeader.AddChild(head);

        var title = EcoSimThemeBuilder.MakeGoldTitle("CPU / GPU Detail");
        EcoSimFonts.ApplyFont(title, EcoSimFonts.Scaled7);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);

        var closeBtn = new Button { Text = "×", FocusMode = FocusModeEnum.None };
        EcoSimThemeBuilder.StyleCollapseButton(closeBtn);
        closeBtn.Pressed += () =>
        {
            SaveLayout();
            PanelOpen = false;
            PanelClosed?.Invoke();
        };
        head.AddChild(closeBtn);
        PanelHeaderDrag.ConfigureStrip(_dragHeader, head, OnDragHeaderInput);

        var tabRow = new HBoxContainer();
        _cpuTab = MakeTabButton("CPU");
        _gpuTab = MakeTabButton("GPU");
        _cpuTab.Pressed += () => SetTab(0);
        _gpuTab.Pressed += () => SetTab(1);
        tabRow.AddChild(_cpuTab);
        tabRow.AddChild(_gpuTab);
        outer.AddChild(tabRow);

        _hint = new Label
        {
            Text = "Green = fast · Red = hot. Click ▸ to expand. Total % / Self % sum to 100% among siblings.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(_hint, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        outer.AddChild(_hint);

        _cpuPage = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        _cpuTree = new ProfilerCpuTreeView
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _cpuPage.AddChild(_cpuTree);
        outer.AddChild(_cpuPage);

        _gpuPage = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Visible = false,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        _gpuGrid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _gpuPage.AddChild(_gpuGrid);
        outer.AddChild(_gpuPage);

        _resizeGrip = new Control
        {
            CustomMinimumSize = new Vector2(20, 20),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Bdiagsize,
            TooltipText = "Drag to resize",
        };
        _resizeGrip.SetAnchorsPreset(LayoutPreset.BottomRight);
        _resizeGrip.OffsetLeft = -20;
        _resizeGrip.OffsetTop = -20;
        _resizeGrip.OffsetRight = 0;
        _resizeGrip.OffsetBottom = 0;
        _resizeGrip.GuiInput += OnResizeGripInput;
        shell.AddChild(_resizeGrip);

        SetTab(0);
        SetProcess(true);
        Callable.From(() => EnsureLayout()).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (_dragging)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _dragging = false;
                SaveLayout();
                return;
            }

            GlobalPosition = GetGlobalMousePosition() + _dragOffset;
            PanelLayoutService.ClampToViewport(this);
        }

        if (_resizing)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _resizing = false;
                SaveLayout();
                _cpuTree.QueueRedraw();
                return;
            }

            Vector2 mouse = GetGlobalMousePosition();
            Vector2 deltaSize = mouse - _resizeStartMouse;
            Size = new Vector2(
                Mathf.Max(MinSize.X, _resizeStartSize.X + deltaSize.X),
                Mathf.Max(MinSize.Y, _resizeStartSize.Y + deltaSize.Y));
            PanelLayoutService.ClampToViewport(this);
            _cpuTree.QueueRedraw();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            _cpuTree.QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_resizeGrip == null) return;
        Vector2 p = _resizeGrip.Position + new Vector2(6, 6);
        Color c = EcoSimThemeBuilder.Dim;
        for (int i = 0; i < 3; i++)
        {
            Vector2 o = new(i * 4, i * 4);
            DrawLine(p + o, p + o + new Vector2(8, 0), c, 1.5f);
            DrawLine(p + o, p + o + new Vector2(0, 8), c, 1.5f);
        }
    }

    public void EnsureLayout(ProfilerPanel? summary = null)
    {
        if (_hasSavedLayout)
        {
            PanelLayoutService.ClampToViewport(this);
            return;
        }

        var vp = GetViewportRect().Size;
        Vector2 defaultPos;
        Vector2 defaultSize = new Vector2(
            Math.Min(DefaultSize.X, vp.X - 24),
            Math.Min(DefaultSize.Y, vp.Y - 120));

        if (summary != null && summary.Visible)
        {
            defaultPos = summary.GlobalPosition + new Vector2(summary.Size.X + 8, 0);
            if (defaultPos.X + defaultSize.X > vp.X - 8)
            {
                defaultPos = new Vector2(vp.X - defaultSize.X - 8, summary.GlobalPosition.Y);
            }
        }
        else
        {
            defaultPos = new Vector2((vp.X - defaultSize.X) * 0.5f, 88);
        }

        _hasSavedLayout = PanelLayoutService.ApplyBounds(this, LayoutKey, defaultPos, defaultSize);
        PanelLayoutService.ClampToViewport(this);
    }

    public void SaveLayout()
    {
        PanelLayoutService.SaveBounds(this, LayoutKey);
        _hasSavedLayout = true;
    }

    public void Refresh()
    {
        if (!_open)
        {
            return;
        }

        var p = PerfProfiler.Instance;
        double frameMs = p.Get("frameTotal");
        if (frameMs <= 0)
        {
            frameMs = p.FrameMsAvg;
        }

        _cpuTree.SetData(p.GetCpuTree(), frameMs);
        RebuildGpuGrid(p, frameMs);
    }

    private void OnDragHeaderInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _dragging = true;
                _dragOffset = GlobalPosition - GetGlobalMousePosition();
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion && _dragging)
        {
            AcceptEvent();
        }
    }

    private void OnResizeGripInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _resizing = true;
                _resizeStartMouse = GetGlobalMousePosition();
                _resizeStartSize = Size;
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion && _resizing)
        {
            AcceptEvent();
        }
    }

    private void RebuildGpuGrid(PerfProfiler p, double frameMs)
    {
        foreach (Node child in _gpuGrid.GetChildren())
        {
            child.QueueFree();
        }

        var gpu = p.GetGpuSnapshot();
        double submitMs = p.Get("render.submit");
        AddGpuRow("Draw calls", $"{gpu.RenderDrawCalls}");
        AddGpuRow("Instances", $"{gpu.RenderInstances}");
        AddGpuRow("Compute dispatches", $"{gpu.ComputeDispatches}");
        AddGpuRow("Buffer uploads", $"{gpu.BufferUploadBytes / 1024.0:F1} KB");
        AddGpuRow("Buffer transfers", $"{gpu.BufferTransfers}");
        AddGpuRow("Est. VRAM", $"{p.BufferMemoryBytes / (1024.0 * 1024.0):F2} MB");
        AddGpuRow("Submit time", $"{submitMs:F2} ms");
        AddGpuRow("GPU load (est.)", $"{p.GpuLoadPct:F1} %");
        AddGpuRow("Renderer", "godot-canvas");
        AddGpuRow("Frame budget", $"sim {p.Get("sim"):F1} ms · render {p.Get("render"):F1} ms · ui {p.Get("ui"):F1} ms");
        AddGpuRow("Frame total", $"{frameMs:F1} ms");
    }

    private void AddGpuRow(string label, string value)
    {
        var row = new HBoxContainer();
        var lbl = new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimFonts.ApplyFont(lbl, ProfilerCpuTreeView.ListFontSize, EcoSimThemeBuilder.Dim);
        var val = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right };
        EcoSimFonts.ApplyFont(val, ProfilerCpuTreeView.ListFontSize, EcoSimThemeBuilder.Gold);
        row.AddChild(lbl);
        row.AddChild(val);
        _gpuGrid.AddChild(row);
    }

    private Button MakeTabButton(string text)
    {
        var btn = new Button { Text = text, ToggleMode = true, FocusMode = FocusModeEnum.None };
        EcoSimThemeBuilder.StyleCollapseButton(btn);
        return btn;
    }

    private void SetTab(int tab)
    {
        _tab = tab;
        _cpuTab.ButtonPressed = tab == 0;
        _gpuTab.ButtonPressed = tab == 1;
        _cpuPage.Visible = tab == 0;
        _gpuPage.Visible = tab == 1;
        EcoSimThemeBuilder.StyleActiveButton(_cpuTab, tab == 0);
        EcoSimThemeBuilder.StyleActiveButton(_gpuTab, tab == 1);
    }
}
