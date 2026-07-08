using Godot;

namespace WildlandsEcoSim.UI;

public sealed class CpuTreeNode
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string ParentKey { get; init; } = "";
    public int Depth { get; init; }
    public int Calls { get; set; }
    public double TotalMs { get; set; }
    public double SelfMs { get; set; }
}

public sealed class GpuFrameStats
{
    public int RenderDrawCalls { get; set; }
    public int RenderInstances { get; set; }
    public int ComputeDispatches { get; set; }
    public long BufferUploadBytes { get; set; }
    public int BufferTransfers { get; set; }
}

public sealed class PerfProfiler
{
    public static PerfProfiler Instance { get; } = new();

    private const double EmaAlpha = 0.15;
    private const double SummaryAlpha = 0.12;
    private const int HistoryLen = 90;

    private static readonly HashSet<string> AlwaysKeys = new(StringComparer.Ordinal)
    {
        "frameTotal", "sim", "render",
    };

    private static readonly string[] FrameKeys = ["sim", "snapshot", "scrub", "displaySmooth", "render", "ui", "other"];

    private readonly Dictionary<string, double> _timers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _timerStarts = new(StringComparer.Ordinal);
    private readonly double[] _frameRing = new double[64];
    private int _frameRingIdx;

    private readonly List<ScopeFrame> _scopeStack = [];
    private readonly Dictionary<string, CpuTreeNode> _frameNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CpuTreeNode> _aggregateNodes = new(StringComparer.Ordinal);

    private GpuFrameStats _gpuFrame = new();
    private long _bufferMemoryBytes;
    private double _gpuSubmitDoneMs;
    private double _lastFrameMs = 16.7;

    private bool _enabled;
    private bool _detailEnabled;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public bool DetailEnabled
    {
        get => _detailEnabled;
        set
        {
            _detailEnabled = value;
            if (!_detailEnabled)
            {
                _scopeStack.Clear();
                _frameNodes.Clear();
            }
        }
    }

    public bool IsInstrumentationActive => _enabled || _detailEnabled;

    public double FrameMsAvg { get; private set; }
    public double SimMsAvg { get; private set; }
    public double RenderMsAvg { get; private set; }
    public double UiMsAvg { get; private set; }
    public int QualityTier { get; private set; }

    public string QualityName => QualityTier switch
    {
        0 => "high",
        1 => "medium",
        2 => "low",
        _ => "emergency",
    };

    public int DetailTier => QualityTier switch
    {
        0 => 2,
        1 => 1,
        _ => 0,
    };

    public int HighlightTier => QualityTier switch
    {
        0 => 2,
        1 => 1,
        _ => 0,
    };

    public int RenderDecimation => QualityTier >= 3 ? 2 : 1;

    public IReadOnlyList<double> FrameRing => _frameRing;

    public int EffectiveHighlight(string? lockedSpecies, string? hoveredSpecies, bool hasLiveSelection)
    {
        if (!string.IsNullOrEmpty(lockedSpecies))
        {
            return Math.Max(HighlightTier, 2);
        }

        if (!string.IsNullOrEmpty(hoveredSpecies) || hasLiveSelection)
        {
            return Math.Max(HighlightTier, 1);
        }

        return HighlightTier;
    }

    public double Get(string key) => _timers.GetValueOrDefault(key);

    public void BeginFrame()
    {
        if (!_detailEnabled)
        {
            return;
        }

        _scopeStack.Clear();
        _frameNodes.Clear();
        _gpuFrame = new GpuFrameStats();
    }

    public void EndFrame(double frameTotalMs)
    {
        if (_detailEnabled)
        {
            MergeFrameNodes();
        }

        _lastFrameMs = frameTotalMs;
        FrameMsAvg = Lerp(FrameMsAvg, frameTotalMs, SummaryAlpha);
        _frameRing[_frameRingIdx++ % _frameRing.Length] = frameTotalMs;
        QualityTier = FrameMsAvg switch
        {
            <= 16.8 => 0,
            <= 22 => 1,
            <= 30 => 2,
            _ => 3,
        };
        QualityTier = Math.Max(QualityTier, Gpu.GpuThrottle.MinQualityTier);

        if (!_enabled && !_detailEnabled)
        {
            return;
        }

        Record("frameTotal", frameTotalMs);

        double sim = Get("sim");
        double render = Get("render");
        double ui = Get("ui");
        SimMsAvg = Lerp(SimMsAvg, sim, SummaryAlpha);
        RenderMsAvg = Lerp(RenderMsAvg, render, SummaryAlpha);
        UiMsAvg = Lerp(UiMsAvg, ui, SummaryAlpha);

        double accounted = FrameKeys.Sum(Get);
        Record("other", Math.Max(0, frameTotalMs - accounted));
    }

