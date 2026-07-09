using System.Text.Json.Nodes;

namespace EcoSim.Core.Behavior;

/// <summary>
/// Mutable editor-side node with a stable id independent of the compiled path UID.
/// Composite nodes use <see cref="ChildIds"/>; action/condition leaves carry an inline
/// JSON copy of their definition so field edits are self-contained.
/// </summary>
public sealed class BtEditorNode
{
  public string Id { get; init; } = Guid.NewGuid().ToString("N");
  public BehaviorNodeType Type { get; set; }

  /// <summary>Action/condition definition name, or optional composite branch id.</summary>
  public string? RefId { get; set; }

  public JsonObject? Action { get; set; }
  public JsonObject? Condition { get; set; }

  public double X { get; set; }
  public double Y { get; set; }

  public List<string> ChildIds { get; set; } = [];

  /// <summary>Best-effort compiled UID for live-trace mapping (valid until structure edits).</summary>
  public string Uid { get; set; } = "";

  public bool IsComposite => Type is BehaviorNodeType.Selector or BehaviorNodeType.Sequence;
}

public sealed class BtEditorDocument
{
  public string BehaviorKey { get; set; } = "";
  public string RootId { get; set; } = "";
  public Dictionary<string, BtEditorNode> Nodes { get; } = new(StringComparer.Ordinal);
  public Dictionary<string, double> Thresholds { get; set; } = new();

  public BtEditorNode? Root => RootId.Length > 0 && Nodes.TryGetValue(RootId, out var n) ? n : null;

  public BtEditorNode? Get(string? id) => id != null && Nodes.TryGetValue(id, out var n) ? n : null;

  public void Add(BtEditorNode node) => Nodes[node.Id] = node;

  public string? ParentOf(string childId)
  {
    foreach (var node in Nodes.Values)
    {
      if (node.ChildIds.Contains(childId)) return node.Id;
    }
    return null;
  }

  /// <summary>Removes a node and its subtree, detaching it from its parent.</summary>
  public void RemoveSubtree(string id)
  {
    string? parent = ParentOf(id);
    if (parent != null && Nodes.TryGetValue(parent, out var p))
    {
      p.ChildIds.Remove(id);
    }

    var stack = new Stack<string>();
    stack.Push(id);
    while (stack.Count > 0)
    {
      string cur = stack.Pop();
      if (!Nodes.TryGetValue(cur, out var node)) continue;
      foreach (string child in node.ChildIds) stack.Push(child);
      Nodes.Remove(cur);
    }
  }

  /// <summary>True if <paramref name="ancestorId"/> is an ancestor of (or equal to) <paramref name="nodeId"/>.</summary>
  public bool IsAncestor(string ancestorId, string nodeId)
  {
    string? cur = nodeId;
    while (cur != null)
    {
      if (cur == ancestorId) return true;
      cur = ParentOf(cur);
    }
    return false;
  }

  /// <summary>Reparents <paramref name="nodeId"/> under <paramref name="newParentId"/> (appended). Guards cycles.</summary>
  public bool Reparent(string nodeId, string newParentId)
  {
    if (nodeId == newParentId) return false;
    if (!Nodes.TryGetValue(newParentId, out var parent) || !parent.IsComposite) return false;
    if (IsAncestor(nodeId, newParentId)) return false;
    if (nodeId == RootId) return false;

    string? oldParent = ParentOf(nodeId);
    if (oldParent != null && Nodes.TryGetValue(oldParent, out var op))
    {
      op.ChildIds.Remove(nodeId);
    }
    if (!parent.ChildIds.Contains(nodeId))
    {
      parent.ChildIds.Add(nodeId);
    }
    return true;
  }

  /// <summary>Builds a compiled-shape tree plus the referenced action/condition definition dictionaries.</summary>
  public (BehaviorTreeNode Root, Dictionary<string, JsonObject> Conditions, Dictionary<string, JsonObject> Actions) BuildTree()
  {
    var conditions = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
    var actions = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

    BehaviorTreeNode Build(string id)
    {
      var node = Nodes[id];
      switch (node.Type)
      {
        case BehaviorNodeType.Action:
        {
          string refId = node.RefId ?? "Action";
          if (node.Action != null) actions[refId] = node.Action.DeepClone().AsObject();
          return new BehaviorTreeNode { Type = BehaviorNodeType.Action, Id = refId };
        }
        case BehaviorNodeType.Condition:
        {
          string refId = node.RefId ?? "Condition";
          if (node.Condition != null) conditions[refId] = node.Condition.DeepClone().AsObject();
          return new BehaviorTreeNode { Type = BehaviorNodeType.Condition, Id = refId };
        }
        default:
        {
          var children = new List<BehaviorTreeNode>();
          foreach (string childId in node.ChildIds)
          {
            if (Nodes.ContainsKey(childId)) children.Add(Build(childId));
          }
          return new BehaviorTreeNode
          {
            Type = node.Type,
            Id = string.IsNullOrEmpty(node.RefId) ? null : node.RefId,
            Children = children,
          };
        }
      }
    }

    var root = RootId.Length > 0 && Nodes.ContainsKey(RootId)
      ? Build(RootId)
      : new BehaviorTreeNode { Type = BehaviorNodeType.Selector };
    return (root, conditions, actions);
  }
}
