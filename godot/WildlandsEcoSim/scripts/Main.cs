using Godot;

namespace WildlandsEcoSim;

public partial class Main : Control
{
    private Label _status = null!;

    public override void _Ready()
    {
        _status = GetNode<Label>("%StatusLabel");
        var host = GetNode<EcoSimHost>("/root/EcoSimHost");
        _status.Text = $"Wildlands EcoSim — {host.Species.SpeciesKeys.Count} species loaded";
    }
}
