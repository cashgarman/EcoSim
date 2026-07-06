using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class GenPanel : DraggablePanel
{
    private SpinBox _seed = null!;
    private OptionButton _size = null!;
    private HSlider _sea = null!;
    private HSlider _temp = null!;
    private HSlider _moist = null!;
    private HSlider _relief = null!;
    private HSlider _animals = null!;

    public override void _Ready()
    {
        LayoutKey = "gen";
        base._Ready();

        _seed = GetNode<SpinBox>("%SeedBox");
        _size = GetNode<OptionButton>("%SizeOption");
        _sea = GetNode<HSlider>("%SeaSlider");
        _temp = GetNode<HSlider>("%TempSlider");
        _moist = GetNode<HSlider>("%MoistSlider");
        _relief = GetNode<HSlider>("%ReliefSlider");
        _animals = GetNode<HSlider>("%AnimalsSlider");

        foreach (string key in new[] { "s", "m", "l", "xl", "xxl" })
        {
            _size.AddItem(key);
        }
        _size.Select(0);
        _seed.Value = 42;
    }

    public WorldGenConfig BuildConfig()
    {
        return new WorldGenConfig
        {
            Size = _size.GetItemText(_size.Selected),
            Sea = _sea.Value,
            Temp = _temp.Value,
            Moist = _moist.Value,
            Relief = _relief.Value,
            Animals = _animals.Value,
        };
    }

    public uint Seed => (uint)_seed.Value;
}