    public void EnterScope(string name)
    {
        if (!_detailEnabled)
        {
            return;
        }

        ScopeFrame? parent = _scopeStack.Count > 0 ? _scopeStack[^1] : null;
        string parentKey = parent?.NodeKey ?? "";
        int depth = parent != null ? parent.Depth + 1 : 0;
        string nodeKey = NodeKey(parentKey, name);
        CpuTreeNode node = EnsureFrameNode(nodeKey, name, parentKey, depth);
        node.Calls++;
        _scopeStack.Add(new ScopeFrame(name, nodeKey, parentKey, depth, Godot.Time.GetTicksMsec(), 0));
    }

    public void ExitScope()
    {
        if (!_detailEnabled || _scopeStack.Count == 0)
        {
            return;
        }

        ScopeFrame frame = _scopeStack[^1];
        _scopeStack.RemoveAt(_scopeStack.Count - 1);
        double ms = Godot.Time.GetTicksMsec() - frame.T0Ms;
        double selfMs = Math.Max(0, ms - frame.ChildMs);
        if (_frameNodes.TryGetValue(frame.NodeKey, out CpuTreeNode? node))
        {
            node.TotalMs += ms;
            node.SelfMs += selfMs;
        }

        if (_scopeStack.Count > 0)
        {
            _scopeStack[^1].ChildMs += ms;
        }
    }

    public void Scope(string name, Action fn)
    {
        if (!_detailEnabled)
        {
            fn();
            return;
        }

        EnterScope(name);
        try
        {
            fn();
        }
        finally
        {
            ExitScope();
        }
    }

    public T Scope<T>(string name, Func<T> fn)
    {
        if (!_detailEnabled)
        {
            return fn();
        }

        EnterScope(name);
        try
        {
            return fn();
        }
        finally
        {
            ExitScope();
        }
    }

    public void Timed(string key, Action fn)
    {
        bool recordFlat = ShouldRecord(key);
        bool recordScope = _detailEnabled;
        if (!recordFlat && !recordScope)
        {
            fn();
            return;
        }

        if (recordScope)
        {
            EnterScope(key);
        }

        double t0Ms = recordFlat ? Godot.Time.GetTicksMsec() : 0;
        try
        {
            fn();
        }
        finally
        {
            if (recordFlat)
            {
                Record(key, Godot.Time.GetTicksMsec() - t0Ms);
            }

            if (recordScope)
            {
                ExitScope();
            }
        }
    }

    public void Begin(string key)
    {
        if (_detailEnabled)
        {
            EnterScope(key);
        }

        if (!ShouldRecord(key))
        {
            return;
        }

        _timerStarts[key] = Godot.Time.GetTicksMsec();
    }

    public void End(string key)
    {
        if (_detailEnabled)
        {
            ExitScope();
        }

        if (!ShouldRecord(key))
        {
            return;
        }

        if (!_timerStarts.TryGetValue(key, out double t0))
        {
            return;
        }

        _timerStarts.Remove(key);
        Record(key, Godot.Time.GetTicksMsec() - t0);
    }

