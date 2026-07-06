using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;

namespace EcoSim.Core.Sim;

public sealed class CreatureSystem
{
    private readonly SimState _state;
    private readonly SpeciesCatalog _catalog;
    private readonly BehaviorTree _behaviorTree;
    private readonly List<Creature> _nearbyScratch = [];
    public LifeStory? LifeStory { get; set; }
    public SpeciesStatsTracker? SpeciesStats { get; set; }

    public CreatureSystem(SimState state, SpeciesCatalog catalog, BehaviorTree behaviorTree)
    {
        _state = state;
        _catalog = catalog;
        _behaviorTree = behaviorTree;
    }

    public string RandomSex() => GlobalRng.Next() < 0.5 ? "female" : "male";

    public Creature MakeCreature(string sp, double x, double y, Genome? genome = null, int? gen = null, string? sex = null)
    {
        var g = genome ?? Genetics.NewGenome(_catalog, sp);
        var c = new Creature
        {
            Id = _state.NextId++,
            Sp = sp,
            Sex = sex ?? RandomSex(),
            X = x,
            Y = y,
            Genome = g,
            Gen = gen ?? 1,
            Age = GlobalRng.Rf(0.15, 0.4) * g.Lifespan,
            Hp = 100,
            Hunger = GlobalRng.Rf(60, 90),
            Thirst = GlobalRng.Rf(60, 90),
            Energy = GlobalRng.Rf(70, 100),
            State = "wander",
            Tx = x,
            Ty = y,
            MateCd = GlobalRng.Rf(1, 4),
            Rx = x,
            Ry = y,
        };
        _state.Creatures.Add(c);
        InsertIntoGrid(c, GridKeyFor(c));
        if (c.Gen > _state.GenerationMax) _state.GenerationMax = c.Gen;
        LifeStory?.Record(c, _state, "appeared");
        SpeciesStats?.RecordBirth(c.Sp, _state.TGlobal);
        return c;
    }

    public Creature? GetById(int id)
    {
        foreach (var c in _state.Creatures)
        {
            if (c.Id == id) return c;
        }
        return null;
    }

    public double ESize(Creature c) => c.Genome.Size * (IsAdult(c) ? 1 : 0.55);

    public double HuntStrikeRange(Creature hunter, Creature? prey)
    {
        double h = ESize(hunter);
        double p = prey != null ? ESize(prey) : 0.5;
        return h * 0.55 + p * 0.45 + 0.65;
    }

    public double HuntStrikeChance(Creature hunter) => 0.35 + hunter.Genome.Agg * 0.35;

    public bool IsAdult(Creature c) => c.Age >= c.Genome.Lifespan * 0.25;

    public (double X, double Y) SimPos(Creature c) => (c.X, c.Y);

    public void RebuildGrid()
    {
        _state.Grid.Clear();
        foreach (var c in _state.Creatures)
        {
            if (c.Dead) { c.GridKey = null; continue; }
            InsertIntoGrid(c, GridKeyFor(c));
        }
    }

    public void SyncGrid()
    {
        foreach (var c in _state.Creatures)
        {
            if (c.Dead)
            {
                if (c.GridKey != null) RemoveFromGrid(c);
                continue;
            }
            long key = GridKeyFor(c);
            if (c.GridKey != key)
            {
                if (c.GridKey != null) RemoveFromGrid(c);
                InsertIntoGrid(c, key);
            }
        }
    }

    private long GridKeyFor(Creature c)
    {
        var p = SimPos(c);
        return GridHelpers.Gkey((int)Math.Floor(p.X / SimConstants.Cell), (int)Math.Floor(p.Y / SimConstants.Cell));
    }

    private void InsertIntoGrid(Creature c, long key)
    {
        if (!_state.Grid.TryGetValue(key, out var bucket))
        {
            bucket = [];
            _state.Grid[key] = bucket;
        }
        bucket.Add(c);
        c.GridKey = key;
    }

    private void RemoveFromGrid(Creature c)
    {
        if (c.GridKey == null) return;
        if (_state.Grid.TryGetValue(c.GridKey.Value, out var bucket))
        {
            bucket.Remove(c);
            if (bucket.Count == 0) _state.Grid.Remove(c.GridKey.Value);
        }
        c.GridKey = null;
    }

