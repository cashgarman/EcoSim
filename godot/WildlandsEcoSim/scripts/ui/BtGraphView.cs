using System.Text.Json.Nodes;
using EcoSim.Core.Behavior;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class BtGraphView : Control
{
    private static readonly Color BgColor = new("1b1b22");
    private static readonly Color GridDot = new("333340");
    private static readonly Color BodyBg = new("2c2c34");
    private static readonly Color BodyBorder = new("4a4a56");
    private static readonly Color HeaderRoot = new("3dba6a");
    private static readonly Color HeaderSelector = new("e8914f");
    private static readonly Color HeaderSequence = new("a66bd4");
    private static readonly Color HeaderLeaf = new("4f4f57");
    private static readonly Color EdgeColor = new("dcdce6");
    private static readonly Color TextPrimary = new("ececf2");
    private static readonly Color TextMuted = new("9a9aaa");

    private const float HeaderH = 24f;
    private const float Pad = 10f;
    private const float Corner = 8f;

    private BehaviorFlatDocument? _doc;
    private BehaviorConfig? _cfg;
    private string? _committedUid;
    private string? _proposedUid;
    private string? _rootUid;
    private IReadOnlyList<BehaviorTraceStep> _traceSteps = [];
    private readonly Dictionary<string, BehaviorTraceStep> _stepByUid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _nodeHeights = new(StringComparer.Ordinal);
    private readonly HashSet<(string From, string To)> _committedPathEdges = new();
    private readonly HashSet<(string From, string To)> _activeTraceEdges = new();
    private readonly Dictionary<(string From, string To), string> _edgeLabels = new();
    private double _animTime;
    private int _hoverNode = -1;

    public override void _Ready()
    {
        SetProcess(true);
        ClipContents = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _doc == null || _doc.Nodes.Count == 0) return;
        _animTime += delta;
        QueueRedraw();
    }

    public void SetDocument(
        BehaviorFlatDocument? doc,
        BehaviorConfig? cfg,
        string? committedUid,
        string? proposedUid,
        IReadOnlyList<BehaviorTraceStep>? traceSteps)
    {
        _doc = doc;
        _cfg = cfg;
        _committedUid = committedUid;
        _proposedUid = proposedUid;
        _traceSteps = traceSteps ?? [];
        _stepByUid.Clear();
        _nodeHeights.Clear();
        _committedPathEdges.Clear();
        _activeTraceEdges.Clear();
        _edgeLabels.Clear();
        _rootUid = null;

        foreach (var step in _traceSteps)
        {
            _stepByUid[step.Uid] = step;
        }

        if (_doc == null || _doc.Nodes.Count == 0 || _cfg == null)
        {
            CustomMinimumSize = new Vector2(320, 120);
            QueueRedraw();
            return;
        }

        var incoming = new HashSet<string>(_doc.Edges.Select(e => e.To), StringComparer.Ordinal);
        _rootUid = _doc.Nodes.FirstOrDefault(n => !incoming.Contains(n.Uid))?.Uid;

        foreach (var node in _doc.Nodes)
        {
            _nodeHeights[node.Uid] = ComputeNodeHeight(node);
        }

        BehaviorGraphLayout.ApplyAutoLayout(_doc, n => _nodeHeights[n.Uid]);
        BuildPathEdges(committedUid);
        BuildTraceFlowEdges();
        BuildEdgeLabels();

        double maxX = _doc.Nodes.Max(n => n.X) + BehaviorGraphLayout.NodeWidth;
        double maxY = _doc.Nodes.Max(n => n.Y + _nodeHeights[n.Uid]);
        CustomMinimumSize = new Vector2(
            (float)Math.Max(400, maxX + 32),
            (float)Math.Max(280, maxY + 32));
        QueueRedraw();
    }

    private double ComputeNodeHeight(BehaviorFlatNode node)
    {
        var display = BuildDisplay(node);
        int bodyLines = string.IsNullOrEmpty(display.BodyLine2) ? 1 : 2;
        return HeaderH + Pad + bodyLines * 16 + Pad;
    }

    private void BuildEdgeLabels()
    {
        if (_doc == null) return;

        foreach (var edge in _doc.Edges)
        {
            var parent = _doc.Nodes.FirstOrDefault(n => n.Uid == edge.From);
            var child = _doc.Nodes.FirstOrDefault(n => n.Uid == edge.To);
            if (parent?.Type != "selector" || child == null) continue;

            string label = child.Id ?? child.Ref ?? $"branch {edge.Order + 1}";
            if (label.Length > 18) label = label[..16] + "…";
            _edgeLabels[(edge.From, edge.To)] = label;
        }
    }

    private void BuildPathEdges(string? committedUid)
    {
        if (_doc == null || string.IsNullOrEmpty(committedUid)) return;

        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in _doc.Edges)
        {
            parentOf[edge.To] = edge.From;
        }

        string? uid = committedUid;
        while (uid != null && parentOf.TryGetValue(uid, out var parent))
        {
            _committedPathEdges.Add((parent, uid));
            uid = parent;
        }
    }

    private void BuildTraceFlowEdges()
    {
        if (_doc == null) return;

        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in _doc.Edges)
        {
            parentOf[edge.To] = edge.From;
        }

        foreach (var step in _traceSteps)
        {
            if (step.Outcome is not (TraceOutcome.Passed or TraceOutcome.Selected)) continue;
            if (!parentOf.TryGetValue(step.Uid, out var parent)) continue;
            _activeTraceEdges.Add((parent, step.Uid));
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            int hit = HitTestNode(motion.Position);
            if (hit != _hoverNode)
            {
                _hoverNode = hit;
                QueueRedraw();
            }
        }
    }

    private int HitTestNode(Vector2 localPos)
    {
        if (_doc == null) return -1;
        for (int i = _doc.Nodes.Count - 1; i >= 0; i--)
        {
            if (NodeRect(_doc.Nodes[i]).HasPoint(localPos)) return i;
        }
        return -1;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), BgColor, true);
        DrawDotGrid();

        if (_doc == null || _doc.Nodes.Count == 0)
        {
            DrawString(Theme.DefaultFont, new Vector2(16, 28), "No behavior tree", HorizontalAlignment.Left, -1, 13, TextMuted);
            return;
        }

        DrawEdges();
        for (int i = 0; i < _doc.Nodes.Count; i++)
        {
            DrawNodeCard(_doc.Nodes[i], i == _hoverNode);
        }

        DrawEvalSweep();
    }

    private void DrawDotGrid()
    {
        const float spacing = 18f;
        for (float x = spacing; x < Size.X; x += spacing)
        {
            for (float y = spacing; y < Size.Y; y += spacing)
            {
                DrawCircle(new Vector2(x, y), 1.2f, GridDot);
            }
        }
    }

    private void DrawEdges()
    {
        if (_doc == null) return;

        foreach (var edge in _doc.Edges)
        {
            var from = _doc.Nodes.FirstOrDefault(n => n.Uid == edge.From);
            var to = _doc.Nodes.FirstOrDefault(n => n.Uid == edge.To);
            if (from == null || to == null) continue;

            var start = EdgeStart(from);
            var end = EdgeEnd(to);
            bool onCommittedPath = _committedPathEdges.Contains((edge.From, edge.To));
            bool onTrace = _activeTraceEdges.Contains((edge.From, edge.To));

            Color lineColor = onCommittedPath ? EcoSimThemeBuilder.Gold : onTrace ? EcoSimThemeBuilder.Hp : EdgeColor;
            float width = onCommittedPath ? 3f : onTrace ? 2.2f : 2f;

            DrawConnector(start, end, lineColor, width);
            DrawArrowHead(end, start, lineColor);

            if (onCommittedPath || onTrace)
            {
                DrawFlowDot(start, end, onCommittedPath ? EcoSimThemeBuilder.Gold : EcoSimThemeBuilder.Hp);
            }

            if (_edgeLabels.TryGetValue((edge.From, edge.To), out var label))
            {
                var mid = new Vector2((start.X + end.X) * 0.5f, (start.Y + end.Y) * 0.5f);
                DrawBranchPill(mid, label, onCommittedPath || onTrace);
            }
        }
    }

    private void DrawConnector(Vector2 start, Vector2 end, Color color, float width)
    {
        var elbow = new Vector2(end.X, start.Y + (end.Y - start.Y) * 0.45f);
        DrawLine(start, elbow, color, width);
        DrawLine(elbow, end, color, width);
    }

    private void DrawArrowHead(Vector2 tip, Vector2 from, Color color)
    {
        var dir = (tip - from).Normalized();
        if (dir.LengthSquared() < 0.01f) dir = Vector2.Down;
        var left = tip - dir * 9f + dir.Orthogonal() * 5f;
        var right = tip - dir * 9f - dir.Orthogonal() * 5f;
        DrawColoredPolygon(new[] { tip, left, right }, color);
    }

    private void DrawBranchPill(Vector2 center, string label, bool active)
    {
        var font = Theme.DefaultFont;
        var textSize = font.GetStringSize(label, HorizontalAlignment.Left, -1, 10);
        var rect = new Rect2(center - new Vector2(textSize.X * 0.5f + 8, 9), new Vector2(textSize.X + 16, 18));
        DrawRect(rect, active ? HeaderSelector.Darkened(0.1f) : HeaderLeaf, true);
        DrawRect(rect, BodyBorder, false, 1f);
        DrawString(font, rect.Position + new Vector2(8, 13), label, HorizontalAlignment.Left, -1, 10, TextPrimary);
    }

    private void DrawFlowDot(Vector2 start, Vector2 end, Color color)
    {
        var elbow = new Vector2(end.X, start.Y + (end.Y - start.Y) * 0.45f);
        float t = (float)(_animTime % 1.4 / 1.4);
        Vector2 pos = t < 0.5f
            ? start.Lerp(elbow, t * 2f)
            : elbow.Lerp(end, (t - 0.5f) * 2f);
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_animTime * 7f);
        DrawCircle(pos, 3.5f + pulse, color);
    }

    private void DrawEvalSweep()
    {
        if (_traceSteps.Count == 0 || _doc == null) return;

        int sweepIdx = (int)(_animTime / 0.14) % _traceSteps.Count;
        var step = _traceSteps[sweepIdx];
        var node = _doc.Nodes.FirstOrDefault(n => n.Uid == step.Uid);
        if (node == null) return;

        var rect = NodeRect(node);
        float ring = 6f + (float)(_animTime % 0.7 / 0.7) * 14f;
        Color ringColor = step.Outcome switch
        {
            TraceOutcome.Failed => EcoSimThemeBuilder.PopDeltaDown,
            TraceOutcome.Selected => EcoSimThemeBuilder.Gold,
            TraceOutcome.Passed => EcoSimThemeBuilder.Hp,
            _ => TextMuted,
        };
        DrawArc(rect.GetCenter(), ring, 0, Mathf.Tau, 36, ringColor, 2f);
    }

    private void DrawNodeCard(BehaviorFlatNode node, bool hovered)
    {
        var rect = NodeRect(node);
        _stepByUid.TryGetValue(node.Uid, out var step);
        var display = BuildDisplay(node);

        bool isCommitted = node.Uid == _committedUid;
        bool isProposed = node.Uid == _proposedUid && node.Uid != _committedUid;
        bool isFailed = step?.Outcome == TraceOutcome.Failed;
        bool isPassed = step?.Outcome is TraceOutcome.Passed or TraceOutcome.Selected;

        if (isCommitted)
        {
            float glow = 5f + Mathf.Sin((float)_animTime * 4.5f) * 3f;
            DrawRect(rect.Grow(glow), new Color(EcoSimThemeBuilder.Gold, 0.22f), false, 2f);
        }

        Color header = display.HeaderColor;
        if (isFailed) header = header.Darkened(0.25f);
        else if (isPassed) header = header.Lightened(0.08f);

        DrawRoundedCard(rect, header, BodyBg, isCommitted ? EcoSimThemeBuilder.Gold : isProposed ? EcoSimThemeBuilder.Blue : BodyBorder,
            isCommitted ? 2.5f : hovered ? 2f : 1.2f);

        DrawString(Theme.DefaultFont, rect.Position + new Vector2(Pad, 17), display.HeaderTitle,
            HorizontalAlignment.Left, -1, 11, TextPrimary);

        float bodyY = rect.Position.Y + HeaderH + 14;
        DrawString(Theme.DefaultFont, rect.Position + new Vector2(Pad, bodyY), display.BodyLine1,
            HorizontalAlignment.Left, -1, 11, TextPrimary);
        if (!string.IsNullOrEmpty(display.BodyLine2))
        {
            Color detailColor = isFailed ? EcoSimThemeBuilder.PopDeltaDown : TextMuted;
            DrawString(Theme.DefaultFont, rect.Position + new Vector2(Pad, bodyY + 16), display.BodyLine2,
                HorizontalAlignment.Left, -1, 10, detailColor);
        }

        if (node.Type == "conditionRef")
        {
            DrawResultToggle(new Vector2(rect.End.X - 34, rect.Position.Y + HeaderH + 14), isPassed, isFailed);
        }
        else if (node.Type == "actionRef" && isCommitted)
        {
            DrawActiveBadge(new Vector2(rect.End.X - 52, rect.Position.Y + 8));
        }
    }

    private void DrawRoundedCard(Rect2 rect, Color header, Color body, Color border, float borderW)
    {
        DrawRect(rect, body, true);
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, HeaderH)), header, true);
        DrawRect(rect, border, false, borderW);

        float r = Corner;
        DrawLine(rect.Position, rect.Position + new Vector2(r, 0), border, borderW);
        DrawLine(rect.Position + new Vector2(rect.Size.X - r, 0), rect.Position + new Vector2(rect.Size.X, 0), border, borderW);
    }

    private void DrawResultToggle(Vector2 pos, bool passed, bool failed)
    {
        var track = new Rect2(pos, new Vector2(28, 14));
        DrawRect(track, failed ? new Color("5a3030") : passed ? new Color("2f5a38") : new Color("3a3a44"), true);
        DrawRect(track, BodyBorder, false, 1f);
        float knobX = passed ? track.End.X - 9 : track.Position.X + 5;
        DrawCircle(new Vector2(knobX, track.GetCenter().Y), 5f, passed ? EcoSimThemeBuilder.Hp : failed ? EcoSimThemeBuilder.PopDeltaDown : TextMuted);
    }

    private void DrawActiveBadge(Vector2 pos)
    {
        var rect = new Rect2(pos, new Vector2(44, 16));
        DrawRect(rect, EcoSimThemeBuilder.Gold.Darkened(0.2f), true);
        DrawString(Theme.DefaultFont, rect.Position + new Vector2(5, 12), "ACTIVE", HorizontalAlignment.Left, -1, 9, TextPrimary);
    }

    private BtNodeDisplay BuildDisplay(BehaviorFlatNode node)
    {
        _stepByUid.TryGetValue(node.Uid, out var step);
        string? refId = node.Ref ?? node.Id;

        switch (node.Type)
        {
            case "selector":
            {
                bool isRoot = node.Uid == _rootUid;
                return new BtNodeDisplay(
                    isRoot ? "Behavior Tree" : "Branch on",
                    isRoot ? HeaderRoot : HeaderSelector,
                    isRoot ? "Evaluate survival branches" : FormatBranchName(node.Id),
                    null);
            }
            case "sequence":
                return new BtNodeDisplay(
                    "Then",
                    HeaderSequence,
                    FormatBranchName(node.Id),
                    "All children must succeed");
            case "conditionRef":
                return new BtNodeDisplay(
                    node.Id ?? "Condition",
                    HeaderLeaf,
                    FormatConditionSummary(refId),
                    step?.Detail);
            case "actionRef":
            {
                string label = refId ?? "Action";
                string? state = null;
                string? goal = null;
                if (refId != null && _cfg?.Actions.TryGetValue(refId, out var action) == true)
                {
                    label = action["label"]?.GetValue<string>() ?? label;
                    state = action["state"]?.GetValue<string>();
                    goal = action["goal"]?.GetValue<string>();
                }
                string body = state != null ? $"state: {state}" : label;
                string? body2 = goal != null ? $"goal: {goal}" : step?.Detail;
                return new BtNodeDisplay(label, HeaderLeaf, body, body2);
            }
            default:
                return new BtNodeDisplay(node.Type, HeaderLeaf, node.Id ?? "node", step?.Detail);
        }
    }

    private string FormatBranchName(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "branch";
        return id.Replace('_', ' ');
    }

    private string FormatConditionSummary(string? refId)
    {
        if (refId == null || _cfg == null || !_cfg.Conditions.TryGetValue(refId, out var cond))
        {
            return refId ?? "condition";
        }

        string op = cond["op"]?.GetValue<string>() ?? "";
        return op switch
        {
            "hasThreat" => "Has threat nearby",
            "atWaterEdge" => "At water edge",
            "thirstBelowOrState" => "Thirst below urgent threshold",
            "thirstBelow" => "Thirst below exit threshold",
            "hungerBelowOrState" => "Hunger below graze threshold",
            "hungerBelowWithPrey" => "Hungry with prey visible",
            "hungerBelowNoPrey" => "Hungry without prey",
            "energyBelowOrState" => "Energy below rest threshold",
            "canMate" => "Can mate with partner",
            "nightWanderTired" => "Night wander tired",
            _ => refId,
        };
    }

    private Rect2 NodeRect(BehaviorFlatNode node)
    {
        double h = _nodeHeights.TryGetValue(node.Uid, out var height) ? height : BehaviorGraphLayout.NodeHeight;
        return new Rect2((float)node.X, (float)node.Y, (float)BehaviorGraphLayout.NodeWidth, (float)h);
    }

    private Vector2 EdgeStart(BehaviorFlatNode from)
    {
        var rect = NodeRect(from);
        return new Vector2(rect.Position.X + rect.Size.X * 0.5f, rect.End.Y);
    }

    private Vector2 EdgeEnd(BehaviorFlatNode to)
    {
        var rect = NodeRect(to);
        return new Vector2(rect.Position.X + rect.Size.X * 0.5f, rect.Position.Y);
    }

    private readonly record struct BtNodeDisplay(string HeaderTitle, Color HeaderColor, string BodyLine1, string? BodyLine2);
}
