using EcoSim.Core.Batch;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class BatchTestController : Control
{
    private SpinBox _days = null!;
    private SpinBox _seed = null!;
    private Label _status = null!;
    private EcoSimHost? _host;

    public override void _Ready()
    {
        _host = GetNode<EcoSimHost>("/root/EcoSimHost");
        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 8);

        var title = new Label { Text = "Batch Test Runner" };
        EcoSimFonts.StylePanelTitle(title);
        vbox.AddChild(title);

        _days = new SpinBox { MinValue = 10, MaxValue = 500, Value = 80 };
        _seed = new SpinBox { MinValue = 1, MaxValue = 999999, Value = 42 };
        vbox.AddChild(new Label { Text = "Days" });
        vbox.AddChild(_days);
        vbox.AddChild(new Label { Text = "Seed" });
        vbox.AddChild(_seed);

        var runBtn = new Button { Text = "Run batch" };
        runBtn.Pressed += OnRun;
        vbox.AddChild(runBtn);

        var backBtn = new Button { Text = "Back to sandbox" };
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        vbox.AddChild(backBtn);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        EcoSimFonts.ApplyFont(_status, EcoSimFonts.Scaled7);
        vbox.AddChild(_status);

        AddChild(vbox);
    }

    private void OnRun()
    {
        if (_host == null) return;
        _status.Text = "Running…";
        var session = _host.EnsureSession();
        var harness = new BatchHarness(session);
        harness.Init(new BatchRunConfig
        {
            TargetDays = (int)_days.Value,
            Seed = (uint)_seed.Value,
            Size = "s",
            SampleEveryDays = 10,
        });
        harness.GenerateWorld();
        var result = harness.Run(new BatchRunOptions { TargetDays = (int)_days.Value, SampleEveryDays = 10 });
        int pop = session.Creatures.AliveCount();
        _status.Text = $"Done: outcome={result.Outcome} pop={pop} earlyStop={result.EarlyStop} timedOut={result.TimedOut}";
    }
}
