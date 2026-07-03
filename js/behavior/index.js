import { SPECIES } from '../data.js';
import { state } from '../state.js';
import { buildBehaviorContext } from './context.js';
import { evaluateTree } from './evaluator.js';
import { applyActionEffects, applyDecisionWithContext } from './executor.js';
import { getSpeciesBehavior } from './loader.js';
import { lifeStory } from '../life-story.js';

export class BehaviorTree
{
  decide(creature, creatureSystem)
  {
    const cfg = getSpeciesBehavior(creature.sp);
    if (!cfg) return null;
    const ctx = buildBehaviorContext(creature, creatureSystem);
    const result = evaluateTree(cfg.root, ctx);
    if (!result || !result.action) return null;
    return { ...result, ctx };
  }

  tick(creature, dt, creatureSystem, options = {})
  {
    const { executeActions = true } = options;
    const S = SPECIES[creature.sp];
    const g = creature.genome;
    const decision = this.decide(creature, creatureSystem);
    if (!decision) return null;

    applyDecisionWithContext(creature, decision, decision.ctx, creatureSystem);

    let speed = g.speed;
    if (!creatureSystem.isAdult(creature)) speed *= 0.8;
    if (state.isNight) speed *= 0.6;

    if (executeActions)
    {
      applyActionEffects(decision.action, decision.nodeId, decision.ctx, creatureSystem, dt, speed);
    }

    if (state.simBackend !== 'gpu' || options.logLifeStory)
    {
      lifeStory.observeDecision(creature, creature.state, creature.target, decision.nodeId);
      lifeStory.observeAge(creature);
    }

    return decision;
  }

  tickDecisionOnly(creature, creatureSystem)
  {
    const decision = this.decide(creature, creatureSystem);
    if (!decision) return null;
    applyDecisionWithContext(creature, decision, decision.ctx, creatureSystem);
    lifeStory.observeDecision(creature, creature.state, creature.target, decision.nodeId);
    lifeStory.observeAge(creature);
    return decision;
  }
}

export const behaviorTree = new BehaviorTree();
