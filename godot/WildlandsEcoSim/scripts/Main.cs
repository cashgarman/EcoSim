using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.Render;

namespace WildlandsEcoSim;

public partial class Main : Control
{
    private EcoSimHost _host = null!;
    private GameApp _gameApp = null!;
    private WorldRenderer _world = null!;
    private WorldCamera _camera = null!;

    private Label _dayLabel = null!;
    private Label _popLabel = null!;
    private Label _inspectLabel = null!;
    private HSlider _speedSlider = null!;
    private SpinBox _seedBox = null!;
    private OptionButton _sizeOption = null!;

    public override void _Ready()
    {
        _host = GetNode<EcoSimHost>("/root/EcoSimHost");
        _gameApp = GetNode<GameApp>("/root/GameApp");

        _dayLabel = GetNode<Label>("%DayLabel");
        _popLabel = GetNode<Label>("%PopLabel");
        _inspectLabel = GetNode<Label>("%InspectLabel");
        _speedSlider = GetNode<HSlider>("%SpeedSlider");
        _seedBox = GetNode<SpinBox>("%SeedBox");
        _sizeOption = GetNode<OptionButton>("%SizeOption");

        _sizeOption.AddItem("s", 0);
        _sizeOption.AddItem("m", 1);
        _sizeOption.Select(0);
        _seedBox.Value = 42;
        _speedSlider.Value = 2;

        var viewport = GetNode<SubViewport>("%WorldViewport");
        _world = viewport.GetNode<WorldRenderer>("WorldRoot");
        _camera = viewport.GetNode<WorldCamera>("WorldRoot/Camera2D");

        GetNode<Button>("%GenerateButton").Pressed += OnGenerate;
        _speedSlider.ValueChanged += OnSpeedChanged;
        _gameApp.SimTicked += OnSimTicked;

        if (!Engine.IsEditorHint())
        {
            GenerateWorld();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Space)
        {
            _gameApp.TogglePause();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnGenerate()
    {
        GenerateWorld();
    }

    private void GenerateWorld()
    {
        string size = _sizeOption.GetItemText(_sizeOption.Selected);
        uint seed = (uint)_seedBox.Value;
        _host.GenerateWorld(size, seed);
        var session = _host.Session!;
        session.State.Speed = _speedSlider.Value;
        _gameApp.Paused = false;

        _world.BindWorld(session, _host.Species!);
        _camera.CenterOnWorld();
        UpdateHud();
    }

    private void OnSpeedChanged(double value)
    {
        if (_host.Session != null && !_gameApp.Paused)
        {
            _host.Session.State.Speed = value;
        }
    }

    private void OnSimTicked()
    {
        UpdateHud();
    }

    private void UpdateHud()
    {
        var session = _host.Session;
        if (session == null) return;

        int alive = session.Creatures.AliveCount();
        _dayLabel.Text = $"Day {session.State.Day}";
        _popLabel.Text = $"Pop {alive}";
        _inspectLabel.Text = FormatInspector(session.State.Selected);
    }

    private static string FormatInspector(Creature? c)
    {
        if (c == null) return "Click a creature to inspect";
        return $"{c.Sp} #{c.Id}  {c.State}\nHP {c.Hp:F0}  Hunger {c.Hunger:F0}  Thirst {c.Thirst:F0}  Energy {c.Energy:F0}";
    }
}
