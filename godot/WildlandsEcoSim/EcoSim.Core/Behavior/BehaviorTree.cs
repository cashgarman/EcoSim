using EcoSim.Core.Data;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Behavior;

public sealed class BehaviorTree
{
    private readonly SimState _state;
    private readonly SpeciesCatalog _catalog;

    public BehaviorTree(SimState state, SpeciesCatalog catalog)
    {
        _state = state;
        _catalog = catalog;
    }

    public BehaviorDecision? Decide(Creature creature, CreatureSystem creatures)
    {
        var cfg = _catalog.Get(creature.Sp).BehaviorConfig;
        if (cfg == null) return null;
        var ctx = BehaviorContextBuilder.Build(creature, creatures, _state, _catalog);
        var result = BehaviorEvaluator.EvaluateTree(_state, cfg.Root, ctx);
        if (result?.Action == null) return null;
        return new BehaviorDecision { NodeId = result.NodeId, Action = result.Action, Ctx = ctx };
    }

    public BehaviorDecision? Tick(Creature creature, double dt, CreatureSystem creatures, bool executeActions = true)
    {
        var decision = Decide(creature, creatures);
        if (decision == null) return null;

        BehaviorExecutor.ApplyDecision(creature, decision, creatures, _state);

        double speed = creature.Genome.Speed;
        if (!creatures.IsAdult(creature)) speed *= 0.8;
        if (_state.IsNight) speed *= 0.6;

        if (executeActions)
        {
            BehaviorExecutor.ApplyActionEffects(_catalog, _state, decision.Action, decision.Ctx, creatures, dt, speed);
        }

        return decision;
    }
}
