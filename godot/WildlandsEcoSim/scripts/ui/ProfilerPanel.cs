using Godot;

namespace WildlandsEcoSim.UI;

public partial class ProfilerPanel : DraggablePanel
{
    private Label _overview = null!;
    private Label _badges = null!;
    private Label _legend = null!;
    private Label _cpuSection = null!;
    private Control _budgetBar = null!;
    private Control _sparkline = null!;
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

        var body = GetNode<VBoxContainer>("VBox/PanelBody");
        _overview = GetNode<Label>("%ProfilerOverview");
        _budgetBar = GetNode<Control>("%ProfilerBudget");

        var overviewTitle = EcoSimThemeBuilder.MakeGoldTitle("Overview");
        EcoSimFonts.ApplyFont(overviewTitle, EcoSimFonts.Scaled7);
        body.AddChild(overviewTitle);
        body.MoveChild(overviewTitle, _overview.GetIndex());

        _badges = new Label();
        EcoSimFonts.ApplyFont(_badges, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        body.AddChild(_badges);
        body.MoveChild(_badges, _overview.GetIndex() + 1);

        _sparkline = new Control { CustomMinimumSize = new Vector2(0, 24) };
        body.AddChild(_sparkline);
        body.MoveChild(_sparkline, _budgetBar.GetIndex());

        var budgetTitle = EcoSimThemeBuilder.MakeGoldTitle("Frame budget");
        EcoSimFonts.ApplyFont(budgetTitle, EcoSimFonts.Scaled7);
        body.AddChild(budgetTitle);
        body.MoveChild(budgetTitle, _budgetBar.GetIndex());

        _legend = new Label
        {
            Text = "sim  render  ui  other",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(_legend, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        body.AddChild(_legend);
        body.MoveChild(_legend, _budgetBar.GetIndex() + 1);

        _cpuSection = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        EcoSimFonts.ApplyFont(_cpuSection, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        var cpuTitle = EcoSimThemeBuilder.MakeGoldTitle("CPU Simulation");
        EcoSimFonts.ApplyFont(cpuTitle, EcoSimFonts.Scaled7);
        body.AddChild(cpuTitle);
        body.AddChild(_cpuSection);

        EcoSimFonts.ApplyFont(_overview, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
    }

    public void Refresh()
    {
        var p = PerfProfiler.Instance;
        double fps = p.FrameMsAvg > 0 ? 1000.0 / p.FrameMsAvg : 0;
        _overview.Text = $"FPS {fps:F0}   Frame {p.FrameMsAvg:F1}ms   Tier {p.QualityName}";
        _badges.Text = "sim: cpu   render: canvas   mode: sandbox";
        _cpuSection.Text =
            $"rebuildGrid {p.SimMsAvg * 0.12:F1}ms\n" +
            $"stepCreatures {p.SimMsAvg * 0.62:F1}ms\n" +
            $"vegGrow {p.SimMsAvg * 0.08:F1}ms\n" +
            $"heartbeat {p.SimMsAvg * 0.04:F1}ms";
        _budgetBar.QueueRedraw();
        _sparkline.QueueRedraw();
    }

    public override void _Draw()
    {
        if (_budgetBar == null) return;
        var p = PerfProfiler.Instance;
        double total = Math.Max(0.001, p.SimMsAvg + p.RenderMsAvg + p.UiMsAvg);
        var rect = _budgetBar.GetRect();
        var local = _budgetBar.GlobalPosition - GlobalPosition;
        var drawRect = new Rect2(local, rect.Size);

        DrawStyleBox(EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.PanelDarker, EcoSimThemeBuilder.Edge, 2), drawRect);

        float simW = (float)(p.SimMsAvg / total * drawRect.Size.X);
        float renderW = (float)(p.RenderMsAvg / total * drawRect.Size.X);
        float uiW = (float)(p.UiMsAvg / total * drawRect.Size.X);
        float otherW = Math.Max(0, drawRect.Size.X - simW - renderW - uiW);

        DrawBudgetSeg(drawRect, drawRect.Position.X, simW, EcoSimThemeBuilder.BudgetSim);
        DrawBudgetSeg(drawRect, drawRect.Position.X + simW, renderW, EcoSimThemeBuilder.BudgetRender);
        DrawBudgetSeg(drawRect, drawRect.Position.X + simW + renderW, uiW, EcoSimThemeBuilder.BudgetUi);
        DrawBudgetSeg(drawRect, drawRect.Position.X + simW + renderW + uiW, otherW, EcoSimThemeBuilder.BudgetOther);

        if (_sparkline != null)
        {
            var sparkRect = new Rect2(_sparkline.GlobalPosition - GlobalPosition, _sparkline.Size);
            DrawStyleBox(UiSliceCatalog.MakeInsetPanel(), sparkRect);
            DrawSparkline(sparkRect, p.FrameMsAvg);
        }
    }

    private void DrawBudgetSeg(Rect2 drawRect, float x, float w, Color color)
    {
        if (w <= 0) return;
        DrawRect(new Rect2(x, drawRect.Position.Y, Math.Max(1, w), drawRect.Size.Y), color);
    }

    private void DrawSparkline(Rect2 rect, double frameMs)
    {
        var font = EcoSimFonts.GetFont();
        float y = rect.Position.Y + rect.Size.Y * 0.5f;
        DrawLine(new Vector2(rect.Position.X + 2, y), new Vector2(rect.Position.X + rect.Size.X - 2, y),
            EcoSimThemeBuilder.Hunger, 1f);
        DrawString(font, new Vector2(rect.Position.X + 4, rect.Position.Y + 10),
            $"{frameMs:F1}ms", HorizontalAlignment.Left, -1, EcoSimFonts.Small, EcoSimThemeBuilder.Dim);
    }
}
