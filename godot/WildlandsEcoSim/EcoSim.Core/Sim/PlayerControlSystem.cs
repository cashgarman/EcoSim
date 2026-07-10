using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;

namespace EcoSim.Core.Sim;

/// <summary>Frame-level input intents fed from the host each frame, consumed by sim ticks.</summary>
public sealed class PlayerIntents
{
    public double MoveX { get; set; }
    public double MoveY { get; set; }
    public bool SprintHeld { get; set; }
    public bool AttackPressed { get; set; }
    public bool MatePressed { get; set; }

    public bool HasMove => Math.Abs(MoveX) > 0.01 || Math.Abs(MoveY) > 0.01;
}

public enum PlayerOrderKind
{
    /// <summary>Travel to a point on the map.</summary>
    MoveTo,

    /// <summary>Travel to water (or its edge for non-swimmers) and drink until full.</summary>
    DrinkAt,

    /// <summary>Travel to a vegetation tile and graze until full or it's bare.</summary>
    GrazeAt,

    /// <summary>Chase the target, strike when in range, and feed on the kill.</summary>
    Hunt,

    /// <summary>Run away from the target until it's dead or out of range.</summary>
    FleeFrom,

    /// <summary>Approach the target and mate on contact.</summary>
    MateWith,
}

/// <summary>A contextual click order for the possessed creature.</summary>
public sealed class PlayerOrder
{
    public required PlayerOrderKind Kind { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public int? TargetId { get; init; }
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
/// contextual click orders) while its needs keep simulating. Clicks are interpreted by
/// what was clicked — ground moves, water drinks, prey hunts, threats flee, mates court.
/// Handles generation points on breeding and body transfer on death
/// (killer → sibling → same species → game over).
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
    public PlayerOrder? Order { get; private set; }

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
        Order = null;
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
        Order = null;
    }

    /// <summary>
    /// Interpret a map click as a contextual order for the possessed creature:
    /// a mate is courted, a predator of ours is fled from, our prey is hunted,
    /// water means go drink, vegetation (for grazers) means go feed, anything else is a move order.
    /// </summary>
    public void IssueClickOrder(double x, double y, Creature? clicked)
    {
        var c = Controlled;
        if (c == null) return;
        var def = _catalog.Get(c.Sp);
        bool canSwim = SpeciesCatalog.SpeciesCanSwim(def);

        if (clicked != null && !clicked.Dead && clicked.Id != c.Id)
        {
            uint bit = SpeciesBit(clicked.Sp);
            if (clicked.Sp == c.Sp && clicked.Sex != c.Sex)
            {
                Order = new PlayerOrder { Kind = PlayerOrderKind.MateWith, TargetId = clicked.Id };
                return;
            }
            if ((def.PreyMask & bit) != 0)
            {
                Order = new PlayerOrder { Kind = PlayerOrderKind.FleeFrom, TargetId = clicked.Id };
                return;
            }
            if ((def.HuntsMask & bit) != 0)
            {
                Order = new PlayerOrder { Kind = PlayerOrderKind.Hunt, TargetId = clicked.Id };
                return;
            }

            Order = MoveOrder(clicked.X, clicked.Y, canSwim);
            return;
        }

        int tx = GridHelpers.TileX(_state, x);
        int ty = GridHelpers.TileY(_state, y);
        int ti = GridHelpers.Idx(_state, tx, ty);
        if (BiomeData.IsWater(_state.Biome[ti]))
        {
            (double gx, double gy) = canSwim
                ? (x, y)
                : Navigation.NearestShoreDrinkTile(_state, x, y, 24)
                    ?? Navigation.UnsnappedWalkableGoal(_state, x, y, canSwim: false);
            Order = new PlayerOrder { Kind = PlayerOrderKind.DrinkAt, X = gx, Y = gy };
            return;
        }

        if (def.Diet != 1 && GrazeFood.IsEdible(_state, ti))
        {
            Order = new PlayerOrder { Kind = PlayerOrderKind.GrazeAt, X = tx + 0.5, Y = ty + 0.5 };
            return;
        }

        Order = MoveOrder(x, y, canSwim);
    }

    public void CancelOrder() => Order = null;

