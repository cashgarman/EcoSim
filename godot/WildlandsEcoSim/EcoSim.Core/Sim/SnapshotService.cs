using System.Buffers.Binary;
using System.Text.Json;

namespace EcoSim.Core.Sim;

public sealed class WorldSnapshot
{
    public string RunId { get; set; } = "";
    public double T { get; set; }
    public int Day { get; set; }
    public double TimeOfDay { get; set; }
    public int NextId { get; set; }
    public int GenerationMax { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public int GrowRow { get; set; }
    public float[] Veg { get; set; } = [];
    public float[] VegCap { get; set; } = [];
    public List<CreatureSnapshot> Creatures { get; set; } = [];
}

public sealed class CreatureSnapshot
{
    public int Id { get; set; }
    public string Sp { get; set; } = "";
    public string Sex { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
    public int Dir { get; set; }
    public Genome Genome { get; set; } = new();
    public int Gen { get; set; }
    public double Age { get; set; }
    public double Hp { get; set; }
    public double Hunger { get; set; }
    public double Thirst { get; set; }
    public double Energy { get; set; }
    public string State { get; set; } = "";
    public double Tx { get; set; }
    public double Ty { get; set; }
    public int? Target { get; set; }
    public double MateCd { get; set; }
    public double Pregnant { get; set; }
    public int LitterQ { get; set; }
    public double Walk { get; set; }
    public bool Dead { get; set; }
    public string Cause { get; set; } = "";
    public List<int> ParentIds { get; set; } = [];
    public List<int> OffspringIds { get; set; } = [];
    public double Rx { get; set; }
    public double Ry { get; set; }
}

public static class SnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static WorldSnapshot Capture(SimState state)
    {
        var snap = new WorldSnapshot
        {
            T = state.TGlobal,
            Day = state.Day,
            TimeOfDay = state.TimeOfDay,
            NextId = state.NextId,
            GenerationMax = state.GenerationMax,
            W = state.W,
            H = state.H,
            GrowRow = state.GrowRow,
            Veg = (float[])state.Veg.Clone(),
            VegCap = (float[])state.VegCap.Clone(),
        };

        foreach (var c in state.Creatures)
        {
            if (c.Dead) continue;
            snap.Creatures.Add(ToSnapshot(c));
        }

        if (state.Selected != null && state.Selected.Dead &&
            snap.Creatures.All(x => x.Id != state.Selected.Id))
        {
            snap.Creatures.Add(ToSnapshot(state.Selected));
        }

        return snap;
    }

    public static void Restore(SimState state, WorldSnapshot snap, bool light = false, bool preserveDisplay = false)
    {
        RestoreScalars(state, snap);

        if (snap.Veg.Length == state.Veg.Length)
        {
            Array.Copy(snap.Veg, state.Veg, snap.Veg.Length);
            Array.Copy(snap.VegCap, state.VegCap, snap.VegCap.Length);
        }

        state.VegDirty = true;

        int? selectedId = state.Selected?.Id;
        if (light)
        {
            RestoreCreaturesLight(state, snap, preserveDisplay);
        }
        else
        {
            RestoreCreaturesFull(state, snap);
        }

        if (selectedId.HasValue)
        {
            state.Selected = state.Creatures.FirstOrDefault(c => c.Id == selectedId.Value);
        }
    }

    public static string Serialize(WorldSnapshot snap) => JsonSerializer.Serialize(snap, JsonOptions);

    public static WorldSnapshot? Deserialize(string json) =>
        JsonSerializer.Deserialize<WorldSnapshot>(json, JsonOptions);

    public static SnapshotMeta ToMeta(WorldSnapshot snap) => new()
    {
        T = snap.T,
        Day = snap.Day,
        TimeOfDay = snap.TimeOfDay,
        NextId = snap.NextId,
        GenerationMax = snap.GenerationMax,
        GrowRow = snap.GrowRow,
        W = snap.W,
        H = snap.H,
    };

    public static string SerializeMeta(SnapshotMeta meta) =>
        JsonSerializer.Serialize(meta, JsonOptions);

    public static SnapshotMeta? DeserializeMeta(string json) =>
        JsonSerializer.Deserialize<SnapshotMeta>(json, JsonOptions);

    public static string SerializeCreatures(IReadOnlyList<CreatureSnapshot> creatures) =>
        JsonSerializer.Serialize(creatures, JsonOptions);

