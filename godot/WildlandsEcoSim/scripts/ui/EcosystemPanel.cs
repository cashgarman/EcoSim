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
    private string? _hoveredSpecies;
    private readonly Dictionary<string, int> _prevCounts = new(StringComparer.Ordinal);

    public string? LockedSpecies => _lockedSpecies;

    public override void _Ready()
    {
        LayoutKey = "ecosystem";
        base._Ready();
        _graph = GetNode<PopGraph>("%PopGraph");
        _rows = GetNode<VBoxContainer>("%SpeciesRows");
        _rows.AddThemeConstantOverride("separation", 4);
    }

    public void Bind(SpeciesCatalog catalog)
    {
        _catalog = catalog;
        _graph.Bind(catalog);
        _rows.GetChildren().ToList().ForEach(c => c.QueueFree());
        _prevCounts.Clear();

        foreach (string sp in catalog.SpeciesKeys)
        {
            var def = catalog.Get(sp);
            var row = BuildSpeciesRow(sp, def);
            _rows.AddChild(row);
            _prevCounts[sp] = 0;
        }
    }

    private PanelContainer BuildSpeciesRow(string sp, SpeciesDefinition def)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeInsetPanel());
        row.CustomMinimumSize = new Vector2(0, 24);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);

        var dotWrap = new PanelContainer();
        dotWrap.AddThemeStyleboxOverride("panel",
            EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.SpeciesColor(def), EcoSimThemeBuilder.Edge, 1));
        dotWrap.CustomMinimumSize = new Vector2(10, 10);

        var name = new Label
        {
            Text = $"{def.Emoji} {def.Label}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        EcoSimFonts.ApplyFont(name, EcoSimFonts.Scaled7);

        var countWrap = new HBoxContainer();
        countWrap.AddThemeConstantOverride("separation", 3);
        var delta = new Label { Text = "" };
        EcoSimFonts.ApplyFont(delta, EcoSimFonts.Scaled8);
        var count = new Label { Text = "0" };
        EcoSimFonts.ApplyFont(count, EcoSimFonts.Scaled8, EcoSimThemeBuilder.Gold);
        countWrap.AddChild(delta);
        countWrap.AddChild(count);

        hbox.AddChild(dotWrap);
        hbox.AddChild(name);
        hbox.AddChild(countWrap);
        row.AddChild(hbox);

        row.SetMeta("species", sp);
        row.MouseEntered += () =>
        {
            _hoveredSpecies = sp;
            UpdateRowStyles();
            _graph.SetHighlight(_lockedSpecies ?? _hoveredSpecies);
        };
        row.MouseExited += () =>
        {
            if (_hoveredSpecies == sp) _hoveredSpecies = null;
            UpdateRowStyles();
            _graph.SetHighlight(_lockedSpecies ?? _hoveredSpecies);
        };
        row.GuiInput += e => OnRowInput(e, sp, row);
        return row;
    }

    public void Refresh(SimSession session)
    {
        if (_catalog == null) return;
        _graph.Sample(session);
        _graph.SetHighlight(_lockedSpecies ?? _hoveredSpecies);

        foreach (Node child in _rows.GetChildren())
        {
            if (child is not PanelContainer row) continue;
            string sp = row.GetMeta("species").AsString();
            int count = session.State.Creatures.Count(c => c.Sp == sp && !c.Dead);
            var def = _catalog.Get(sp);

            if (row.GetChild(0) is HBoxContainer hbox)
            {
                if (hbox.GetChild(1) is Label name)
                {
                    name.Text = $"{def.Emoji} {def.Label}";
                }

                if (hbox.GetChild(2) is HBoxContainer countWrap && countWrap.GetChildCount() >= 2)
                {
                    var delta = countWrap.GetChild(0) as Label;
                    int prev = _prevCounts.GetValueOrDefault(sp, count);
                    int diff = count - prev;
                    _prevCounts[sp] = count;

                    if (delta != null)
                    {
                        if (diff > 0)
                        {
                            delta.Text = "▲";
                            EcoSimFonts.ApplyFont(delta, EcoSimFonts.Scaled8, EcoSimThemeBuilder.PopDeltaUp);
                        }
                        else if (diff < 0)
                        {
                            delta.Text = "▼";
                            EcoSimFonts.ApplyFont(delta, EcoSimFonts.Scaled8, EcoSimThemeBuilder.PopDeltaDown);
                        }
                        else
                        {
                            delta.Text = "";
                        }
                    }

                    if (countWrap.GetChild(1) is Label countLabel)
                    {
                        countLabel.Text = count.ToString();
                    }
                }
            }

            bool active = sp == _lockedSpecies;
            bool hovered = sp == _hoveredSpecies;
            row.AddThemeStyleboxOverride("panel", EcoSimThemeBuilder.MakeSpeciesRowStyle(active, hovered));
        }
    }

    private void UpdateRowStyles()
    {
        foreach (Node child in _rows.GetChildren())
        {
            if (child is not PanelContainer row) continue;
            string sp = row.GetMeta("species").AsString();
            row.AddThemeStyleboxOverride("panel",
                EcoSimThemeBuilder.MakeSpeciesRowStyle(sp == _lockedSpecies, sp == _hoveredSpecies));
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
