import { SPECIES } from '../data.js';
import { state } from '../state.js';
import { atWaterEdge } from '../nav.js';

export function buildBehaviorContext(creature, creatureSystem)
{
  const S = SPECIES[creature.sp];
  const g = creature.genome;
  const senseR = g.sense;
  const neigh = creatureSystem.nearby(creature, senseR);
  const pos = creatureSystem.simPos(creature);

  let threat = null;
  let tdist = 1e9;
  let prey = null;
  let pdist = 1e9;
  let mate = null;
  let mdist = 1e9;

  for (const o of neigh)
  {
    const op = creatureSystem.simPos(o);
    const d = Math.hypot(op.x - pos.x, op.y - pos.y);
    if (S.preyOf && S.preyOf.includes(o.sp) && creatureSystem.eSize(o) > creatureSystem.eSize(creature) * 0.7)
    {
      if (d < tdist) { tdist = d; threat = o; }
    }
    if (S.hunts && S.hunts.includes(o.sp) && creatureSystem.eSize(o) < creatureSystem.eSize(creature) * 1.25)
    {
      if (d < pdist) { pdist = d; prey = o; }
    }
    if (o.sp === creature.sp && o.sex !== creature.sex && creatureSystem.isAdult(o) && creatureSystem.isAdult(creature)
      && o.mateCd <= 0 && creature.mateCd <= 0
      && o.pregnant <= 0 && creature.pregnant <= 0 && o.energy > 45 && creature.energy > 45)
    {
      if (d < mdist) { mdist = d; mate = o; }
    }
  }

  return {
    creature,
    species: S,
    thresholds: S.behaviorConfig?.thresholds || {},
    threat,
    tdist,
    prey,
    pdist,
    mate,
    mdist,
    senseR,
    canSwim: S.shape === 'bird',
    isNight: state.isNight,
    prevState: creature.state,
  };
}

export function threshold(ctx, key, fallback = 0)
{
  return ctx.thresholds[key] ?? fallback;
}
