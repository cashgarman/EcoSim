using System.Text.Json.Nodes;
using EcoSim.Core.Behavior;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>
/// Right pane of the BT editor: schema-driven field editors for the selected node.
/// Edits mutate the live <see cref="BtEditorNode"/> JSON and raise <see cref="Changed"/>.
/// </summary>
public partial class BtInspectorPane : VBoxContainer
{
    public event Action? Changed;

    private BehaviorSchema _schema = BehaviorSchema.Load();
    private BtEditorDocument? _doc;
    private BtEditorNode? _node;

    private Label _title = null!;
    private Label _empty = null!;
    private VBoxContainer _fields = null!;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(232, 0);
        AddThemeConstantOverride("separation", 6);

        _title = new Label { Text = "Inspector" };
        EcoSimFonts.StylePanelTitle(_title, EcoSimFonts.SpeciesStatsTitle);
        AddChild(_title);

        _empty = new Label
        {
            Text = "Select a node to edit its properties.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.StyleDimLabel(_empty);
        AddChild(_empty);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _fields = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _fields.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_fields);
        AddChild(scroll);
    }

    public void SetSchema(BehaviorSchema schema) => _schema = schema;

    public void SetContext(BtEditorDocument? doc) => _doc = doc;

    public void ShowNode(string? nodeId)
    {
        _node = _doc?.Get(nodeId);
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (Node child in _fields.GetChildren()) child.QueueFree();

        if (_node == null)
        {
            _empty.Visible = true;
            _title.Text = "Inspector";
            return;
        }

        _empty.Visible = false;
        _title.Text = _node.Type switch
        {
            BehaviorNodeType.Selector => "Selector",
            BehaviorNodeType.Sequence => "Sequence",
            BehaviorNodeType.Action => "Action",
            BehaviorNodeType.Condition => "Condition",
            _ => "Node",
        };

        switch (_node.Type)
        {
            case BehaviorNodeType.Selector:
            case BehaviorNodeType.Sequence:
                BuildComposite();
                break;
            case BehaviorNodeType.Action:
                BuildAction();
                break;
            case BehaviorNodeType.Condition:
                BuildCondition();
                break;
        }
    }

    private void BuildComposite()
    {
        var node = _node!;
        AddOption("Type", ["selector", "sequence"], node.Type == BehaviorNodeType.Sequence ? 1 : 0, idx =>
        {
            node.Type = idx == 1 ? BehaviorNodeType.Sequence : BehaviorNodeType.Selector;
            Raise();
        });
        AddLineEdit("Branch id (optional)", node.RefId ?? "", v =>
        {
            node.RefId = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            Raise();
        });
    }

    private void BuildAction()
    {
        var node = _node!;
        node.Action ??= new JsonObject();
        var a = node.Action;

        AddLineEdit("Name", node.RefId ?? "", v => { node.RefId = v.Trim(); Raise(); });
        AddLineEdit("Label", GetStr(a, "label"), v => { a["label"] = v; Raise(); });
        AddLineEdit("State", GetStr(a, "state"), v => { a["state"] = v; Raise(); });
        AddLineEdit("Goal", GetStr(a, "goal"), v => { a["goal"] = v; Raise(); });
        AddSpin("Speed mult", GetNum(a, "speedMult", 1.0), 0, 3, 0.05, v => { a["speedMult"] = v; Raise(); });

        var tiers = _schema.InterruptTiers;
        if (tiers.Count > 0)
        {
            var items = tiers.Select(t => $"{t.Tier} {t.Label}").ToArray();
            int cur = (int)GetNum(a, "interruptTier", 3);
            int selIdx = tiers.ToList().FindIndex(t => t.Tier == cur);
            AddOption("Interrupt tier", items, selIdx < 0 ? 0 : selIdx, idx => { a["interruptTier"] = tiers[idx].Tier; Raise(); });
        }

        AddSpin("Min commit sec", GetNum(a, "minCommitSec", 0), 0, 10, 0.1, v => { a["minCommitSec"] = v; Raise(); });
        AddCheck("Drink at shore", GetBool(a, "drinkAtShore"), v => { a["drinkAtShore"] = v; Raise(); });
    }

    private void BuildCondition()
    {
        var node = _node!;
        node.Condition ??= new JsonObject();
        var c = node.Condition;

        AddLineEdit("Name", node.RefId ?? "", v => { node.RefId = v.Trim(); Raise(); });

        var ops = _schema.ConditionOpSpecs;
        string curOp = GetStr(c, "op");
        if (ops.Count > 0)
        {
            var items = ops.Select(o => o.Op).ToArray();
            int selIdx = ops.ToList().FindIndex(o => o.Op == curOp);
            AddOption("Op", items, selIdx < 0 ? 0 : selIdx, idx =>
            {
                c["op"] = ops[idx].Op;
                Raise();
                Rebuild(); // params change with op
            });

            var opSpec = ops.FirstOrDefault(o => o.Op == GetStr(c, "op"));
            if (opSpec != null)
            {
                foreach (var p in opSpec.Params)
                {
                    BuildParam(c, p);
                }
            }
        }
        else
        {
            AddLineEdit("Op", curOp, v => { c["op"] = v; Raise(); });
        }
    }

    private void BuildParam(JsonObject c, SchemaParam p)
    {
        switch (p.ParamType)
        {
            case "thresholdKey":
            {
                var keys = _schema.ThresholdKeySpecs.Select(t => t.Key).ToArray();
                if (keys.Length == 0) { AddLineEdit(p.Name, GetStr(c, p.Name), v => { c[p.Name] = v; Raise(); }); break; }
                string cur = c[p.Name]?.GetValue<string>() ?? (p.Default?.GetValue<string>() ?? keys[0]);
                int idx = Array.IndexOf(keys, cur);
                AddOption(p.Name, keys, idx < 0 ? 0 : idx, i => { c[p.Name] = keys[i]; Raise(); });
                break;
            }
            case "int":
                AddSpin(p.Name + (p.Optional ? " (opt)" : ""), GetNum(c, p.Name, 0), -5, 20, 1, v => { c[p.Name] = (int)v; Raise(); });
                break;
            case "bool":
                AddCheck(p.Name, GetBool(c, p.Name), v => { c[p.Name] = v; Raise(); });
                break;
            default:
                AddLineEdit(p.Name, GetStr(c, p.Name), v => { c[p.Name] = v; Raise(); });
                break;
        }
    }

    // ── Field builders ───────────────────────────────────────────────────────

    private void AddLabel(string text)
    {
        var l = new Label { Text = text };
        EcoSimFonts.StyleDimLabel(l);
        _fields.AddChild(l);
    }

    private void AddLineEdit(string label, string value, Action<string> onChange)
    {
        AddLabel(label);
        var edit = new LineEdit { Text = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimFonts.ApplyFont(edit, EcoSimFonts.Small);
        edit.TextChanged += t => onChange(t);
        _fields.AddChild(edit);
    }

    private void AddSpin(string label, double value, double min, double max, double step, Action<double> onChange)
    {
        AddLabel(label);
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimFonts.ApplyFont(spin.GetLineEdit(), EcoSimFonts.Small);
        spin.ValueChanged += v => onChange(v);
        _fields.AddChild(spin);
    }

    private void AddOption(string label, string[] items, int selected, Action<int> onChange)
    {
        AddLabel(label);
        var opt = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimFonts.ApplyFont(opt, EcoSimFonts.Small);
        for (int i = 0; i < items.Length; i++) opt.AddItem(items[i], i);
        if (selected >= 0 && selected < items.Length) opt.Selected = selected;
        opt.ItemSelected += idx => onChange((int)idx);
        _fields.AddChild(opt);
    }

    private void AddCheck(string label, bool value, Action<bool> onChange)
    {
        var chk = new CheckBox { Text = label, ButtonPressed = value };
        EcoSimFonts.ApplyFont(chk, EcoSimFonts.Small);
        chk.Toggled += v => onChange(v);
        _fields.AddChild(chk);
    }

    private void Raise() => Changed?.Invoke();

    private static string GetStr(JsonObject o, string key) => o[key]?.GetValue<string>() ?? "";
    private static double GetNum(JsonObject o, string key, double def)
    {
        try { return o[key]?.GetValue<double>() ?? def; }
        catch { return def; }
    }
    private static bool GetBool(JsonObject o, string key)
    {
        try { return o[key]?.GetValue<bool>() ?? false; }
        catch { return false; }
    }
}
