using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class SnapshotCacheTests
{
    [Test]
    public void TryGetNearestAtOrBefore_ReturnsLatestAtOrBeforeTarget()
    {
        var cache = new SnapshotCache();
        cache.Note(MakeSnap(0, 0), 1);
        cache.Note(MakeSnap(2, 1), 1);
        cache.Note(MakeSnap(4, 2), 1);

        Assert.That(cache.TryGetNearestAtOrBefore(3.5, 1, out var snap), Is.True);
        Assert.That(snap!.T, Is.EqualTo(2));
        Assert.That(snap.Day, Is.EqualTo(1));
    }

    [Test]
    public void Lru_EvictsOldestWhenOverCapacity()
    {
        var cache = new SnapshotCache();
        cache.Configure(2);
        cache.Note(MakeSnap(0, 0), 1);
        cache.Note(MakeSnap(1, 0), 1);
        cache.Note(MakeSnap(2, 0), 1);

        Assert.That(cache.Count, Is.EqualTo(2));
        Assert.That(cache.TryGet(0, out _), Is.False);
        Assert.That(cache.TryGet(1, out _), Is.True);
        Assert.That(cache.TryGet(2, out _), Is.True);
    }

    private static WorldSnapshot MakeSnap(double t, int day) => new()
    {
        T = t,
        Day = day,
        W = 2,
        H = 2,
        Veg = [0.1f, 0.2f, 0.3f, 0.4f],
        VegCap = [1f, 1f, 1f, 1f],
        Creatures = [],
    };
}

[TestFixture]
public class SnapshotServiceRestoreTests
{
    [Test]
    public void LightRestore_PreservesDisplayPositionsForMatchingIds()
    {
        var state = MakeState();
        state.Creatures.Add(new Creature
        {
            Id = 1,
            Sp = "rabbit",
            X = 1,
            Y = 2,
            Rx = 9,
            Ry = 8,
        });

        var snap = new WorldSnapshot
        {
            T = 5,
            Day = 0,
            W = 2,
            H = 2,
            Veg = [0f, 0f, 0f, 0f],
            VegCap = [1f, 1f, 1f, 1f],
            Creatures =
            [
                new CreatureSnapshot
                {
                    Id = 1,
                    Sp = "rabbit",
                    X = 3,
                    Y = 4,
                    Rx = 3,
                    Ry = 4,
                },
            ],
        };

        SnapshotService.Restore(state, snap, light: true, preserveDisplay: true);

        Assert.That(state.Creatures, Has.Count.EqualTo(1));
        Assert.That(state.Creatures[0].X, Is.EqualTo(3));
        Assert.That(state.Creatures[0].Rx, Is.EqualTo(9));
        Assert.That(state.Creatures[0].Ry, Is.EqualTo(8));
    }

    [Test]
    public void SplitPayload_RoundTripsThroughBlobAndJson()
    {
        var original = new WorldSnapshot
        {
            T = 12,
            Day = 3,
            TimeOfDay = 0.42,
            NextId = 99,
            GenerationMax = 4,
            GrowRow = 2,
            W = 2,
            H = 2,
            Veg = [0.1f, 0.2f, 0.3f, 0.4f],
            VegCap = [1f, 0.8f, 0.6f, 0.4f],
            Creatures =
            [
                new CreatureSnapshot { Id = 7, Sp = "fox", X = 1.5, Y = 2.5 },
            ],
        };

        var meta = SnapshotService.ToMeta(original);
        string metaJson = SnapshotService.SerializeMeta(meta);
        string creaturesJson = SnapshotService.SerializeCreatures(original.Creatures);
        byte[] vegBlob = SnapshotService.PackVegBlob(original.Veg, original.VegCap);
        var restoredMeta = SnapshotService.DeserializeMeta(metaJson);
        var assembled = SnapshotService.AssembleFromSplit(restoredMeta!, creaturesJson, vegBlob);

        Assert.That(assembled, Is.Not.Null);
        Assert.That(assembled!.T, Is.EqualTo(12));
        Assert.That(assembled.Creatures[0].Sp, Is.EqualTo("fox"));
        Assert.That(assembled.Veg[2], Is.EqualTo(0.3f).Within(1e-5));
        Assert.That(assembled.VegCap[1], Is.EqualTo(0.8f).Within(1e-5));
    }

    [Test]
    public void TimelineDb_LoadNearestSnapshot_ReadsSplitColumns()
    {
        using var db = new TimelineDb();
        db.Open("Data Source=:memory:");
        db.BeginRun("run-test");

        var snap = new WorldSnapshot
        {
            T = 3,
            Day = 1,
            W = 2,
            H = 2,
            Veg = [0.5f, 0.5f, 0.5f, 0.5f],
            VegCap = [1f, 1f, 1f, 1f],
            Creatures = [new CreatureSnapshot { Id = 1, Sp = "mouse", X = 1, Y = 1 }],
        };
        db.SaveSnapshot(snap, 1);

        var loaded = db.LoadNearestSnapshot(3.5);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Creatures[0].Sp, Is.EqualTo("mouse"));
        Assert.That(loaded.Veg[0], Is.EqualTo(0.5f).Within(1e-5));
    }

    private static SimState MakeState() => new()
    {
        W = 2,
        H = 2,
        Veg = [0f, 0f, 0f, 0f],
        VegCap = [1f, 1f, 1f, 1f],
    };
}

[TestFixture]
public class TimeScrubControllerTests
{
    [Test]
    public void SeekTo_SkipsSameBucketDuringLightSeek()
    {
        var session = MakeSession();
        using var db = OpenTempDb();
        var scrub = new TimeScrubController(session, db) { SnapshotIntervalSec = 1 };
        scrub.ResetBaseline();

        session.State.TGlobal = 2;
        var creature = session.State.Creatures.First(c => !c.Dead);
        var snap = SnapshotService.Capture(session.State);
        scrub.RegisterSnapshot(snap);
        db.SaveSnapshot(snap, 1);

        Assert.That(scrub.SeekTo(2.1, light: true), Is.True);
        creature.X = 999;
        Assert.That(scrub.SeekTo(2.4, light: true), Is.False);
        Assert.That(creature.X, Is.EqualTo(999));
    }

    private static SimSession MakeSession()
    {
        var session = SimSession.Create(FindRepoRoot(), 42);
        session.GenerateWorld(new WorldGenConfig { Size = "s", Animals = 0.1f });
        return session;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EcoSim.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }

    private static TimelineDb OpenTempDb()
    {
        var db = new TimelineDb();
        db.Open("Data Source=:memory:");
        db.BeginRun("run-test");
        return db;
    }
}
