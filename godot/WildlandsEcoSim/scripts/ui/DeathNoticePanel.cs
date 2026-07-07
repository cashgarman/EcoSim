using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class DeathNoticePanel : PanelContainer
{
    private Label _bodyLabel = null!;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        TopLevel = true;
        ZIndex = 200;
        SetAnchorsPreset(LayoutPreset.Center);
        OffsetLeft = -190;
        OffsetTop = -72;
        OffsetRight = 190;
        OffsetBottom = 72;
        CustomMinimumSize = new Vector2(380, 0);

        AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());
        AddThemeConstantOverride("margin_left", 12);
        AddThemeConstantOverride("margin_right", 12);
        AddThemeConstantOverride("margin_top", 10);
        AddThemeConstantOverride("margin_bottom", 10);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);

        var title = new Label
        {
            Text = "Your animal died",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.StylePanelTitle(title);

        _bodyLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.ApplyFont(_bodyLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Text);

        var dismiss = new Button { Text = "Dismiss" };
        EcoSimFonts.StylePanelUiButton(dismiss);
        dismiss.Pressed += HideNotice;

        vbox.AddChild(title);
        vbox.AddChild(_bodyLabel);
        vbox.AddChild(dismiss);
        AddChild(vbox);
    }

    public void ShowNotice(Creature creature, SpeciesCatalog catalog, SimState state)
    {
        _bodyLabel.Text = CreatureNotify.FormatFollowedDeathMessage(catalog, creature, state);
        Visible = true;
    }

    public void HideNotice()
    {
        Visible = false;
    }
}
