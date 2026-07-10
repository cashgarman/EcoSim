using System.Text.Json.Nodes;
using EcoSim.Core.Behavior;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>
/// Unity-Behavior-Graph-style node canvas: colored header cards, ports, orthogonal
/// arrow connectors, pan/zoom, node select/drag/connect, context menu, and a live-trace
/// overlay (committed-path glow + eval sweep) when a creature is selected.
/// </summary>
public partial class BtGraphCanvas : Control
{
    public event Action<string?>? NodeSelected;
    public event Action? StructureChanged;
    public event Action? Dirtied;

    private BtEditorDocument? _doc;
    private readonly Dictionary<string, JsonObject> _availableActions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject> _availableConditions = new(StringComparer.Ordinal);

    private string? _selectedId;
    private double _animTime;
    private bool _liveActive;

    // Live trace
    private string? _committedUid;
    private string? _proposedUid;
    private readonly Dictionary<string, BehaviorTraceStep> _stepByUid = new(StringComparer.Ordinal);
    private readonly HashSet<string> _committedNodeIds = new(StringComparer.Ordinal);

    // View transform
    private Vector2 _panOffset;
    private float _zoom = 1f;
    private bool _fitPending = true;
    private const float ZoomMin = 0.2f;
    private const float ZoomMax = 2.5f;
    private const float ZoomStep = 1.12f;

    // Interaction
    private bool _panning;
    private Vector2 _panStart;
    private Vector2 _panOffsetAtStart;
    private string? _movingNodeId;
    private Vector2 _moveGrabWorldOffset;
    private bool _moved;
    private string? _connectingFrom;
    private Vector2 _connectMouse;

    private PopupMenu _ctxMenu = null!;
    private PopupMenu _addActionMenu = null!;
    private PopupMenu _addConditionMenu = null!;
    private string? _ctxTarget;
    private readonly List<string> _actionMenuIds = [];
    private readonly List<string> _conditionMenuIds = [];

    // Hover tooltip
    private PanelContainer _tooltip = null!;
    private VBoxContainer  _tooltipBody = null!;
    private string? _hoveredId;
    private Vector2 _mousePos;

    private const float HeaderH = 30f;
    private const float BodyLineH = 16f;
    private const float BodyPad = 12f;
    private const float DescLineH = 13f;
    private const float DescSepH = 8f;
    private const int   DescMaxCharsPerLine = 30;
    private const float PortR = 7f;

