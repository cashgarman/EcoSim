using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;

namespace EcoSim.Core.Sim;

/// <summary>Frame-level input intents fed from the host each frame, consumed by sim ticks.</summary>
public sealed class PlayerIntents
{
    public double MoveX { get; set; }
    public double MoveY { get; set; }
    public double ClickGoalX { get; set; } = double.NaN;
    public double ClickGoalY { get; set; } = double.NaN;
    public bool AttackPressed { get; set; }
    public bool MatePressed { get; set; }

    public bool HasMove => Math.Abs(MoveX) > 0.01 || Math.Abs(MoveY) > 0.01;
    public bool HasClickGoal => double.IsFinite(ClickGoalX) && double.IsFinite(ClickGoalY);

    public void ClearClickGoal()
    {
        ClickGoalX = double.NaN;
        ClickGoalY = double.NaN;
    }
}

public abstract record PlayerEvent;

/// <summary>The player's animal bred; the player may switch to a newborn.</summary>
public sealed record BirthChoiceEvent(string Species, List<int> NewbornIds) : PlayerEvent;

/// <summary>Control moved to a new body. Reason: "killer", "sibling", "species".</summary>
public sealed record TransferEvent(Creature From, Creature To, string Reason) : PlayerEvent;

/// <summary>The controlled species went extinct with the player's death.</summary>
public sealed record GameOverEvent(string Species, Creature LastCreature) : PlayerEvent;

/// <summary>
/// Possession gameplay: the player controls one creature directly (WASD steering or
/// click-to-move pathfinding) while its needs keep simulating. Handles contextual
/// auto-actions (drink/graze/rest), explicit attack/mate intents, generation points
/// on breeding, and body transfer on death (killer → sibling → same species → game over).
/// </summary>
public sealed class PlayerControlSystem
{
    private readonly SimState _state;
    private readonly SpeciesCatalog _catalog;
    private readonly CreatureSystem _creatures;
    private bool _resting;

    public PlayerIntents Intents { get; } = new();
    public Queue<PlayerEvent> PendingEvents { get; } = new();
    public PlayerProgress Progress { get; }
    public int? ControlledId { get; private set; }

    public PlayerControlSystem(SimState state, SpeciesCatalog catalog, CreatureSystem creatures, PlayerProgress progress)
    {
        _state = state;
        _catalog = catalog;
        _creatures = creatures;
        Progress = progress;
    }

    public bool IsControlling => ControlledId.HasValue;

    public bool Controls(Creature c) => ControlledId == c.Id;

    /// <summary>Resolve the controlled creature by id; null if not possessing or unresolvable (e.g. mid-scrub).</summary>
    public Creature? Controlled
    {
        get
        {
            if (ControlledId is not int id) return null;
            var c = _creatures.GetById(id);
            return c == null || c.Dead ? null : c;
        }
    }

    public void Possess(Creature c)
    {
        if (c.Dead) return;
        ControlledId = c.Id;
        Progress.BodiesInhabited++;
        _state.Selected = c;
        _resting = false;
        Intents.ClearClickGoal();
        Intents.MoveX = 0;
        Intents.MoveY = 0;
        // Drop stale AI state so the inspector/BT editor don't show a phantom decision.
        c.BtNodeId = null;
        c.BtBranchUid = null;
        c.BtAction = null;
        c.Target = null;
        c.NavGoalX = double.NaN;
        c.NavGoalY = double.NaN;
        c.NavWpX = double.NaN;
        c.NavWpY = double.NaN;
        c.State = "wander";
    }

    public void Release()
    {
        ControlledId = null;
        _resting = false;
        Intents.ClearClickGoal();
    }

    /// <summary>Called from CreatureSystem.StepCreature in place of the behavior tree tick.</summary>
    public void StepPlayer(Creature c, double dt)
    {
        var def = _catalog.Get(c.Sp);
        bool canSwim = SpeciesCatalog.SpeciesCanSwim(def);
        double speed = _creatures.EffectiveSpeed(c);

        bool moved = StepMovement(c, dt, speed, canSwim);
        StepAutoActions(c, dt, def, canSwim, moved);
        StepExplicitActions(c, def);

        Intents.AttackPressed = false;
        Intents.MatePressed = false;
    }

