using EcoSim.Core.Behavior;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class BtGraphView : Control
{
    private BehaviorFlatDocument? _doc;
    private string? _committedUid;
    private string? _proposedUid;
    private IReadOnlyList<BehaviorTraceStep> _traceSteps = [];
    private readonly Dictionary<string, BehaviorTraceStep> _stepByUid = new(StringComparer.Ordinal);
    private readonly HashSet<(string From, string To)> _committedPathEdges = new();
    private readonly HashSet<(string From, string To)> _activeTraceEdges = new();
    private double _animTime;
    private int _hoverNode = -1;

    public override void _Ready()
    {
        SetProcess(true);
        ClipContents = false;
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => _animTime = 0;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _doc == null || _doc.Nodes.Count == 0) return;
        _animTime += delta;
        QueueRedraw();
    }

    public void SetDocument(
        BehaviorFlatDocument? doc,
        string? committedUid,
        string? proposedUid,
        IReadOnlyList<BehaviorTraceStep>? traceSteps)
    {
        _doc = doc;
        _committedUid = committedUid;
        _proposedUid = proposedUid;
        _traceSteps = traceSteps ?? [];
        _stepByUid.Clear();
        _committedPathEdges.Clear();
        _activeTraceEdges.Clear();

        foreach (var step in _traceSteps)
        {
            _stepByUid[step.Uid] = step;
        }

        if (_doc == null || _doc.Nodes.Count == 0)
        {
            CustomMinimumSize = new Vector2(280, 100);
            QueueRedraw();
            return;
        }

        BuildPathEdges(committedUid);
        BuildTraceFlowEdges();

        double maxX = _doc.Nodes.Max(n => n.X) + BehaviorGraphLayout.NodeWidth;
        double maxY = _doc.Nodes.Max(n => n.Y) + BehaviorGraphLayout.NodeHeight;
        bool hasDetails = _traceSteps.Any(s => !string.IsNullOrEmpty(s.Detail));
        float detailPad = hasDetails ? 20f : 0f;
        CustomMinimumSize = new Vector2(
            (float)Math.Max(320, maxX + 24),
            (float)Math.Max(200, maxY + 24 + detailPad));
        QueueRedraw();
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
            var node = _doc.Nodes[i];
            var rect = NodeRect(node);
            if (rect.HasPoint(localPos)) return i;
        }
        return -1;
    }

    public override void _Draw()
    {
        if (_doc == null || _doc.Nodes.Count == 0)
        {
            DrawString(Theme.DefaultFont, new Vector2(8, 24), "No behavior tree", HorizontalAlignment.Left, -1, 12, EcoSimThemeBuilder.Dim);
            return;
        }

        DrawEdges();
        for (int i = 0; i < _doc.Nodes.Count; i++)
        {
            DrawNode(_doc.Nodes[i], i == _hoverNode);
        }

        DrawEvalSweep();
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

            Color lineColor = EcoSimThemeBuilder.Edge.Lightened(0.15f);
            float width = 1.5f;
            if (onTrace && !onCommittedPath)
            {
                lineColor = EcoSimThemeBuilder.Hp.Darkened(0.2f);
                width = 2f;
            }
            if (onCommittedPath)
            {
                lineColor = EcoSimThemeBuilder.Gold;
                width = 2.5f;
            }

            DrawBezierEdge(start, end, lineColor, width);

            if (onCommittedPath || onTrace)
            {
                DrawFlowDot(start, end, onCommittedPath ? EcoSimThemeBuilder.Gold : EcoSimThemeBuilder.Hp);
            }
        }
    }

    private void DrawBezierEdge(Vector2 start, Vector2 end, Color color, float width)
    {
        var mid = new Vector2(start.X, (start.Y + end.Y) * 0.5f);
        int segments = 12;
        Vector2 prev = start;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float u = 1f - t;
            var point = u * u * start + 2f * u * t * mid + t * t * end;
            DrawLine(prev, point, color, width);
            prev = point;
        }
    }

    private void DrawFlowDot(Vector2 start, Vector2 end, Color color)
    {
        var mid = new Vector2(start.X, (start.Y + end.Y) * 0.5f);
        float t = (float)(_animTime % 1.2 / 1.2);
        float u = 1f - t;
        var pos = u * u * start + 2f * u * t * mid + t * t * end;
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_animTime * 8f);
        DrawCircle(pos, 3f + pulse, color);
        DrawCircle(pos, 5f + pulse * 2f, new Color(color, 0.25f));
    }

    private void DrawEvalSweep()
    {
        if (_traceSteps.Count == 0) return;

        int sweepIdx = (int)(_animTime / 0.12) % _traceSteps.Count;
        var step = _traceSteps[sweepIdx];
        var node = _doc?.Nodes.FirstOrDefault(n => n.Uid == step.Uid);
        if (node == null) return;

        var rect = NodeRect(node);
        var center = rect.GetCenter();
        float ring = 8f + (float)(_animTime % 0.6 / 0.6) * 12f;
        Color ringColor = step.Outcome switch
        {
            TraceOutcome.Failed => EcoSimThemeBuilder.PopDeltaDown,
            TraceOutcome.Selected => EcoSimThemeBuilder.Gold,
            TraceOutcome.Passed => EcoSimThemeBuilder.Hp,
            _ => EcoSimThemeBuilder.Dim,
        };
        DrawArc(center, ring, 0, Mathf.Tau, 32, ringColor, 2f);
    }

    private void DrawNode(BehaviorFlatNode node, bool hovered)
    {
        var rect = NodeRect(node);
        _stepByUid.TryGetValue(node.Uid, out var step);

        bool isCommitted = node.Uid == _committedUid;
        bool isProposed = node.Uid == _proposedUid && node.Uid != _committedUid;

        Color fill = EcoSimThemeBuilder.PanelDark;
        if (step != null)
        {
            fill = step.Outcome switch
            {
                TraceOutcome.Passed => new Color("2a3d2a"),
                TraceOutcome.Failed => new Color("3d2424"),
                TraceOutcome.Selected => new Color("3d3520"),
                TraceOutcome.Skipped => EcoSimThemeBuilder.PanelDarker,
                _ => fill,
            };
        }

        if (isCommitted)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)_animTime * 4f);
            fill = EcoSimThemeBuilder.Gold.Darkened(0.45f - pulse * 0.08f);
        }

        DrawNodeBody(rect, fill, node.Type);

        Color border = EcoSimThemeBuilder.Edge.Lightened(0.2f);
        if (isProposed)
        {
            border = EcoSimThemeBuilder.Blue;
        }
        else if (step != null)
        {
            border = step.Outcome switch
            {
                TraceOutcome.Passed => EcoSimThemeBuilder.Hp,
                TraceOutcome.Failed => EcoSimThemeBuilder.PopDeltaDown,
                TraceOutcome.Selected => EcoSimThemeBuilder.Gold,
                _ => border,
            };
        }

        float borderW = isCommitted ? 3f : hovered ? 2.5f : 1.5f;
        if (isCommitted)
        {
            float glow = 4f + Mathf.Sin((float)_animTime * 5f) * 2f;
            DrawRect(rect.Grow(glow), new Color(EcoSimThemeBuilder.Gold, 0.18f), false, 1f);
        }

        DrawRect(rect, border, false, borderW);

        string badge = TypeBadge(node.Type);
        DrawString(Theme.DefaultFont, rect.Position + new Vector2(6, 14), badge, HorizontalAlignment.Left, -1, 9, EcoSimThemeBuilder.Dim);

        string label = node.Id ?? node.Ref ?? node.Type;
        if (label.Length > 16)
        {
            label = label[..14] + "…";
        }
        DrawString(Theme.DefaultFont, rect.Position + new Vector2(6, 30), label, HorizontalAlignment.Left, -1, 12, EcoSimThemeBuilder.Text);

        if (step != null && !string.IsNullOrEmpty(step.Detail) && (hovered || isCommitted || step.Outcome == TraceOutcome.Failed))
        {
            string detail = step.Detail;
            if (detail.Length > 38) detail = detail[..36] + "…";
            DrawString(Theme.DefaultFont, rect.Position + new Vector2(4, rect.Size.Y + 12), detail, HorizontalAlignment.Left, -1, 9,
                step.Outcome == TraceOutcome.Failed ? EcoSimThemeBuilder.PopDeltaDown : EcoSimThemeBuilder.Dim);
        }
    }

    private void DrawNodeBody(Rect2 rect, Color fill, string type)
    {
        DrawRect(rect, fill, true);
        if (type is "selector" or "sequence")
        {
            float inset = 4f;
            DrawRect(rect.Grow(-inset), fill.Lightened(0.06f), true);
        }
    }

    private static string TypeBadge(string type) => type switch
    {
        "selector" => "SEL",
        "sequence" => "SEQ",
        "conditionRef" => "IF",
        "actionRef" => "ACT",
        _ => "NODE",
    };

    private static Rect2 NodeRect(BehaviorFlatNode node)
    {
        return new Rect2(
            (float)node.X,
            (float)node.Y,
            (float)BehaviorGraphLayout.NodeWidth,
            (float)BehaviorGraphLayout.NodeHeight);
    }

    private static Vector2 EdgeStart(BehaviorFlatNode from)
    {
        var rect = NodeRect(from);
        return new Vector2(rect.Position.X + rect.Size.X * 0.5f, rect.End.Y);
    }

    private static Vector2 EdgeEnd(BehaviorFlatNode to)
    {
        var rect = NodeRect(to);
        return new Vector2(rect.Position.X + rect.Size.X * 0.5f, rect.Position.Y);
    }
}