    public IReadOnlyList<CpuTreeNode> GetCpuTree()
    {
        var nodes = _aggregateNodes.Values.ToList();
        nodes.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs) != 0
            ? b.TotalMs.CompareTo(a.TotalMs)
            : string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return nodes.Take(200).ToList();
    }

    public GpuFrameStats GetGpuSnapshot()
    {
        double frameMs = Get("frameTotal");
        if (frameMs <= 0)
        {
            frameMs = _lastFrameMs;
        }

        double submitMs = Get("render.submit");
        double gpuDoneMs = _gpuSubmitDoneMs > 0 ? _gpuSubmitDoneMs : Get("gpu.submitDone");
        return new GpuFrameStats
        {
            RenderDrawCalls = _gpuFrame.RenderDrawCalls,
            RenderInstances = _gpuFrame.RenderInstances,
            ComputeDispatches = _gpuFrame.ComputeDispatches,
            BufferUploadBytes = _gpuFrame.BufferUploadBytes,
            BufferTransfers = _gpuFrame.BufferTransfers,
        };
    }

    public double GpuLoadPct
    {
        get
        {
            double frameMs = Get("frameTotal");
            if (frameMs <= 0)
            {
                frameMs = _lastFrameMs;
            }

            double gpuDoneMs = _gpuSubmitDoneMs > 0 ? _gpuSubmitDoneMs : Get("gpu.submitDone");
            return frameMs > 0 ? Math.Min(100, gpuDoneMs / frameMs * 100) : 0;
        }
    }

    public void RecordGpuDraw(int instances = 0)
    {
        if (!_detailEnabled)
        {
            return;
        }

        _gpuFrame.RenderDrawCalls++;
        _gpuFrame.RenderInstances += Math.Max(0, instances);
    }

    public void RecordGpuDispatch(int count = 1)
    {
        if (!_detailEnabled)
        {
            return;
        }

        _gpuFrame.ComputeDispatches += Math.Max(1, count);
    }

    public void RecordGpuUpload(long bytes)
    {
        if (!_detailEnabled || bytes <= 0)
        {
            return;
        }

        _gpuFrame.BufferUploadBytes += bytes;
    }

    public void TrackBufferMemory(long bytes)
    {
        if (bytes > 0)
        {
            _bufferMemoryBytes += bytes;
        }
    }

    public long BufferMemoryBytes => _bufferMemoryBytes;

    public static Color HeatColor(double ratio)
    {
        double t = Math.Clamp(ratio, 0, 1);
        const int gr = 79, gg = 212, gb = 85;
        const int rr = 232, rg = 90, rb = 90;
        return new Color(
            (float)(gr + (rr - gr) * t),
            (float)(gg + (rg - gg) * t),
            (float)(gb + (rb - gb) * t));
    }

    private bool ShouldRecord(string key)
    {
        if (AlwaysKeys.Contains(key))
        {
            return true;
        }

        return IsInstrumentationActive;
    }

    private void Record(string key, double ms)
    {
        if (!ShouldRecord(key))
        {
            return;
        }

        _timers[key] = Ema(_timers.GetValueOrDefault(key), ms);
    }

    private static string NodeKey(string parentKey, string name) =>
        string.IsNullOrEmpty(parentKey) ? name : $"{parentKey}>{name}";

    private CpuTreeNode EnsureFrameNode(string nodeKey, string name, string parentKey, int depth)
    {
        if (!_frameNodes.TryGetValue(nodeKey, out CpuTreeNode? node))
        {
            node = new CpuTreeNode
            {
                Key = nodeKey,
                Name = name,
                ParentKey = parentKey,
                Depth = depth,
            };
            _frameNodes[nodeKey] = node;
        }

        return node;
    }

    private void MergeFrameNodes()
    {
        foreach (var (key, agg) in _aggregateNodes)
        {
            if (_frameNodes.TryGetValue(key, out CpuTreeNode? frameNode))
            {
                agg.Calls = EmaInt(agg.Calls, frameNode.Calls);
                agg.TotalMs = Ema(agg.TotalMs, frameNode.TotalMs);
                agg.SelfMs = Ema(agg.SelfMs, frameNode.SelfMs);
            }
            else
            {
                agg.Calls = EmaInt(agg.Calls, 0);
                agg.TotalMs = Ema(agg.TotalMs, 0);
                agg.SelfMs = Ema(agg.SelfMs, 0);
            }
        }

        foreach (var (key, frameNode) in _frameNodes)
        {
            if (_aggregateNodes.ContainsKey(key))
            {
                continue;
            }

            _aggregateNodes[key] = new CpuTreeNode
            {
                Key = frameNode.Key,
                Name = frameNode.Name,
                ParentKey = frameNode.ParentKey,
                Depth = frameNode.Depth,
                Calls = frameNode.Calls,
                TotalMs = frameNode.TotalMs,
                SelfMs = frameNode.SelfMs,
            };
        }
    }

    private static double Ema(double prev, double value) =>
        prev > 0 ? prev * (1 - EmaAlpha) + value * EmaAlpha : value;

    private static int EmaInt(int prev, int value) =>
        prev > 0 ? (int)Math.Round(prev * (1 - EmaAlpha) + value * EmaAlpha) : value;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private sealed class ScopeFrame
    {
        public string Name { get; }
        public string NodeKey { get; }
        public string ParentKey { get; }
        public int Depth { get; }
        public double T0Ms { get; }
        public double ChildMs { get; set; }

        public ScopeFrame(string name, string nodeKey, string parentKey, int depth, double t0Ms, double childMs)
        {
            Name = name;
            NodeKey = nodeKey;
            ParentKey = parentKey;
            Depth = depth;
            T0Ms = t0Ms;
            ChildMs = childMs;
        }
    }
}
