namespace EcoSim.Core.Sim;

public sealed class TimeScrubController
{
    public const double DefaultSnapshotIntervalSec = 1.0;

    private readonly SimSession _session;
    private readonly TimelineDb _db;
    private readonly SnapshotCache _cache = new();
    private readonly List<(double T, int Day)> _snapshotTimes = [];

    private double _baselineT;
    private double _headT;
    private WorldSnapshot? _baselineSnapshot;
    private bool _viewingPast;
    private bool _dragging;
    private double _scrubTargetT;
    private long _lastCapturedBucket = -1;
    private long _lastAppliedBucket = long.MinValue;
    private bool _cachePrewarmed;
    private bool _snapshotTimesDirty = true;
    private int _seekSeq;

    public TimeScrubController(SimSession session, TimelineDb db)
    {
        _session = session;
        _db = db;
    }

    public bool ScrubActive => _viewingPast || _dragging;

    public void SetDragging(bool active) => _dragging = active;

    public double BaselineT => _baselineT;
    public double HeadT => _headT;
    public double ScrubTargetT => _scrubTargetT;
    public double SnapshotIntervalSec { get; set; } = DefaultSnapshotIntervalSec;

    public void ResetBaseline()
    {
        _baselineT = _session.State.TGlobal;
        _headT = _baselineT;
        _scrubTargetT = _baselineT;
        _viewingPast = false;
        _lastCapturedBucket = -1;
        _lastAppliedBucket = long.MinValue;
        _baselineSnapshot = SnapshotService.Capture(_session.State);
        _cache.Clear();
        _cachePrewarmed = false;
        _snapshotTimes.Clear();
        _snapshotTimesDirty = true;
        _seekSeq = 0;
    }

    public bool IsViewingPast() => _viewingPast;

    public void NoteLiveAdvance()
    {
        if (_viewingPast || _dragging) return;
        _headT = _session.State.TGlobal;
        _baselineT = _headT;
    }

    public void CaptureIfDue()
    {
        if (_viewingPast) return;
        double t = _session.State.TGlobal;
        long bucket = SnapshotCache.BucketFor(t, SnapshotIntervalSec);
        if (bucket == _lastCapturedBucket) return;
        _lastCapturedBucket = bucket;
        var snap = SnapshotService.Capture(_session.State);
        snap.RunId = _db.RunId;
        _db.SaveSnapshot(snap, SnapshotIntervalSec);
        _cache.Note(snap, SnapshotIntervalSec);
        NoteSnapshotTime(snap.T, snap.Day, bucket);
        _headT = snap.T;
        _baselineSnapshot = SnapshotService.Capture(_session.State);
        _baselineT = _headT;
    }

    public void PrewarmCache()
    {
        if (_cachePrewarmed) return;
        _cachePrewarmed = true;
        foreach (var snap in _db.LoadRecentSnapshots(_headT, SnapshotCache.DefaultCapacity))
        {
            _cache.Note(snap, SnapshotIntervalSec);
        }
    }

    public bool SeekTo(double targetT, bool light = false)
    {
        int seq = ++_seekSeq;
        _scrubTargetT = Math.Max(0, targetT);
        long bucket = SnapshotCache.BucketFor(_scrubTargetT, SnapshotIntervalSec);
        if (light && bucket == _lastAppliedBucket) return false;

        EnsureBaseline();
        if (!_cachePrewarmed) PrewarmCache();
        if (seq != _seekSeq) return false;

        WorldSnapshot? snap = ResolveSnapshot(_scrubTargetT, seq);
        if (snap == null || seq != _seekSeq) return false;

        int creatureCountBefore = _session.State.Creatures.Count;
        SnapshotService.Restore(_session.State, snap, light, preserveDisplay: light);
        _viewingPast = _scrubTargetT < _headT - 0.001;
        _scrubTargetT = _session.State.TGlobal;

        if (!light || creatureCountBefore != _session.State.Creatures.Count)
        {
            _session.Creatures.RebuildGrid();
        }

        if (!light)
        {
            _session.Creatures.SnapAllDisplayPositions();
        }

        _lastAppliedBucket = bucket;
        return true;
    }

    public void GoToPresent()
    {
        if (!_viewingPast && !_dragging) return;
        int seq = ++_seekSeq;
        if (_baselineSnapshot != null)
        {
            SnapshotService.Restore(_session.State, _baselineSnapshot, light: false);
        }
        else
        {
            var snap = ResolveSnapshot(_headT, seq);
            if (snap != null)
            {
                SnapshotService.Restore(_session.State, snap, light: false);
            }
        }

        _viewingPast = false;
        _scrubTargetT = _headT;
        _lastAppliedBucket = long.MinValue;
        _session.Creatures.RebuildGrid();
        _session.Creatures.SnapAllDisplayPositions();
    }

    public void OnMutatingAction()
    {
        if (!_viewingPast) return;
        _db.TruncateFuture(_scrubTargetT);
        _baselineT = _scrubTargetT;
        _headT = _scrubTargetT;
        _baselineSnapshot = SnapshotService.Capture(_session.State);
        _viewingPast = false;
        _lastAppliedBucket = long.MinValue;
        InvalidateSnapshotTimes();
        TrimCacheAfter(_scrubTargetT);
    }

    public IReadOnlyList<(double T, int Day)> SnapshotTimes()
    {
        if (_snapshotTimesDirty)
        {
            _snapshotTimes.Clear();
            _snapshotTimes.AddRange(_db.ListSnapshotTimes());
            _snapshotTimesDirty = false;
        }

        return _snapshotTimes;
    }

    public void InvalidateSnapshotTimes() => _snapshotTimesDirty = true;

    public void RegisterSnapshot(WorldSnapshot snap)
    {
        _cache.Note(snap, SnapshotIntervalSec);
        NoteSnapshotTime(snap.T, snap.Day, SnapshotCache.BucketFor(snap.T, SnapshotIntervalSec));
    }

    private void EnsureBaseline()
    {
        if (_baselineSnapshot != null) return;
        _baselineSnapshot = SnapshotService.Capture(_session.State);
        _headT = _session.State.TGlobal;
        _baselineT = _headT;
    }

    private WorldSnapshot? ResolveSnapshot(double targetT, int seq)
    {
        if (_cache.TryGetNearestAtOrBefore(targetT, SnapshotIntervalSec, out var cached) && cached != null)
        {
            return cached;
        }

        var loaded = _db.LoadNearestSnapshot(targetT);
        if (loaded == null || seq != _seekSeq) return loaded;
        _cache.Note(loaded, SnapshotIntervalSec);
        return loaded;
    }

    private void NoteSnapshotTime(double t, int day, long bucket)
    {
        for (int i = _snapshotTimes.Count - 1; i >= 0; i--)
        {
            long existingBucket = SnapshotCache.BucketFor(_snapshotTimes[i].T, SnapshotIntervalSec);
            if (existingBucket != bucket) continue;
            _snapshotTimes[i] = (t, day);
            _snapshotTimesDirty = false;
            return;
        }

        _snapshotTimes.Add((t, day));
        _snapshotTimesDirty = false;
    }

    private void TrimCacheAfter(double fromT)
    {
        // Cache entries beyond fork point are stale; next miss reloads from DB.
        _cachePrewarmed = false;
        _cache.Clear();
    }
}
