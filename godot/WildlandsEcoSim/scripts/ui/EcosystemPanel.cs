using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class EcosystemPanel : DraggablePanel
{
    [Signal]
    public delegate void SpeciesLockedEventHandler(string speciesKey);

    [Signal]
    public delegate void SpeciesGodMenuEventHandler(string speciesKey, Vector2 globalPos);

    private PopGraph _graph = null!;
    private VBoxContainer _rows = null!;
    private SpeciesCatalog? _catalog;
    private string? _lockedSpecies;

    public string? LockedSpecies => _lockedSpecies;

    public override void _Ready()
    {
        LayoutKey = "ecosystem";
        base._Ready();
        _graph = GetNode<PopGraph>("%PopGraph");
        _rows = GetNode<VBoxContainer>("%SpeciesRows");
    }

    public void Bind(SpeciesCatalog catalog)
    {
        _catalog = catalog;
        _graph.Bind(catalog);
        _rows.GetChildren().ToList().ForEach(c => c.QueueFree());

        foreach (string sp in catalog.SpeciesKeys)
        {
            var def = catalog.Get(sp);
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.PanelDarker, EcoSimThemeBuilder.Edge));
            var hbox = new HBoxContainer();
            var emoji = new Label { Text = def.Emoji };
            var name = new Label { Text = def.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var count = new Label { Text = "0" };
            hbox.AddChild(emoji);
            hbox.AddChild(name);
            hbox.AddChild(count);
            row.AddChild(hbox);
            row.SetMeta("species", sp);
            row.GuiInput += e => OnRowInput(e, sp, row);
            _rows.AddChild(row);
        }
    }

    public void Refresh(SimSession session)
    {
        if (_catalog == null) return;
        _graph.Sample(session);
        _graph.SetHighlight(_lockedSpecies);

        foreach (Node child in _rows.GetChildren())
        {
            if (child is not PanelContainer row) continue;
            string sp = row.GetMeta("species").AsString();
            int count = session.State.Creatures.Count(c => c.Sp == sp && !c.Dead);
            var def = _catalog.Get(sp);
            if (row.GetChild(0) is HBoxContainer hbox && hbox.GetChildCount() >= 3)
            {
                ((Label)hbox.GetChild(0)).Text = def.Emoji;
                ((Label)hbox.GetChild(1)).Text = def.Label;
                ((Label)hbox.GetChild(2)).Text = count.ToString();
            }
            var border = sp == _lockedSpecies ? EcoSimThemeBuilder.Gold : EcoSimThemeBuilder.Edge;
            row.AddThemeStyleboxOverride("panel", EcoSimThemeBuilder.MakeFlat(
                sp == _lockedSpecies ? EcoSimThemeBuilder.Panel : EcoSimThemeBuilder.PanelDarker, border));
        }
    }

    private void OnRowInput(InputEvent e, string sp, PanelContainer row)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _lockedSpecies = _lockedSpecies == sp ? null : sp;
                EmitSignal(SignalName.SpeciesLocked, _lockedSpecies ?? "");
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                EmitSignal(SignalName.SpeciesGodMenu, sp, row.GlobalPosition);
            }
        }
    }
}