    private bool StepMovement(Creature c, double dt, double speed, bool canSwim)
    {
        if (Intents.HasMove)
        {
            Intents.ClearClickGoal();
            c.NavGoalX = double.NaN;
            c.NavGoalY = double.NaN;
            c.NavWpX = double.NaN;
            c.NavWpY = double.NaN;

            double mag = SimMath.Hypot(Intents.MoveX, Intents.MoveY);
            double dx = Intents.MoveX / mag, dy = Intents.MoveY / mag;
            double step = speed * 2.2 * dt;
            double nx = c.X + dx * step;
            double ny = c.Y + dy * step;
            // Axis-separated slide so hugging a shoreline doesn't dead-stop.
            if (!IsWalkable(nx, ny, canSwim))
            {
                if (IsWalkable(nx, c.Y, canSwim)) ny = c.Y;
                else if (IsWalkable(c.X, ny, canSwim)) nx = c.X;
                else
                {
                    c.Vx *= 0.5;
                    c.Vy *= 0.5;
                    return true;
                }
            }
            nx = SimMath.Clamp(nx, 1, _state.W - 2);
            ny = SimMath.Clamp(ny, 1, _state.H - 2);
            c.Vx = (nx - c.X) / dt;
            c.Vy = (ny - c.Y) / dt;
            c.X = nx;
            c.Y = ny;
            c.Tx = nx;
            c.Ty = ny;
            return true;
        }

        if (Intents.HasClickGoal)
        {
            double gx = Intents.ClickGoalX, gy = Intents.ClickGoalY;
            if (SimMath.Hypot(gx - c.X, gy - c.Y) < 0.4)
            {
                Intents.ClearClickGoal();
                c.Vx *= 0.7;
                c.Vy *= 0.7;
                return false;
            }
            _creatures.MoveTowardGoal(c, gx, gy, speed, dt);
            return true;
        }

        c.Vx *= 0.7;
        c.Vy *= 0.7;
        return false;
    }

    private void StepAutoActions(Creature c, double dt, SpeciesDefinition def, bool canSwim, bool moving)
    {
        string state = moving ? "wander" : "idle";

        if (c.Thirst < 65 && CreatureActions.TryDrink(_state, c, canSwim, dt))
        {
            state = "thirst";
        }

        bool grazer = def.Diet == 0 || def.Diet == 2;
        if (grazer && c.Hunger < 70 && CreatureActions.TryGrazeBite(_state, c, dt))
        {
            state = "graze";
        }

        if (moving || Intents.HasClickGoal)
        {
            _resting = false;
        }
        else if (_resting || c.Energy < 35)
        {
            _resting = c.Energy < 90;
            if (_resting && state is "wander" or "idle")
            {
                CreatureActions.Rest(c, dt);
                state = "rest";
            }
        }

        c.State = state == "idle" ? "wander" : state;
    }

    private void StepExplicitActions(Creature c, SpeciesDefinition def)
    {
        if (Intents.AttackPressed && def.HuntsMask != 0)
        {
            var prey = NearestHuntable(c, def);
            if (prey != null) CreatureActions.TryHuntStrike(_creatures, c, prey);
        }

        if (Intents.MatePressed)
        {
            var mate = NearestEligibleMate(c);
            if (mate != null && CreatureActions.TryConsummateMate(_catalog, c, mate))
            {
                c.State = "mate";
            }
        }
    }

    private Creature? NearestHuntable(Creature c, SpeciesDefinition def)
    {
        // Slightly generous search radius; TryHuntStrike enforces the real strike range.
        double searchR = _creatures.HuntStrikeRange(c, null) * 1.5;
        Creature? best = null;
        double bestD2 = double.MaxValue;
        foreach (var o in _creatures.Nearby(c, searchR, []))
        {
            if (!_catalog.SpeciesIndex.TryGetValue(o.Sp, out int osp)) continue;
            uint bit = osp >= 0 && osp < 30 ? 1u << osp : 0;
            if ((def.HuntsMask & bit) == 0) continue;
            double d2 = (o.X - c.X) * (o.X - c.X) + (o.Y - c.Y) * (o.Y - c.Y);
            if (d2 < bestD2) { bestD2 = d2; best = o; }
        }
        return best;
    }

