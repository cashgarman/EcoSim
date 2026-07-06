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

    public static void Restore(SimState state, WorldSnapshot snap)
    {
        state.TGlobal = snap.T;
        state.Day = snap.Day;
        state.TimeOfDay = snap.TimeOfDay;
        state.NextId = snap.NextId;
        state.GenerationMax = snap.GenerationMax;
        state.GrowRow = snap.GrowRow;
        if (snap.Veg.Length == state.Veg.Length)
        {
            Array.Copy(snap.Veg, state.Veg, snap.Veg.Length);
            Array.Copy(snap.VegCap, state.VegCap, snap.VegCap.Length);
        }
        state.VegDirty = true;

        int? selectedId = state.Selected?.Id;
        state.Creatures.Clear();
        state.Grid.Clear();
        foreach (var cs in snap.Creatures)
        {
            state.Creatures.Add(FromSnapshot(cs));
        }
        if (selectedId.HasValue)
        {
            state.Selected = state.Creatures.FirstOrDefault(c => c.Id == selectedId.Value);
        }
    }

    public static string Serialize(WorldSnapshot snap) => JsonSerializer.Serialize(snap);

    public static WorldSnapshot? Deserialize(string json) =>
        JsonSerializer.Deserialize<WorldSnapshot>(json);

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
        Rx = cs.Rx,
        Ry = cs.Ry,
    };
}
