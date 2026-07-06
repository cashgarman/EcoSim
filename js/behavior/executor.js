import { rng, rf, clamp } from '../utils.js';
import { sampleGestation, sampleMateCooldown } from '../data.js';
import { state, idx } from '../state.js';
import {
  atWaterEdge,
  nearestWaterEdgeTarget,
  snapWalkableGoal,
  unsnappedWalkableGoal,
  waterSeekRadius,
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
        const tp = creatureSystem.simPos(ctx.threat);
        const a = Math.atan2(c.y - tp.y, c.x - tp.x);
        const flee = unsnappedWalkableGoal(
          c.x + Math.cos(a) * 6,
          c.y + Math.sin(a) * 6,
          ctx.canSwim,
        );
        goalX = flee.x;
        goalY = flee.y;
      }
      break;

    case 'nearestWater':
    {
      const pos = creatureSystem.simPos(c);
      const w = nearestWaterEdgeTarget(pos.x, pos.y, waterSeekRadius(ctx.senseR));
      if (w)
      {
        goalX = w.x;
        goalY = w.y;
      }
      else
      {
        creatureSystem.wander(c);
        goalX = c.tx;
        goalY = c.ty;
      }
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
        const pp = creatureSystem.simPos(ctx.prey);
        const g = unsnappedWalkableGoal(pp.x, pp.y, ctx.canSwim);
        goalX = g.x;
        goalY = g.y;
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
        const mp = creatureSystem.simPos(ctx.mate);
        const g = unsnappedWalkableGoal(mp.x, mp.y, ctx.canSwim);
        goalX = g.x;
        goalY = g.y;
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

/**
 * Applies the effects of an action for a given creature during the simulation.
 *
 * This function interprets the current state/action of the creature (derived from its behavior tree or planner node)
 * and updates the creature's stats, position, and other environment/game state based on the meaning of that action.
 *
 * Side effects may also include changes to world state (e.g., consumed food) and action recording (for "life story" logging).
 *
 * @param {object} action - The action node (usually from behavior tree) containing the current state and parameters.
 * @param {number} nodeId - The ID of the behavior tree node for this decision/action.
 * @param {object} ctx - Context for the action, containing references to the acting creature, targets, and calculated distances.
 * @param {object} creatureSystem - The system for controlling creature movement and higher-order creature operations.
 * @param {number} dt - The simulation timestep (delta time).
 * @param {number} speed - The base movement speed for the current species or behavior phase.
 *
 * @returns {object} An object with a single boolean property `.moved`, which is true if the creature advanced positionally this tick,
 *                   or false if it "acted in place" (e.g., drank, grazed, or rested).
 */
export function applyActionEffects(action, nodeId, ctx, creatureSystem, dt, speed)
{
  // Expose creature and genome for easier reference
  const c = ctx.creature;
  const g = c.genome;

  // Determine movement speed for this action (can be overridden by action)
  const speedMult = action.speedMult ?? 1;
  const moveSpeed = speed * speedMult;

  // Main action handling branch
  switch (action.state)
  {
    // -- Fleeing (from threat), but can optionally drink at water's edge if thirsty and eligible --
    case 'flee':
      // If action requests drinking at shore AND at water edge AND thirsty enough
      if (action.drinkAtShore && atWaterEdge(c.x, c.y) && c.thirst < 55)
      {
        // Drink: increase thirst meter, slow movement as if pausing to drink
        c.thirst = Math.min(100, c.thirst + 60 * dt);
        c.vx *= 0.7;
        c.vy *= 0.7;
        lifeStory.recordAction(c, 'drank', 'while fleeing', 5);
        // Movement is paused for the drinking tick
        return { moved: false };
      }
      // Otherwise, if not stopping to drink, and there's still a threat, keep fleeing (move)
      if (!action.drinkAtShore && ctx.threat)
      {
        const tp = creatureSystem.simPos(ctx.threat);
        const a = Math.atan2(c.y - tp.y, c.x - tp.x);
        const flee = unsnappedWalkableGoal(
          c.x + Math.cos(a) * 6,
          c.y + Math.sin(a) * 6,
          ctx.canSwim,
        );
        creatureSystem.moveTowardGoal(c, flee.x, flee.y, moveSpeed, dt, { direct: true });
      }
      // Fleeing action considers itself moving (unless just drank above)
      return { moved: true };

    // -- Seeking to drink at water's edge (driven by thirst) --
    case 'thirst':
      if (atWaterEdge(c.x, c.y))
      {
        // At water: Drink and do not move further
        c.thirst = Math.min(100, c.thirst + 60 * dt);
        lifeStory.recordAction(c, 'drank', 'at water edge', 5);
        return { moved: false };
      }
      // Not at water yet: move toward it
      creatureSystem.moveTo(c, moveSpeed, dt);
      return { moved: true };

    // -- Grazing action (eat vegetation if available) --
    case 'graze':
    {
      // Calculate current tile index in world based on integer position
      const ti = idx(clamp(Math.round(c.x), 0, state.W - 1), clamp(Math.round(c.y), 0, state.H - 1));

      // Is there enough vegetation to take a bite from?
      if (state.veg[ti] > 0.04)
      {
        // Amount of vegetation consumed is capped by bite size and available vegetation
        const bite = Math.min(state.veg[ti], 3.5 * dt);
        state.veg[ti] -= bite;
        // Eating provides hunger satisfaction
        c.hunger = Math.min(100, c.hunger + bite * 26);
        // Mark vegetation as changed for rendering or simulation update
        state.vegDirty = true;
        lifeStory.recordAction(c, 'grazed', `bite ${bite.toFixed(2)}`, 4);
        return { moved: false }; // Grazing is stationary
      }
      // Not enough food: continue moving (searching for more to graze)
      creatureSystem.moveTo(c, moveSpeed, dt);
      return { moved: true };
    }

    // -- Searching for prey but has not spotted target yet (usually just moves) --
    case 'huntSearch':
      creatureSystem.moveTo(c, moveSpeed, dt);
      return { moved: true };

    // -- Actual hunting of a known prey target --
    case 'hunt':
      if (ctx.prey)
      {
        const pp = creatureSystem.simPos(ctx.prey);
        const pos = creatureSystem.simPos(c);
        const pdist = Math.hypot(pp.x - pos.x, pp.y - pos.y);
        const strikeR = creatureSystem.huntStrikeRange(c, ctx.prey);

        if (pdist >= strikeR)
        {
          creatureSystem.moveTowardGoal(c, pp.x, pp.y, moveSpeed, dt, { direct: true });
        }
        else
        {
          c.vx *= 0.25;
          c.vy *= 0.25;
        }

        if (pdist < strikeR && rng() < creatureSystem.huntStrikeChance(c))
        {
          ctx.prey.hp -= 30 + g.size * 15;
          ctx.prey.cause = 'predation';
          if (ctx.prey.hp <= 0)
          {
            creatureSystem.die(ctx.prey, 'predation');
            c.hunger = Math.min(100, c.hunger + 50);
            c.energy = Math.min(100, c.energy + 12);
            lifeStory.recordHunted(c, ctx.prey);
          }
        }
      }
      else
      {
        // No current prey; keep moving/searching
        creatureSystem.moveTo(c, moveSpeed, dt);
      }
      return { moved: true };

    // -- Energy recovery action: resting --
    case 'rest':
      // Recover energy at a fairly rapid rate
      c.energy = Math.min(100, c.energy + 9 * dt);
      // Slightly slow down if moving (simulate lying low)
      c.vx *= 0.8;
      c.vy *= 0.8;
      lifeStory.recordAction(c, 'rested', 'recovered energy', 6);
      // Resting does not move the creature
      return { moved: false };

    // -- Mating action: Move to mate and potentially initiate mating if close enough --
    case 'mate':
      if (ctx.mate)
      {
        const mp = creatureSystem.simPos(ctx.mate);
        creatureSystem.moveTowardGoal(c, mp.x, mp.y, moveSpeed, dt, { direct: true });

        tryConsummateMate(c, ctx, creatureSystem);
      }
      else
      {
        // No mate target: wander instead
        creatureSystem.wander(c);
      }
      // Mating by default considered "moving" for AI/control
      return { moved: true };

    // -- General random movement or wall-following (roaming) --
    case 'wander':
      creatureSystem.moveTo(c, moveSpeed, dt);
      lifeStory.recordAction(c, 'wandered', null, 8);
      return { moved: true };

    // -- FALLTHROUGH: Default for unknown or passive actions is to move on decision path --
    default:
      creatureSystem.moveTo(c, moveSpeed, dt);
      return { moved: true };
  }
}

export function tryConsummateMate(creature, ctx, creatureSystem)
{
  if (!ctx?.mate) return false;
  const pos = creatureSystem.simPos(creature);
  const mpos = creatureSystem.simPos(ctx.mate);
  const dist = Math.hypot(mpos.x - pos.x, mpos.y - pos.y);
  if (dist >= 1.0) return false;

  const female = creature.sex === 'female' ? creature : ctx.mate;
  const male = creature.sex === 'male' ? creature : ctx.mate;
  if (female.sex !== 'female' || male.sex !== 'male') return false;
  if (female.pregnant > 0 || female.mateCd > 0 || male.mateCd > 0) return false;

  female.pregnant = sampleGestation(creature.sp);
  female.matePartner = male.genome;
  female.matePartnerId = male.id;
  female.litterQ = Math.max(1, Math.round(female.genome.litter * rf(0.7, 1.15)));
  const cd = sampleMateCooldown(creature.sp);
  female.mateCd = cd;
  male.mateCd = cd * 0.6;
  female.energy -= 20;
  male.energy -= 12;
  lifeStory.recordMated(female, male.id, male.sp, female.sex, male.sex);
  lifeStory.recordMated(male, female.id, female.sp, male.sex, female.sex);
  return true;
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
  if (!(state.simBackend === 'gpu' && state.gpuSimEnabled))
  {
    creature.gpuNeedsUpload = true;
  }
}
