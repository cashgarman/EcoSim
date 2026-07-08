using Godot;

namespace WildlandsEcoSim.UI;

public partial class PauseMenuPanel : Control
{
    [Signal]
    public delegate void ResumedEventHandler();

    [Signal]
    public delegate void QuitRequestedEventHandler();

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        TopLevel = true;
        ZIndex = 300;

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
        };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(280, 0);
        panel.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());
        panel.AddThemeConstantOverride("margin_left", 16);
        panel.AddThemeConstantOverride("margin_right", 16);
        panel.AddThemeConstantOverride("margin_top", 14);
        panel.AddThemeConstantOverride("margin_bottom", 14);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);

        var title = new Label
        {
            Text = "Paused",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.StylePanelTitle(title);

        var resume = new Button { Text = "Resume" };
        EcoSimThemeBuilder.StylePrimaryButton(resume);
        resume.Pressed += OnResumePressed;

        var quit = new Button { Text = "Quit" };
        EcoSimThemeBuilder.StyleDangerButton(quit);
        quit.Pressed += OnQuitPressed;

        vbox.AddChild(title);
        vbox.AddChild(resume);
        vbox.AddChild(quit);
        panel.AddChild(vbox);

        GetViewport().SizeChanged += FitToViewport;
        FitToViewport();
    }

    public void ShowMenu()
    {
        FitToViewport();
        Visible = true;
    }

    public void HideMenu()
    {
        Visible = false;
    }

    private void FitToViewport()
    {
        var rect = GetViewport().GetVisibleRect();
        Position = Vector2.Zero;
        Size = rect.Size;
    }

    private void OnResumePressed()
    {
        EmitSignal(SignalName.Resumed);
    }

    private void OnQuitPressed()
    {
        EmitSignal(SignalName.QuitRequested);
    }
}
