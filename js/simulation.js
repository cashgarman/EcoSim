import { clamp, rng, ri } from './utils.js';
import { SPECIES, SP_KEYS } from './data.js';
import { state, MAX_POP } from './state.js';
import { world } from './world.js';
import { creatures } from './creatures.js';

export class Simulation
{
  updateDayNight()
  {
    const sun = Math.sin((state.timeOfDay - 0.25) * Math.PI * 2);
    state.lightLevel = clamp(Math.pow(sun * 0.5 + 0.5, 0.9), 0.08, 1);
    state.isNight = state.lightLevel < 0.28;
  }

  tick(dt)
  {
    state.timeOfDay = (state.timeOfDay + dt / 40) % 1;
    this.updateDayNight();
    state.day = Math.floor(state.tGlobal / 40);

    creatures.rebuildGrid();
    for (const c of state.creatures)
    {
      if (!c.dead) creatures.stepCreature(c, dt);
    }
    world.growVegetation(dt);

    state.migrantTimer += dt;
    if (state.migrantTimer > 6)
    {
      state.migrantTimer = 0;
      const alive = state.creatures.filter(c => !c.dead);
      const counts = {};
      for (const k of SP_KEYS) counts[k] = 0;
      for (const c of alive) counts[c.sp]++;

      for (const sp of SP_KEYS)
      {
        const isPredator = SPECIES[sp].diet >= 1;
        const preyAround = SPECIES[sp].hunts ? SPECIES[sp].hunts.some(p => counts[p] > 2) : true;
        if (counts[sp] <= 1 && alive.length < MAX_POP * 0.7 && rng() < (isPredator ? 0.25 : 0.6) && (!isPredator || preyAround))
        {
          const n = isPredator ? 1 : ri(2, 3);
          for (let i = 0; i < n; i++)
          {
            const t = creatures.findSpawnTile(sp);
            if (t)
            {
              const c = creatures.makeCreature(sp, t.x, t.y);
              c.hunger = 85;
              c.thirst = 85;
            }
          }
          creatures.log(`${SPECIES[sp].emoji} ${SPECIES[sp].label}s migrate into the region.`);
        }
      }
    }

    creatures.pruneDead();
  }
}

export const simulation = new Simulation();