    /// <summary>Player-facing description of the active order, or null when idle.</summary>
    public string? OrderDescription()
    {
        var c = Controlled;
        var o = Order;
        if (o == null || c == null) return null;
        bool canSwim = SpeciesCatalog.SpeciesCanSwim(_catalog.Get(c.Sp));
        switch (o.Kind)
        {
            case PlayerOrderKind.MoveTo:
                return "Moving";
            case PlayerOrderKind.DrinkAt:
                return Navigation.CanDrinkHere(_state, c.X, c.Y, canSwim) ? "Drinking" : "Heading to water";
            case PlayerOrderKind.GrazeAt:
                return CreatureBehaviorLabels.IsActivelyGrazing(c, _state) ? "Grazing" : "Heading to graze";
            case PlayerOrderKind.Hunt:
                return $"Hunting {TargetLabel()}";
            case PlayerOrderKind.FleeFrom:
                return $"Fleeing {TargetLabel()}!";
            case PlayerOrderKind.MateWith:
                return "Courting";
            default:
                return null;
        }

        string TargetLabel()
        {
            var t = o.TargetId is int id ? _creatures.GetById(id) : null;
            return t == null ? "prey" : $"{_catalog.Get(t.Sp).Label} #{t.Id}";
        }
    }

    private PlayerOrder MoveOrder(double x, double y, bool canSwim)
    {
        var goal = Navigation.UnsnappedWalkableGoal(_state, x, y, canSwim);
        return new PlayerOrder { Kind = PlayerOrderKind.MoveTo, X = goal.X, Y = goal.Y };
    }

    private uint SpeciesBit(string sp)
    {
        if (!_catalog.SpeciesIndex.TryGetValue(sp, out int idx)) return 0;
        return idx >= 0 && idx < 30 ? 1u << idx : 0;
    }

    /// <summary>Called from CreatureSystem.StepCreature in place of the behavior tree tick.</summary>
    public void StepPlayer(Creature c, double dt)
    {
        var def = _catalog.Get(c.Sp);
        bool canSwim = SpeciesCatalog.SpeciesCanSwim(def);
        double speed = _creatures.EffectiveSpeed(c);
        bool sprinting = Intents.SprintHeld && c.Energy > SimConstants.SprintMinEnergy;
        if (sprinting) speed *= SimConstants.SprintSpeedMult;

        bool moved;
        if (Intents.HasMove)
        {
            Order = null;
            moved = SteerDirect(c, dt, speed, canSwim);
        }
        else if (Order != null)
        {
            moved = ExecuteOrder(c, dt, def, canSwim, speed);
        }
        else
        {
            c.Vx *= 0.7;
            c.Vy *= 0.7;
            moved = false;
        }

        if (sprinting && moved)
        {
            c.Energy = Math.Max(0, c.Energy - SimConstants.SprintEnergyPerSec * dt);
        }

        if (Order == null)
        {
            StepAutoActions(c, dt, def, canSwim, moved);
        }

        StepExplicitActions(c, def);

        Intents.AttackPressed = false;
        Intents.MatePressed = false;
    }

