using System.Text.Json.Nodes;
using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Behavior;

public static class BehaviorExecutor
{
    public sealed class GoalResult
    {
        public double GoalX { get; init; }
        public double GoalY { get; init; }
        public int? TargetId { get; init; }
    }

    public static GoalResult ResolveGoals(SimState state, JsonObject action, BehaviorContext ctx, CreatureSystem creatures)
    {
        var c = ctx.Creature;
        string goal = action["goal"]?.GetValue<string>() ?? "";
        double goalX = c.Tx, goalY = c.Ty;
        int? targetId = null;

        void ApplySnapped(double gx, double gy)
        {
            var sn = Navigation.SnapWalkableGoal(state, (int)Math.Round(gx), (int)Math.Round(gy), ctx.CanSwim, 8);
            if (sn.HasValue) { goalX = sn.Value.X + 0.5; goalY = sn.Value.Y + 0.5; }
            else { goalX = gx; goalY = gy; }
        }

        switch (goal)
        {
            case "hold":
                goalX = c.X; goalY = c.Y;
                break;

            case "awayFromThreat":
                if (ctx.Threat != null)
                {
                    targetId = ctx.Threat.Id;
                    var tp = creatures.SimPos(ctx.Threat);
                    double a = Math.Atan2(c.Y - tp.Y, c.X - tp.X);
                    var flee = Navigation.UnsnappedWalkableGoal(state, c.X + Math.Cos(a) * 6, c.Y + Math.Sin(a) * 6, ctx.CanSwim);
                    goalX = flee.X; goalY = flee.Y;
                }
                break;

            case "nearestWater":
            {
                var pos = creatures.SimPos(c);
                int seekR = Navigation.WaterSeekRadius(ctx.SenseR);
                var w = Navigation.WaterEdgeGoalFromField(state, pos.X, pos.Y, seekR)
                    ?? Navigation.NearestWaterEdgeTarget(state, pos.X, pos.Y, seekR);
                if (w.HasValue) { goalX = w.Value.X; goalY = w.Value.Y; }
                else { creatures.Wander(c); goalX = c.Tx; goalY = c.Ty; }
                break;
            }

            case "bestFoodOrWander":
            {
                int ti = GridHelpers.Idx(state,
                    (int)SimMath.Clamp(Math.Round(c.X), 0, state.W - 1),
                    (int)SimMath.Clamp(Math.Round(c.Y), 0, state.H - 1));
                if (state.Veg[ti] <= 0.04f)
                {
                    var t = creatures.FindFood(c, (int)ctx.SenseR);
                    if (t.HasValue) ApplySnapped(t.Value.X, t.Value.Y);
                    else creatures.Wander(c);
                    goalX = c.Tx; goalY = c.Ty;
                }
                else { goalX = c.X; goalY = c.Y; }
                break;
            }

            case "chasePrey":
                if (ctx.Prey != null)
                {
                    targetId = ctx.Prey.Id;
                    var pp = creatures.SimPos(ctx.Prey);
                    var g = Navigation.UnsnappedWalkableGoal(state, pp.X, pp.Y, ctx.CanSwim);
                    goalX = g.X; goalY = g.Y;
                }
                else { creatures.Wander(c); goalX = c.Tx; goalY = c.Ty; }
                break;

            case "approachMate":
                if (ctx.Mate != null)
                {
                    targetId = ctx.Mate.Id;
                    var mp = creatures.SimPos(ctx.Mate);
                    var g = Navigation.UnsnappedWalkableGoal(state, mp.X, mp.Y, ctx.CanSwim);
                    goalX = g.X; goalY = g.Y;
                }
                else { creatures.Wander(c); goalX = c.Tx; goalY = c.Ty; }
                break;

            case "wander":
            case "randomWalkable":
                creatures.Wander(c);
                goalX = c.Tx; goalY = c.Ty;
                break;
        }

        return new GoalResult { GoalX = goalX, GoalY = goalY, TargetId = targetId };
    }

    public static void ApplyDecision(Creature creature, BehaviorDecision decision, CreatureSystem creatures, SimState state)
    {
        bool reuseGoals = ShouldReuseGoals(state, creature, decision);
        var goals = reuseGoals
            ? ReusedGoals(creature)
            : ResolveGoals(state, decision.Action, decision.Ctx, creatures);

        string newState = decision.Action["state"]?.GetValue<string>() ?? creature.State;
        if (newState != creature.State)
        {
            creature.StateCommittedSince = state.TGlobal;
        }

        creature.State = newState;
        creature.BtNodeId = decision.NodeId;
        creature.BtBranchUid = decision.BranchUid;
        creature.BtAction = decision.Action;
        creature.Tx = goals.GoalX;
        creature.Ty = goals.GoalY;
        creature.Target = goals.TargetId;
        creature.BtSpeedMult = decision.Action.TryGetPropertyValue("speedMult", out var sm) ? sm!.GetValue<double>() : 1;

        if (!reuseGoals)
        {
            creature.NavGoalX = double.NaN;
            creature.NavGoalY = double.NaN;
            creature.NavWpX = double.NaN;
            creature.NavWpY = double.NaN;
        }
    }

