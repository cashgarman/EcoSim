using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Panel with draggable header; persists position via PanelLayoutService.</summary>
public partial class DraggablePanel : PanelContainer
{
    [Export] public string LayoutKey { get; set; } = "";

    private Control? _header;
    private bool _dragging;
    private Vector2 _dragOffset;

    public override void _Ready()
    {
        _header = GetNodeOrNull<Control>("%PanelHead");
        if (_header != null)
        {
            _header.GuiInput += OnHeaderGuiInput;
        }

        if (!string.IsNullOrEmpty(LayoutKey))
        {
            PanelLayoutService.ApplyPosition(this, LayoutKey);
        }
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
