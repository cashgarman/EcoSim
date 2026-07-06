using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class InspectorPanel : DraggablePanel
{
    private Label _header = null!;
    private VBoxContainer _statsTab = null!;
    private VBoxContainer _storyTab = null!;
    private Label _hpVal = null!;
    private Label _hunVal = null!;
    private Label _thiVal = null!;
    private Label _eneVal = null!;
    private ProgressBar _hp = null!;
    private ProgressBar _hunger = null!;
    private ProgressBar _thirst = null!;
    private ProgressBar _energy = null!;
    private GridContainer _genes = null!;
    private RichTextLabel _storyLog = null!;
    private SpeciesCatalog? _catalog;
    private bool _storyMode;

    public override void _Ready()
    {
        LayoutKey = "inspect";
        Visible = false;
        base._Ready();
        _header = GetNode<Label>("%InspectHeader");
        _statsTab = GetNode<VBoxContainer>("%StatsTab");
        _storyTab = GetNode<VBoxContainer>("%StoryTab");
        _hpVal = GetNode<Label>("%HpVal");
        _hunVal = GetNode<Label>("%HunVal");
        _thiVal = GetNode<Label>("%ThiVal");
        _eneVal = GetNode<Label>("%EneVal");
        _hp = GetNode<ProgressBar>("%HpBar");
        _hunger = GetNode<ProgressBar>("%HungerBar");
        _thirst = GetNode<ProgressBar>("%ThirstBar");
        _energy = GetNode<ProgressBar>("%EnergyBar");
        _genes = GetNode<GridContainer>("%GeneGrid");
        _storyLog = GetNode<RichTextLabel>("%LifeStoryLog");
        EcoSimThemeBuilder.StyleNeedBar(_hp, EcoSimThemeBuilder.Hp);
        EcoSimThemeBuilder.StyleNeedBar(_hunger, EcoSimThemeBuilder.Hunger);
        EcoSimThemeBuilder.StyleNeedBar(_thirst, EcoSimThemeBuilder.Thirst);
        EcoSimThemeBuilder.StyleNeedBar(_energy, EcoSimThemeBuilder.Energy);
        GetNode<Button>("%StatsTabBtn").Pressed += () => SetTab(false);
        GetNode<Button>("%StoryTabBtn").Pressed += () => SetTab(true);
        SetTab(false);
    }

    private void SetTab(bool story)
    {
        _storyMode = story;
        _statsTab.Visible = !story;
        _storyTab.Visible = story;
        GetNode<Button>("%StatsTabBtn").Modulate = story ? Colors.White : EcoSimThemeBuilder.Gold;
        GetNode<Button>("%StoryTabBtn").Modulate = story ? EcoSimThemeBuilder.Gold : Colors.White;
    }

    public void Bind(SpeciesCatalog catalog)
    {
        _catalog = catalog;
        _genes.GetChildren().ToList().ForEach(c => c.QueueFree());

        foreach (string key in catalog.GeneKeys)
        {
            string label = catalog.GeneLabel.TryGetValue(key, out var lbl) ? lbl : key;
            var chip = new PanelContainer();
            chip.AddThemeStyleboxOverride("panel", EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.PanelDarker, EcoSimThemeBuilder.Edge));
            var v = new VBoxContainer();
            v.AddChild(new Label { Text = label, Modulate = EcoSimThemeBuilder.Dim });
            v.AddChild(new Label { Text = "—", Name = "Val" });
            chip.AddChild(v);
            chip.SetMeta("gene", key);
            _genes.AddChild(chip);
        }
    }

    public void Refresh(Creature? c)
    {
        if (c == null || _catalog == null || c.Dead)
        {
            Visible = false;
            return;
        }

        Visible = true;
        var def = _catalog.Get(c.Sp);
        string sex = c.Sex == "male" ? "♂" : "♀";
        _header.Text = $"{def.Emoji} {def.Label} #{c.Id} {sex}  gen {c.Gen}  {c.State}";
        SetBar(_hp, _hpVal, c.Hp);
        SetBar(_hunger, _hunVal, c.Hunger);
        SetBar(_thirst, _thiVal, c.Thirst);
        SetBar(_energy, _eneVal, c.Energy);

        foreach (Node child in _genes.GetChildren())
        {
            if (child is not PanelContainer chip) continue;
            string key = chip.GetMeta("gene").AsString();
            if (chip.GetChild(0) is VBoxContainer v && v.GetNode<Label>("Val") is Label val)
            {
                val.Text = c.Genome[key].ToString("F2");
            }
        }

        if (c.LifeStory != null)
        {
            var lines = c.LifeStory.Events.OrderByDescending(e => e.Seq).Take(40)
                .Select(e => $"Day {e.Day}: {e.Kind} {e.Decision ?? e.Detail ?? ""}");
            _storyLog.Text = string.Join("\n", lines);
        }
        else
        {
            _storyLog.Text = "";
        }
    }

    private static void SetBar(ProgressBar bar, Label label, double value)
    {
        bar.Value = value;
        label.Text = $"{value:F0}%";
    }
}
