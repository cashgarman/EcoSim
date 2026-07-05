import { lightLevelFromTimeOfDay, rng, ri } from './utils.js';
import { SPECIES, SP_KEYS } from './data.js';
import { state, MAX_POP, simulationMode } from './state.js';
import { world } from './world.js';
import { creatures } from './creatures.js';
import { gpuSimulationBackend } from './gpu/simulation-backend.js';
import { behaviorTree } from './behavior/index.js';
import { timelineDb } from './timeline-db.js';
import { effectiveHeartbeatIntervalSec, shouldRunBehaviorThisSubstep } from './perf-policy.js';

export class Simulation
{
  runMigrantPulse(dt)
  {
    if (state.batchMode)
    {
      if (!state.batchConfig?.autoMigration) return;
    }
    else if (!state.autoMigrationEnabled) return;
    state.migrantTimer += dt;
    if (state.migrantTimer <= 6) return;
    state.migrantTimer = 0;
    const alive = state.creatures.filter(c => !c.dead);
    const counts = {};
    for (const k of SP_KEYS) counts[k] = 0;
    for (const c of alive)
    {
      counts[c.sp] = (counts[c.sp] || 0) + 1;
    }
    for (const sp of SP_KEYS)
    {
      const isPredator = SPECIES[sp].diet >= 1;
      const preyAround = SPECIES[sp].hunts
        ? SPECIES[sp].hunts.some(p => (counts[p] || 0) > 2)
        : true;
      if ((counts[sp] || 0) <= 1
        && alive.length < MAX_POP * 0.7
        && rng() < (isPredator ? 0.25 : 0.6)
        && (!isPredator || preyAround))
      {
        const n = isPredator ? 1 : ri(2, 3);
        for (let i = 0; i < n; i++)
        {
          const t = creatures.findSpawnTile(sp);
          if (!t) continue;
          const c = creatures.makeCreature(sp, t.x, t.y);
          c.hunger = 85;
          c.thirst = 85;
        }
        creatures.log(`${SPECIES[sp].emoji} ${SPECIES[sp].label}s migrate into the region.`);
      }
    }
  }

  captureHeartbeat()
  {
    if (state.batchMode) return;
    const interval = effectiveHeartbeatIntervalSec();
    if (state.tGlobal < state.heartbeatNextAt) return;
    state.heartbeatNextAt = state.tGlobal + interval;
    const alive = state.creatures.filter(c => !c.dead);
    const counts = {};
    const avgNeeds = { hp: 0, hunger: 0, thirst: 0, energy: 0 };
    for (const k of SP_KEYS) counts[k] = 0;
    for (const c of alive)
    {
      counts[c.sp] = (counts[c.sp] || 0) + 1;
      avgNeeds.hp += c.hp || 0;
      avgNeeds.hunger += c.hunger || 0;
      avgNeeds.thirst += c.thirst || 0;
      avgNeeds.energy += c.energy || 0;
    }
    const denom = Math.max(1, alive.length);
    avgNeeds.hp /= denom;
    avgNeeds.hunger /= denom;
    avgNeeds.thirst /= denom;
    avgNeeds.energy /= denom;
    const selected = state.selected
      ? {
        id: state.selected.id,
        sp: state.selected.sp,
        x: state.selected.x,
        y: state.selected.y,
        state: state.selected.state,
        hp: state.selected.hp,
        hunger: state.selected.hunger,
        thirst: state.selected.thirst,
        energy: state.selected.energy,
      }
      : null;
    timelineDb.appendHeartbeat({
      t: state.tGlobal,
      day: state.day,
      speed: state.speed,
      seed: state.SEED,
      world: {
        alive: alive.length,
        counts,
        avgNeeds,
        selected,
      },
    });
  }

  updateDayNight()
  {
    state.lightLevel = lightLevelFromTimeOfDay(state.timeOfDay);
    state.isNight = state.lightLevel < 0.28;
  }

  /**
   * Main simulation tick function.
   * Advances the simulation by a given delta time (dt).
   * Handles day/night cycle, mode switching, GPU backend, and periodic migration.
   * 
   * @param {number} dt – Time delta (fraction of a day, or simulation step).
   */
  tick(dt, options = {})
  {
    // Advance the in-game time of day (fraction between 0 and 1)
    state.timeOfDay = (state.timeOfDay + dt / 40) % 1;

    // Update daylight state, light intensity, and night flag according to time of day
    this.updateDayNight();

    // Advance the absolute day counter based on total simulation time
    state.day = Math.floor(state.tGlobal / 40);
    this.captureHeartbeat();

    // Check for special hybrid GPU simulation initialization step
    // - Only perform minimal vegetative "growRow" work to bootstrap GPU sim backend
    // - Occurs before fully switching over to GPU mode
    const gpuSimPending = (
      simulationMode === 'gpu_hybrid' &&
      state.gpuSimInitPending &&
      !state.gpuSimEnabled
    );
    if (gpuSimPending)
    {
      // Progress one row of vegetation growth per tick (to gradually initialize)
      state.growRow = (state.growRow + 1) % state.H;

      // If a full pass over all rows is complete, mark plants as dirty (need redraw/update)
      if (state.growRow === 0) state.vegDirty = true;

      // Exit early since we're still initializing GPU sim
      return;
    }

    // If backend is GPU and GPU simulation is fully enabled:
    if (state.simBackend === 'gpu' && state.gpuSimEnabled)
    {
      const runBehavior = shouldRunBehaviorThisSubstep(options.substep, options.substepCount);
      if (runBehavior)
      {
        creatures.rebuildGrid();
        for (const c of state.creatures)
        {
          if (!c.dead) behaviorTree.tickDecisionOnly(c, creatures);
        }
        gpuSimulationBackend.uploadBehaviorDecisions();
      }

      const simStart = performance.now();
      gpuSimulationBackend.step(dt);
      const simMs = performance.now() - simStart;

      // Keep process timing telemetry via exponential moving average
      state.gpuTelemetry.simStepMs = state.gpuTelemetry.simStepMs
        ? state.gpuTelemetry.simStepMs * 0.85 + simMs * 0.15
        : simMs;

      // Step 4: Progress one row of vegetation growth each tick
      state.growRow = (state.growRow + 1) % state.H;

      // If we've processed all rows, flag the vegetation ("veg") as dirty so it can sync/redraw
      if (state.growRow === 0) state.vegDirty = true;

      // Step 5: Handle periodic animal migration/refill if population is low
      this.runMigrantPulse(dt);

      // Step 6: Remove dead creatures from simulation arrays/grids (cleanup)
      creatures.pruneDead();
      return;
    }

    // Rebuild the spatial grid with the current positions of all (alive and dead) creatures.
    // This is often used for spatial queries such as finding neighbors, pathfinding, etc.
    creatures.rebuildGrid();

    // Update each alive creature in the simulation for this time step.
    // The stepCreature function usually handles actions such as movement, eating, drinking, aging, etc.
    for (const c of state.creatures)
    {
      if (!c.dead) // Only process creatures that are still alive.
        creatures.stepCreature(c, dt);
    }

    // Allow the world simulation to advance vegetation growth.
    // This could mean grass and plants spread or regrow across the map.
    world.growVegetation(dt);

    this.runMigrantPulse(dt);

    // Clean up the simulation by removing any creatures that have died this cycle,
    // ensuring arrays and spatial data are up-to-date.
    creatures.pruneDead();
  }
}

export const simulation = new Simulation();
