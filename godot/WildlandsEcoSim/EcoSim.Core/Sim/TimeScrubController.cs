namespace EcoSim.Core.Sim;

public sealed class TimeScrubController
{
    public const double DefaultSnapshotIntervalSec = 1.0;

    private readonly SimSession _session;
    private readonly TimelineDb _db;
    private double _baselineT;
    private bool _viewingPast;
    private bool _dragging;
    private double _scrubTargetT;
    private long _lastCapturedBucket = -1;

    public TimeScrubController(SimSession session, TimelineDb db)
    {
        _session = session;
        _db = db;
    }

    public bool ScrubActive => _viewingPast || _dragging;

    public void SetDragging(bool active) => _dragging = active;
    public double BaselineT => _baselineT;
    public double ScrubTargetT => _scrubTargetT;
    public double SnapshotIntervalSec { get; set; } = DefaultSnapshotIntervalSec;

    public void ResetBaseline()
    {
        _baselineT = _session.State.TGlobal;
        _viewingPast = false;
        _scrubTargetT = _baselineT;
        _lastCapturedBucket = -1;
    }

    public bool IsViewingPast() => _viewingPast;

    public void CaptureIfDue()
    {
        if (_viewingPast) return;
        double t = _session.State.TGlobal;
        long bucket = (long)Math.Floor(t / SnapshotIntervalSec);
        if (bucket == _lastCapturedBucket) return;
        _lastCapturedBucket = bucket;
        var snap = SnapshotService.Capture(_session.State);
        snap.RunId = _db.RunId;
        _db.SaveSnapshot(snap, SnapshotIntervalSec);
    }

    public void SeekTo(double targetT, bool light = false)
    {
        _scrubTargetT = Math.Max(0, targetT);
        var snap = _db.LoadNearestSnapshot(_scrubTargetT);
        if (snap == null) return;
        SnapshotService.Restore(_session.State, snap);
        _viewingPast = _scrubTargetT < _baselineT - 0.001;
        _session.Creatures.RebuildGrid();
    }

    public void GoToPresent()
    {
        if (!_viewingPast) return;
        var snap = _db.LoadNearestSnapshot(_baselineT);
        if (snap != null)
        {
            SnapshotService.Restore(_session.State, snap);
        }
        _viewingPast = false;
        _scrubTargetT = _baselineT;
        _session.Creatures.RebuildGrid();
    }

    public void OnMutatingAction()
    {
        if (!_viewingPast) return;
        _db.TruncateFuture(_scrubTargetT);
        _baselineT = _scrubTargetT;
        _viewingPast = false;
    }

    public List<(double T, int Day)> SnapshotTimes() => _db.ListSnapshotTimes();
}