    private bool SteerDirect(Creature c, double dt, double speed, bool canSwim)
    {
        c.NavGoalX = double.NaN;
        c.NavGoalY = double.NaN;
        c.NavWpX = double.NaN;
        c.NavWpY = double.NaN;
        c.State = "wander";

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

    private bool ExecuteOrder(Creature c, double dt, SpeciesDefinition def, bool canSwim, double speed)
    {
        var o = Order!;
        switch (o.Kind)
        {
            case PlayerOrderKind.MoveTo:
            {
                if (SimMath.Hypot(o.X - c.X, o.Y - c.Y) < 0.4)
                {
                    StopHere(c);
                    return false;
                }
                c.State = "wander";
                _creatures.MoveTowardGoal(c, o.X, o.Y, speed, dt);
                return true;
            }

            case PlayerOrderKind.DrinkAt:
            {
                if (Navigation.CanDrinkHere(_state, c.X, c.Y, canSwim))
                {
                    c.State = "thirst";
                    CreatureActions.Drink(c, dt);
                    c.Vx *= 0.7;
                    c.Vy *= 0.7;
                    if (c.Thirst >= 99.5) StopHere(c);
                    return false;
                }
                if (SimMath.Hypot(o.X - c.X, o.Y - c.Y) < 0.4)
                {
                    // Arrived but still dry: retarget the nearest real drink spot or give up.
                    var shore = Navigation.NearestShoreDrinkTile(_state, c.X, c.Y, 16);
                    if (shore.HasValue) { o.X = shore.Value.X; o.Y = shore.Value.Y; }
                    else { StopHere(c); return false; }
                }
                c.State = "thirst";
                _creatures.MoveTowardGoal(c, o.X, o.Y, speed, dt);
                return true;
            }

            case PlayerOrderKind.GrazeAt:
            {
                bool arrived = SimMath.Hypot(o.X - c.X, o.Y - c.Y) < 0.6;
                if (arrived)
                {
                    c.State = "graze";
                    bool ate = CreatureActions.TryGrazeBite(_state, c, dt);
                    c.Vx *= 0.7;
                    c.Vy *= 0.7;
                    if (!ate || c.Hunger >= 99.5) StopHere(c);
                    return false;
                }
                c.State = "graze";
                _creatures.MoveTowardGoal(c, o.X, o.Y, speed, dt);
                return true;
            }

            case PlayerOrderKind.Hunt:
            {
                var prey = o.TargetId is int pid ? _creatures.GetById(pid) : null;
                if (prey == null || prey.Dead)
                {
                    StopHere(c);
                    return false;
                }
                c.State = "hunt";
                c.Target = prey.Id;
                double dist = SimMath.Hypot(prey.X - c.X, prey.Y - c.Y);
                double strikeR = _creatures.HuntStrikeRange(c, prey);
                if (dist >= strikeR)
                {
                    _creatures.MoveTowardGoal(c, prey.X, prey.Y, speed, dt, direct: true);
                    CreatureActions.TryHuntStrike(_creatures, c, prey);
                    if (prey.Dead) StopHere(c);
                    return true;
                }
                c.Vx *= 0.25;
                c.Vy *= 0.25;
                CreatureActions.TryHuntStrike(_creatures, c, prey);
                if (prey.Dead) StopHere(c);
                return false;
            }

            case PlayerOrderKind.FleeFrom:
            {
                var threat = o.TargetId is int tid ? _creatures.GetById(tid) : null;
                double safeDist = Math.Max(12, c.Genome.Sense * 2);
                if (threat == null || threat.Dead
                    || SimMath.Hypot(threat.X - c.X, threat.Y - c.Y) > safeDist)
                {
                    StopHere(c);
                    return false;
                }
                c.State = "flee";
                c.Target = threat.Id;
                double a = Math.Atan2(c.Y - threat.Y, c.X - threat.X);
                var flee = Navigation.UnsnappedWalkableGoal(
                    _state, c.X + Math.Cos(a) * 6, c.Y + Math.Sin(a) * 6, canSwim);
                _creatures.MoveTowardGoal(c, flee.X, flee.Y, speed, dt, direct: true);
                return true;
            }

            case PlayerOrderKind.MateWith:
            {
                var mate = o.TargetId is int mid ? _creatures.GetById(mid) : null;
                if (mate == null || mate.Dead || mate.Sp != c.Sp || mate.Sex == c.Sex)
                {
                    StopHere(c);
                    return false;
                }
                c.State = "mate";
                c.Target = mate.Id;
                double dist = SimMath.Hypot(mate.X - c.X, mate.Y - c.Y);
                if (dist < 1.0)
                {
                    // On contact: either it works now or it can't (cooldown/energy/pregnant) — done either way.
                    CreatureActions.TryConsummateMate(_catalog, c, mate);
                    StopHere(c);
                    return false;
                }
                _creatures.MoveTowardGoal(c, mate.X, mate.Y, speed, dt, direct: true);
                return true;
            }

            default:
                StopHere(c);
                return false;
        }
    }

    private void StopHere(Creature c)
    {
        Order = null;
        c.Target = null;
        c.Vx *= 0.7;
        c.Vy *= 0.7;
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

        if (moving)
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
            if ((def.HuntsMask & SpeciesBit(o.Sp)) == 0) continue;
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
