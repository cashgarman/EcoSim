import { SPECIES } from '../data.js';

import { state } from '../state.js';

import { buildBehaviorContext } from './context.js';

import { evaluateTree } from './evaluator.js';

import { applyActionEffects, applyDecisionWithContext, tryConsummateMate } from './executor.js';

import { getSpeciesBehavior } from './loader.js';

import { lifeStory } from '../life-story.js';

import { perfProfiler } from '../perf-profiler.js';

import { shouldApplyDecision } from './priority.js';



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

      creatureSystem._lastBehaviorCtx = ctx;

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

      const proposed = this.decide(creature, creatureSystem);

      if (!proposed) return null;



      const effective = shouldApplyDecision(creature, proposed, state.tGlobal)

        ? proposed

        : {

          nodeId: creature.btNodeId,

          branchUid: creature.btBranchUid,

          action: creature.btAction || { state: creature.state },

          ctx: creatureSystem._lastBehaviorCtx || proposed.ctx,

        };



      applyDecisionWithContext(creature, effective, effective.ctx, creatureSystem);



      let speed = g.speed;

      if (!creatureSystem.isAdult(creature)) speed *= 0.8;

      if (state.isNight) speed *= 0.6;



      if (executeActions)

      {

        perfProfiler.scope('behavior.execute', () =>

        {

          applyActionEffects(effective.action, effective.nodeId, effective.ctx, creatureSystem, dt, speed);

        });

      }



      this._observeLifeStory(creature, effective, options);



      return effective;

    });

  }



  tickDecisionOnly(creature, creatureSystem)

  {

    return perfProfiler.scope('behavior.tick', () =>

    {

      const proposed = this.decide(creature, creatureSystem);

      if (!proposed) return null;



      const effective = shouldApplyDecision(creature, proposed, state.tGlobal)

        ? proposed

        : {

          nodeId: creature.btNodeId,

          branchUid: creature.btBranchUid,

          action: creature.btAction || { state: creature.state },

          ctx: creatureSystem._lastBehaviorCtx || proposed.ctx,

        };



      applyDecisionWithContext(creature, effective, effective.ctx, creatureSystem);

      if (state.simBackend === 'gpu' && state.gpuSimEnabled && effective.action?.state === 'mate')

      {

        tryConsummateMate(creature, effective.ctx, creatureSystem);

      }

      this._observeLifeStory(creature, effective, { logLifeStory: true });

      return effective;

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

