using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Modal evolution tech tree for the possessed species; spends generation points.</summary>
public partial class EvolutionPanel : Control
{
    [Signal]
    public delegate void ClosedEventHandler();

    private Label _title = null!;
    private Label _pointsLabel = null!;
    private VBoxContainer _nodeList = null!;
    private SimSession? _session;
    private string _species = "";

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        TopLevel = true;
        ZIndex = 240;

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.45f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Stop };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(520, 0) };
        panel.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());
        panel.AddThemeConstantOverride("margin_left", 16);
        panel.AddThemeConstantOverride("margin_right", 16);
        panel.AddThemeConstantOverride("margin_top", 14);
        panel.AddThemeConstantOverride("margin_bottom", 14);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        EcoSimFonts.StylePanelTitle(_title);
        vbox.AddChild(_title);

        _pointsLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        EcoSimFonts.ApplyFont(_pointsLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        vbox.AddChild(_pointsLabel);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 340),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _nodeList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _nodeList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_nodeList);
        vbox.AddChild(scroll);

        var close = new Button { Text = "Close" };
        EcoSimFonts.StylePanelUiButton(close);
        close.Pressed += Close;
        vbox.AddChild(close);

        GetViewport().SizeChanged += FitToViewport;
        FitToViewport();
    }

    public void Open(SimSession session, string species)
    {
        _session = session;
        _species = species;
        RebuildNodes();
        FitToViewport();
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        EmitSignal(SignalName.Closed);
    }

    private void RebuildNodes()
    {
        foreach (var child in _nodeList.GetChildren())
        {
            child.QueueFree();
        }

        if (_session == null) return;
        var def = _session.Species.Get(_species);
        int points = _session.Progress.Points(_species);
        _title.Text = $"{def.Emoji} {def.Label} Evolution";
        _pointsLabel.Text = $"🧬 {points} generation point{(points == 1 ? "" : "s")}";

        var tree = _session.Evolutions.Catalog.TreeFor(_species);
        if (tree == null || tree.Nodes.Count == 0)
        {
            var none = new Label
            {
                Text = "No evolutions available for this species.",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            EcoSimFonts.ApplyFont(none, EcoSimFonts.Body, EcoSimThemeBuilder.Dim);
            _nodeList.AddChild(none);
            return;
        }

        foreach (var node in tree.Nodes)
        {
            _nodeList.AddChild(BuildNodeRow(node));
        }
    }

    private Control BuildNodeRow(EvolutionNode node)
    {
        bool owned = _session!.Progress.HasPurchased(_species, node.Id);
        bool canBuy = _session.Evolutions.CanPurchase(_species, node.Id, out string reason);

        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", EcoSimThemeBuilder.MakeFlat(
            owned ? EcoSimThemeBuilder.PanelDark : EcoSimThemeBuilder.PanelDarker,
            owned ? EcoSimThemeBuilder.Gold : EcoSimThemeBuilder.Edge, 1));

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 10);
        row.AddChild(hbox);

        var textBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var name = new Label { Text = owned ? $"✓ {node.Label}" : node.Label };
        EcoSimFonts.ApplyFont(name, EcoSimFonts.Scaled7,
            owned ? EcoSimThemeBuilder.Gold : EcoSimThemeBuilder.Text);
        var desc = new Label { Text = node.Desc, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        EcoSimFonts.ApplyFont(desc, EcoSimFonts.Small, EcoSimThemeBuilder.Dim);
        textBox.AddChild(name);
        textBox.AddChild(desc);
        if (!owned && !canBuy && !string.IsNullOrEmpty(reason))
        {
            var lockLabel = new Label { Text = $"🔒 {reason}" };
            EcoSimFonts.ApplyFont(lockLabel, EcoSimFonts.Small, EcoSimThemeBuilder.PopDeltaDown);
            textBox.AddChild(lockLabel);
        }
        hbox.AddChild(textBox);

        if (!owned)
        {
            var buy = new Button
            {
                Text = $"Evolve ({node.Cost})",
                Disabled = !canBuy,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            if (canBuy) EcoSimThemeBuilder.StylePrimaryButton(buy);
            else EcoSimFonts.StylePanelUiButton(buy);
            string nodeId = node.Id;
            buy.Pressed += () => OnBuy(nodeId);
            hbox.AddChild(buy);
        }

        return row;
    }

    private void OnBuy(string nodeId)
    {
        if (_session == null) return;
        if (_session.Evolutions.Purchase(_species, nodeId))
        {
            RebuildNodes();
        }
    }

    private void FitToViewport()
    {
        var rect = GetViewport().GetVisibleRect();
        Position = Vector2.Zero;
        Size = rect.Size;
    }
}
