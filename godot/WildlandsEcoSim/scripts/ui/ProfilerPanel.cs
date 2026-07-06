using Godot;

namespace WildlandsEcoSim.UI;

public partial class ProfilerPanel : DraggablePanel
{
    private Label _overview = null!;
    private Control _budgetBar = null!;
    private bool _open;

    public bool PanelOpen
    {
        get => _open;
        set
        {
            _open = value;
            Visible = value;
        }
    }

    public override void _Ready()
    {
        LayoutKey = "profiler";
        Visible = false;
        _open = false;
        base._Ready();
        _overview = GetNode<Label>("%ProfilerOverview");
        _budgetBar = GetNode<Control>("%ProfilerBudget");
    }

    public void Refresh()
    {
        var p = PerfProfiler.Instance;
        double fps = p.FrameMsAvg > 0 ? 1000.0 / p.FrameMsAvg : 0;
        _overview.Text = $"FPS {fps:F0}  Frame {p.FrameMsAvg:F1}ms  Tier {p.QualityName}";
        _budgetBar.QueueRedraw();
    }

    public override void _Draw()
    {
        if (_budgetBar == null) return;
        var p = PerfProfiler.Instance;
        double total = Math.Max(0.001, p.SimMsAvg + p.RenderMsAvg + p.UiMsAvg);
        var rect = _budgetBar.GetRect();
        var global = _budgetBar.GlobalPosition;
        var local = global - GlobalPosition;
        var drawRect = new Rect2(local, rect.Size);

        DrawStyleBox(EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.PanelDarker, EcoSimThemeBuilder.Edge), drawRect);

        float simW = (float)(p.SimMsAvg / total * drawRect.Size.X);
        float renderW = (float)(p.RenderMsAvg / total * drawRect.Size.X);
        float uiW = (float)(p.UiMsAvg / total * drawRect.Size.X);
        float otherW = Math.Max(0, drawRect.Size.X - simW - renderW - uiW);

        DrawRect(new Rect2(drawRect.Position, new Vector2(simW, drawRect.Size.Y)), new Color(0.3f, 0.7f, 0.4f));
        DrawRect(new Rect2(drawRect.Position + new Vector2(simW, 0), new Vector2(renderW, drawRect.Size.Y)), new Color(0.35f, 0.55f, 0.9f));
        DrawRect(new Rect2(drawRect.Position + new Vector2(simW + renderW, 0), new Vector2(uiW, drawRect.Size.Y)), new Color(0.85f, 0.65f, 0.25f));
        DrawRect(new Rect2(drawRect.Position + new Vector2(simW + renderW + uiW, 0), new Vector2(otherW, drawRect.Size.Y)), new Color(0.45f, 0.45f, 0.45f));
    }
}
