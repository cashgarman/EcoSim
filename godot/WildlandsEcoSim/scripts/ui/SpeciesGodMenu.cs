using EcoSim.Core.Data;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class SpeciesGodMenu : PopupPanel
{
    private Label _title = null!;
    private Button _killAll = null!;
    private string? _speciesKey;

    [Signal]
    public delegate void KillAllRequestedEventHandler(string speciesKey);

    public override void _Ready()
    {
        _title = GetNode<Label>("%GodMenuTitle");
        _killAll = GetNode<Button>("%GodKillAll");
        _killAll.Pressed += () =>
        {
            if (!string.IsNullOrEmpty(_speciesKey))
            {
                EmitSignal(SignalName.KillAllRequested, _speciesKey);
            }
            Hide();
        };
    }

    public void OpenFor(string speciesKey, SpeciesCatalog catalog, Vector2 globalPos)
    {
        _speciesKey = speciesKey;
        var def = catalog.Get(speciesKey);
        _title.Text = $"{def.Emoji} {def.Label} GOD";
        Position = (Vector2I)globalPos;
        Popup();
    }
}
