import { threshold } from './context.js';
import { atWaterEdge } from '../nav.js';

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

    case 'hungerBelowNoPrey':
    {
      if (conditionDef.diet != null && S.diet !== conditionDef.diet) return false;
      if (conditionDef.dietMin != null && S.diet < conditionDef.dietMin) return false;
      if (ctx.prey) return false;
      return c.hunger < threshold(ctx, conditionDef.key, 55);
    }

    case 'hungerBelowWithPrey':
    {
      if (conditionDef.dietMin != null && S.diet < conditionDef.dietMin) return false;
      if (!ctx.prey) return false;
      return c.hunger < threshold(ctx, conditionDef.key, 55);
    }

    case 'energyBelow':
      return c.energy < threshold(ctx, conditionDef.key, 18);

    case 'canMate':
    {
      if (!ctx.mate || c.pregnant > 0) return false;
      const hungerMin = threshold(ctx, 'mateHungerMin', 45);
      const thirstMin = threshold(ctx, 'mateThirstMin', 40);
      return c.hunger > hungerMin && c.thirst > thirstMin;
    }

    case 'nightWanderTired':
      return ctx.isNight && c.energy < threshold(ctx, 'nightWanderRestEnergy', 75);

    default:
      return false;
  }
}