    private static GoalResult ReusedGoals(Creature creature)
    {
        double goalX = double.IsFinite(creature.NavGoalX) ? creature.NavGoalX : creature.Tx;
        double goalY = double.IsFinite(creature.NavGoalY) ? creature.NavGoalY : creature.Ty;
        return new GoalResult { GoalX = goalX, GoalY = goalY, TargetId = creature.Target };
    }

    public static bool ApplyActionEffects(SpeciesCatalog catalog, SimState state, JsonObject action, BehaviorContext ctx, CreatureSystem creatures, double dt, double speed)
    {
        var c = ctx.Creature;
        var g = c.Genome;
        double speedMult = action.TryGetPropertyValue("speedMult", out var sm) ? sm!.GetValue<double>() : 1;
        double moveSpeed = speed * speedMult;
        string behaviorState = action["state"]?.GetValue<string>() ?? "";

        switch (behaviorState)
        {
            case "flee":
                bool drinkAtShore = action.TryGetPropertyValue("drinkAtShore", out var ds) && ds!.GetValue<bool>();
                if (drinkAtShore && Navigation.AtWaterEdge(state, c.X, c.Y) && c.Thirst < 55)
                {
                    c.Thirst = Math.Min(100, c.Thirst + 60 * dt);
                    c.Vx *= 0.7; c.Vy *= 0.7;
                    return false;
                }
                if (!drinkAtShore && ctx.Threat != null)
                {
                    var tp = creatures.SimPos(ctx.Threat);
                    double a = Math.Atan2(c.Y - tp.Y, c.X - tp.X);
                    var flee = Navigation.UnsnappedWalkableGoal(state, c.X + Math.Cos(a) * 6, c.Y + Math.Sin(a) * 6, ctx.CanSwim);
                    creatures.MoveTowardGoal(c, flee.X, flee.Y, moveSpeed, dt, direct: true);
                }
                return true;

            case "thirst":
                if (Navigation.AtWaterEdge(state, c.X, c.Y))
                {
                    c.Thirst = Math.Min(100, c.Thirst + 60 * dt);
                    return false;
                }
                {
                    double goalDist = SimMath.Hypot(c.Tx - c.X, c.Ty - c.Y);
                    bool direct = goalDist <= SimConstants.DirectPursuitRadius;
                    creatures.MoveTowardGoal(c, c.Tx, c.Ty, moveSpeed, dt, direct: direct, forceReplan: direct);
                }
                return true;

            case "graze":
            {
                int ti = GridHelpers.Idx(state,
                    (int)SimMath.Clamp(Math.Round(c.X), 0, state.W - 1),
                    (int)SimMath.Clamp(Math.Round(c.Y), 0, state.H - 1));
                if (state.Veg[ti] > 0.04f)
                {
                    double bite = Math.Min(state.Veg[ti], 3.5 * dt);
                    state.Veg[ti] -= (float)bite;
                    c.Hunger = Math.Min(100, c.Hunger + bite * 26);
                    state.VegDirty = true;
                    return false;
                }
                creatures.MoveTo(c, moveSpeed, dt);
                return true;
            }

            case "huntSearch":
                creatures.MoveTo(c, moveSpeed, dt);
                return true;

            case "hunt":
                if (ctx.Prey != null)
                {
                    var pp = creatures.SimPos(ctx.Prey);
                    var pos = creatures.SimPos(c);
                    double pdist = SimMath.Hypot(pp.X - pos.X, pp.Y - pos.Y);
                    double strikeR = creatures.HuntStrikeRange(c, ctx.Prey);
                    if (pdist >= strikeR)
                    {
                        creatures.MoveTowardGoal(c, pp.X, pp.Y, moveSpeed, dt, direct: true);
                    }
                    else { c.Vx *= 0.25; c.Vy *= 0.25; }

                    pp = creatures.SimPos(ctx.Prey);
                    pos = creatures.SimPos(c);
                    pdist = SimMath.Hypot(pp.X - pos.X, pp.Y - pos.Y);
                    if (pdist < strikeR && GlobalRng.Next() < creatures.HuntStrikeChance(c))
                    {
                        ctx.Prey.Hp -= 30 + g.Size * 15;
                        ctx.Prey.Cause = "predation";
                        if (ctx.Prey.Hp <= 0)
                        {
                            creatures.Die(ctx.Prey, "predation");
                            c.Hunger = Math.Min(100, c.Hunger + 50);
                            c.Energy = Math.Min(100, c.Energy + 12);
                        }
                    }
                }
                else creatures.MoveTo(c, moveSpeed, dt);
                return true;

            case "rest":
                c.Energy = Math.Min(100, c.Energy + 9 * dt);
                c.Vx *= 0.8; c.Vy *= 0.8;
                return false;

            case "mate":
                if (ctx.Mate != null)
                {
                    var mp = creatures.SimPos(ctx.Mate);
                    creatures.MoveTowardGoal(c, mp.X, mp.Y, moveSpeed, dt, direct: true);
                    TryConsummateMate(catalog, c, ctx, creatures);
                }
                else creatures.Wander(c);
                return true;

            case "wander":
                creatures.MoveTo(c, moveSpeed, dt);
                return true;

            default:
                creatures.MoveTo(c, moveSpeed, dt);
                return true;
        }
    }

