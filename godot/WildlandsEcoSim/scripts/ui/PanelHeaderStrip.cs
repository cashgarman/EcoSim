using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Panel header strip matching .panel-head in wildlands-ecosim.html.</summary>
public partial class PanelHeaderStrip : PanelContainer
{
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeHeaderStrip());
    }
}
