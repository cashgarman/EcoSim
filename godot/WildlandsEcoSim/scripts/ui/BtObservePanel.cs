using System.Text.Json;
using System.Text.Json.Nodes;
using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class BtObservePanel : DraggablePanel
{
    private Label _title = null!;
    private Label _emptyLabel = null!;
    private VBoxContainer _content = null!;
    private Label _statusLabel = null!;
    private Label _legendLabel = null!;
    private ScrollContainer _graphScroll = null!;
    private BtGraphView _graphView = null!;

    public override void _Ready()
    {
        LayoutKey = "btobserve";
        Visible = false;
        CustomMinimumSize = new Vector2(480, 520);
        ZIndex = 55;

        var rootVBox = new VBoxContainer();
        rootVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rootVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(rootVBox);

        var head = new HBoxContainer { Name = "PanelHead" };
        _title = new Label
        {
            Text = "Behavior Tree",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        EcoSimFonts.StylePanelTitle(_title);
        head.AddChild(_title);
        rootVBox.AddChild(head);

        var body = new VBoxContainer
        {
            Name = "PanelBody",
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 6);
        rootVBox.AddChild(body);

        base._Ready();

        _emptyLabel = new Label
        {
            Text = "Select a creature to inspect BT",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(_emptyLabel, EcoSimFonts.Body, EcoSimThemeBuilder.Dim);
        body.AddChild(_emptyLabel);

        _content = new VBoxContainer
        {
            Visible = false,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _content.AddThemeConstantOverride("separation", 4);
        body.AddChild(_content);

        _statusLabel = MakeInfoLabel();
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(_statusLabel);

        _legendLabel = MakeInfoLabel();
        _legendLabel.Text = "● committed path   ○ eval sweep   green=passed   red=failed   gold=selected";
        _legendLabel.AddThemeColorOverride("font_color", EcoSimThemeBuilder.Dim);
        _content.AddChild(_legendLabel);

        _graphScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 320),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        _graphView = new BtGraphView
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        _graphScroll.AddChild(_graphView);
        _content.AddChild(_graphScroll);
    }

    private static Label MakeInfoLabel()
    {
        var label = new Label();
        EcoSimFonts.ApplyFont(label, EcoSimFonts.Scaled6);
        return label;
    }

    public void Refresh(SimSession? session)
    {
        if (_content == null) return;

        if (session?.State.Selected is not { } creature || creature.Dead)
        {
            _emptyLabel.Visible = true;
            _content.Visible = false;
            _title.Text = "Behavior Tree";
            _graphView.SetDocument(null, null, null, null);
            return;
        }

        _emptyLabel.Visible = false;
        _content.Visible = true;

        var def = session.Species.Get(creature.Sp);
        _title.Text = $"{def.Emoji} {def.Label} BT";

        var cfg = def.BehaviorConfig;
        if (cfg == null)
        {
            _statusLabel.Text = "No behavior config for species.";
            _graphView.SetDocument(null, null, null, null);
            return;
        }

        var trace = session.BehaviorTree.EvaluateWithTrace(creature, session.Creatures);
        var (proposed, wouldApply) = session.BehaviorTree.PeekDecision(creature, session.Creatures);

        string committedAction = ActionLabel(creature.BtAction, cfg) ?? creature.State;
        double dwell = creature.StateCommittedSince > 0
            ? session.State.TGlobal - creature.StateCommittedSince
            : 0;
        bool atWater = Navigation.AtWaterEdge(session.State, creature.X, creature.Y);
        int tier = creature.BtAction != null ? BehaviorPriority.GetTier(creature.BtAction) : -1;

        string proposedText = "none";
        if (proposed != null)
        {
            string proposedAction = ActionLabel(proposed.Action, cfg) ?? proposed.Action["state"]?.GetValue<string>() ?? "?";
            proposedText = wouldApply ? $"{proposedAction} (apply)" : $"{proposedAction} BLOCKED";
        }

        _statusLabel.Text =
            $"Committed: {CreatureBehaviorLabels.GetDisplayLabel(creature, session.State)} · {committedAction} · dwell {dwell:F1}s\n" +
            $"Proposed: {proposedText} · thirst {creature.Thirst:F0} hunger {creature.Hunger:F0} energy {creature.Energy:F0} · water {atWater} · tier {tier}";

        var doc = BehaviorGraphAdapter.ToFlatDocument(cfg);
        ApplyLayoutSidecar(doc, cfg.BehaviorKey);
        BehaviorGraphLayout.ApplyAutoLayout(doc);
        _graphView.SetDocument(
            doc,
            creature.BtBranchUid,
            proposed?.BranchUid,
            trace.Steps);
        _graphView.Size = _graphView.CustomMinimumSize;
    }

    private static void ApplyLayoutSidecar(BehaviorFlatDocument doc, string behaviorKey)
    {
        string path = DataPaths.BehaviorEditorLayout(behaviorKey);
        if (!File.Exists(path)) return;

        try
        {
            var layout = JsonSerializer.Deserialize<BehaviorLayoutSidecar>(File.ReadAllText(path));
            if (layout != null)
            {
                BehaviorGraphAdapter.ApplyLayout(doc, layout);
            }
        }
        catch
        {
            // Optional sidecar; ignore malformed files.
        }
    }

    private static string? ActionLabel(JsonObject? action, BehaviorConfig cfg)
    {
        if (action == null) return null;
        if (action.TryGetPropertyValue("label", out var labelNode))
        {
            return labelNode?.GetValue<string>();
        }

        string? nodeId = action["id"]?.GetValue<string>();
        if (nodeId != null && cfg.Actions.TryGetValue(nodeId, out var def))
        {
            return def["label"]?.GetValue<string>() ?? nodeId;
        }

        return action["state"]?.GetValue<string>();
    }
}
