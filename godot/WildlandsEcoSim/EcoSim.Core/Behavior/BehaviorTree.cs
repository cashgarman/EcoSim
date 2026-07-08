using System.Text.Json.Nodes;
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
    return new BehaviorDecision
    {
      NodeId = result.NodeId,
      BranchUid = result.BranchUid,
      Action = result.Action,
      Ctx = ctx,
    };
  }

  public BehaviorEvalTrace EvaluateWithTrace(Creature creature, CreatureSystem creatures)
  {
    var cfg = _catalog.Get(creature.Sp).BehaviorConfig;
    if (cfg?.Root == null)
    {
      return new BehaviorEvalTrace();
    }

    var ctx = BehaviorContextBuilder.Build(creature, creatures, _state, _catalog);
    return BehaviorEvaluator.EvaluateTreeWithTrace(_state, cfg.Root, ctx);
  }

  public (BehaviorDecision? proposed, bool wouldApply) PeekDecision(Creature creature, CreatureSystem creatures)
  {
    var proposed = Decide(creature, creatures);
    if (proposed == null) return (null, false);
    return (proposed, WouldApplyDecision(creature, proposed));
  }

  public bool WouldApplyDecision(Creature creature, BehaviorDecision proposed)
  {
    return ShouldApplyDecision(creature, proposed);
  }

  public BehaviorDecision? Tick(Creature creature, double dt, CreatureSystem creatures, bool executeActions = true)
  {
    var proposed = Decide(creature, creatures);
    if (proposed == null) return null;

    BehaviorDecision effective;
    if (ShouldApplyDecision(creature, proposed))
    {
      effective = proposed;
      BehaviorExecutor.ApplyDecision(creature, effective, creatures, _state);
    }
    else
    {
      effective = BuildCommittedDecision(creature, creatures);
    }

    double speed = creature.Genome.Speed;
    if (!creatures.IsAdult(creature)) speed *= 0.8;
    if (_state.IsNight) speed *= 0.6;

    if (executeActions)
    {
      BehaviorExecutor.ApplyActionEffects(_catalog, _state, effective.Action, effective.Ctx, creatures, dt, speed);
    }

    return effective;
  }

  private bool ShouldApplyDecision(Creature creature, BehaviorDecision proposed)
  {
    string proposedState = proposed.Action["state"]?.GetValue<string>() ?? creature.State;
    if (creature.State == "flee" || proposedState == "flee") return true;
    if (creature.BtAction == null) return true;

    int currentTier = BehaviorPriority.GetTier(creature.BtAction);
    int proposedTier = BehaviorPriority.GetTier(proposed.Action);
    if (proposedTier < currentTier) return true;
    if (BehaviorPriority.IsUrgentNeed(creature, proposed.Ctx)) return true;

    if (proposedState == creature.State && proposedTier == currentTier && proposedTier >= 2)
    {
      double dwell = Math.Max(
        BehaviorPriority.GetMinCommitSec(creature.BtAction),
        BehaviorPriority.SameTierDwellSec);
      double elapsed = creature.StateCommittedSince <= 0
        ? 0
        : _state.TGlobal - creature.StateCommittedSince;
      if (elapsed < dwell)
      {
        return false;
      }
    }

    if (proposedState == creature.State) return true;

    if (proposedTier == currentTier)
    {
      double dwell = Math.Max(
        BehaviorPriority.GetMinCommitSec(creature.BtAction),
        BehaviorPriority.SameTierDwellSec);
      if (creature.StateCommittedSince <= 0) return true;
      return _state.TGlobal - creature.StateCommittedSince >= dwell;
    }

    return false;
  }

  private BehaviorDecision BuildCommittedDecision(Creature creature, CreatureSystem creatures)
  {
    var action = creature.BtAction ?? new JsonObject { ["state"] = creature.State };
    return new BehaviorDecision
    {
      NodeId = creature.BtNodeId ?? "",
      BranchUid = creature.BtBranchUid ?? "",
      Action = action,
      Ctx = BehaviorContextBuilder.Build(creature, creatures, _state, _catalog),
    };
  }
}