    public List<Creature> Nearby(Creature c, double r, List<Creature>? outList = null)
    {
        var list = outList ?? _nearbyScratch;
        list.Clear();
        var pos = SimPos(c);
        int cx = (int)Math.Floor(pos.X / SimConstants.Cell);
        int cy = (int)Math.Floor(pos.Y / SimConstants.Cell);
        int rr = (int)Math.Ceiling(r / SimConstants.Cell);
        double r2 = r * r;
        for (int dy = -rr; dy <= rr; dy++)
        {
            for (int dx = -rr; dx <= rr; dx++)
            {
                if (!_state.Grid.TryGetValue(GridHelpers.Gkey(cx + dx, cy + dy), out var bucket)) continue;
                foreach (var o in bucket)
                {
                    if (o == c || o.Dead) continue;
                    var op = SimPos(o);
                    double ddx = op.X - pos.X, ddy = op.Y - pos.Y;
                    if (ddx * ddx + ddy * ddy < r2) list.Add(o);
                }
            }
        }
        return list;
    }

    public void Wander(Creature c)
    {
        bool canSwim = SpeciesCatalog.SpeciesCanSwim(_catalog.Get(c.Sp));
        if (SimMath.Hypot(c.Tx - c.X, c.Ty - c.Y) < 0.6 || GlobalRng.Next() < 0.02)
        {
            var t = Navigation.PickRandomWalkableTile(_state, c.X, c.Y, 6, canSwim);
            c.Tx = SimMath.Clamp(t.X, 1, _state.W - 2);
            c.Ty = SimMath.Clamp(t.Y, 1, _state.H - 2);
        }
    }

    public void MoveTowardGoal(Creature c, double goalX, double goalY, double speed, double dt, bool direct = false, bool forceReplan = false)
    {
        bool canSwim = SpeciesCatalog.SpeciesCanSwim(_catalog.Get(c.Sp));
        int navR = QualityConfig.NavRadius;
        if (c.State == "thirst" && Navigation.AtWaterEdge(_state, c.X, c.Y))
        {
            c.Vx *= 0.7;
            c.Vy *= 0.7;
            return;
        }

        int replanEvery = QualityConfig.NavReplanInterval;
        int phase = ((int)Math.Floor(_state.TGlobal * 24) + c.Id) % replanEvery;
        bool goalChanged = c.NavGoalX != goalX || c.NavGoalY != goalY;
        bool shouldPlan = direct || forceReplan || phase == 0 || goalChanged
            || !double.IsFinite(c.NavWpX) || !double.IsFinite(c.NavWpY);

        double tx, ty;
        if (shouldPlan)
        {
            var wp = Navigation.ResolveMovementTarget(_state, c.X, c.Y, goalX, goalY, canSwim, navR, direct);
            tx = wp?.X ?? goalX;
            ty = wp?.Y ?? goalY;
            c.NavGoalX = goalX;
            c.NavGoalY = goalY;
            c.NavWpX = tx;
            c.NavWpY = ty;
        }
        else
        {
            tx = c.NavWpX;
            ty = c.NavWpY;
        }
        c.Tx = tx;
        c.Ty = ty;

        double dx = tx - c.X, dy = ty - c.Y, d = SimMath.Hypot(dx, dy);
        if (d < 0.05) { c.Vx *= 0.7; c.Vy *= 0.7; return; }
        double nx = c.X + dx / d * speed * 2.2 * dt;
        double ny = c.Y + dy / d * speed * 2.2 * dt;
        int rx = (int)Math.Round(nx), ry = (int)Math.Round(ny);
        Biome bi = GridHelpers.InBounds(_state, rx, ry)
            ? (Biome)_state.Biome[GridHelpers.Idx(_state, rx, ry)]
            : Biome.Ocean;
        if ((BiomeData.IsWater(bi) && !canSwim) || bi == Biome.Peak)
        {
            c.Vx *= 0.5;
            c.Vy *= 0.5;
            return;
        }
        c.Vx = (nx - c.X) / dt;
        c.Vy = (ny - c.Y) / dt;
        c.X = nx;
        c.Y = ny;
    }

    public void MoveTo(Creature c, double speed, double dt) => MoveTowardGoal(c, c.Tx, c.Ty, speed, dt);

    public (int X, int Y)? FindFood(Creature c, int r)
    {
        (int X, int Y)? best = null;
        double bv = 0.05;
        int ix = (int)Math.Round(c.X), iy = (int)Math.Round(c.Y);
        for (int dy = -r; dy <= r; dy += 2)
        {
            for (int dx = -r; dx <= r; dx += 2)
            {
                int nx = ix + dx, ny = iy + dy;
                if (!GridHelpers.InBounds(_state, nx, ny)) continue;
                int ti = GridHelpers.Idx(_state, nx, ny);
                if (!BiomeData.IsWater(_state.Biome[ti]) && _state.Veg[ti] > bv)
                {
                    bv = _state.Veg[ti];
                    best = (nx, ny);
                }
            }
        }
        return best;
    }

