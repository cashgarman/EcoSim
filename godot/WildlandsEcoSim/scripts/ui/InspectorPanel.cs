using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class InspectorPanel : DraggablePanel
{
    private Label _header = null!;
    private ProgressBar _hp = null!;
    private ProgressBar _hunger = null!;
    private ProgressBar _thirst = null!;
    private ProgressBar _energy = null!;
    private GridContainer _genes = null!;
    private SpeciesCatalog? _catalog;

    public override void _Ready()
    {
        LayoutKey = "inspect";
        base._Ready();
        _header = GetNode<Label>("%InspectHeader");
        _hp = GetNode<ProgressBar>("%HpBar");
        _hunger = GetNode<ProgressBar>("%HungerBar");
        _thirst = GetNode<ProgressBar>("%ThirstBar");
        _energy = GetNode<ProgressBar>("%EnergyBar");
        _genes = GetNode<GridContainer>("%GeneGrid");
    }

    public void Bind(SpeciesCatalog catalog)
    {
        _catalog = catalog;
        _genes.GetChildren().ToList().ForEach(c => c.QueueFree());

        foreach (string key in _catalog.GeneKeys)
        {
            string label = _catalog.GeneLabel.TryGetValue(key, out var lbl) ? lbl : key;
            _genes.AddChild(new Label { Text = label });
            _genes.AddChild(new Label { Text = "—" });
        }
    }

    public void Refresh(Creature? c)
    {
        if (c == null || _catalog == null)
        {
            _header.Text = "Click a creature to inspect";
            SetBar(_hp, 0);
            SetBar(_hunger, 0);
            SetBar(_thirst, 0);
            SetBar(_energy, 0);
            ClearGenes();
            return;
        }

        var def = _catalog.Get(c.Sp);
        string sex = c.Sex == "male" ? "♂" : "♀";
        _header.Text = $"{def.Emoji} {def.Label} #{c.Id} {sex}  gen {c.Gen}  {c.State}";
        SetBar(_hp, c.Hp);
        SetBar(_hunger, c.Hunger);
        SetBar(_thirst, c.Thirst);
        SetBar(_energy, c.Energy);

        int row = 0;
        foreach (string key in _catalog.GeneKeys)
        {
            int valIdx = row * 2 + 1;
            if (valIdx < _genes.GetChildCount() && _genes.GetChild(valIdx) is Label valLabel)
            {
                valLabel.Text = c.Genome[key].ToString("F2");
            }
            row++;
        }
    }

    private void ClearGenes()
    {
        for (int i = 1; i < _genes.GetChildCount(); i += 2)
        {
            if (_genes.GetChild(i) is Label val)
            {
                val.Text = "—";
            }
        }
    }

    private static void SetBar(ProgressBar bar, double value)
    {
        bar.Value = value;
    }
}
