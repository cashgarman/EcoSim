using System.Text.Json.Nodes;

namespace EcoSim.Core.Behavior;

public static class BehaviorGraphAdapter
{
  /// <summary>Builds a mutable editor document from a compiled config, with tree-layout positions.</summary>
  public static BtEditorDocument ToEditorDocument(BehaviorConfig config)
  {
    var doc = new BtEditorDocument
    {
      BehaviorKey = config.BehaviorKey,
      Thresholds = new Dictionary<string, double>(config.Thresholds),
    };

    string Build(BehaviorTreeNode node)
    {
      var editorNode = new BtEditorNode
      {
        Type = node.Type,
        RefId = node.Id,
        Uid = node.Uid,
        Action = node.Action?.DeepClone().AsObject(),
        Condition = node.Condition?.DeepClone().AsObject(),
      };
      doc.Add(editorNode);
      foreach (var child in node.Children)
      {
        editorNode.ChildIds.Add(Build(child));
      }
      return editorNode.Id;
    }

    doc.RootId = Build(config.Root);
    ApplyEditorLayout(doc);
    return doc;
  }

  /// <summary>
  /// Copies compiled UIDs from a config onto a structurally identical editor document
  /// (as produced right after a save). Enables live-trace mapping without rebuilding the doc.
  /// </summary>
  public static void SyncUidsFromConfig(BtEditorDocument doc, BehaviorConfig config)
  {
    void Walk(string? editorId, BehaviorTreeNode configNode)
    {
      if (editorId == null || !doc.Nodes.TryGetValue(editorId, out var editorNode)) return;
      editorNode.Uid = configNode.Uid;
      int count = Math.Min(editorNode.ChildIds.Count, configNode.Children.Count);
      for (int i = 0; i < count; i++)
      {
        Walk(editorNode.ChildIds[i], configNode.Children[i]);
      }
    }

    if (doc.RootId.Length > 0)
    {
      Walk(doc.RootId, config.Root);
    }
  }

  /// <summary>Assigns X/Y to editor nodes via the shared tree auto-layout.</summary>
  public static void ApplyEditorLayout(BtEditorDocument doc)
  {
    if (doc.Nodes.Count == 0) return;

    var flat = new BehaviorFlatDocument { BehaviorKey = doc.BehaviorKey };
    foreach (var node in doc.Nodes.Values)
    {
      flat.Nodes.Add(new BehaviorFlatNode
      {
        Uid = node.Id,
        Type = node.Type switch
        {
          BehaviorNodeType.Sequence => "sequence",
          BehaviorNodeType.Condition => "conditionRef",
          BehaviorNodeType.Action => "actionRef",
          _ => "selector",
        },
      });
    }
    foreach (var parent in doc.Nodes.Values)
    {
      for (int i = 0; i < parent.ChildIds.Count; i++)
      {
        flat.Edges.Add(new BehaviorFlatEdge { From = parent.Id, To = parent.ChildIds[i], Order = i });
      }
    }

    BehaviorGraphLayout.ApplyAutoLayout(flat);

    foreach (var flatNode in flat.Nodes)
    {
      if (doc.Nodes.TryGetValue(flatNode.Uid, out var editorNode))
      {
        editorNode.X = flatNode.X;
        editorNode.Y = flatNode.Y;
      }
    }
  }

  public static BehaviorFlatDocument ToFlatDocument(BehaviorConfig config)
  {
    var doc = new BehaviorFlatDocument
    {
      BehaviorKey = config.BehaviorKey,
      Extends = config.TemplateName,
    };

    WalkNode(config.Root, null, 0, doc);
    return doc;
  }

  private static void WalkNode(BehaviorTreeNode node, string? parentUid, int order, BehaviorFlatDocument doc)
  {
    string type = node.Type switch
    {
      BehaviorNodeType.Selector => "selector",
      BehaviorNodeType.Sequence => "sequence",
      BehaviorNodeType.Condition => "conditionRef",
      BehaviorNodeType.Action => "actionRef",
      _ => "selector",
    };

    doc.Nodes.Add(new BehaviorFlatNode
    {
      Uid = node.Uid,
      Type = type,
      Id = node.Id,
      Ref = node.Type is BehaviorNodeType.Action or BehaviorNodeType.Condition ? node.Id : null,
    });

    if (parentUid != null)
    {
      doc.Edges.Add(new BehaviorFlatEdge
      {
        From = parentUid,
        To = node.Uid,
        Order = order,
      });
    }

    for (int i = 0; i < node.Children.Count; i++)
    {
      WalkNode(node.Children[i], node.Uid, i, doc);
    }
  }

  public static void ApplyLayout(BehaviorFlatDocument doc, BehaviorLayoutSidecar? layout)
  {
    if (layout == null) return;
    foreach (var node in doc.Nodes)
    {
      if (!layout.Nodes.TryGetValue(node.Uid, out var layoutNode)) continue;
      node.X = layoutNode.X;
      node.Y = layoutNode.Y;
      node.Collapsed = layoutNode.Collapsed;
      node.Comment = layoutNode.Comment;
    }
  }

  public static BehaviorLayoutSidecar ExtractLayout(BehaviorFlatDocument doc)
  {
    var sidecar = new BehaviorLayoutSidecar();
    foreach (var node in doc.Nodes)
    {
      if (node.X == 0 && node.Y == 0 && !node.Collapsed && string.IsNullOrEmpty(node.Comment)) continue;
      sidecar.Nodes[node.Uid] = new BehaviorLayoutNode
      {
        X = node.X,
        Y = node.Y,
        Collapsed = node.Collapsed,
        Comment = node.Comment,
      };
    }
    return sidecar;
  }

  public static bool TreesEquivalent(BehaviorTreeNode a, BehaviorTreeNode b)
  {
    if (a.Type != b.Type) return false;
    if (a.Id != b.Id) return false;
    if ((a.Type is BehaviorNodeType.Action or BehaviorNodeType.Condition) && a.Id != b.Id) return false;
    if (a.Children.Count != b.Children.Count) return false;
    for (int i = 0; i < a.Children.Count; i++)
    {
      if (!TreesEquivalent(a.Children[i], b.Children[i])) return false;
    }
    return true;
  }
}