    private Creature? NearestEligibleMate(Creature c)
    {
        if (!_creatures.IsAdult(c) || c.MateCd > 0 || c.Pregnant > 0 || c.Energy <= 45) return null;
        Creature? best = null;
        double bestD2 = double.MaxValue;
        foreach (var o in _creatures.Nearby(c, 1.5, []))
        {
            if (o.Sp != c.Sp || o.Sex == c.Sex) continue;
            if (!_creatures.IsAdult(o) || o.MateCd > 0 || o.Pregnant > 0 || o.Energy <= 45) continue;
            double d2 = (o.X - c.X) * (o.X - c.X) + (o.Y - c.Y) * (o.Y - c.Y);
            if (d2 < bestD2) { bestD2 = d2; best = o; }
        }
        return best;
    }

    /// <summary>Called from CreatureSystem.GiveBirth for every litter, before MatePartnerId is cleared.</summary>
    public void NotifyBirth(Creature mother, int? fatherId, List<int> newbornIds)
    {
        if (ControlledId is not int id || newbornIds.Count == 0) return;
        if (mother.Id != id && fatherId != id) return;
        Progress.AddPoint(mother.Sp);
        PendingEvents.Enqueue(new BirthChoiceEvent(mother.Sp, [.. newbornIds]));
    }

    /// <summary>Called synchronously from CreatureSystem.Die, before PruneDead can run.</summary>
    public void NotifyDeath(Creature dead)
    {
        if (ControlledId != dead.Id) return;

        if (dead.Cause == "predation" && dead.KilledById is int killerId)
        {
            var killer = _creatures.GetById(killerId);
            if (killer != null && !killer.Dead)
            {
                Progress.TimesKilled++;
                Transfer(dead, killer, "killer");
                return;
            }
        }

        Progress.NaturalDeaths++;
        var sibling = PickRandom(FindCandidates(dead, siblingsOnly: true));
        if (sibling != null)
        {
            Transfer(dead, sibling, "sibling");
            return;
        }

        var sameSpecies = PickRandom(FindCandidates(dead, siblingsOnly: false));
        if (sameSpecies != null)
        {
            Transfer(dead, sameSpecies, "species");
            return;
        }

        ControlledId = null;
        PendingEvents.Enqueue(new GameOverEvent(dead.Sp, dead));
    }

    private void Transfer(Creature from, Creature to, string reason)
    {
        Possess(to);
        PendingEvents.Enqueue(new TransferEvent(from, to, reason));
    }

    private List<Creature> FindCandidates(Creature dead, bool siblingsOnly)
    {
        var list = new List<Creature>();
        foreach (var c in _state.Creatures)
        {
            if (c.Dead || c.Id == dead.Id || c.Sp != dead.Sp) continue;
            if (siblingsOnly && !SharesParent(c, dead)) continue;
            list.Add(c);
        }
        return list;
    }

    private static bool SharesParent(Creature a, Creature b)
    {
        foreach (int p in a.ParentIds)
        {
            if (b.ParentIds.Contains(p)) return true;
        }
        return false;
    }

    private static Creature? PickRandom(List<Creature> list)
    {
        if (list.Count == 0) return null;
        int i = (int)(GlobalRng.Next() * list.Count);
        return list[Math.Min(i, list.Count - 1)];
    }

    private bool IsWalkable(double x, double y, bool canSwim)
    {
        int rx = (int)Math.Round(x), ry = (int)Math.Round(y);
        if (!GridHelpers.InBounds(_state, rx, ry)) return false;
        var bi = (Biome)_state.Biome[GridHelpers.Idx(_state, rx, ry)];
        if (BiomeData.IsWater(bi) && !canSwim) return false;
        return bi != Biome.Peak;
    }
}
