import { rng, rf, clamp } from '../utils.js';
import { sampleGestation, sampleMateCooldown } from '../data.js';
import { state, idx } from '../state.js';
import {
  atWaterEdge,
  nearestWaterEdgeTarget,
  snapWalkableGoal,
} from '../nav.js';
import { lifeStory } from '../life-story.js';
import { stateToGpuCode } from './state-codes.js';

export function resolveGoals(action, ctx, creatureSystem)
{
  const c = ctx.creature;
  const goal = action.goal;
  let goalX = c.tx;
  let goalY = c.ty;
  let targetId = null;
  let targetSlot = -1;

  const applySnapped = (gx, gy) =>
  {
    const sn = snapWalkableGoal(Math.round(gx), Math.round(gy), ctx.canSwim, 8);
    if (sn) { goalX = sn.x + 0.5; goalY = sn.y + 0.5; }
    else { goalX = gx; goalY = gy; }
  };

  switch (goal)
  {
    case 'hold':
      goalX = c.x;
      goalY = c.y;
      break;

    case 'awayFromThreat':
      if (ctx.threat)
      {
        targetId = ctx.threat.id;
        targetSlot = ctx.threat.gpuSlot ?? -1;
        const a = Math.atan2(c.y - ctx.threat.y, c.x - ctx.threat.x);
        applySnapped(c.x + Math.cos(a) * 6, c.y + Math.sin(a) * 6);
      }
      break;

    case 'nearestWater':
    {
      const w = nearestWaterEdgeTarget(c.x, c.y, ctx.senseR + 6);
      if (w) { goalX = w.x; goalY = w.y; }
      else creatureSystem.wander(c);
      goalX = c.tx;
      goalY = c.ty;
      break;
    }

    case 'bestFoodOrWander':
    {
      const ti = idx(clamp(Math.round(c.x), 0, state.W - 1), clamp(Math.round(c.y), 0, state.H - 1));
      if (state.veg[ti] <= 0.04)
      {
        const t = creatureSystem.findFood(c, ctx.senseR);
        if (t) applySnapped(t.x, t.y);
        else creatureSystem.wander(c);
        goalX = c.tx;
        goalY = c.ty;
      }
      else
      {
        goalX = c.x;
        goalY = c.y;
      }
      break;
    }

    case 'chasePrey':
      if (ctx.prey)
      {
        targetId = ctx.prey.id;
        targetSlot = ctx.prey.gpuSlot ?? -1;
        applySnapped(ctx.prey.x, ctx.prey.y);
      }
      else
      {
        creatureSystem.wander(c);
        goalX = c.tx;
        goalY = c.ty;
      }
      break;

    case 'approachMate':
      if (ctx.mate)
      {
        targetId = ctx.mate.id;
        targetSlot = ctx.mate.gpuSlot ?? -1;
        applySnapped(ctx.mate.x, ctx.mate.y);
      }
      else
      {
        creatureSystem.wander(c);
        goalX = c.tx;
        goalY = c.ty;
      }
      break;

    case 'wander':
    case 'randomWalkable':
      creatureSystem.wander(c);
      goalX = c.tx;
      goalY = c.ty;
      break;

    default:
      goalX = c.tx;
      goalY = c.ty;
  }

  return { goalX, goalY, targetId, targetSlot };
}

