import { SPECIES } from '../data.js';
import { state } from '../state.js';
import { buildBehaviorContext } from './context.js';
import { evaluateTree } from './evaluator.js';
import { applyActionEffects, applyDecisionWithContext, tryConsummateMate } from './executor.js';
import { getSpeciesBehavior } from './loader.js';
import { lifeStory } from '../life-story.js';
import { perfProfiler } from '../perf-profiler.js';

export class BehaviorTree
{
  decide(creature, creatureSystem)
  {
    return perfProfiler.scope('behavior.decide', () =>
    {
      const cfg = getSpeciesBehavior(creature.sp);
      if (!cfg) return null;
      const ctx = perfProfiler.scope('behavior.buildContext', () =>
        buildBehaviorContext(creature, creatureSystem));
      const result = perfProfiler.scope('behavior.evaluateTree', () =>
        evaluateTree(cfg.root, ctx));
      if (!result || !result.action) return null;
      return { ...result, ctx };
    });
  }

  tick(creature, dt, creatureSystem, options = {})
  {
    return perfProfiler.scope('behavior.tick', () =>
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
        perfProfiler.scope('behavior.execute', () =>
        {
          applyActionEffects(decision.action, decision.nodeId, decision.ctx, creatureSystem, dt, speed);
        });
      }

      this._observeLifeStory(creature, decision, options);

      return decision;
    });
  }

  tickDecisionOnly(creature, creatureSystem)
  {
    return perfProfiler.scope('behavior.tick', () =>
    {
      const decision = this.decide(creature, creatureSystem);
      if (!decision) return null;
      applyDecisionWithContext(creature, decision, decision.ctx, creatureSystem);
      if (state.simBackend === 'gpu' && state.gpuSimEnabled && decision.action?.state === 'mate')
      {
        tryConsummateMate(creature, decision.ctx, creatureSystem);
      }
      this._observeLifeStory(creature, decision, { logLifeStory: true });
      return decision;
    });
  }

  _observeLifeStory(creature, decision, options = {})
  {
    if (state.batchMode) return;
    if (state.simBackend === 'gpu' && !options.logLifeStory) return;
    perfProfiler.scope('behavior.lifeStory', () =>
    {
      lifeStory.observeDecision(creature, creature.state, creature.target, decision.nodeId);
      lifeStory.observeAge(creature);
    });
  }
}

export const behaviorTree = new BehaviorTree();
