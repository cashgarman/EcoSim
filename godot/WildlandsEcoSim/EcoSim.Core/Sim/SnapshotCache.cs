namespace EcoSim.Core.Sim;

public sealed class SnapshotCache
{
    public const int DefaultCapacity = 240;

    private readonly Dictionary<long, WorldSnapshot> _byBucket = new();
    private readonly LinkedList<long> _lruBuckets = new();
    private readonly Dictionary<long, LinkedListNode<long>> _lruNodes = new();
    private readonly List<(double T, long Bucket)> _sorted = new();
    private int _capacity = DefaultCapacity;

    public int Count => _byBucket.Count;

    public void Configure(int capacity)
    {
        _capacity = Math.Max(1, capacity);
        TrimToCapacity();
    }

    public void Clear()
    {
        _byBucket.Clear();
        _lruBuckets.Clear();
        _lruNodes.Clear();
        _sorted.Clear();
    }

    public void Note(WorldSnapshot snap, double intervalSec)
    {
        if (intervalSec <= 0) return;
        long bucket = BucketFor(snap.T, intervalSec);
        if (_byBucket.TryGetValue(bucket, out var existing))
        {
            existing.T = snap.T;
            existing.Day = snap.Day;
            existing.TimeOfDay = snap.TimeOfDay;
            existing.NextId = snap.NextId;
            existing.GenerationMax = snap.GenerationMax;
            existing.GrowRow = snap.GrowRow;
            existing.Veg = snap.Veg;
            existing.VegCap = snap.VegCap;
            existing.Creatures = snap.Creatures;
            TouchLru(bucket);
            UpdateSortedEntry(bucket, snap.T);
            return;
        }

        _byBucket[bucket] = CloneSnapshot(snap);
        TouchLru(bucket, insert: true);
        InsertSorted(bucket, snap.T);
        TrimToCapacity();
    }

    public bool TryGet(long bucket, out WorldSnapshot? snap)
    {
        if (_byBucket.TryGetValue(bucket, out var found))
        {
            TouchLru(bucket);
            snap = found;
            return true;
        }

        snap = null;
        return false;
    }

    public bool TryGetNearestAtOrBefore(double targetT, double intervalSec, out WorldSnapshot? snap)
    {
        snap = null;
        if (_sorted.Count == 0) return false;

        int idx = BinarySearchLastAtOrBefore(targetT);
        if (idx < 0) return false;

        long bucket = _sorted[idx].Bucket;
        if (!_byBucket.TryGetValue(bucket, out var found)) return false;

        TouchLru(bucket);
        snap = found;
        return true;
    }

    public static long BucketFor(double t, double intervalSec) =>
        (long)Math.Floor(t / intervalSec);

    private int BinarySearchLastAtOrBefore(double targetT)
    {
        int lo = 0;
        int hi = _sorted.Count - 1;
        int best = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (_sorted[mid].T <= targetT + 1e-9)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best;
    }

    private void InsertSorted(long bucket, double t)
    {
        int idx = BinarySearchLastAtOrBefore(t);
        if (idx >= 0 && _sorted[idx].Bucket == bucket)
        {
            _sorted[idx] = (t, bucket);
            return;
        }

        int insertAt = idx + 1;
        _sorted.Insert(insertAt, (t, bucket));
    }

    private void UpdateSortedEntry(long bucket, double t)
    {
        for (int i = 0; i < _sorted.Count; i++)
        {
            if (_sorted[i].Bucket != bucket) continue;
            _sorted[i] = (t, bucket);
            if (i > 0 && _sorted[i - 1].T > t)
            {
                _sorted.RemoveAt(i);
                InsertSorted(bucket, t);
            }
            else if (i + 1 < _sorted.Count && _sorted[i + 1].T < t)
            {
                _sorted.RemoveAt(i);
                InsertSorted(bucket, t);
            }

            return;
        }

        InsertSorted(bucket, t);
    }

    private void TouchLru(long bucket, bool insert = false)
    {
        if (insert || !_lruNodes.TryGetValue(bucket, out var node))
        {
            if (_lruNodes.TryGetValue(bucket, out node))
            {
                _lruBuckets.Remove(node);
                _lruNodes.Remove(bucket);
            }

            var newNode = _lruBuckets.AddFirst(bucket);
            _lruNodes[bucket] = newNode;
            return;
        }

        _lruBuckets.Remove(node);
        var moved = _lruBuckets.AddFirst(bucket);
        _lruNodes[bucket] = moved;
    }

    private void TrimToCapacity()
    {
        while (_byBucket.Count > _capacity && _lruBuckets.Last != null)
        {
            long evict = _lruBuckets.Last.Value;
            _lruBuckets.RemoveLast();
            _lruNodes.Remove(evict);
            _byBucket.Remove(evict);
            _sorted.RemoveAll(entry => entry.Bucket == evict);
        }
    }

    private static WorldSnapshot CloneSnapshot(WorldSnapshot snap) => new()
    {
        RunId = snap.RunId,
        T = snap.T,
        Day = snap.Day,
        TimeOfDay = snap.TimeOfDay,
        NextId = snap.NextId,
        GenerationMax = snap.GenerationMax,
        W = snap.W,
        H = snap.H,
        GrowRow = snap.GrowRow,
        Veg = (float[])snap.Veg.Clone(),
        VegCap = (float[])snap.VegCap.Clone(),
        Creatures = snap.Creatures.Select(SnapshotService.CloneCreatureSnapshot).ToList(),
    };
}