export function applyActionEffects(action, nodeId, ctx, creatureSystem, dt, speed)
{
  const c = ctx.creature;
  const g = c.genome;
  const speedMult = action.speedMult ?? 1;
  const moveSpeed = speed * speedMult;

  switch (action.state)
  {
    case 'flee':
      if (action.drinkAtShore && atWaterEdge(c.x, c.y) && c.thirst < 55)
      {
        c.thirst = Math.min(100, c.thirst + 60 * dt);
        c.vx *= 0.7;
        c.vy *= 0.7;
        lifeStory.recordAction(c, 'drank', 'while fleeing', 5);
        return { moved: false };
      }
      if (!action.drinkAtShore && ctx.threat)
      {
        creatureSystem.moveTo(c, moveSpeed, dt);
      }
      return { moved: true };

    case 'thirst':
      if (atWaterEdge(c.x, c.y))
      {
        c.thirst = Math.min(100, c.thirst + 60 * dt);
        lifeStory.recordAction(c, 'drank', 'at water edge', 5);
        return { moved: false };
      }
      creatureSystem.moveTo(c, moveSpeed, dt);
      return { moved: true };

    case 'graze':
    {
      const ti = idx(clamp(Math.round(c.x), 0, state.W - 1), clamp(Math.round(c.y), 0, state.H - 1));
      if (state.veg[ti] > 0.04)
      {
        const bite = Math.min(state.veg[ti], 3.5 * dt);
        state.veg[ti] -= bite;
        c.hunger = Math.min(100, c.hunger + bite * 26);
        state.vegDirty = true;
        lifeStory.recordAction(c, 'grazed', `bite ${bite.toFixed(2)}`, 4);
        return { moved: false };
      }
      creatureSystem.moveTo(c, moveSpeed, dt);
      return { moved: true };
    }

    case 'huntSearch':
      creatureSystem.moveTo(c, moveSpeed, dt);
      return { moved: true };

    case 'hunt':
      if (ctx.prey)
      {
        creatureSystem.moveTo(c, moveSpeed, dt);
        if (ctx.pdist < creatureSystem.eSize(c) * 0.6 + 0.5)
        {
          if (rng() < (0.10 + g.agg * 0.10))
          {
            ctx.prey.hp -= 30 + g.size * 15;
            ctx.prey.cause = 'predation';
            if (ctx.prey.hp <= 0)
            {
              c.hunger = Math.min(100, c.hunger + 50);
              c.energy = Math.min(100, c.energy + 12);
              lifeStory.recordHunted(c, ctx.prey);
            }
          }
        }
      }
      else
      {
        creatureSystem.moveTo(c, moveSpeed, dt);
      }
      return { moved: true };

    case 'rest':
      c.energy = Math.min(100, c.energy + 9 * dt);
      c.vx *= 0.8;
      c.vy *= 0.8;
      lifeStory.recordAction(c, 'rested', 'recovered energy', 6);
      return { moved: false };

    case 'mate':
      if (ctx.mate)
      {
        creatureSystem.moveTo(c, moveSpeed, dt);
        if (ctx.mdist < 1.0)
        {
          const female = c.sex === 'female' ? c : ctx.mate;
          const male = c.sex === 'male' ? c : ctx.mate;
          if (female.sex === 'female' && male.sex === 'male'
            && female.pregnant <= 0 && female.mateCd <= 0 && male.mateCd <= 0)
          {
            female.pregnant = sampleGestation(c.sp);
            female.matePartner = male.genome;
            female.matePartnerId = male.id;
            female.litterQ = Math.max(1, Math.round(female.genome.litter * rf(0.7, 1.15)));
            const cd = sampleMateCooldown(c.sp);
            female.mateCd = cd;
            male.mateCd = cd * 0.6;
            female.energy -= 20;
            male.energy -= 12;
            lifeStory.recordMated(female, male.id, male.sp, female.sex, male.sex);
            lifeStory.recordMated(male, female.id, female.sp, male.sex, female.sex);
          }
        }
      }
      else
      {
        creatureSystem.wander(c);
      }
      return { moved: true };

    case 'wander':
      creatureSystem.moveTo(c, moveSpeed, dt);
      lifeStory.recordAction(c, 'wandered', null, 8);
      return { moved: true };

    default:
      creatureSystem.moveTo(c, moveSpeed, dt);
      return { moved: true };
  }
}

export function applyDecisionWithContext(creature, decision, ctx, creatureSystem)
{
  const { action, nodeId } = decision;
  creature.state = action.state;
  creature.btNodeId = nodeId;
  creature.gpuStateCode = stateToGpuCode(action.state);
  const goals = resolveGoals(action, ctx, creatureSystem);
  creature.tx = goals.goalX;
  creature.ty = goals.goalY;
  creature.target = goals.targetId;
  creature.gpuTargetSlot = goals.targetSlot;
  creature.btSpeedMult = action.speedMult ?? 1;
  creature.gpuNeedsUpload = true;
}
