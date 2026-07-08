import { threshold } from './context.js';

const DEFAULT_TIER_BY_STATE = {
  flee: 0,
  thirst: 1,
  graze: 1,
  hunt: 1,
  rest: 1,
  huntSearch: 2,
  mate: 2,
  wander: 3,
};

export const SAME_TIER_DWELL_SEC = 0.3;

export function getInterruptTier(action)
{
  if (!action) return 3;
  if (action.interruptTier != null) return action.interruptTier;
  return DEFAULT_TIER_BY_STATE[action.state] ?? 3;
}

export function getMinCommitSec(action)
{
  if (action?.minCommitSec != null) return action.minCommitSec;
  return getInterruptTier(action) >= 2 ? 0.5 : 0;
}

export function isUrgentNeed(creature, ctx)
{
  const hungerUrgent = threshold(ctx, 'hungerUrgent', 25);
  const thirstUrgent = threshold(ctx, 'thirstUrgent', 30);
  const energyUrgent = threshold(ctx, 'energyUrgent', 12);
  return creature.hunger < hungerUrgent ||
    creature.thirst < thirstUrgent ||
    creature.energy < energyUrgent;
}

export function shouldApplyDecision(creature, proposed, tGlobal)
{
  const proposedState = proposed.action?.state ?? creature.state;
  if (creature.state === 'flee' || proposedState === 'flee') return true;
  if (!creature.btAction) return true;

  const currentTier = getInterruptTier(creature.btAction);
  const proposedTier = getInterruptTier(proposed.action);
  if (proposedTier < currentTier) return true;
  if (isUrgentNeed(creature, proposed.ctx)) return true;

  if (proposedState === creature.state && proposedTier === currentTier && proposedTier >= 2)
  {
    const dwell = Math.max(getMinCommitSec(creature.btAction), SAME_TIER_DWELL_SEC);
    const elapsed = creature.stateCommittedSince <= 0
      ? 0
      : tGlobal - creature.stateCommittedSince;
    if (elapsed < dwell) return false;
  }

  if (proposedState === creature.state) return true;

  if (proposedTier === currentTier)
  {
    const dwell = Math.max(getMinCommitSec(creature.btAction), SAME_TIER_DWELL_SEC);
    if (creature.stateCommittedSince <= 0) return true;
    return tGlobal - creature.stateCommittedSince >= dwell;
  }

  return false;
}

export function buildCommittedDecision(creature, creatureSystem)
{
  const action = creature.btAction || { state: creature.state };
  return {
    nodeId: creature.btNodeId,
    branchUid: creature.btBranchUid,
    action,
    ctx: creatureSystem._lastBehaviorCtx,
  };
}
