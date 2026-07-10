using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Toast shown when control transfers to a new body (killer, sibling, or same species).</summary>
public partial class TransferNoticePanel : PanelContainer
{
    private Label _label = null!;
    private double _hideAt;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        TopLevel = true;
        ZIndex = 150;

        AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());
        AddThemeConstantOverride("margin_left", 14);
        AddThemeConstantOverride("margin_right", 14);
        AddThemeConstantOverride("margin_top", 8);
        AddThemeConstantOverride("margin_bottom", 8);

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(_label, EcoSimFonts.Scaled7, EcoSimThemeBuilder.Text);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (Visible && Time.GetTicksMsec() * 0.001 > _hideAt)
        {
            Visible = false;
        }
    }

    public void ShowTransfer(TransferEvent ev, SpeciesCatalog catalog, SimState state)
    {
        var fromDef = catalog.Get(ev.From.Sp);
        var toDef = catalog.Get(ev.To.Sp);
        string cause = CreatureNotify.RefineDeathCause(ev.From);
        _label.Text = ev.Reason switch
        {
            "killer" => $"{fromDef.Emoji} Your {fromDef.Label} was killed — you are now the {toDef.Emoji} {toDef.Label} that killed it!",
            "sibling" => $"{fromDef.Emoji} Your {fromDef.Label} {CreatureNotify.DeathCausePhrase(cause)} — you live on as its sibling.",
            _ => $"{fromDef.Emoji} Your {fromDef.Label} {CreatureNotify.DeathCausePhrase(cause)} — you continue as another {toDef.Label}.",
        };
        _hideAt = Time.GetTicksMsec() * 0.001 + 5.0;
        Visible = true;
        Reposition();
    }

    private void Reposition()
    {
        var rect = GetViewport().GetVisibleRect();
        ResetSize();
        Vector2 size = GetMinimumSize();
        CustomMinimumSize = new Vector2(Mathf.Min(520, rect.Size.X - 40), 0);
        Position = new Vector2(rect.Size.X * 0.5f - Mathf.Max(size.X, CustomMinimumSize.X) * 0.5f, 64);
    }
}
