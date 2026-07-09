using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>
/// Left pane of the BT editor: editable species thresholds (the "blackboard variables")
/// plus a read-only live snapshot of the selected creature (Self / Target / State / needs).
/// </summary>
public partial class BtBlackboardPane : VBoxContainer
{
    public event Action? ThresholdsChanged;

    private BtEditorDocument? _doc;
    private PanelContainer _thresholdsFrame = null!;
    private PanelContainer _liveFrame = null!;
    private VBoxContainer _thresholdList = null!;
    private Label _liveHeader = null!;
    private Label _selfLabel = null!;
    private Label _targetLabel = null!;
    private Label _stateLabel = null!;
    private ProgressBar _hp = null!;
    private ProgressBar _hunger = null!;
    private ProgressBar _thirst = null!;
    private ProgressBar _energy = null!;
    private readonly List<SchemaThresholdKey> _thresholdSpecs = [];

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(210, 0);
        AddThemeConstantOverride("separation", 8);

        var thresholdsBody = new VBoxContainer();
        thresholdsBody.AddThemeConstantOverride("separation", 6);

        var title = new Label { Text = "Blackboard" };
        EcoSimFonts.StylePanelTitle(title, EcoSimFonts.SpeciesStatsTitle);
        thresholdsBody.AddChild(title);

        var thHeader = new Label { Text = "Thresholds" };
        EcoSimFonts.StyleDimLabel(thHeader, EcoSimFonts.Scaled7);
        thresholdsBody.AddChild(thHeader);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _thresholdList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _thresholdList.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_thresholdList);
        thresholdsBody.AddChild(scroll);

        _thresholdsFrame = EcoSimThemeBuilder.MakeStoneFrame(thresholdsBody);
        AddChild(_thresholdsFrame);

        var liveBody = new VBoxContainer();
        liveBody.AddThemeConstantOverride("separation", 3);

        _liveHeader = new Label { Text = "Live creature" };
        EcoSimFonts.StyleDimLabel(_liveHeader, EcoSimFonts.Scaled7);
        liveBody.AddChild(_liveHeader);

        _selfLabel = MakeInfo();
        _targetLabel = MakeInfo();
        _stateLabel = MakeInfo();
        liveBody.AddChild(_selfLabel);
        liveBody.AddChild(_targetLabel);
        liveBody.AddChild(_stateLabel);

        _hp = AddNeed(liveBody, "Health", EcoSimThemeBuilder.Hp);
        _hunger = AddNeed(liveBody, "Fullness", EcoSimThemeBuilder.Hunger);
        _thirst = AddNeed(liveBody, "Hydration", EcoSimThemeBuilder.Thirst);
        _energy = AddNeed(liveBody, "Energy", EcoSimThemeBuilder.Energy);

        _liveFrame = EcoSimThemeBuilder.MakeStoneFrame(liveBody, expandVertical: false);
        _liveFrame.Visible = false;
        AddChild(_liveFrame);
    }

    private Label MakeInfo()
    {
        var l = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        EcoSimFonts.ApplyFont(l, EcoSimFonts.Scaled7, EcoSimThemeBuilder.Text);
        return l;
    }

    private ProgressBar AddNeed(VBoxContainer parent, string name, Color color)
    {
        var label = new Label { Text = name };
        EcoSimFonts.StyleDimLabel(label, EcoSimFonts.Scaled7);
        parent.AddChild(label);
        var bar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false };
        EcoSimThemeBuilder.StyleNeedBar(bar, color);
        parent.AddChild(bar);
        return bar;
    }

    public void SetSchema(BehaviorSchema schema)
    {
        _thresholdSpecs.Clear();
        _thresholdSpecs.AddRange(schema.ThresholdKeySpecs);
    }

    public void SetDocument(BtEditorDocument? doc)
    {
        _doc = doc;
        RebuildThresholds();
    }

    private void RebuildThresholds()
    {
        foreach (Node child in _thresholdList.GetChildren()) child.QueueFree();
        if (_doc == null) return;

        IEnumerable<string> keys = _thresholdSpecs.Count > 0
            ? _thresholdSpecs.Select(s => s.Key)
            : _doc.Thresholds.Keys.OrderBy(k => k, StringComparer.Ordinal);

        foreach (string key in keys)
        {
            if (!_doc.Thresholds.TryGetValue(key, out double value)) continue;

            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 4);

            var label = new Label { Text = key, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            EcoSimFonts.ApplyFont(label, EcoSimFonts.Scaled7, EcoSimThemeBuilder.Text);
            label.TooltipText = _thresholdSpecs.FirstOrDefault(s => s.Key == key)?.Label ?? key;
            row.AddChild(label);

            var spin = new SpinBox
            {
                MinValue = 0,
                MaxValue = 100,
                Step = 1,
                Value = value,
                CustomMinimumSize = new Vector2(64, 0),
            };
            EcoSimFonts.ApplyFont(spin.GetLineEdit(), EcoSimFonts.Scaled7);
            string capturedKey = key;
            spin.ValueChanged += v =>
            {
                if (_doc != null)
                {
                    _doc.Thresholds[capturedKey] = v;
                    ThresholdsChanged?.Invoke();
                }
            };
            row.AddChild(spin);
            _thresholdList.AddChild(row);
        }
    }

    public void UpdateLive(Creature? creature, SpeciesCatalog species, SimState state)
    {
        if (creature == null || creature.Dead)
        {
            _liveFrame.Visible = false;
            return;
        }

        _liveFrame.Visible = true;
        var def = species.Get(creature.Sp);
        _selfLabel.Text = $"Self: {def.Emoji} {def.Label} #{creature.Id}";
        _targetLabel.Text = creature.Target.HasValue ? $"Target: #{creature.Target.Value}" : "Target: none";
        _stateLabel.Text = $"State: {CreatureBehaviorLabels.GetDisplayLabel(creature, state)}";
        _hp.Value = creature.Hp;
        _hunger.Value = creature.Hunger;
        _thirst.Value = creature.Thirst;
        _energy.Value = creature.Energy;
    }
}
