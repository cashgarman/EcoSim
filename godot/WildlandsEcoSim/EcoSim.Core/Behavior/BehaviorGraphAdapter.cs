namespace EcoSim.Core.Behavior;

public static class BehaviorGraphAdapter
{
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
