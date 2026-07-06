using Godot;

namespace WildlandsEcoSim.UI;

public partial class ProfilerDetailPanel : PanelContainer
{
    private RichTextLabel _cpuTree = null!;
    private RichTextLabel _gpuStats = null!;
    private bool _open;
    private int _tab;

    public bool PanelOpen
    {
        get => _open;
        set
        {
            _open = value;
            Visible = value;
        }
    }

    public override void _Ready()
    {
        Visible = false;
        AnchorRight = 1;
        AnchorBottom = 1;
        OffsetLeft = -320;
        OffsetTop = 100;
        OffsetRight = -8;
        OffsetBottom = -100;

        var tabs = new HBoxContainer();
        var cpuBtn = new Button { Text = "CPU" };
        var gpuBtn = new Button { Text = "GPU" };
        cpuBtn.Pressed += () => { _tab = 0; UpdateTab(); };
        gpuBtn.Pressed += () => { _tab = 1; UpdateTab(); };
        tabs.AddChild(cpuBtn);
        tabs.AddChild(gpuBtn);

        _cpuTree = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        _gpuStats = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddChild(tabs);
        vbox.AddChild(_cpuTree);
        vbox.AddChild(_gpuStats);
        AddChild(vbox);
        UpdateTab();
    }

    public void Refresh()
    {
        var p = PerfProfiler.Instance;
        _cpuTree.Text = p.GetCpuTreeText();
        _gpuStats.Text = p.GetGpuStatsText();
    }

    private void UpdateTab()
    {
        _cpuTree.Visible = _tab == 0;
        _gpuStats.Visible = _tab == 1;
    }
}
