using Godot;

namespace WildlandsEcoSim.UI;

public partial class DraggablePanel : PanelContainer
{
    [Export] public string LayoutKey { get; set; } = "";

    private Control? _header;
    private Control? _body;
    private Button? _collapseBtn;
    private bool _dragging;
    private Vector2 _dragOffset;
    private bool _collapsed;
    private Vector2 _expandedSize;
    private Vector2 _expandedCustomMinSize;
    private Vector2 _expandedBodyCustomMinSize;
    private SizeFlags _expandedBodySizeFlagsVertical;

    public Control? Body => _body;
    public bool IsCollapsed => _collapsed;

    public void SetCollapsed(bool collapsed)
    {
        if (_collapsed != collapsed)
        {
            ToggleCollapse();
        }
    }

    public override void _Ready()
    {
        NormalizePanelHead();
        _header = FindChild("PanelHead", true, false) as Control;
        _body = FindChild("PanelBody", true, false) as Control;

        WrapHeaderStrip();
        StylePanelChrome();
        BindCollapseButton();
        BindHeaderDrag();

        if (!string.IsNullOrEmpty(LayoutKey))
        {
            PanelLayoutService.ApplyPosition(this, LayoutKey);
            Callable.From(() => PanelLayoutService.ClampToViewport(this)).CallDeferred();
        }

        SetProcess(true);
        Callable.From(CaptureExpandedSize).CallDeferred();
    }

    private void CaptureExpandedSize()
    {
        if (Size.Y > 1f)
        {
            _expandedSize = Size;
        }
    }

    public override void _Process(double delta)
    {
        if (!_dragging) return;

        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _dragging = false;
            SaveLayout();
            return;
        }

