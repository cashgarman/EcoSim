using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>
/// Modal shown when control transfers to a new body (killer, sibling, or same species).
/// Pauses the sim until the player dismisses it.
/// </summary>
public partial class TransferNoticePanel : Control
{
    [Signal]
    public delegate void ConfirmedEventHandler();

    private Label _body = null!;

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

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
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
            Text = "Control transferred",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.StylePanelTitle(title);

        _body = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.ApplyFont(_body, EcoSimFonts.Body, EcoSimThemeBuilder.Text);

        var ok = new Button { Text = "OK" };
        EcoSimThemeBuilder.StylePrimaryButton(ok);
        ok.Pressed += Dismiss;

        vbox.AddChild(title);
        vbox.AddChild(_body);
        vbox.AddChild(ok);
        panel.AddChild(vbox);

        GetViewport().SizeChanged += FitToViewport;
        FitToViewport();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey key && key.Pressed && !key.Echo
            && key.Keycode is Key.Enter or Key.KpEnter or Key.Space)
        {
            Dismiss();
            GetViewport().SetInputAsHandled();
        }
    }

    public void ShowTransfer(TransferEvent ev, SpeciesCatalog catalog, SimState state)
    {
        var fromDef = catalog.Get(ev.From.Sp);
        var toDef = catalog.Get(ev.To.Sp);
        string cause = CreatureNotify.RefineDeathCause(ev.From);
        _body.Text = ev.Reason switch
        {
            "killer" => $"{fromDef.Emoji} Your {fromDef.Label} was killed — you are now the {toDef.Emoji} {toDef.Label} that killed it!",
            "sibling" => $"{fromDef.Emoji} Your {fromDef.Label} {CreatureNotify.DeathCausePhrase(cause)} — you live on as its sibling.",
            _ => $"{fromDef.Emoji} Your {fromDef.Label} {CreatureNotify.DeathCausePhrase(cause)} — you continue as another {toDef.Label}.",
        };
        FitToViewport();
        Visible = true;
    }

    private void Dismiss()
    {
        Visible = false;
        EmitSignal(SignalName.Confirmed);
    }

    private void FitToViewport()
    {
        var rect = GetViewport().GetVisibleRect();
        Position = Vector2.Zero;
        Size = rect.Size;
    }
}
