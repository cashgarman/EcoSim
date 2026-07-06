import { SPECIES, SPECIES_INDEX } from '../data.js';
import { state } from '../state.js';

export function buildBehaviorContext(creature, creatureSystem)
{
  const S = SPECIES[creature.sp];
  const g = creature.genome;
  const senseR = g.sense;
  const senseR2 = senseR * senseR;
  const pos = creatureSystem.simPosInto(creatureSystem._posScratch, creature);
  const neigh = creatureSystem.nearby(creature, senseR);
  const npos = creatureSystem._neighborScratch;
  const preyMask = S.preyMask || 0;
  const huntsMask = S.huntsMask || 0;
  const selfSize = creatureSystem.eSize(creature);
  const selfAdult = creatureSystem.isAdult(creature);

  let threat = null;
  let tdist2 = 1e18;
  let prey = null;
  let pdist2 = 1e18;
  let mate = null;
  let mdist2 = 1e18;

  for (const o of neigh)
  {
    creatureSystem.simPosInto(npos, o);
    const ddx = npos.x - pos.x;
    const ddy = npos.y - pos.y;
    const d2 = ddx * ddx + ddy * ddy;
    if (d2 >= senseR2) continue;

    const osp = SPECIES_INDEX[o.sp];
    const bit = osp >= 0 ? (1 << osp) : 0;
    if (preyMask & bit)
    {
      const oSize = creatureSystem.eSize(o);
      if (oSize > selfSize * 0.7 && d2 < tdist2)
      {
        tdist2 = d2;
        threat = o;
      }
    }
    if (huntsMask & bit)
    {
      const oSize = creatureSystem.eSize(o);
      if (oSize < selfSize * 1.25 && d2 < pdist2)
      {
        pdist2 = d2;
        prey = o;
      }
    }
    if (o.sp === creature.sp && o.sex !== creature.sex && creatureSystem.isAdult(o) && selfAdult
      && o.mateCd <= 0 && creature.mateCd <= 0
      && o.pregnant <= 0 && creature.pregnant <= 0 && o.energy > 45 && creature.energy > 45)
    {
      if (d2 < mdist2)
      {
        mdist2 = d2;
        mate = o;
      }
    }
  }

  return {
    creature,
    species: S,
    thresholds: S.behaviorConfig?.thresholds || {},
    threat,
    tdist: tdist2 < 1e18 ? Math.sqrt(tdist2) : 1e9,
    prey,
    pdist: pdist2 < 1e18 ? Math.sqrt(pdist2) : 1e9,
    mate,
    mdist: mdist2 < 1e18 ? Math.sqrt(mdist2) : 1e9,
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
