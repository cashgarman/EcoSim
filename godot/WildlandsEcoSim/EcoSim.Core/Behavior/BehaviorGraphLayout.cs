namespace EcoSim.Core.Behavior;

public static class BehaviorGraphLayout
{
  public const double NodeWidth = 196;
  public const double NodeHeight = 72;
  public const double HGap = 28;
  public const double VGap = 64;

  public static void ApplyAutoLayout(BehaviorFlatDocument doc, Func<BehaviorFlatNode, double>? nodeHeight = null)
  {
    if (doc.Nodes.Count == 0) return;
    if (doc.Nodes.Any(n => n.X != 0 || n.Y != 0)) return;

    double Height(BehaviorFlatNode n) => nodeHeight?.Invoke(n) ?? NodeHeight;

    var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    foreach (var edge in doc.Edges)
    {
      if (!children.TryGetValue(edge.From, out var list))
      {
        list = [];
        children[edge.From] = list;
      }
      list.Add(edge.To);
    }

    foreach (var list in children.Values)
    {
      list.Sort((a, b) =>
      {
        int orderA = doc.Edges.First(e => e.To == a).Order;
        int orderB = doc.Edges.First(e => e.To == b).Order;
        return orderA.CompareTo(orderB);
      });
    }

    var incoming = new HashSet<string>(doc.Edges.Select(e => e.To), StringComparer.Ordinal);
    string rootUid = doc.Nodes.FirstOrDefault(n => !incoming.Contains(n.Uid))?.Uid ?? doc.Nodes[0].Uid;

    var subtreeWidth = new Dictionary<string, double>(StringComparer.Ordinal);

    double MeasureWidth(string uid)
    {
      if (!children.TryGetValue(uid, out var kids) || kids.Count == 0)
      {
        subtreeWidth[uid] = NodeWidth;
        return NodeWidth;
      }

      double total = 0;
      for (int i = 0; i < kids.Count; i++)
      {
        total += MeasureWidth(kids[i]);
        if (i < kids.Count - 1) total += HGap;
      }
      subtreeWidth[uid] = Math.Max(NodeWidth, total);
      return subtreeWidth[uid];
    }
    MeasureWidth(rootUid);

    double Place(string uid, double centerX, double topY)
    {
      var node = doc.Nodes.First(n => n.Uid == uid);
      double h = Height(node);
      node.X = centerX - NodeWidth * 0.5;
      node.Y = topY;

      if (!children.TryGetValue(uid, out var kids) || kids.Count == 0)
      {
        return h;
      }

      double childTop = topY + h + VGap;
      double span = 0;
      foreach (var kid in kids)
      {
        span += subtreeWidth[kid];
      }
      span += HGap * Math.Max(0, kids.Count - 1);

      double cursor = centerX - span * 0.5;
      double maxChildDepth = 0;
      foreach (var kid in kids)
      {
        double kidCenter = cursor + subtreeWidth[kid] * 0.5;
        double kidDepth = Place(kid, kidCenter, childTop);
        maxChildDepth = Math.Max(maxChildDepth, kidDepth);
        cursor += subtreeWidth[kid] + HGap;
      }

      return h + VGap + maxChildDepth;
    }

    Place(rootUid, 0, 0);

    double minX = doc.Nodes.Min(n => n.X);
    double shift = 20 - minX;
    if (shift != 0)
    {
      foreach (var node in doc.Nodes)
      {
        node.X += shift;
      }
    }
  }
}
