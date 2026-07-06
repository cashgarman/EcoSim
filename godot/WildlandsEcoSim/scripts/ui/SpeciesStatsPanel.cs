using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class SpeciesStatsPanel : DraggablePanel
{
    private Label _title = null!;
    private PopGraph _graph = null!;
    private Label _hpVal = null!;
    private Label _hunVal = null!;
    private Label _thiVal = null!;
    private Label _eneVal = null!;
    private ProgressBar _hpBar = null!;
    private ProgressBar _hunBar = null!;
    private ProgressBar _thiBar = null!;
    private ProgressBar _eneBar = null!;
    private Label _summary = null!;
    private VBoxContainer _deaths = null!;
    private SpeciesCatalog? _catalog;
    private string? _speciesKey;

    public override void _Ready()
    {
        LayoutKey = "speciestats";
        Visible = false;
        base._Ready();
        _title = GetNode<Label>("%SpeciesStatsTitle");
        _graph = GetNode<PopGraph>("%SpeciesStatsGraph");
        _hpVal = GetNode<Label>("%SsHpVal");
        _hunVal = GetNode<Label>("%SsHunVal");
        _thiVal = GetNode<Label>("%SsThiVal");
        _eneVal = GetNode<Label>("%SsEneVal");
        _hpBar = GetNode<ProgressBar>("%SsHpBar");
        _hunBar = GetNode<ProgressBar>("%SsHunBar");
        _thiBar = GetNode<ProgressBar>("%SsThiBar");
        _eneBar = GetNode<ProgressBar>("%SsEneBar");
        _summary = GetNode<Label>("%SpeciesStatsSummary");
        _deaths = GetNode<VBoxContainer>("%SpeciesStatsDeaths");
        EcoSimThemeBuilder.StyleNeedBar(_hpBar, EcoSimThemeBuilder.Hp);
        EcoSimThemeBuilder.StyleNeedBar(_hunBar, EcoSimThemeBuilder.Hunger);
        EcoSimThemeBuilder.StyleNeedBar(_thiBar, EcoSimThemeBuilder.Thirst);
        EcoSimThemeBuilder.StyleNeedBar(_eneBar, EcoSimThemeBuilder.Energy);
    }

    public void Bind(SpeciesCatalog catalog)
    {
        _catalog = catalog;
        _graph.Bind(catalog);
    }

    public void ShowSpecies(string? speciesKey, SimSession session, SpeciesStatsTracker stats)
    {
        if (string.IsNullOrEmpty(speciesKey) || _catalog == null)
        {
            Visible = false;
            _speciesKey = null;
            return;
        }

        _speciesKey = speciesKey;
        Visible = true;
        var def = _catalog.Get(speciesKey);
        _title.Text = $"{def.Emoji} {def.Label}";
        _graph.SetHighlight(speciesKey);
        _graph.Sample(session);

        double hp = 0, hun = 0, thi = 0, ene = 0;
        int n = 0;
        foreach (var c in session.State.Creatures)
        {
            if (c.Dead || c.Sp != speciesKey) continue;
            hp += c.Hp;
            hun += c.Hunger;
            thi += c.Thirst;
            ene += c.Energy;
            n++;
        }

        if (n > 0)
        {
            hp /= n; hun /= n; thi /= n; ene /= n;
        }

        _hpVal.Text = $"{hp:F0}%";
        _hunVal.Text = $"{hun:F0}%";
        _thiVal.Text = $"{thi:F0}%";
        _eneVal.Text = $"{ene:F0}%";
        _hpBar.Value = hp;
        _hunBar.Value = hun;
        _thiBar.Value = thi;
        _eneBar.Value = ene;

        var entry = stats.Get(speciesKey);
        int birthRate = stats.BirthRateLastDay(speciesKey, session.State.TGlobal);
        _summary.Text = $"Pop {n}  Deaths {entry.TotalDied}  Ever {entry.TotalBorn}  Births/day {birthRate}";

        _deaths.GetChildren().ToList().ForEach(c => c.QueueFree());
        foreach (var kv in entry.DeathsByKey.OrderByDescending(kv => kv.Value))
        {
            _deaths.AddChild(new Label { Text = $"{kv.Key}: {kv.Value}" });
        }
    }
}
