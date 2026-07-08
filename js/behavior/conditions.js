import { threshold } from './context.js';
import { atWaterEdge } from '../nav.js';

const HUNGER_ACTIVE_STATES = new Set(['graze', 'hunt', 'huntSearch']);

function evaluateHungerBelowOrState(conditionDef, ctx, c, S)
{
  if (conditionDef.diet != null && S.diet !== conditionDef.diet) return false;
  if (conditionDef.dietMax != null && S.diet > conditionDef.dietMax) return false;
  if (conditionDef.dietMin != null && S.diet < conditionDef.dietMin) return false;
  if (conditionDef.noPrey && ctx.prey) return false;

  const graze = threshold(ctx, conditionDef.grazeKey || 'hungerGraze', 55);
  const exit = threshold(ctx, conditionDef.exitKey || 'hungerExit', 65);
  if (HUNGER_ACTIVE_STATES.has(c.state)) return c.hunger < exit;
  return c.hunger < graze;
}

function evaluateHungerHysteresis(conditionDef, ctx, c, requirePrey)
{
  const exit = threshold(ctx, conditionDef.exitKey || 'hungerExit', 65);
  const graze = threshold(ctx, conditionDef.grazeKey || 'hungerGraze', 55);

  if (HUNGER_ACTIVE_STATES.has(c.state)) return c.hunger < exit;

  if (requirePrey)
  {
    if (!ctx.prey) return false;
    return c.hunger < graze;
  }

  if (ctx.prey) return false;
  return c.hunger < graze;
}

export function evaluateCondition(conditionId, conditionDef, ctx)
{
  const c = ctx.creature;
  const S = ctx.species;
  const op = conditionDef.op;

  switch (op)
  {
    case 'hasThreat':
      return !!ctx.threat;

    case 'atWaterEdge':
      return atWaterEdge(c.x, c.y);

    case 'thirstBelow':
    {
      const limit = threshold(ctx, conditionDef.key, 55);
      return c.thirst < limit;
    }

    case 'thirstBelowWhileState':
    {
      const limit = threshold(ctx, conditionDef.key, 55);
      return c.state === conditionDef.state && c.thirst < limit;
    }

    case 'thirstBelowOrState':
    {
      const urgent = threshold(ctx, conditionDef.key, 30);
      const exit = threshold(ctx, conditionDef.exitKey || 'thirstExit', 55);
      return c.thirst < urgent || (c.state === conditionDef.state && c.thirst < exit);
    }

    case 'hungerBelow':
    {
      if (conditionDef.dietMax != null && S.diet > conditionDef.dietMax) return false;
      return c.hunger < threshold(ctx, conditionDef.key, 55);
    }

    case 'hungerBelowOrState':
      return evaluateHungerBelowOrState(conditionDef, ctx, c, S);

    case 'hungerBelowNoPrey':
    {
      if (conditionDef.diet != null && S.diet !== conditionDef.diet) return false;
      if (conditionDef.dietMin != null && S.diet < conditionDef.dietMin) return false;
      if (ctx.prey) return false;
      return c.hunger < threshold(ctx, conditionDef.key, 55);
    }

    case 'hungerBelowNoPreyOrState':
    {
      if (conditionDef.dietMin != null && S.diet < conditionDef.dietMin) return false;
      return evaluateHungerHysteresis(conditionDef, ctx, c, false);
    }

    case 'hungerBelowWithPrey':
    {
      if (conditionDef.dietMin != null && S.diet < conditionDef.dietMin) return false;
      if (!ctx.prey) return false;
      return c.hunger < threshold(ctx, conditionDef.key, 55);
    }

    case 'hungerBelowWithPreyOrState':
    {
      if (conditionDef.dietMin != null && S.diet < conditionDef.dietMin) return false;
      return evaluateHungerHysteresis(conditionDef, ctx, c, true);
    }

    case 'energyBelow':
      return c.energy < threshold(ctx, conditionDef.key, 18);

    case 'energyBelowOrState':
    {
      const entry = threshold(ctx, conditionDef.key, 18);
      const exit = threshold(ctx, conditionDef.exitKey || 'energyExit', 30);
      const stateName = conditionDef.state || 'rest';
      return c.energy < entry || (c.state === stateName && c.energy < exit);
    }

    case 'canMate':
    {
      if (!ctx.mate || c.pregnant > 0) return false;
      const hungerMin = threshold(ctx, 'mateHungerMin', 45);
      const thirstMin = threshold(ctx, 'mateThirstMin', 40);
      const energyMin = threshold(ctx, 'mateEnergyMin', 45);
      return c.hunger > hungerMin && c.thirst > thirstMin && c.energy > energyMin;
    }

    case 'nightWanderTired':
      return ctx.isNight && c.energy < threshold(ctx, 'nightWanderRestEnergy', 75);

    default:
      return false;
  }
}
