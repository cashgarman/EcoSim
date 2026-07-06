using Godot;

namespace WildlandsEcoSim.Render;

public partial class ToolFxOverlay : Node2D
{
    private string _activeTool = "inspect";
    private bool _visible;
    private Vector2 _tilePos;

    public override void _Ready()
    {
        ZIndex = 4;
    }

    public void SetTool(string tool, bool show, Vector2 tilePos)
    {
        _activeTool = tool;
        _visible = show && tool != "inspect" && !tool.StartsWith("spawn-");
        _tilePos = tilePos;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_visible) return;

        float radius = _activeTool switch
        {
            "meteor" => 3f,
            "cull" => 1.5f,
            _ => 4f,
        };

        Color col = _activeTool switch
        {
            "rain" => new Color(0.3f, 0.6f, 1f, 0.35f),
            "drought" => new Color(1f, 0.7f, 0.2f, 0.35f),
            "meteor" => new Color(1f, 0.3f, 0.2f, 0.4f),
            _ => new Color(1f, 1f, 1f, 0.25f),
        };

        DrawArc(_tilePos, radius, 0, Mathf.Tau, 48, col, 0.08f);
        DrawArc(_tilePos, radius, 0, Mathf.Tau, 48, col.Lightened(0.3f), 0.03f);
    }
}