    public static List<CreatureSnapshot>? DeserializeCreatures(string json) =>
        JsonSerializer.Deserialize<List<CreatureSnapshot>>(json, JsonOptions);

    public static byte[] PackVegBlob(float[] veg, float[] vegCap)
    {
        int n = Math.Min(veg.Length, vegCap.Length);
        var blob = new byte[n * 8];
        for (int i = 0; i < n; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(blob.AsSpan(i * 8), veg[i]);
            BinaryPrimitives.WriteSingleLittleEndian(blob.AsSpan(i * 8 + 4), vegCap[i]);
        }

        return blob;
    }

    public static (float[] Veg, float[] VegCap) UnpackVegBlob(byte[] blob, int expectedLen)
    {
        var veg = new float[expectedLen];
        var vegCap = new float[expectedLen];
        int pairs = Math.Min(expectedLen, blob.Length / 8);
        for (int i = 0; i < pairs; i++)
        {
            veg[i] = BinaryPrimitives.ReadSingleLittleEndian(blob.AsSpan(i * 8));
            vegCap[i] = BinaryPrimitives.ReadSingleLittleEndian(blob.AsSpan(i * 8 + 4));
        }

        return (veg, vegCap);
    }

    public static WorldSnapshot? AssembleFromSplit(SnapshotMeta meta, string creaturesJson, byte[] vegBlob)
    {
        var creatures = DeserializeCreatures(creaturesJson);
        if (creatures == null) return null;

        int len = meta.W * meta.H;
        var (veg, vegCap) = UnpackVegBlob(vegBlob, len);
        return new WorldSnapshot
        {
            T = meta.T,
            Day = meta.Day,
            TimeOfDay = meta.TimeOfDay,
            NextId = meta.NextId,
            GenerationMax = meta.GenerationMax,
            GrowRow = meta.GrowRow,
            W = meta.W,
            H = meta.H,
            Veg = veg,
            VegCap = vegCap,
            Creatures = creatures,
        };
    }

    public static CreatureSnapshot CloneCreatureSnapshot(CreatureSnapshot cs) => new()
    {
        Id = cs.Id,
        Sp = cs.Sp,
        Sex = cs.Sex,
        X = cs.X,
        Y = cs.Y,
        Vx = cs.Vx,
        Vy = cs.Vy,
        Dir = cs.Dir,
        Genome = cs.Genome,
        Gen = cs.Gen,
        Age = cs.Age,
        Hp = cs.Hp,
        Hunger = cs.Hunger,
        Thirst = cs.Thirst,
        Energy = cs.Energy,
        State = cs.State,
        Tx = cs.Tx,
        Ty = cs.Ty,
        Target = cs.Target,
        MateCd = cs.MateCd,
        Pregnant = cs.Pregnant,
        LitterQ = cs.LitterQ,
        Walk = cs.Walk,
        Dead = cs.Dead,
        Cause = cs.Cause,
        ParentIds = cs.ParentIds.ToList(),
        OffspringIds = cs.OffspringIds.ToList(),
        Rx = cs.Rx,
        Ry = cs.Ry,
    };

    private static void RestoreScalars(SimState state, WorldSnapshot snap)
    {
        state.TGlobal = snap.T;
        state.Day = snap.Day;
        state.TimeOfDay = snap.TimeOfDay;
        state.NextId = snap.NextId;
        state.GenerationMax = snap.GenerationMax;
        state.GrowRow = snap.GrowRow;
    }

    private static void RestoreCreaturesFull(SimState state, WorldSnapshot snap)
    {
        state.Creatures.Clear();
        state.Grid.Clear();
        foreach (var cs in snap.Creatures)
        {
            state.Creatures.Add(FromSnapshot(cs));
        }
    }