        GlobalPosition = GetGlobalMousePosition() + _dragOffset;
        PanelLayoutService.ClampToViewport(this);
    }

    private void NormalizePanelHead()
    {
        var panelHead = FindChild("PanelHead", true, false) as HBoxContainer;
        if (panelHead == null) return;

        foreach (string name in new[] { "Title", "InspectHeader", "SpeciesStatsTitle" })
        {
            var label = FindChild(name, true, false) as Label;
            if (label == null || label.GetParent() == panelHead) continue;

            label.Reparent(panelHead);
            panelHead.MoveChild(label, 0);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }

        panelHead.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    private void WrapHeaderStrip()
    {
        if (_header == null || _header.GetParent() is PanelHeaderStrip)
        {
            return;
        }

        var parent = _header.GetParent();
        int index = _header.GetIndex();
        parent.RemoveChild(_header);

        var strip = new PanelHeaderStrip();
        strip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        strip.AddChild(_header);
        parent.AddChild(strip);
        parent.MoveChild(strip, index);
        _header = strip;
    }

    private void StylePanelChrome()
    {
        HBoxContainer? headHBox = GetPanelHeadHBox();
        if (headHBox == null) return;

        headHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headHBox.MouseFilter = MouseFilterEnum.Pass;
        headHBox.AddThemeConstantOverride("separation", 6);

        foreach (Node child in headHBox.GetChildren())
        {
            if (child is Label title)
            {
                int size = child.Name == "SpeciesStatsTitle"
                    ? EcoSimFonts.SpeciesStatsTitle
                    : EcoSimFonts.PanelTitle;
                EcoSimFonts.StylePanelTitle(title, size);
                if (child.Name == "Title" && LayoutKey == "gen")
                {
                    title.HorizontalAlignment = HorizontalAlignment.Center;
                }

                title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            }
            else if (child is Button collapse && collapse != _collapseBtn)
            {
                EcoSimThemeBuilder.StyleCollapseButton(collapse);
            }
        }
    }

    private void BindCollapseButton()
    {
        _collapseBtn = FindChild("CollapseBtn", true, false) as Button;
        if (_collapseBtn == null)
        {
            _collapseBtn = CreateCollapseButton();
        }

        if (_collapseBtn == null) return;

        EcoSimThemeBuilder.StyleCollapseButton(_collapseBtn);
        _collapseBtn.MouseFilter = MouseFilterEnum.Stop;
        _collapseBtn.FocusMode = FocusModeEnum.None;
        _collapseBtn.Pressed += ToggleCollapse;
    }

    private Button? CreateCollapseButton()
    {
        HBoxContainer? headHBox = GetPanelHeadHBox();
        if (headHBox == null) return null;

        var btn = new Button { Name = "CollapseBtn", Text = "▼" };
        headHBox.AddChild(btn);
        return btn;
    }

    private void BindHeaderDrag()
    {
        if (_header == null) return;
        PanelHeaderDrag.ConfigureStrip(_header, GetPanelHeadHBox(), OnHeaderGuiInput);
    }

    /// <summary>Call after adding buttons to <c>PanelHead</c> (e.g. maximize).</summary>
    protected void RefreshHeaderDrag()
    {
        PanelHeaderDrag.ConfigureHeadChildren(GetPanelHeadHBox());
    }

    private HBoxContainer? GetPanelHeadHBox()
    {
        if (_header is HBoxContainer hbox) return hbox;

        if (_header != null)
        {
            foreach (Node child in _header.GetChildren())
            {
                if (child is HBoxContainer inner) return inner;
            }
        }

        return FindChild("PanelHead", true, false) as HBoxContainer;
    }

    private void OnHeaderGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _dragging = true;
                _dragOffset = GlobalPosition - GetGlobalMousePosition();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseMotion && _dragging)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void ToggleCollapse()
    {
        if (!_collapsed)
        {
            _expandedSize = Size.Y > 1f ? Size : _expandedSize;
            _expandedCustomMinSize = CustomMinimumSize;
            if (_body != null)
            {
                _expandedBodyCustomMinSize = _body.CustomMinimumSize;
                _expandedBodySizeFlagsVertical = _body.SizeFlagsVertical;
            }
        }

        _collapsed = !_collapsed;
        ApplyBodyCollapseState();

        if (_collapseBtn != null)
        {
            _collapseBtn.Text = _collapsed ? "▶" : "▼";
        }

        OnCollapseToggled(_collapsed);
        Callable.From(ApplyCollapseLayout).CallDeferred();
    }

    protected virtual void OnCollapseToggled(bool collapsed)
    {
    }

    private void ApplyBodyCollapseState()
    {
        if (_body == null) return;

        if (_collapsed)
        {
            _body.Visible = false;
            _body.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            _body.CustomMinimumSize = Vector2.Zero;
            _body.Size = new Vector2(_body.Size.X, 0);
        }
        else
        {
            _body.Visible = true;
            _body.SizeFlagsVertical = _expandedBodySizeFlagsVertical;
            _body.CustomMinimumSize = _expandedBodyCustomMinSize;
        }
    }

    private void ApplyCollapseLayout()
    {
        if (_collapsed)
        {
            float collapsedH = MeasureCollapsedHeight();
            Size = new Vector2(Size.X, collapsedH);
            CustomMinimumSize = new Vector2(CustomMinimumSize.X, collapsedH);
            if (_body != null)
            {
                _body.Size = new Vector2(_body.Size.X, 0);
            }
        }
        else
        {
            if (_expandedSize.Y > 1f)
            {
                Size = _expandedSize;
            }

            CustomMinimumSize = _expandedCustomMinSize;
        }

        QueueRedraw();
    }

    private float MeasureCollapsedHeight()
    {
        float headerH = 0;
        if (_header != null)
        {
            headerH = _header.Size.Y;
            if (headerH < 1f)
            {
                headerH = _header.GetCombinedMinimumSize().Y;
            }
        }

        if (headerH < 1f)
        {
            HBoxContainer? headHBox = GetPanelHeadHBox();
            if (headHBox != null)
            {
                headerH = headHBox.GetCombinedMinimumSize().Y;
            }
        }

        int marginTop = GetThemeConstant("panel_margin_top", "PanelContainer");
        int marginBottom = GetThemeConstant("panel_margin_bottom", "PanelContainer");
        return Mathf.Max(headerH + marginTop + marginBottom + 4f, 32f);
    }

    public void SaveLayout()
    {
        if (!string.IsNullOrEmpty(LayoutKey))
        {
            PanelLayoutService.SavePosition(this, LayoutKey);
        }
    }
}