    public static bool TryConsummateMate(SpeciesCatalog catalog, Creature creature, BehaviorContext ctx, CreatureSystem creatures)
    {
        if (ctx.Mate == null) return false;
        var pos = creatures.SimPos(creature);
        var mpos = creatures.SimPos(ctx.Mate);
        if (SimMath.Hypot(mpos.X - pos.X, mpos.Y - pos.Y) >= 1.0) return false;

        var female = creature.Sex == "female" ? creature : ctx.Mate;
        var male = creature.Sex == "male" ? creature : ctx.Mate;
        if (female.Sex != "female" || male.Sex != "male") return false;
        if (female.Pregnant > 0 || female.MateCd > 0 || male.MateCd > 0) return false;

        female.Pregnant = catalog.SampleGestation(creature.Sp);
        female.MatePartner = male.Genome;
        female.MatePartnerId = male.Id;
        female.LitterQ = Math.Max(1, (int)Math.Round(female.Genome.Litter * GlobalRng.Rf(0.7, 1.15)));
        double cd = catalog.SampleMateCooldown(creature.Sp);
        female.MateCd = cd;
        male.MateCd = cd * 0.6;
        female.Energy -= 20;
        male.Energy -= 12;
        return true;
    }

    private static bool ShouldReuseGoals(SimState state, Creature creature, BehaviorDecision decision)
    {
        if (decision.NodeId != creature.BtNodeId) return false;
        string behaviorState = decision.Action["state"]?.GetValue<string>() ?? "";
        if (behaviorState != creature.State) return false;

        int replanEvery = QualityConfig.NavReplanInterval;
        int phase = ((int)Math.Floor(state.TGlobal * 24) + creature.Id) % replanEvery;
        if (phase == 0) return false;

        return behaviorState switch
        {
            "flee" => ctxValidThreat(creature, decision.Ctx),
            "hunt" => ctxValidPrey(creature, decision.Ctx),
            "huntSearch" => true,
            "mate" => ctxValidMate(creature, decision.Ctx),
            "thirst" => !Navigation.AtWaterEdge(state, creature.X, creature.Y)
                && (double.IsFinite(creature.NavGoalX) || double.IsFinite(creature.Tx)),
            "graze" => GrazeReuse(state, creature),
            "rest" or "wander" => true,
            _ => false,
        };
    }

    private static bool ctxValidThreat(Creature creature, BehaviorContext ctx)
        => ctx.Threat != null && !ctx.Threat.Dead && creature.Target == ctx.Threat.Id;

    private static bool ctxValidPrey(Creature creature, BehaviorContext ctx)
        => ctx.Prey != null && !ctx.Prey.Dead && creature.Target == ctx.Prey.Id;

    private static bool ctxValidMate(Creature creature, BehaviorContext ctx)
        => ctx.Mate != null && !ctx.Mate.Dead && creature.Target == ctx.Mate.Id;

    private static bool GrazeReuse(SimState state, Creature creature)
    {
        int ti = GridHelpers.Idx(state,
            (int)SimMath.Clamp(Math.Round(creature.X), 0, state.W - 1),
            (int)SimMath.Clamp(Math.Round(creature.Y), 0, state.H - 1));
        return state.Veg[ti] > 0.04f;
    }
}