    private static readonly Dictionary<string, string> NodeDescMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Composites ───────────────────────────────────────────────────
        ["SELECTOR"]              = "Tries each branch in priority order. First success wins.",
        ["threat_response"]       = "Tier 0 — Critical. Life-threatening danger; always preempts everything below.",
        ["survival_needs"]        = "Tier 1 — Survival. Thirst, hunger, and energy needs.",
        ["hunger_response"]       = "Chooses how to satisfy hunger: urgent hunting first, then opportunistic hunting.",
        ["discretionary"]         = "Tier 2 — Discretionary. Mating and stalking; interruptible by survival needs.",
        ["ambient"]               = "Tier 3 — Ambient. Default idle behavior when nothing else applies.",
        ["flee_drink_branch"]     = "Flee a threat while pausing to drink at the water's edge.",
        ["flee_branch"]           = "Escape from a nearby predator at high speed.",
        ["thirst_branch"]         = "Move to the nearest water source and drink.",
        ["graze_branch"]          = "Seek out food tiles and eat until satisfied.",
        ["rest_branch"]           = "Stand still and recover depleted energy.",
        ["mate_branch"]           = "Seek a partner and reproduce when ready.",
        ["hunt_branch"]           = "Chase and attack prey that is in sensing range.",
        ["moderate_hunt_branch"]  = "Pursue prey when moderately hungry.",
        ["stalk_branch"]          = "Roam while searching for prey to enter range.",
        ["night_rest_branch"]     = "Rest when energy is low during nighttime.",
        // ── Conditions ───────────────────────────────────────────────────
        ["HasThreat"]             = "A predator has been sensed nearby.",
        ["AtWaterEdge"]           = "Standing at a water's edge; can drink here.",
        ["ThirstyWhileFleeing"]   = "Too thirsty to ignore, even while fleeing.",
        ["Thirsty"]               = "Thirst below threshold; water needed soon.",
        ["HungryHerbivore"]       = "Hunger is low; needs vegetation to eat.",
        ["HungryOmnivoreNoPrey"]  = "Hungry with no prey visible; will graze instead.",
        ["HungryCarnivoreNearby"] = "Hungry and a prey animal is within sensing range.",
        ["HungryCarnivoreStalk"]  = "Hungry, but no prey is currently visible.",
        ["Tired"]                 = "Energy is getting low; rest should follow.",
        ["Exhausted"]             = "Energy critically depleted; must rest now.",
        ["CanMate"]               = "Adult, cooldown elapsed, partner nearby.",
        ["ModerateHunt"]          = "A prey animal is visible while moderately hungry.",
        ["NightWanderTired"]      = "Energy below rest threshold during nighttime.",
        // ── Actions ──────────────────────────────────────────────────────
        ["FleeDrinkAtShore"]      = "Hold position briefly to drink, then resume fleeing.",
        ["Flee"]                  = "Sprint directly away from the nearest threat.",
        ["Fleeing!"]              = "Sprint directly away from the nearest threat.",
        ["Fleeing (drinking)"]    = "Pause at shore to drink, then resume fleeing.",
        ["SeekWater"]             = "Move toward the nearest water tile and drink.",
        ["Seeking water"]         = "Move toward the nearest water tile and drink.",
        ["Graze"]                 = "Move to the best nearby food tile and eat vegetation.",
        ["Grazing"]               = "Move to the best nearby food tile and eat vegetation.",
        ["HuntNearby"]            = "Actively chase the nearest prey and attack if caught.",
        ["Hunting"]               = "Actively chase the nearest prey and attack if caught.",
        ["StalkPrey"]             = "Wander randomly, scanning for prey to enter range.",
        ["Stalking"]              = "Wander randomly, scanning for prey to enter range.",
        ["Rest"]                  = "Stop moving and regenerate energy over time.",
        ["Resting"]               = "Stop moving and regenerate energy over time.",
        ["Mate"]                  = "Approach the nearest eligible partner to mate.",
        ["Mating"]                = "Approach the nearest eligible partner to mate.",
        ["Wander"]                = "Walk to a random reachable tile at a relaxed pace.",
        ["Wandering"]             = "Walk to a random reachable tile at a relaxed pace.",
    };

    public override void _Ready()
    {
        SetProcess(true);
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;

        _addActionMenu = new PopupMenu { Name = "AddActionMenu" };
        _addConditionMenu = new PopupMenu { Name = "AddConditionMenu" };
        _ctxMenu = new PopupMenu { Name = "CtxMenu" };
        _ctxMenu.AddChild(_addActionMenu);
        _ctxMenu.AddChild(_addConditionMenu);
        AddChild(_ctxMenu);

        _ctxMenu.IdPressed += OnCtxMenuId;
        _addActionMenu.IdPressed += id => AddLeafToTarget(BehaviorNodeType.Action, _actionMenuIds[(int)id]);
        _addConditionMenu.IdPressed += id => AddLeafToTarget(BehaviorNodeType.Condition, _conditionMenuIds[(int)id]);

        _tooltip = BuildTooltipPanel();
        _tooltip.MouseFilter = MouseFilterEnum.Ignore;
        _tooltip.ZIndex = 20;
        _tooltip.Visible = false;
        AddChild(_tooltip);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _animTime += delta;

        if (_fitPending && Size.X > 1 && Size.Y > 1 && _doc != null && _doc.Nodes.Count > 0)
        {
            FitDocument();
            _fitPending = false;
        }

        if (_liveActive || _connectingFrom != null || _movingNodeId != null)
        {
            QueueRedraw();
        }

        if (_tooltip.Visible)
        {
            PositionTooltip(_mousePos);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
        {
            _hoveredId = null;
            _tooltip.Visible = false;
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void SetDocument(BtEditorDocument? doc, bool resetView)
    {
        _doc = doc;
        _selectedId = null;
        _hoveredId = null;
        _tooltip.Visible = false;
        if (resetView) _fitPending = true;
        QueueRedraw();
    }

    public void SetAvailableDefs(
        IReadOnlyDictionary<string, JsonObject> actions,
        IReadOnlyDictionary<string, JsonObject> conditions)
    {
        _availableActions.Clear();
        _availableConditions.Clear();
        foreach (var (k, v) in actions) _availableActions[k] = v;
        foreach (var (k, v) in conditions) _availableConditions[k] = v;
        RebuildAddMenus();
    }

    public void SetTrace(
        bool liveActive,
        string? committedUid,
        string? proposedUid,
        IReadOnlyList<BehaviorTraceStep>? steps)
    {
        _liveActive = liveActive;
        _committedUid = committedUid;
        _proposedUid = proposedUid;
        _stepByUid.Clear();
        _committedNodeIds.Clear();

        if (steps != null)
        {
            foreach (var step in steps) _stepByUid[step.Uid] = step;
        }

        if (liveActive && _doc != null && !string.IsNullOrEmpty(committedUid))
        {
            string? cur = _doc.Nodes.Values.FirstOrDefault(n => n.Uid == committedUid)?.Id;
            while (cur != null)
            {
                _committedNodeIds.Add(cur);
                cur = _doc.ParentOf(cur);
            }
        }

        QueueRedraw();
    }

    public string? SelectedId => _selectedId;

    public void SelectNode(string? id)
    {
        _selectedId = id;
        QueueRedraw();
    }

    public void ResetView() { _fitPending = true; QueueRedraw(); }

    public void AutoLayout()
    {
        if (_doc == null) return;
        BehaviorGraphAdapter.ApplyEditorLayout(_doc);
        _fitPending = true;
        QueueRedraw();
    }

    // ── Coordinate helpers ─────────────────────────────────────────────────

    private Vector2 WorldToScreen(double x, double y) => new((float)x * _zoom + _panOffset.X, (float)y * _zoom + _panOffset.Y);
    private Vector2 ScreenToWorld(Vector2 s) => (s - _panOffset) / _zoom;
    private float NodeHeightWorld(BtEditorNode n)
    {
        _stepByUid.TryGetValue(n.Uid, out var step);
        int bodyLines = Mathf.Max(BodyLines(n, step).Count, 2);
        float h = HeaderH + BodyPad + bodyLines * BodyLineH + BodyPad * 0.5f;
        var desc = DescriptionLines(n);
        if (desc.Count > 0)
            h += DescSepH + desc.Count * DescLineH + 4f;
        return Mathf.Max((float)BehaviorGraphLayout.NodeHeight, h);
    }

    private Rect2 NodeWorldRect(BtEditorNode n) => new((float)n.X, (float)n.Y, (float)BehaviorGraphLayout.NodeWidth, NodeHeightWorld(n));
    private Rect2 NodeScreenRect(BtEditorNode n) => new(WorldToScreen(n.X, n.Y), NodeWorldRect(n).Size * _zoom);
    private Vector2 OutPortWorld(BtEditorNode n) => new((float)(n.X + BehaviorGraphLayout.NodeWidth * 0.5), (float)(n.Y + NodeHeightWorld(n)));
    private Vector2 InPortWorld(BtEditorNode n) => new((float)(n.X + BehaviorGraphLayout.NodeWidth * 0.5), (float)n.Y);

    private void FitDocument()
    {
        if (_doc == null || _doc.Nodes.Count == 0) return;
        double minX = _doc.Nodes.Values.Min(n => n.X);
        double minY = _doc.Nodes.Values.Min(n => n.Y);
        double maxX = _doc.Nodes.Values.Max(n => n.X) + BehaviorGraphLayout.NodeWidth;
        double maxY = _doc.Nodes.Values.Max(n => n.Y + NodeHeightWorld(n));
        float docW = (float)(maxX - minX);
        float docH = (float)(maxY - minY);
        const float pad = 30f;
        float fit = Mathf.Min((Size.X - pad * 2f) / docW, (Size.Y - pad * 2f) / docH);
        _zoom = Mathf.Clamp(fit, ZoomMin, ZoomMax);
        _panOffset = new Vector2(
            (Size.X - docW * _zoom) * 0.5f - (float)minX * _zoom,
            pad - (float)minY * _zoom);
    }

    // ── Input ──────────────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (_doc == null) return;

        switch (@event)
        {
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                break;
            case InputEventMouseMotion motion:
                HandleMouseMotion(motion);
                break;
            case InputEventKey key when key.Pressed && key.Keycode == Key.Delete:
                DeleteSelected();
                AcceptEvent();
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) { ApplyZoom(ZoomStep, mb.Position); AcceptEvent(); return; }
        if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) { ApplyZoom(1f / ZoomStep, mb.Position); AcceptEvent(); return; }

        if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
        {
            var node = NodeAt(mb.Position);
            if (node != null)
            {
                _ctxTarget = node.Id;
                OpenContextMenu(node, mb.Position);
                AcceptEvent();
            }
            return;
        }

        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                GrabFocus();

                if (mb.DoubleClick) { FitDocument(); AcceptEvent(); return; }

                var portNode = OutPortAt(mb.Position);
                if (portNode != null)
                {
                    _connectingFrom = portNode.Id;
                    _connectMouse = mb.Position;
                    AcceptEvent();
                    return;
                }

                var node = NodeAt(mb.Position);
                if (node != null)
                {
                    _selectedId = node.Id;
                    NodeSelected?.Invoke(node.Id);
                    _movingNodeId = node.Id;
                    _moved = false;
                    _moveGrabWorldOffset = ScreenToWorld(mb.Position) - new Vector2((float)node.X, (float)node.Y);
                    QueueRedraw();
                    AcceptEvent();
                    return;
                }

                // empty space: pan (and deselect)
                _panning = true;
                _panStart = mb.Position;
                _panOffsetAtStart = _panOffset;
                if (_selectedId != null)
                {
                    _selectedId = null;
                    NodeSelected?.Invoke(null);
                }
                QueueRedraw();
                AcceptEvent();
            }
            else
            {
                if (_connectingFrom != null)
                {
                    var target = NodeAt(mb.Position);
                    if (target != null && target.Id != _connectingFrom && _doc!.Reparent(target.Id, _connectingFrom))
                    {
                        StructureChanged?.Invoke();
                        Dirtied?.Invoke();
                    }
                    _connectingFrom = null;
                    QueueRedraw();
                }
                if (_movingNodeId != null)
                {
                    if (_moved) Dirtied?.Invoke();
                    _movingNodeId = null;
                }
                _panning = false;
            }
        }
        else if (mb.ButtonIndex == MouseButton.Middle)
        {
            if (mb.Pressed) { _panning = true; _panStart = mb.Position; _panOffsetAtStart = _panOffset; }
            else _panning = false;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        _mousePos = motion.Position;

        if (_panning)
        {
            _panOffset = _panOffsetAtStart + (motion.Position - _panStart);
            _hoveredId = null;
            _tooltip.Visible = false;
            QueueRedraw();
            return;
        }
        if (_connectingFrom != null)
        {
            _connectMouse = motion.Position;
            _hoveredId = null;
            _tooltip.Visible = false;
            QueueRedraw();
            return;
        }
        if (_movingNodeId != null && _doc!.Nodes.TryGetValue(_movingNodeId, out var movingNode))
        {
            var world = ScreenToWorld(motion.Position) - _moveGrabWorldOffset;
            movingNode.X = world.X;
            movingNode.Y = world.Y;
            _moved = true;
            _tooltip.Visible = false;
            QueueRedraw();
            return;
        }

        // Hover tooltip
        var hovered = NodeAt(motion.Position);
        string? newId = hovered?.Id;
        if (newId != _hoveredId)
        {
            _hoveredId = newId;
            if (hovered != null)
            {
                UpdateTooltipContent(hovered);
                _tooltip.Visible = true;
            }
            else
            {
                _tooltip.Visible = false;
            }
        }
    }

    private void ApplyZoom(float factor, Vector2 pivot)
    {
        float newZoom = Mathf.Clamp(_zoom * factor, ZoomMin, ZoomMax);
        float ratio = newZoom / _zoom;
        _panOffset = pivot + ((_panOffset - pivot) * ratio);
        _zoom = newZoom;
        QueueRedraw();
    }

    private BtEditorNode? NodeAt(Vector2 screenPos)
    {
        if (_doc == null) return null;
        var world = ScreenToWorld(screenPos);
        foreach (var node in _doc.Nodes.Values)
        {
            if (NodeWorldRect(node).HasPoint(world)) return node;
        }
        return null;
    }

    private BtEditorNode? OutPortAt(Vector2 screenPos)
    {
        if (_doc == null) return null;
        foreach (var node in _doc.Nodes.Values)
        {
            if (!node.IsComposite) continue;
            var port = WorldToScreen(OutPortWorld(node).X, OutPortWorld(node).Y);
            if (screenPos.DistanceTo(port) <= (PortR + 4f) * _zoom + 4f) return node;
        }
        return null;
    }

    // ── Context menu / structural edits ─────────────────────────────────────

    private void OpenContextMenu(BtEditorNode node, Vector2 localPos)
    {
        _ctxMenu.Clear();
        bool composite = node.IsComposite;
        if (composite)
        {
            _ctxMenu.AddItem("Add Selector child", 1);
            _ctxMenu.AddItem("Add Sequence child", 2);
            _ctxMenu.AddSubmenuNodeItem("Add Action", _addActionMenu);
            _ctxMenu.AddSubmenuNodeItem("Add Condition", _addConditionMenu);
            _ctxMenu.AddSeparator();
        }
        _ctxMenu.AddItem("Wrap in Selector", 5);
        _ctxMenu.AddItem("Wrap in Sequence", 6);
        _ctxMenu.AddSeparator();
        _ctxMenu.AddItem("Delete", 7);

        _ctxMenu.Position = (Vector2I)(GetScreenTransform() * localPos);
        _ctxMenu.ResetSize();
        _ctxMenu.Popup();
    }

    private void RebuildAddMenus()
    {
        _addActionMenu.Clear();
        _actionMenuIds.Clear();
        foreach (string id in _availableActions.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            _addActionMenu.AddItem(id, _actionMenuIds.Count);
            _actionMenuIds.Add(id);
        }

        _addConditionMenu.Clear();
        _conditionMenuIds.Clear();
        foreach (string id in _availableConditions.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            _addConditionMenu.AddItem(id, _conditionMenuIds.Count);
            _conditionMenuIds.Add(id);
        }
    }

    private void OnCtxMenuId(long id)
    {
        if (_doc == null || _ctxTarget == null) return;
        switch (id)
        {
            case 1: AddCompositeChild(_ctxTarget, BehaviorNodeType.Selector); break;
            case 2: AddCompositeChild(_ctxTarget, BehaviorNodeType.Sequence); break;
            case 5: WrapNode(_ctxTarget, BehaviorNodeType.Selector); break;
            case 6: WrapNode(_ctxTarget, BehaviorNodeType.Sequence); break;
            case 7: DeleteNode(_ctxTarget); break;
        }
    }

    private BtEditorNode PlaceNear(BtEditorNode parent, BtEditorNode node)
    {
        int childCount = parent.ChildIds.Count;
        node.X = parent.X + childCount * (BehaviorGraphLayout.NodeWidth + BehaviorGraphLayout.HGap);
        node.Y = parent.Y + NodeHeightWorld(parent) + BehaviorGraphLayout.VGap;
        return node;
    }

    private void AddCompositeChild(string parentId, BehaviorNodeType type)
    {
        if (_doc == null || !_doc.Nodes.TryGetValue(parentId, out var parent) || !parent.IsComposite) return;
        var node = new BtEditorNode { Type = type };
        PlaceNear(parent, node);
        _doc.Add(node);
        parent.ChildIds.Add(node.Id);
        AfterStructuralEdit(node.Id);
    }

    private void AddLeafToTarget(BehaviorNodeType type, string refId)
    {
        if (_doc == null || _ctxTarget == null || !_doc.Nodes.TryGetValue(_ctxTarget, out var parent) || !parent.IsComposite) return;
        var node = new BtEditorNode { Type = type, RefId = refId };
        if (type == BehaviorNodeType.Action && _availableActions.TryGetValue(refId, out var a))
        {
            node.Action = a.DeepClone().AsObject();
        }
        else if (type == BehaviorNodeType.Condition && _availableConditions.TryGetValue(refId, out var c))
        {
            node.Condition = c.DeepClone().AsObject();
        }
        PlaceNear(parent, node);
        _doc.Add(node);
        parent.ChildIds.Add(node.Id);
        AfterStructuralEdit(node.Id);
    }

    private void WrapNode(string nodeId, BehaviorNodeType type)
    {
        if (_doc == null || !_doc.Nodes.TryGetValue(nodeId, out var node)) return;
        var wrapper = new BtEditorNode { Type = type, X = node.X, Y = node.Y };
        node.Y = wrapper.Y + NodeHeightWorld(wrapper) + BehaviorGraphLayout.VGap;

        string? parentId = _doc.ParentOf(nodeId);
        _doc.Add(wrapper);
        if (parentId == null)
        {
            _doc.RootId = wrapper.Id;
        }
        else if (_doc.Nodes.TryGetValue(parentId, out var parent))
        {
            int idx = parent.ChildIds.IndexOf(nodeId);
            if (idx >= 0) parent.ChildIds[idx] = wrapper.Id;
        }
        wrapper.ChildIds.Add(nodeId);
        AfterStructuralEdit(wrapper.Id);
    }

    private void DeleteSelected()
    {
        if (_selectedId != null) DeleteNode(_selectedId);
    }

    private void DeleteNode(string nodeId)
    {
        if (_doc == null || nodeId == _doc.RootId) return;
        _doc.RemoveSubtree(nodeId);
        if (_selectedId == nodeId) _selectedId = null;
        AfterStructuralEdit(null);
        NodeSelected?.Invoke(_selectedId);
    }

    private void AfterStructuralEdit(string? selectId)
    {
        if (selectId != null) _selectedId = selectId;
        StructureChanged?.Invoke();
        Dirtied?.Invoke();
        QueueRedraw();
        if (selectId != null) NodeSelected?.Invoke(selectId);
    }

    // ── Drawing ──────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (_doc == null || _doc.Nodes.Count == 0)
        {
            DrawString(GetThemeDefaultFont(), new Vector2(12, 24), "No behavior tree", HorizontalAlignment.Left, -1, 12, EcoSimThemeBuilder.Dim);
            return;
        }

        DrawEdges();
        if (_connectingFrom != null && _doc.Nodes.TryGetValue(_connectingFrom, out var fromNode))
        {
            var start = WorldToScreen(OutPortWorld(fromNode).X, OutPortWorld(fromNode).Y);
            DrawLine(start, _connectMouse, EcoSimThemeBuilder.Gold, 2f);
        }

        foreach (var node in _doc.Nodes.Values)
        {
            DrawNode(node);
        }

        if (_liveActive) DrawEvalSweep();
    }

    private void DrawEdges()
    {
        if (_doc == null) return;
        foreach (var parent in _doc.Nodes.Values)
        {
            if (!parent.IsComposite) continue;
            var start = WorldToScreen(OutPortWorld(parent).X, OutPortWorld(parent).Y);
            foreach (string childId in parent.ChildIds)
            {
                if (!_doc.Nodes.TryGetValue(childId, out var child)) continue;
                var end = WorldToScreen(InPortWorld(child).X, InPortWorld(child).Y);

                bool onCommitted = _committedNodeIds.Contains(parent.Id) && _committedNodeIds.Contains(childId);
                Color color = onCommitted ? EcoSimThemeBuilder.Gold : EcoSimThemeBuilder.Edge.Lightened(0.28f);
                float width = onCommitted ? 2.6f : 1.6f;

                DrawOrthogonalEdge(start, end, color, width);
                if (onCommitted) DrawFlowDot(start, end);
            }
        }
    }

    private void DrawOrthogonalEdge(Vector2 start, Vector2 end, Color color, float width)
    {
        float midY = (start.Y + end.Y) * 0.5f;
        var p1 = new Vector2(start.X, midY);
        var p2 = new Vector2(end.X, midY);
        DrawLine(start, p1, color, width);
        DrawLine(p1, p2, color, width);
        DrawLine(p2, end, color, width);

        // arrowhead pointing down into the child input port
        float a = 5f * Mathf.Max(0.6f, _zoom);
        var tip = end;
        DrawColoredPolygon(
            [tip, tip + new Vector2(-a, -a * 1.4f), tip + new Vector2(a, -a * 1.4f)],
            color);
    }

    private void DrawFlowDot(Vector2 start, Vector2 end)
    {
        float midY = (start.Y + end.Y) * 0.5f;
        var pts = new[] { start, new Vector2(start.X, midY), new Vector2(end.X, midY), end };
        float t = (float)(_animTime % 1.1 / 1.1);
        // total 3 segments, interpolate along them
        var pos = InterpPolyline(pts, t);
        DrawCircle(pos, 3.2f, EcoSimThemeBuilder.Gold);
        DrawCircle(pos, 5.5f, new Color(EcoSimThemeBuilder.Gold, 0.25f));
    }

    private static Vector2 InterpPolyline(Vector2[] pts, float t)
    {
        float total = 0;
        for (int i = 0; i < pts.Length - 1; i++) total += pts[i].DistanceTo(pts[i + 1]);
        float target = total * t;
        float acc = 0;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            float seg = pts[i].DistanceTo(pts[i + 1]);
            if (acc + seg >= target)
            {
                float local = seg > 0 ? (target - acc) / seg : 0;
                return pts[i].Lerp(pts[i + 1], local);
            }
            acc += seg;
        }
        return pts[^1];
    }

    private void DrawNode(BtEditorNode node)
    {
        var rect = NodeScreenRect(node);
        if (rect.End.X < 0 || rect.Position.X > Size.X || rect.End.Y < 0 || rect.Position.Y > Size.Y) return;

        Color headerColor = HeaderColor(node.Type);
        Color bodyColor = EcoSimThemeBuilder.PanelDark;
        _stepByUid.TryGetValue(node.Uid, out var step);
        if (_liveActive && step != null)
        {
            bodyColor = step.Outcome switch
            {
                TraceOutcome.Passed => new Color("263626"),
                TraceOutcome.Failed => new Color("3a2323"),
                TraceOutcome.Selected => new Color("3a3320"),
                _ => bodyColor,
            };
        }

        bool committed = _liveActive && node.Uid == _committedUid;
        bool proposed = _liveActive && node.Uid == _proposedUid && !committed;
        bool selected = node.Id == _selectedId;

        // committed glow
        if (committed)
        {
            float glow = (4f + Mathf.Sin((float)_animTime * 5f) * 2f);
            DrawRect(rect.Grow(glow), new Color(EcoSimThemeBuilder.Gold, 0.16f), false, 2f);
        }

        // body card
        var body = new StyleBoxFlat
        {
            BgColor = bodyColor,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        };
        DrawStyleBox(body, rect);

        // header strip
        float hh = HeaderH * _zoom;
        var headerRect = new Rect2(rect.Position, new Vector2(rect.Size.X, hh));
        var header = new StyleBoxFlat
        {
            BgColor = committed ? EcoSimThemeBuilder.Gold : headerColor,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
        };
        DrawStyleBox(header, headerRect);

        // border
        Color border = selected ? EcoSimThemeBuilder.Gold
            : proposed ? EcoSimThemeBuilder.Blue
            : (_liveActive && step != null) ? step.Outcome switch
            {
                TraceOutcome.Passed => EcoSimThemeBuilder.Hp,
                TraceOutcome.Failed => EcoSimThemeBuilder.PopDeltaDown,
                TraceOutcome.Selected => EcoSimThemeBuilder.Gold,
                _ => EcoSimThemeBuilder.Edge,
            }
            : EcoSimThemeBuilder.Edge;
        float borderW = selected ? 2.5f : committed ? 2f : 1.5f;
        var outline = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = border,
            BorderWidthTop = (int)borderW, BorderWidthBottom = (int)borderW,
            BorderWidthLeft = (int)borderW, BorderWidthRight = (int)borderW,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        };
        DrawStyleBox(outline, rect);

        DrawNodeText(node, rect, hh, step);
        DrawPorts(node, rect);
    }

    private void DrawNodeText(BtEditorNode node, Rect2 rect, float headerH, BehaviorTraceStep? step)
    {
        var font = EcoSimFonts.GetFont();
        int hSize = Mathf.Max(1, (int)(13f * _zoom));
        int bSize = Mathf.Max(1, (int)(11f * _zoom));
        int dSize = Mathf.Max(1, (int)(8f  * _zoom));
        float pad = 10f * _zoom;
        float lineGap = 5f * _zoom;

        Color headerText = node.Type == BehaviorNodeType.Selector || node.Type == BehaviorNodeType.Action
            ? new Color("1a1d14") : Colors.White;

        string title = HeaderTitle(node);
        title = Truncate(title, rect.Size.X - pad * 2, font, hSize);
        DrawString(font, rect.Position + new Vector2(pad, headerH * 0.72f), title, HorizontalAlignment.Left, -1, hSize, headerText);

        float y = rect.Position.Y + headerH + bSize + pad * 0.35f;
        foreach (string line in BodyLines(node, step))
        {
            if (y > rect.End.Y - pad * 0.5f) break;
            string t = Truncate(line, rect.Size.X - pad * 2, font, bSize);
            DrawString(font, new Vector2(rect.Position.X + pad, y), t, HorizontalAlignment.Left, -1, bSize, EcoSimThemeBuilder.Text);
            y += bSize + lineGap;
        }

        // ── Description section ──────────────────────────────────────────
        var descLines = DescriptionLines(node);
        if (descLines.Count > 0)
        {
            float sepY = y + DescSepH * _zoom * 0.4f;
            DrawLine(
                new Vector2(rect.Position.X + pad, sepY),
                new Vector2(rect.End.X - pad, sepY),
                EcoSimThemeBuilder.Dim.Darkened(0.4f), 1f);
            y = sepY + DescSepH * _zoom * 0.6f + dSize;

            var descColor = new Color(EcoSimThemeBuilder.Text.R, EcoSimThemeBuilder.Text.G, EcoSimThemeBuilder.Text.B, 0.55f);
            foreach (string dl in descLines)
            {
                if (y > rect.End.Y - 2) break;
                string dt = Truncate(dl, rect.Size.X - pad * 2, font, dSize);
                DrawString(font, new Vector2(rect.Position.X + pad, y), dt, HorizontalAlignment.Left, -1, dSize, descColor);
                y += dSize + 3f * _zoom;
            }
        }
    }

    private void DrawPorts(BtEditorNode node, Rect2 rect)
    {
        float r = PortR * Mathf.Max(0.6f, _zoom);
        var inPort = new Vector2(rect.GetCenter().X, rect.Position.Y);
        DrawCircle(inPort, r, EcoSimThemeBuilder.Edge.Lightened(0.4f));
        if (node.IsComposite)
        {
            var outPort = new Vector2(rect.GetCenter().X, rect.End.Y);
            DrawCircle(outPort, r, EcoSimThemeBuilder.Gold.Darkened(0.1f));
        }
    }

    private void DrawEvalSweep()
    {
        if (_doc == null || _stepByUid.Count == 0) return;
        var steps = _stepByUid.Values.ToList();
        int idx = (int)(_animTime / 0.12) % steps.Count;
        var step = steps[idx];
        var node = _doc.Nodes.Values.FirstOrDefault(n => n.Uid == step.Uid);
        if (node == null) return;
        var center = NodeScreenRect(node).GetCenter();
        float ring = (10f + (float)(_animTime % 0.6 / 0.6) * 12f) * Mathf.Max(0.6f, _zoom);
        Color c = step.Outcome switch
        {
            TraceOutcome.Failed => EcoSimThemeBuilder.PopDeltaDown,
            TraceOutcome.Selected => EcoSimThemeBuilder.Gold,
            TraceOutcome.Passed => EcoSimThemeBuilder.Hp,
            _ => EcoSimThemeBuilder.Dim,
        };
        DrawArc(center, ring, 0, Mathf.Tau, 28, c, 2f);
    }

    // ── Tooltip ────────────────────────────────────────────────────────────

    private PanelContainer BuildTooltipPanel()
    {
        var outer = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor    = new Color(EcoSimThemeBuilder.PanelDark, 0.97f),
            BorderColor = EcoSimThemeBuilder.Edge.Lightened(0.18f),
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        };
        outer.AddThemeStyleboxOverride("panel", style);
        outer.AddThemeConstantOverride("margin_left", 0);
        outer.AddThemeConstantOverride("margin_right", 0);
        outer.AddThemeConstantOverride("margin_top", 0);
        outer.AddThemeConstantOverride("margin_bottom", 0);

        _tooltipBody = new VBoxContainer();
        _tooltipBody.AddThemeConstantOverride("separation", 0);
        outer.AddChild(_tooltipBody);
        return outer;
    }

    private void UpdateTooltipContent(BtEditorNode node)
    {
        foreach (var child in _tooltipBody.GetChildren())
            child.QueueFree();

        // ── Header bar ─────────────────────────────────────────────────
        var headerPanel = new PanelContainer();
        var headerStyle = new StyleBoxFlat
        {
            BgColor = HeaderColor(node.Type),
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
            ContentMarginLeft   = 12,
            ContentMarginRight  = 12,
            ContentMarginTop    = 7,
            ContentMarginBottom = 7,
        };
        headerPanel.AddThemeStyleboxOverride("panel", headerStyle);

        Color titleFg = node.Type is BehaviorNodeType.Selector or BehaviorNodeType.Action
            ? new Color("1a1d14") : Colors.White;
        var titleLabel = new Label { Text = HeaderTitle(node) };
        EcoSimFonts.ApplyFont(titleLabel, 11, titleFg);
        headerPanel.AddChild(titleLabel);
        _tooltipBody.AddChild(headerPanel);

        // ── Body rows ──────────────────────────────────────────────────
        var bodyBox = new VBoxContainer();
        bodyBox.AddThemeConstantOverride("separation", 4);

        foreach (string line in TooltipBodyLines(node))
        {
            var lbl = new Label { Text = line };
            EcoSimFonts.ApplyFont(lbl, 10, EcoSimThemeBuilder.Text);
            bodyBox.AddChild(lbl);
        }

        var bodyMargin = new MarginContainer();
        bodyMargin.AddThemeConstantOverride("margin_left",   12);
        bodyMargin.AddThemeConstantOverride("margin_right",  12);
        bodyMargin.AddThemeConstantOverride("margin_top",     8);
        bodyMargin.AddThemeConstantOverride("margin_bottom",  6);
        bodyMargin.AddChild(bodyBox);
        _tooltipBody.AddChild(bodyMargin);

        // ── Description ────────────────────────────────────────────────
        string? desc = NodeDescription(node);
        if (!string.IsNullOrEmpty(desc))
        {
            var sep = new HSeparator();
            sep.AddThemeColorOverride("color", EcoSimThemeBuilder.Dim.Darkened(0.35f));
            _tooltipBody.AddChild(sep);

            var descBox = new VBoxContainer();
            descBox.AddThemeConstantOverride("separation", 3);

            foreach (string dl in WordWrap(desc, 44))
            {
                var lbl = new Label { Text = dl };
                EcoSimFonts.ApplyFont(lbl, 8, EcoSimThemeBuilder.Dim);
                descBox.AddChild(lbl);
            }

            var descMargin = new MarginContainer();
            descMargin.AddThemeConstantOverride("margin_left",   12);
            descMargin.AddThemeConstantOverride("margin_right",  12);
            descMargin.AddThemeConstantOverride("margin_top",     6);
            descMargin.AddThemeConstantOverride("margin_bottom", 10);
            descMargin.AddChild(descBox);
            _tooltipBody.AddChild(descMargin);
        }
    }

    private static List<string> TooltipBodyLines(BtEditorNode node)
    {
        var lines = new List<string>();
        switch (node.Type)
        {
            case BehaviorNodeType.Selector:
                lines.Add("(root selector)");
                break;
            case BehaviorNodeType.Sequence:
                if (!string.IsNullOrEmpty(node.RefId)) lines.Add($"id: {node.RefId}");
                lines.Add($"{node.ChildIds.Count} children");
                break;
            case BehaviorNodeType.Condition:
                if (!string.IsNullOrEmpty(node.RefId)) lines.Add($"name: {node.RefId}");
                if (node.Condition != null)
                {
                    foreach (var kv in node.Condition)
                        lines.Add($"{kv.Key}: {kv.Value}");
                }
                break;
            case BehaviorNodeType.Action:
                if (node.Action != null)
                {
                    foreach (var kv in node.Action)
                    {
                        if (kv.Key == "label") continue;
                        lines.Add($"{kv.Key}: {kv.Value}");
                    }
                }
                break;
        }
        return lines;
    }

    private void PositionTooltip(Vector2 cursor)
    {
        const float gap = 14f;
        var tipSize = _tooltip.Size;
        float x = cursor.X + gap;
        float y = cursor.Y + gap;
        if (x + tipSize.X > Size.X) x = cursor.X - tipSize.X - gap;
        if (y + tipSize.Y > Size.Y) y = cursor.Y - tipSize.Y - gap;
        _tooltip.Position = new Vector2(Mathf.Max(0, x), Mathf.Max(0, y));
    }

    // ── Content helpers ────────────────────────────────────────────────────

    private static Color HeaderColor(BehaviorNodeType type) => type switch
    {
        BehaviorNodeType.Selector => EcoSimThemeBuilder.Hp,
        BehaviorNodeType.Sequence => EcoSimThemeBuilder.NodeSequence,
        BehaviorNodeType.Condition => EcoSimThemeBuilder.Blue,
        BehaviorNodeType.Action => EcoSimThemeBuilder.Hunger,
        _ => EcoSimThemeBuilder.Dim,
    };

    private static string HeaderTitle(BtEditorNode node) => node.Type switch
    {
        BehaviorNodeType.Selector => string.IsNullOrEmpty(node.RefId) ? "SELECTOR" : GroupTitle(node.RefId),
        BehaviorNodeType.Sequence => "SEQUENCE",
        BehaviorNodeType.Condition => "IF " + (node.RefId ?? "?"),
        BehaviorNodeType.Action => node.Action?["label"]?.GetValue<string>() ?? node.RefId ?? "Action",
        _ => "NODE",
    };

    private static string GroupTitle(string refId) => refId.Replace('_', ' ').ToUpperInvariant();

    private List<string> BodyLines(BtEditorNode node, BehaviorTraceStep? step)
    {
        var lines = new List<string>();
        switch (node.Type)
        {
            case BehaviorNodeType.Selector:
            case BehaviorNodeType.Sequence:
                if (!string.IsNullOrEmpty(node.RefId)) lines.Add(node.RefId);
                else if (node.Id == _doc?.RootId) lines.Add("(root)");
                lines.Add($"{node.ChildIds.Count} children");
                break;
            case BehaviorNodeType.Action:
                lines.Add($"state: {node.Action?["state"]?.GetValue<string>() ?? "?"}");
                lines.Add($"goal: {node.Action?["goal"]?.GetValue<string>() ?? "?"}");
                break;
            case BehaviorNodeType.Condition:
                if (!string.IsNullOrEmpty(node.RefId)) lines.Add(node.RefId);
                lines.Add($"op: {node.Condition?["op"]?.GetValue<string>() ?? "?"}");
                if (_liveActive && step != null && !string.IsNullOrEmpty(step.Detail)) lines.Add(step.Detail);
                break;
        }
        return lines;
    }

    // ── Node descriptions ──────────────────────────────────────────────────

    private static string? NodeDescription(BtEditorNode node)
    {
        switch (node.Type)
        {
            case BehaviorNodeType.Selector:
                if (!string.IsNullOrEmpty(node.RefId) && NodeDescMap.TryGetValue(node.RefId, out var selDesc))
                    return selDesc;
                return NodeDescMap.GetValueOrDefault("SELECTOR");

            case BehaviorNodeType.Sequence:
                if (!string.IsNullOrEmpty(node.RefId) && NodeDescMap.TryGetValue(node.RefId, out var seqDesc))
                    return seqDesc;
                return "All children must succeed in order.";

            case BehaviorNodeType.Condition:
                if (!string.IsNullOrEmpty(node.RefId) && NodeDescMap.TryGetValue(node.RefId, out var condDesc))
                    return condDesc;
                return null;

            case BehaviorNodeType.Action:
            {
                // Prefer the display label, fall back to RefId
                string? label = node.Action?["label"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(label) && NodeDescMap.TryGetValue(label, out var actDesc))
                    return actDesc;
                if (!string.IsNullOrEmpty(node.RefId) && NodeDescMap.TryGetValue(node.RefId, out var refDesc))
                    return refDesc;
                return null;
            }

            default:
                return null;
        }
    }

    private static List<string> DescriptionLines(BtEditorNode node)
    {
        string? desc = NodeDescription(node);
        if (string.IsNullOrEmpty(desc)) return [];
        return WordWrap(desc, DescMaxCharsPerLine);
    }

    private static List<string> WordWrap(string text, int maxChars)
    {
        var result = new List<string>();
        var words = text.Split(' ');
        var line = new System.Text.StringBuilder();
        foreach (string word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
            {
                result.Add(line.ToString());
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) result.Add(line.ToString());
        return result;
    }

    private static string Truncate(string text, float maxWidth, Font font, int fontSize)
    {
        if (maxWidth <= 0) return text;
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X <= maxWidth) return text;
        while (text.Length > 1)
        {
            text = text[..^1];
            if (font.GetStringSize(text + "…", HorizontalAlignment.Left, -1, fontSize).X <= maxWidth) return text + "…";
        }
        return text;
    }
}