    private static void RestoreCreaturesLight(SimState state, WorldSnapshot snap, bool preserveDisplay)
    {
        var prevDisplay = preserveDisplay
            ? state.Creatures.Where(c => !c.Dead).ToDictionary(c => c.Id, c => (c.Rx, c.Ry))
            : null;

        var existing = state.Creatures.ToDictionary(c => c.Id);
        var incomingIds = new HashSet<int>(snap.Creatures.Select(c => c.Id));

        for (int i = state.Creatures.Count - 1; i >= 0; i--)
        {
            if (!incomingIds.Contains(state.Creatures[i].Id))
            {
                state.Creatures.RemoveAt(i);
            }
        }

        existing = state.Creatures.ToDictionary(c => c.Id);
        foreach (var cs in snap.Creatures)
        {
            if (existing.TryGetValue(cs.Id, out var creature))
            {
                ApplySnapshotToCreature(creature, cs);
                if (prevDisplay != null && prevDisplay.TryGetValue(cs.Id, out var prev))
                {
                    creature.Rx = prev.Rx;
                    creature.Ry = prev.Ry;
                }
            }
            else
            {
                state.Creatures.Add(FromSnapshot(cs));
            }
        }
    }

    private static void ApplySnapshotToCreature(Creature creature, CreatureSnapshot cs)
    {
        creature.Sp = cs.Sp;
        creature.Sex = cs.Sex;
        creature.X = cs.X;
        creature.Y = cs.Y;
        creature.Vx = cs.Vx;
        creature.Vy = cs.Vy;
        creature.Dir = cs.Dir;
        creature.Genome = cs.Genome;
        creature.Gen = cs.Gen;
        creature.Age = cs.Age;
        creature.Hp = cs.Hp;
        creature.Hunger = cs.Hunger;
        creature.Thirst = cs.Thirst;
        creature.Energy = cs.Energy;
        creature.State = cs.State;
        creature.Tx = cs.Tx;
        creature.Ty = cs.Ty;
        creature.Target = cs.Target;
        creature.MateCd = cs.MateCd;
        creature.Pregnant = cs.Pregnant;
        creature.LitterQ = cs.LitterQ;
        creature.Walk = cs.Walk;
        creature.Dead = cs.Dead;
        creature.Cause = cs.Cause;
        creature.ParentIds.Clear();
        creature.ParentIds.AddRange(cs.ParentIds);
        creature.OffspringIds.Clear();
        creature.OffspringIds.AddRange(cs.OffspringIds);
        if (cs.Rx != 0 || cs.Ry != 0)
        {
            creature.Rx = cs.Rx;
            creature.Ry = cs.Ry;
        }
        else
        {
            creature.Rx = cs.X;
            creature.Ry = cs.Y;
        }
    }

    private static CreatureSnapshot ToSnapshot(Creature c) => new()
    {
        Id = c.Id,
        Sp = c.Sp,
        Sex = c.Sex,
        X = c.X,
        Y = c.Y,
        Vx = c.Vx,
        Vy = c.Vy,
        Dir = c.Dir,
        Genome = c.Genome,
        Gen = c.Gen,
        Age = c.Age,
        Hp = c.Hp,
        Hunger = c.Hunger,
        Thirst = c.Thirst,
        Energy = c.Energy,
        State = c.State,
        Tx = c.Tx,
        Ty = c.Ty,
        Target = c.Target,
        MateCd = c.MateCd,
        Pregnant = c.Pregnant,
        LitterQ = c.LitterQ,
        Walk = c.Walk,
        Dead = c.Dead,
        Cause = c.Cause,
        ParentIds = c.ParentIds.ToList(),
        OffspringIds = c.OffspringIds.ToList(),
        Rx = c.Rx,
        Ry = c.Ry,
    };

    private static Creature FromSnapshot(CreatureSnapshot cs) => new()
    {
        Id = cs.Id,
        Sp = cs.Sp,
        Sex = cs.Sex,
        X = cs.X,
        Y = cs.Y,
        Vx = cs.Vx,
        Vy = cs.Vy,
        Dir = cs.Dir,
        Genome = cs.Genome,
        Gen = cs.Gen,
        Age = cs.Age,
        Hp = cs.Hp,
        Hunger = cs.Hunger,
        Thirst = cs.Thirst,
        Energy = cs.Energy,
        State = cs.State,
        Tx = cs.Tx,
        Ty = cs.Ty,
        Target = cs.Target,
        MateCd = cs.MateCd,
        Pregnant = cs.Pregnant,
        LitterQ = cs.LitterQ,
        Walk = cs.Walk,
        Dead = cs.Dead,
        Cause = cs.Cause,
        Rx = cs.Rx != 0 || cs.Ry != 0 ? cs.Rx : cs.X,
        Ry = cs.Rx != 0 || cs.Ry != 0 ? cs.Ry : cs.Y,
    };
}
