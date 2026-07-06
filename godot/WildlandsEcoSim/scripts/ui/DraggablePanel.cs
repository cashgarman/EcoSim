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

    public Control? Body => _body;

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
        strip.MouseFilter = MouseFilterEnum.Stop;
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
        headHBox.MouseFilter = MouseFilterEnum.Stop;
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

        _header.MouseDefaultCursorShape = CursorShape.Move;
        _header.GuiInput += OnHeaderGuiInput;

        HBoxContainer? headHBox = GetPanelHeadHBox();
        if (headHBox != null && headHBox != _header)
        {
            headHBox.MouseDefaultCursorShape = CursorShape.Move;
            headHBox.GuiInput += OnHeaderGuiInput;
        }
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
        if (IsPointerOverCollapseButton()) return;

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

    private bool IsPointerOverCollapseButton()
    {
        return _collapseBtn != null
            && _collapseBtn.Visible
            && _collapseBtn.GetGlobalRect().HasPoint(GetGlobalMousePosition());
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        if (_body != null)
        {
            _body.Visible = !_collapsed;
        }

        if (_collapseBtn != null)
        {
            _collapseBtn.Text = _collapsed ? "▶" : "▼";
        }
    }

    public void SaveLayout()
    {
        if (!string.IsNullOrEmpty(LayoutKey))
        {
            PanelLayoutService.SavePosition(this, LayoutKey);
        }
    }
}
