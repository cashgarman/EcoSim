using EcoSim.Core.Data;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Modal shown when the player's animal breeds: continue as parent or play as a newborn.</summary>
public partial class BirthChoicePanel : Control
{
    [Signal]
    public delegate void ContinueChosenEventHandler();

    [Signal]
    public delegate void NewbornChosenEventHandler(int newbornId);

    private Label _body = null!;
    private int _newbornId;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        TopLevel = true;
        ZIndex = 250;

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.45f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Stop };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(400, 0) };
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
            Text = "A new generation!",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.StylePanelTitle(title);

        _body = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.ApplyFont(_body, EcoSimFonts.Body, EcoSimThemeBuilder.Text);

        var playNewborn = new Button { Text = "Play as newborn" };
        EcoSimThemeBuilder.StylePrimaryButton(playNewborn);
        playNewborn.Pressed += () =>
        {
            Visible = false;
            EmitSignal(SignalName.NewbornChosen, _newbornId);
        };

        var stay = new Button { Text = "Continue as parent" };
        EcoSimFonts.StylePanelUiButton(stay);
        stay.Pressed += () =>
        {
            Visible = false;
            EmitSignal(SignalName.ContinueChosen);
        };

        vbox.AddChild(title);
        vbox.AddChild(_body);
        vbox.AddChild(playNewborn);
        vbox.AddChild(stay);
        panel.AddChild(vbox);

        GetViewport().SizeChanged += FitToViewport;
        FitToViewport();
    }

    public void ShowChoice(SpeciesDefinition def, int litterSize, int newbornId, int points)
    {
        _newbornId = newbornId;
        string litter = litterSize == 1 ? "1 newborn" : $"{litterSize} newborns";
        _body.Text = $"{def.Emoji} Your bloodline continues: {litter}!\n" +
            $"+1 generation point ({points} total for {def.Label}).";
        FitToViewport();
        Visible = true;
    }

    private void FitToViewport()
    {
        var rect = GetViewport().GetVisibleRect();
        Position = Vector2.Zero;
        Size = rect.Size;
    }
}