    public int GiveBirth(Creature c)
    {
        int q = c.LitterQ > 0 ? c.LitterQ : 1;
        c.LitterQ = 0;
        var partner = c.MatePartner ?? c.Genome;
        int? fatherId = c.MatePartnerId;
        int born = 0;
        for (int i = 0; i < q; i++)
        {
            if (AliveCount() >= SimConstants.MaxPop) break;
            var cg = Genetics.BreedGenome(_catalog, c.Genome, partner);
            var pos = PickBirthPosition(c);
            var baby = MakeCreature(c.Sp, pos.X, pos.Y, cg, c.Gen + 1, RandomSex());
            baby.Age = 0;
            baby.Hunger = 70;
            baby.Thirst = 70;
            baby.Energy = 80;
            LinkBirthParents(baby, c, fatherId);
            born++;
        }
        c.MatePartnerId = null;
        return born;
    }

    private void LinkBirthParents(Creature baby, Creature mother, int? fatherId)
    {
        baby.ParentIds.Add(mother.Id);
        if (!mother.OffspringIds.Contains(baby.Id)) mother.OffspringIds.Add(baby.Id);
        if (fatherId != null)
        {
            baby.ParentIds.Add(fatherId.Value);
            var father = GetById(fatherId.Value);
            if (father != null && !father.OffspringIds.Contains(baby.Id)) father.OffspringIds.Add(baby.Id);
        }
    }

    public void Die(Creature c, string? cause = null)
    {
        if (c.Dead) return;
        if (cause != null) c.Cause = cause;
        c.Dead = true;
        RemoveFromGrid(c);
        string deathKey = string.IsNullOrEmpty(c.Cause) ? "unknown" : c.Cause;
        SpeciesStats?.RecordDeath(c.Sp, deathKey);
        LifeStory?.Record(c, _state, "died", detail: deathKey);
        int ti = GridHelpers.Idx(_state,
            (int)SimMath.Clamp(Math.Round(c.X), 0, _state.W - 1),
            (int)SimMath.Clamp(Math.Round(c.Y), 0, _state.H - 1));
        if (!BiomeData.IsWater(_state.Biome[ti]))
        {
            _state.Veg[ti] = Math.Min(_state.VegCap[ti], _state.Veg[ti] + 0.15f);
        }
    }

    public bool StepNeeds(Creature c, double dt)
    {
        var g = c.Genome;
        double load = g.Size * g.Metab;
        c.Hunger -= (0.9 * load + (c.State == "hunt" ? 0.6 : 0)) * dt;
        c.Thirst -= 1.0 * load * dt;
        c.Energy -= (0.6 * load + (c.Vx * c.Vx + c.Vy * c.Vy > 0.02 ? 0.9 : 0)) * dt;
        c.Age += dt / 24;
        if (c.MateCd > 0) c.MateCd -= dt;

        int ti = GridHelpers.Idx(_state,
            (int)SimMath.Clamp(Math.Round(c.X), 0, _state.W - 1),
            (int)SimMath.Clamp(Math.Round(c.Y), 0, _state.H - 1));
        double localT = _state.Temp[ti];
        double stress = Math.Max(0, Math.Abs(localT - g.Temp) - g.Tol);
        if (stress > 0) c.Hp -= stress * 14 * dt;

        if (c.Hunger <= 0) { c.Hp -= 6 * dt; c.Hunger = 0; }
        if (c.Thirst <= 0) { c.Hp -= 7 * dt; c.Thirst = 0; }
        if (c.Energy <= 0) c.Energy = 0;
        if (c.Hunger > 55 && c.Thirst > 55 && stress <= 0) c.Hp = Math.Min(100, c.Hp + 4 * dt);

        if (c.Hp <= 0)
        {
            if (string.IsNullOrEmpty(c.Cause) || c.Cause == "exhaustion")
            {
                if (c.Hunger <= 0 && c.Thirst <= 0) c.Cause = "starvation and dehydration";
                else if (c.Hunger <= 0) c.Cause = "starvation";
                else if (c.Thirst <= 0) c.Cause = "dehydration";
            }
            Die(c, string.IsNullOrEmpty(c.Cause) ? "exhaustion" : c.Cause);
            return false;
        }
        if (c.Age >= g.Lifespan) { Die(c, "old age"); return false; }

        if (c.Pregnant > 0)
        {
            c.Pregnant -= dt;
            if (c.Pregnant <= 0) GiveBirth(c);
        }
        return true;
    }

    public void StepCreature(Creature c, double dt)
    {
        if (!StepNeeds(c, dt)) return;
        string prevState = c.State;
        _behaviorTree.Tick(c, dt, this, executeActions: true);
        if (c.State != prevState)
        {
            LifeStory?.ObserveDecision(c, _state, c.State);
        }
        double sp2 = SimMath.Hypot(c.Vx, c.Vy);
        c.Walk += sp2 * dt * 10 + dt * 0.5;
        if (Math.Abs(c.Vx) > 0.001) c.Dir = c.Vx > 0 ? 1 : -1;
    }

