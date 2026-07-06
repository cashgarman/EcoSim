using EcoSim.Core.Data;
using EcoSim.Core.Rng;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class GenPanel : DraggablePanel
{
    [Signal]
    public delegate void GenerateRequestedEventHandler();

    [Signal]
    public delegate void RestockRequestedEventHandler();

    private SpinBox _seed = null!;
    private HBoxContainer _sizeRow = null!;
    private HSlider _sea = null!;
    private HSlider _temp = null!;
    private HSlider _moist = null!;
    private HSlider _relief = null!;
    private HSlider _animals = null!;
    private Label _seaVal = null!;
    private Label _tempVal = null!;
    private Label _moistVal = null!;
    private Label _reliefVal = null!;
    private Label _animVal = null!;
    private string _size = "s";
    private readonly Dictionary<string, Button> _sizeButtons = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        LayoutKey = "gen";
        base._Ready();

        _seed = GetNode<SpinBox>("%SeedBox");
        _sizeRow = GetNode<HBoxContainer>("%SizeRow");
        _sea = GetNode<HSlider>("%SeaSlider");
        _temp = GetNode<HSlider>("%TempSlider");
        _moist = GetNode<HSlider>("%MoistSlider");
        _relief = GetNode<HSlider>("%ReliefSlider");
        _animals = GetNode<HSlider>("%AnimalsSlider");
        _seaVal = GetNode<Label>("%SeaVal");
        _tempVal = GetNode<Label>("%TempVal");
        _moistVal = GetNode<Label>("%MoistVal");
        _reliefVal = GetNode<Label>("%ReliefVal");
        _animVal = GetNode<Label>("%AnimVal");

        BuildSizeButtons();
        _seed.Value = 42;
        _sea.ValueChanged += _ => UpdateLabels();
        _temp.ValueChanged += _ => UpdateLabels();
        _moist.ValueChanged += _ => UpdateLabels();
        _relief.ValueChanged += _ => UpdateLabels();
        _animals.ValueChanged += _ => UpdateLabels();
        GetNode<Button>("%RandSeedBtn").Pressed += () => _seed.Value = GlobalRng.Ri(1, 999999);
        GetNode<Button>("%GenerateWorldBtn").Pressed += () => EmitSignal(SignalName.GenerateRequested);
        GetNode<Button>("%RestockBtn").Pressed += () => EmitSignal(SignalName.RestockRequested);
        StyleFieldLabels();
        UpdateLabels();
    }

    private void StyleFieldLabels()
    {
        foreach (Node row in GetNode<VBoxContainer>("%PanelBody").GetChildren())
        {
            if (row is not HBoxContainer hbox) continue;
            foreach (Node child in hbox.GetChildren())
            {
                if (child is Label label && child.Name.ToString().EndsWith("Label"))
                {
                    EcoSimFonts.StyleDimLabel(label);
                }
            }
        }
    }

    private void BuildSizeButtons()
    {
        (string key, string label)[] sizes =
        [
            ("s", "25"), ("m", "64"), ("l", "100"), ("xl", "400"), ("xxl", "900"),
        ];
        foreach (var (key, label) in sizes)
        {
            var btn = new Button { Text = label };
            btn.Pressed += () => SelectSize(key);
            _sizeRow.AddChild(btn);
            _sizeButtons[key] = btn;
        }
        SelectSize("s");
    }

    private void SelectSize(string key)
    {
        _size = key;
        foreach (var kv in _sizeButtons)
        {
            kv.Value.Modulate = kv.Key == key ? EcoSimThemeBuilder.Gold : Colors.White;
        }
    }

    private void UpdateLabels()
    {
        _seaVal.Text = $"{_sea.Value * 100:F0}%";
        _tempVal.Text = TempLabel(_temp.Value);
        _moistVal.Text = MoistLabel(_moist.Value);
        _reliefVal.Text = ReliefLabel(_relief.Value);
        _animVal.Text = AnimalsLabel(_animals.Value);
    }

    private static string TempLabel(double v) => v switch
    {
        < 0.33 => "Cold",
        < 0.66 => "Temperate",
        _ => "Warm",
    };

    private static string MoistLabel(double v) => v switch
    {
        < 0.33 => "Dry",
        < 0.66 => "Balanced",
        _ => "Wet",
    };

    private static string ReliefLabel(double v) => v switch
    {
        < 0.35 => "Low",
        < 0.7 => "Medium",
        _ => "High",
    };

    private static string AnimalsLabel(double v) => v switch
    {
        < 0.25 => "Sparse",
        < 0.6 => "Normal",
        _ => "Dense",
    };

    public WorldGenConfig BuildConfig() => new()
    {
        Size = _size,
        Sea = _sea.Value,
        Temp = _temp.Value,
        Moist = _moist.Value,
        Relief = _relief.Value,
        Animals = _animals.Value,
    };

    public uint Seed => (uint)_seed.Value;
}
