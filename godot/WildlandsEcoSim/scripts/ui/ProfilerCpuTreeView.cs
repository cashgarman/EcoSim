using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Heat-colored hierarchical CPU scope tree (JS profiler-detail parity).</summary>
public partial class ProfilerCpuTreeView : Control
{
    // Was 12px; reduced 30%.
    public const int ListFontSize = 8;

    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly List<TreeRow> _visibleRows = [];
    private Dictionary<string, CpuTreeNode> _nodeByKey = new(StringComparer.Ordinal);
    private Dictionary<string, SiblingTotals> _siblingTotals = new(StringComparer.Ordinal);
    private double _frameMs;
    private double _maxMs = 0.001;

    private const int RowHeight = 25;
    private const int HeaderHeight = 28;
    private const int DepthIndent = 17;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 200);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void SetData(IReadOnlyList<CpuTreeNode> nodes, double frameMs)
    {
        _frameMs = frameMs;
        _nodeByKey = nodes.ToDictionary(n => n.Key, n => n);
        RebuildVisibleRows();
        RefreshTreeMinimumSize();
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            int rowIdx = (int)((mb.Position.Y - HeaderHeight) / RowHeight);
            if (rowIdx >= 0 && rowIdx < _visibleRows.Count)
            {
                TreeRow row = _visibleRows[rowIdx];
                if (row.HasKids)
                {
                    if (_collapsed.Contains(row.Node.Key))
                    {
                        _collapsed.Remove(row.Node.Key);
                    }
                    else
                    {
                        _collapsed.Add(row.Node.Key);
                    }

                    RebuildVisibleRows();
                    RefreshTreeMinimumSize();
                    QueueRedraw();
                    AcceptEvent();
                }
            }
        }
    }

    public override void _Draw()
    {
        DrawStyleBox(UiSliceCatalog.MakeInsetPanel(), new Rect2(Vector2.Zero, Size));

        int nameColW = NameColWidth();
        int valColW = ValColWidth(nameColW, Size.X);
        var headerFont = EcoSimFonts.GetFont();
        float y = 4;
        float textY = y + ListFontSize;
        DrawString(headerFont, new Vector2(8, textY), "Name", HorizontalAlignment.Left, -1, ListFontSize, EcoSimThemeBuilder.Gold);
        DrawString(headerFont, new Vector2(ColX(0, nameColW, valColW), textY), "Calls", HorizontalAlignment.Right, valColW, ListFontSize, EcoSimThemeBuilder.Gold);
        DrawString(headerFont, new Vector2(ColX(1, nameColW, valColW), textY), "Total %", HorizontalAlignment.Right, valColW, ListFontSize, EcoSimThemeBuilder.Gold);
        DrawString(headerFont, new Vector2(ColX(2, nameColW, valColW), textY), "Self %", HorizontalAlignment.Right, valColW, ListFontSize, EcoSimThemeBuilder.Gold);
        DrawString(headerFont, new Vector2(ColX(3, nameColW, valColW), textY), "Frame %", HorizontalAlignment.Right, valColW, ListFontSize, EcoSimThemeBuilder.Gold);

        y = HeaderHeight;
        var font = EcoSimFonts.GetFont();
        foreach (TreeRow row in _visibleRows)
        {
            if (row.Node.TotalMs < 0.01 && row.Node.Calls < 1)
            {
                continue;
            }

            float pad = row.Depth * DepthIndent;
            string toggle = row.HasKids ? (row.Collapsed ? "▸" : "▾") : "·";
            double heat = row.Node.TotalMs / _maxMs;
            Color nameColor = PerfProfiler.HeatColor(heat);
            double totalPct = NormalizedTotalPct(row.Node);
            double selfPct = NormalizedSelfPct(row.Node);
            double framePct = FramePct(row.Node);

            float rowTextY = y + ListFontSize + 2;
            DrawString(font, new Vector2(8 + pad, rowTextY), $"{toggle} {row.Node.Name}", HorizontalAlignment.Left, nameColW - pad, ListFontSize, nameColor);
            DrawString(font, new Vector2(ColX(0, nameColW, valColW), rowTextY), $"{row.Node.Calls}", HorizontalAlignment.Right, valColW, ListFontSize, EcoSimThemeBuilder.Dim);
            DrawString(font, new Vector2(ColX(1, nameColW, valColW), rowTextY), $"{totalPct:F1}", HorizontalAlignment.Right, valColW, ListFontSize, EcoSimThemeBuilder.Dim);
            DrawString(font, new Vector2(ColX(2, nameColW, valColW), rowTextY), $"{selfPct:F1}", HorizontalAlignment.Right, valColW, ListFontSize, EcoSimThemeBuilder.Dim);
            DrawString(font, new Vector2(ColX(3, nameColW, valColW), rowTextY), $"{framePct:F1}", HorizontalAlignment.Right, valColW, ListFontSize, EcoSimThemeBuilder.Dim);
            y += RowHeight;
        }

        if (_visibleRows.Count == 0)
        {
            DrawString(font, new Vector2(8, HeaderHeight + ListFontSize + 6), "No samples yet — pan the map or run the sim.", HorizontalAlignment.Left, -1, ListFontSize, EcoSimThemeBuilder.Dim);
        }
    }

    private static float ColX(int col, int nameColW, int valColW) => nameColW + 8 + col * (valColW + 4);

    private int NameColWidth() => Math.Max(180, (int)(Size.X * 0.40f));

    private static int ValColWidth(int nameColW, float totalWidth) =>
        Math.Max(52, (int)((totalWidth - nameColW - 28) / 4f));

    private void RefreshTreeMinimumSize()
    {
        int nameColW = NameColWidth();
        int valColW = ValColWidth(nameColW, Size.X);
        float minW = nameColW + valColW * 4 + 28;
        CustomMinimumSize = new Vector2(minW, Math.Max(120, _visibleRows.Count * RowHeight + HeaderHeight + 8));
    }

    private void RebuildVisibleRows()
    {
        _visibleRows.Clear();
        var byParent = new Dictionary<string, List<CpuTreeNode>>(StringComparer.Ordinal);
        foreach (CpuTreeNode node in _nodeByKey.Values)
        {
            string pk = node.ParentKey ?? "";
            if (!byParent.TryGetValue(pk, out List<CpuTreeNode>? list))
            {
                list = [];
                byParent[pk] = list;
            }

            list.Add(node);
        }

        _siblingTotals = new Dictionary<string, SiblingTotals>(StringComparer.Ordinal);
        foreach (var (parentKey, kids) in byParent)
        {
            double totalSum = 0;
            double selfSum = 0;
            foreach (CpuTreeNode kid in kids)
            {
                totalSum += kid.TotalMs;
                selfSum += kid.SelfMs;
            }

            _siblingTotals[parentKey] = new SiblingTotals(totalSum, selfSum);
        }

        foreach (List<CpuTreeNode> list in byParent.Values)
        {
            list.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs) != 0
                ? b.TotalMs.CompareTo(a.TotalMs)
                : string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }

        Walk("", 0, byParent);
        _visibleRows.RemoveAll(r => r.Node.TotalMs < 0.01 && r.Node.Calls < 1);
        if (_visibleRows.Count > 200)
        {
            _visibleRows.RemoveRange(200, _visibleRows.Count - 200);
        }

        _maxMs = _visibleRows.Count > 0
            ? _visibleRows.Max(r => r.Node.TotalMs)
            : 0.001;
        if (_maxMs < 0.001)
        {
            _maxMs = 0.001;
        }
    }

    private void Walk(string parentKey, int depth, Dictionary<string, List<CpuTreeNode>> byParent)
    {
        if (depth > 8)
        {
            return;
        }

        if (!byParent.TryGetValue(parentKey, out List<CpuTreeNode>? kids))
        {
            return;
        }

        foreach (CpuTreeNode node in kids)
        {
            bool hasKids = byParent.TryGetValue(node.Key, out List<CpuTreeNode>? childList) && childList.Count > 0;
            bool collapsed = _collapsed.Contains(node.Key);
            _visibleRows.Add(new TreeRow(node, depth, hasKids, collapsed));
            if (hasKids && !collapsed)
            {
                Walk(node.Key, depth + 1, byParent);
            }
        }
    }

    private double NormalizedTotalPct(CpuTreeNode node)
    {
        string pk = node.ParentKey ?? "";
        if (!_siblingTotals.TryGetValue(pk, out SiblingTotals sums) || sums.TotalSum <= 0)
        {
            return 0;
        }

        return node.TotalMs / sums.TotalSum * 100;
    }

    private double NormalizedSelfPct(CpuTreeNode node)
    {
        string pk = node.ParentKey ?? "";
        if (!_siblingTotals.TryGetValue(pk, out SiblingTotals sums) || sums.SelfSum <= 0)
        {
            return 0;
        }

        return node.SelfMs / sums.SelfSum * 100;
    }

    private double FramePct(CpuTreeNode node) =>
        _frameMs > 0 ? node.TotalMs / _frameMs * 100 : 0;

    private readonly struct SiblingTotals
    {
        public double TotalSum { get; }
        public double SelfSum { get; }

        public SiblingTotals(double totalSum, double selfSum)
        {
            TotalSum = totalSum;
            SelfSum = selfSum;
        }
    }

    private readonly struct TreeRow
    {
        public CpuTreeNode Node { get; }
        public int Depth { get; }
        public bool HasKids { get; }
        public bool Collapsed { get; }

        public TreeRow(CpuTreeNode node, int depth, bool hasKids, bool collapsed)
        {
            Node = node;
            Depth = depth;
            HasKids = hasKids;
            Collapsed = collapsed;
        }
    }
}