    public bool TileHasAdjacentWater(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (GridHelpers.InBounds(_state, nx, ny) && BiomeData.IsWater(_state.Biome[GridHelpers.Idx(_state, nx, ny)]))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public (double X, double Y)? FindSpawnTile(string sp)
    {
        var def = _catalog.Get(sp);
        bool nearWater = def.SpawnNearWater;
        bool canSwim = SpeciesCatalog.SpeciesCanSwim(def);
        int maxTries = nearWater ? 450 : 300;
        int nearWaterTries = nearWater ? 150 : 0;
        for (int tries = 0; tries < maxTries; tries++)
        {
            int x = GlobalRng.Ri(2, _state.W - 3);
            int y = GlobalRng.Ri(2, _state.H - 3);
            var b = (Biome)_state.Biome[GridHelpers.Idx(_state, x, y)];
            if (BiomeData.IsWater(b) || b == Biome.Peak) continue;
            if (tries < nearWaterTries && !TileHasAdjacentWater(x, y)) continue;
            if (TryPickSpawnPosition(x, y, canSwim, out double px, out double py))
            {
                return (px, py);
            }
        }
        return null;
    }

    private bool TryPickSpawnPosition(int tileX, int tileY, bool canSwim, out double x, out double y)
    {
        x = tileX + GlobalRng.Rf(-0.3, 0.3);
        y = tileY + GlobalRng.Rf(-0.3, 0.3);
        int rx = (int)Math.Round(x);
        int ry = (int)Math.Round(y);
        if (GridHelpers.InBounds(_state, rx, ry) && Navigation.IsTileWalkable(_state, rx, ry, canSwim))
        {
            return true;
        }

        x = tileX + 0.5;
        y = tileY + 0.5;
        return Navigation.IsTileWalkable(_state, tileX, tileY, canSwim);
    }

    private (double X, double Y) PickBirthPosition(Creature parent)
    {
        bool canSwim = SpeciesCatalog.SpeciesCanSwim(_catalog.Get(parent.Sp));
        int px = (int)Math.Round(parent.X);
        int py = (int)Math.Round(parent.Y);
        for (int i = 0; i < 8; i++)
        {
            double x = parent.X + GlobalRng.Rf(-0.8, 0.8);
            double y = parent.Y + GlobalRng.Rf(-0.8, 0.8);
            int rx = (int)Math.Round(x);
            int ry = (int)Math.Round(y);
            if (GridHelpers.InBounds(_state, rx, ry) && Navigation.IsTileWalkable(_state, rx, ry, canSwim))
            {
                return (x, y);
            }
        }
        if (GridHelpers.InBounds(_state, px, py) && Navigation.IsTileWalkable(_state, px, py, canSwim))
        {
            return (px + 0.5, py + 0.5);
        }
        return (parent.X, parent.Y);
    }

    public void StockLife()
    {
        double density = _state.Cfg.Animals;
        double areaScale = Math.Max(0.5, _state.WorldAreaKm2 / 64);
        double scaledBudget = 260 * Math.Sqrt(areaScale);
        int budget = Math.Min((int)Math.Floor(SimConstants.MaxPop * 0.45), (int)Math.Floor(SimMath.Lerp(0, scaledBudget, density)));
        foreach (string sp in _catalog.SpeciesKeys)
        {
            double weight = _catalog.Get(sp).StockWeight;
            int n = (int)Math.Round(budget * weight);
            for (int i = 0; i < n; i++)
            {
                var t = FindSpawnTile(sp);
                if (t.HasValue) MakeCreature(sp, t.Value.X, t.Value.Y);
            }
        }
    }

    public void PruneDead()
    {
        int aliveCount = AliveCount();
        int deadCount = _state.Creatures.Count - aliveCount;
        bool overCapacity = _state.Creatures.Count > SimConstants.MaxPop * 1.15;
        bool deadBloat = deadCount > Math.Max(40, aliveCount * 0.08);
        if (!overCapacity && !deadBloat && GlobalRng.Next() >= 0.05) return;

        int before = _state.Creatures.Count;
        _state.Creatures.RemoveAll(c => c.Dead && c != _state.Selected);
        if (_state.Creatures.Count != before) RebuildGrid();
    }

    public int AliveCount()
    {
        int n = 0;
        foreach (var c in _state.Creatures)
        {
            if (!c.Dead) n++;
        }
        return n;
    }

    public int KillAllBySpecies(string speciesKey)
    {
        int killed = 0;
        foreach (var c in _state.Creatures)
        {
            if (!c.Dead && c.Sp == speciesKey)
            {
                Die(c, "removed");
                killed++;
            }
        }
        return killed;
    }
}
