using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Panel with draggable header; persists position via PanelLayoutService.</summary>
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
        _header = FindChild("PanelHead", true, false) as Control;
        _body = FindChild("PanelBody", true, false) as Control;
        _collapseBtn = FindChild("CollapseBtn", true, false) as Button;

        if (_header != null)
        {
            _header.GuiInput += OnHeaderGuiInput;
        }

        if (_collapseBtn != null)
        {
            _collapseBtn.Pressed += ToggleCollapse;
        }

        if (!string.IsNullOrEmpty(LayoutKey))
        {
            PanelLayoutService.ApplyPosition(this, LayoutKey);
        }
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        if (_body != null) _body.Visible = !_collapsed;
        if (_collapseBtn != null) _collapseBtn.Text = _collapsed ? "▶" : "▼";
    }

    private void OnHeaderGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _dragging = mb.Pressed;
            _dragOffset = Position - GetGlobalMousePosition();
        }
        else if (@event is InputEventMouseMotion && _dragging)
        {
            Position = GetGlobalMousePosition() + _dragOffset;
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
