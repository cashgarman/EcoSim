using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class InspectorPanel : DraggablePanel
{
    [Signal]
    public delegate void CreatureLinkPressedEventHandler(int creatureId);

    private Label _header = null!;
    private VBoxContainer _statsTab = null!;
    private VBoxContainer _storyTab = null!;
    private Button _statsTabBtn = null!;
    private Button _storyTabBtn = null!;
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
    private RichTextLabel _lineageLog = null!;
    private SpeciesCatalog? _catalog;
    private bool _storyMode;

    public override void _Ready()
    {
        LayoutKey = "inspect";
        Visible = false;
        base._Ready();
        _header = Req<Label>("InspectHeader");
        _statsTab = Req<VBoxContainer>("StatsTab");
        _storyTab = Req<VBoxContainer>("StoryTab");
        _statsTabBtn = Req<Button>("StatsTabBtn");
        _storyTabBtn = Req<Button>("StoryTabBtn");
        _hpVal = Req<Label>("HpVal");
        _hunVal = Req<Label>("HunVal");
        _thiVal = Req<Label>("ThiVal");
        _eneVal = Req<Label>("EneVal");
        _hp = Req<ProgressBar>("HpBar");
        _hunger = Req<ProgressBar>("HungerBar");
        _thirst = Req<ProgressBar>("ThirstBar");
        _energy = Req<ProgressBar>("EnergyBar");
        _genes = Req<GridContainer>("GeneGrid");
        _storyLog = Req<RichTextLabel>("LifeStoryLog");
        EcoSimThemeBuilder.StyleNeedBar(_hp, EcoSimThemeBuilder.Hp);
        EcoSimThemeBuilder.StyleNeedBar(_hunger, EcoSimThemeBuilder.Hunger);
        EcoSimThemeBuilder.StyleNeedBar(_thirst, EcoSimThemeBuilder.Thirst);
        EcoSimThemeBuilder.StyleNeedBar(_energy, EcoSimThemeBuilder.Energy);
        _statsTabBtn.Pressed += () => SetTab(false);
        _storyTabBtn.Pressed += () => SetTab(true);
        EcoSimFonts.StylePanelTitle(_header, EcoSimFonts.InspectorTitle);
        _header.HorizontalAlignment = HorizontalAlignment.Center;
        EcoSimFonts.ApplyFont(_storyLog, EcoSimFonts.Scaled6);
        StyleNeedLabels(_statsTab);
        EcoSimFonts.StyleTabButton(_statsTabBtn);
        EcoSimFonts.StyleTabButton(_storyTabBtn);
        SetupLineagePanel();
        SetTab(false);
    }

    private void SetupLineagePanel()
    {
        _statsTab.SizeFlagsVertical = SizeFlags.ExpandFill;

        var bottomRow = new HBoxContainer
        {
            Name = "StatsBottomRow",
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        bottomRow.AddThemeConstantOverride("separation", 8);

        int geneIndex = _genes.GetIndex();
        _genes.GetParent()?.RemoveChild(_genes);
        _genes.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        bottomRow.AddChild(_genes);

        var lineagePanel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        lineagePanel.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeInsetPanel());
        lineagePanel.AddThemeConstantOverride("margin_left", 6);
        lineagePanel.AddThemeConstantOverride("margin_right", 6);
        lineagePanel.AddThemeConstantOverride("margin_top", 6);
        lineagePanel.AddThemeConstantOverride("margin_bottom", 6);

        _lineageLog = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = false,
            ScrollActive = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
        };
        EcoSimFonts.ApplyFont(_lineageLog, EcoSimFonts.Scaled6);
        _lineageLog.AddThemeColorOverride("default_color", EcoSimThemeBuilder.Text);
        _lineageLog.MetaClicked += OnLineageMetaClicked;

        lineagePanel.AddChild(_lineageLog);
        bottomRow.AddChild(lineagePanel);
        _statsTab.AddChild(bottomRow);
        _statsTab.MoveChild(bottomRow, geneIndex);
    }

    private void OnLineageMetaClicked(Variant meta)
    {
        if (!int.TryParse(meta.AsString(), out int creatureId)) return;
        EmitSignal(SignalName.CreatureLinkPressed, creatureId);
    }

    private static T Req<T>(Node root, string name) where T : Node
    {
        return root.FindChild(name, true, false) as T
            ?? throw new InvalidOperationException($"Missing node: {name}");
    }

    private T Req<T>(string name) where T : Node => Req<T>(this, name);

    private static void StyleNeedLabels(VBoxContainer statsTab)
    {
        foreach (Node child in statsTab.GetChildren())
        {
            if (child is not HBoxContainer row) continue;
            foreach (Node lab in row.GetChildren())
            {
                if (lab is Label label)
                {
                    EcoSimFonts.StyleDimLabel(label);
                }
            }
        }
    }

    private void SetTab(bool story)
    {
        _storyMode = story;
        _statsTab.Visible = !story;
        _storyTab.Visible = story;
        _statsTabBtn.Modulate = story ? Colors.White : EcoSimThemeBuilder.Gold;
        _storyTabBtn.Modulate = story ? EcoSimThemeBuilder.Gold : Colors.White;
    }

    public void Bind(SpeciesCatalog catalog)
    {
        _catalog = catalog;
        _genes.GetChildren().ToList().ForEach(c => c.QueueFree());

        foreach (string key in catalog.GeneKeys)
        {
            string label = catalog.GeneLabel.TryGetValue(key, out var lbl) ? lbl : key;
            var chip = new PanelContainer();
            chip.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeInsetPanel());
            var v = new VBoxContainer();
            var nameLabel = new Label { Text = label };
            var valLabel = new Label { Text = "—", Name = "Val" };
            EcoSimFonts.StyleDimLabel(nameLabel);
            EcoSimFonts.StyleBodyLabel(valLabel, EcoSimFonts.Small);
            v.AddChild(nameLabel);
            v.AddChild(valLabel);
            chip.AddChild(v);
            chip.SetMeta("gene", key);
            _genes.AddChild(chip);
        }
    }

    public void Refresh(Creature? c, CreatureSystem? creatures = null, SimState? state = null)
    {
        if (c == null || _catalog == null || c.Dead)
        {
            Visible = false;
            _lineageLog.Text = "";
            return;
        }

        Visible = true;
        Callable.From(() => PanelLayoutService.ClampToViewport(this)).CallDeferred();
        var def = _catalog.Get(c.Sp);
        string sex = c.Sex == "male" ? "♂" : "♀";
        string behavior = state != null
            ? CreatureBehaviorLabels.GetDisplayLabel(c, state)
            : c.State;
        _header.Text = $"{def.Emoji} {def.Label} #{c.Id} {sex}  gen {c.Gen}  {behavior}";
        RefreshLineage(c, creatures);
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

    private void RefreshLineage(Creature c, CreatureSystem? creatures)
    {
        if (creatures == null)
        {
            _lineageLog.Text = "";
            return;
        }

        bool hasParents = c.ParentIds.Count > 0;
        bool hasOffspring = c.OffspringIds.Count > 0;
        if (!hasParents && !hasOffspring)
        {
            _lineageLog.Text = "[color=#8a9a7a]No lineage recorded.[/color]";
            return;
        }

        var lines = new List<string>();
        if (hasParents)
        {
            lines.Add("[color=#8a9a7a]Parents[/color]");
            foreach (int pid in c.ParentIds)
            {
                lines.Add(FormatLineageEntry(creatures.GetById(pid), pid));
            }
        }

        if (hasOffspring)
        {
            if (hasParents)
            {
                lines.Add("");
            }

            lines.Add("[color=#8a9a7a]Offspring[/color]");
            foreach (int oid in c.OffspringIds)
            {
                lines.Add(FormatLineageEntry(creatures.GetById(oid), oid));
            }
        }

        _lineageLog.Text = string.Join("\n", lines);
    }

    private string FormatLineageEntry(Creature? rel, int fallbackId)
    {
        if (rel == null)
        {
            return $"[color=#666666]Unknown #{fallbackId} †[/color]";
        }

        var def = _catalog!.Get(rel.Sp);
        string sex = rel.Sex == "male" ? "♂" : "♀";
        string label = $"{def.Emoji} {def.Label} #{rel.Id} {sex}";

        if (rel.Dead)
        {
            return $"[color=#666666]{label} †[/color]";
        }

        return $"[url={rel.Id}][u][color=#ffdc3c]{label}[/color][/u][/url]";
    }

    private static void SetBar(ProgressBar bar, Label label, double value)
    {
        bar.Value = value;
        label.Text = $"{value:F0}%";
    }
}
