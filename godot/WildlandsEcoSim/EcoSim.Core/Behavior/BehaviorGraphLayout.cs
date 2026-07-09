namespace EcoSim.Core.Behavior;

public static class BehaviorGraphLayout
{
  public const double NodeWidth = 158;
  public const double NodeHeight = 62;
  public const double HGap = 22;
  public const double VGap = 72;

  public static void ApplyAutoLayout(BehaviorFlatDocument doc)
  {
    if (doc.Nodes.Count == 0) return;
    if (doc.Nodes.Any(n => n.X != 0 || n.Y != 0)) return;

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

    double Measure(string uid)
    {
      if (!children.TryGetValue(uid, out var kids) || kids.Count == 0)
      {
        subtreeWidth[uid] = NodeWidth;
        return NodeWidth;
      }

      double total = 0;
      for (int i = 0; i < kids.Count; i++)
      {
        total += Measure(kids[i]);
        if (i < kids.Count - 1) total += HGap;
      }
      subtreeWidth[uid] = Math.Max(NodeWidth, total);
      return subtreeWidth[uid];
    }
    Measure(rootUid);

    void Place(string uid, double centerX, int level)
    {
      var node = doc.Nodes.First(n => n.Uid == uid);
      node.X = centerX - NodeWidth * 0.5;
      node.Y = level * (NodeHeight + VGap);

      if (!children.TryGetValue(uid, out var kids) || kids.Count == 0) return;

      double span = 0;
      foreach (var kid in kids)
      {
        span += subtreeWidth[kid];
      }
      span += HGap * Math.Max(0, kids.Count - 1);

      double cursor = centerX - span * 0.5;
      foreach (var kid in kids)
      {
        double kidCenter = cursor + subtreeWidth[kid] * 0.5;
        Place(kid, kidCenter, level + 1);
        cursor += subtreeWidth[kid] + HGap;
      }
    }

    Place(rootUid, 0, 0);

    double minX = doc.Nodes.Min(n => n.X);
    double shift = 16 - minX;
    if (shift != 0)
    {
      foreach (var node in doc.Nodes)
      {
        node.X += shift;
      }
    }
  }
}
