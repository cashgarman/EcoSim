import { rng, ri, rf, gauss, clamp, lerp, expSmoothT } from './utils.js';
import { B, SPECIES, SP_KEYS, GENE_KEYS, GENE_RANGE, isWater } from './data.js';
import { state, MAX_POP, idx, inB, gkey, CELL } from './state.js';
import {
  atWaterEdge,
  planGridStep,
  pickRandomWalkableTile,
} from './nav.js';
import { quality } from './render/quality.js';
import { lifeStory } from './life-story.js';
import { behaviorTree } from './behavior/index.js';

export class CreatureSystem
{
  constructor()
  {
    this._logFn = null;
    this._notifyFn = null;
  }

  setLogger(fn)
  {
    this._logFn = fn;
  }

  setNotifyFn(fn)
  {
    this._notifyFn = fn;
  }

  notify(html, creatureId)
  {
    if (this._notifyFn) this._notifyFn(html, creatureId);
  }

  randomSex()
  {
    return rng() < 0.5 ? 'female' : 'male';
  }

  log(msg)
  {
    if (this._logFn) this._logFn(msg);
  }

  newGenome(sp)
  {
    const g = {};
    const b = SPECIES[sp].base;
    for (const k of GENE_KEYS)
    {
      g[k] = clamp(
        b[k] * (1 + gauss() * 0.12) + (k === 'hue' ? gauss() * 20 : 0),
        GENE_RANGE[k][0],
        GENE_RANGE[k][1],
      );
    }
    return g;
  }

  breedGenome(a, b)
  {
    const g = {};
    for (const k of GENE_KEYS)
    {
      let v = (a[k] + b[k]) / 2 + gauss() * (GENE_RANGE[k][1] - GENE_RANGE[k][0]) * 0.05;
      if (rng() < 0.02) v += gauss() * (GENE_RANGE[k][1] - GENE_RANGE[k][0]) * 0.18;
      g[k] = clamp(v, GENE_RANGE[k][0], GENE_RANGE[k][1]);
    }
    return g;
  }

  findSpawnTile(sp)
  {
    for (let tries = 0; tries < 300; tries++)
    {
      const x = ri(2, state.W - 3), y = ri(2, state.H - 3);
      const b = state.biome[idx(x, y)];
      if (isWater(b)) continue;
      if (b === B.PEAK) continue;
      return { x: x + rf(-0.3, 0.3), y: y + rf(-0.3, 0.3) };
    }
    return null;
  }

  allocateGpuSlot()
  {
    const used = new Set();
    for (const c of state.creatures)
    {
      if (!c) continue;
      if (typeof c.gpuSlot === 'number' && c.gpuSlot >= 0)
      {
        used.add(c.gpuSlot);
      }
    }
    let slot = 0;
    while (used.has(slot)) slot++;
    return slot;
  }

  makeCreature(sp, x, y, genome, gen, recordStory = true, sex)
  {
    const S = SPECIES[sp];
    const g = genome || this.newGenome(sp);
    const c = {
      id: state.nextId++,
      sp,
      sex: sex || this.randomSex(),
      x,
      y,
      vx: 0,
      vy: 0,
      dir: 1,
      genome: g,
      gen: gen || 1,
      age: rf(0.15, 0.4) * g.lifespan,
      hp: 100,
      hunger: rf(60, 90),
      thirst: rf(60, 90),
      energy: rf(70, 100),
      state: 'wander',
      tx: x,
      ty: y,
      target: null,
      mateCd: rf(1, 4),
      pregnant: 0,
      litterQ: 0,
      walk: rng() * 6.28,
      dead: false,
      cause: '',
      parentIds: [],
      offspringIds: [],
      gpuSlot: this.allocateGpuSlot(),
      gpuNeedsUpload: true,
      rx: x,
      ry: y,
    };
    lifeStory.initCreature(c);
    state.creatures.push(c);
    if ((gen || 1) > state.generationMax) state.generationMax = gen || 1;
    state.gpuSimDirtyFromCpu = true;
    if (recordStory && !state.batchMode) lifeStory.recordAppeared(c, 'spawned');
    return c;
  }

  getById(id)
  {
    for (const c of state.creatures)
    {
      if (c.id === id) return c;
    }
    return null;
  }

  displayX(c)
  {
    return typeof c.rx === 'number' ? c.rx : c.x;
  }

  displayY(c)
  {
    return typeof c.ry === 'number' ? c.ry : c.y;
  }

  snapDisplayPosition(c)
  {
    if (!c) return;
    c.rx = c.x;
    c.ry = c.y;
  }

  snapAllDisplayPositions()
  {
    for (const c of state.creatures)
    {
      if (!c || c.dead) continue;
      this.snapDisplayPosition(c);
    }
  }

  advanceDisplayPositions(dt)
  {
    if (!dt || dt <= 0) return;
    const scrubbing = state.scrubActive;
    const t = expSmoothT(scrubbing ? 10 : 16, dt);
    const onGpu = state.simBackend === 'gpu' && state.gpuSimEnabled && !scrubbing;
    const extrapolateGpu = onGpu && state.speed > 0 && state.gpuDisplayExtrapolate !== false;
    const sinceReadback = extrapolateGpu && state.gpuPosSyncAt
      ? (performance.now() - state.gpuPosSyncAt) / 1000
      : 0;

    for (const c of state.creatures)
    {
      if (!c || c.dead) continue;
      if (typeof c.rx !== 'number')
      {
        c.rx = c.x;
        c.ry = c.y;
      }

      let tx = c.x;
      let ty = c.y;
      if (extrapolateGpu)
      {
        tx = c.x + c.vx * sinceReadback;
        ty = c.y + c.vy * sinceReadback;
      }

      c.rx += (tx - c.rx) * t;
      c.ry += (ty - c.ry) * t;
      if (scrubbing && typeof c.walk === 'number')
      {
        c.walk += dt * 7;
      }
    }
  }

  addOffspring(parent, childId)
  {
    if (!parent) return;
    if (!parent.offspringIds.includes(childId)) parent.offspringIds.push(childId);
  }

  linkBirthParents(baby, mother, fatherId)
  {
    baby.parentIds = [mother.id];
    this.addOffspring(mother, baby.id);
    if (fatherId != null)
    {
      baby.parentIds.push(fatherId);
      this.addOffspring(this.getById(fatherId), baby.id);
    }
  }

  eSize(c)
  {
    return c.genome.size * (c.age < c.genome.lifespan * 0.25 ? 0.55 : 1);
  }

  isAdult(c)
  {
    return c.age >= c.genome.lifespan * 0.25;
  }

  rebuildGrid()
  {
    state.grid.clear();
    for (const c of state.creatures)
    {
      if (c.dead) continue;
      const k = gkey(Math.floor(c.x / CELL), Math.floor(c.y / CELL));
      let a = state.grid.get(k);
      if (!a) { a = []; state.grid.set(k, a); }
      a.push(c);
    }
  }

  nearby(c, r)
  {
    const out = [];
    const cx = Math.floor(c.x / CELL), cy = Math.floor(c.y / CELL);
    const rr = Math.ceil(r / CELL);
    for (let dy = -rr; dy <= rr; dy++)
    {
      for (let dx = -rr; dx <= rr; dx++)
      {
        const a = state.grid.get(gkey(cx + dx, cy + dy));
        if (!a) continue;
        for (const o of a)
        {
          if (o !== c && !o.dead)
          {
            const ddx = o.x - c.x, ddy = o.y - c.y;
            if (ddx * ddx + ddy * ddy < r * r) out.push(o);
          }
        }
      }
    }
    return out;
  }

  wander(c)
  {
    const canSwim = SPECIES[c.sp].shape === 'bird';
    if (Math.hypot(c.tx - c.x, c.ty - c.y) < 0.6 || rng() < 0.02)
    {
      const t = pickRandomWalkableTile(c.x, c.y, 6, canSwim);
      c.tx = clamp(t.x, 1, state.W - 2);
      c.ty = clamp(t.y, 1, state.H - 2);
    }
  }

  moveTowardGoal(c, goalX, goalY, speed, dt)
  {
    const canSwim = SPECIES[c.sp].shape === 'bird';
    const navR = quality.config().navRadius;
    if (c.state === 'thirst' && atWaterEdge(c.x, c.y))
    {
      c.vx *= 0.7;
      c.vy *= 0.7;
      return;
    }
    const wp = planGridStep(c.x, c.y, goalX, goalY, canSwim, navR);
    const tx = wp ? wp.x : goalX;
    const ty = wp ? wp.y : goalY;
    c.tx = tx;
    c.ty = ty;

    const dx = tx - c.x, dy = ty - c.y, d = Math.hypot(dx, dy);
    if (d < 0.05) { c.vx *= 0.7; c.vy *= 0.7; return; }
    const nx = c.x + dx / d * speed * 2.2 * dt;
    const ny = c.y + dy / d * speed * 2.2 * dt;
    const bi = inB(Math.round(nx), Math.round(ny)) ? state.biome[idx(Math.round(nx), Math.round(ny))] : B.OCEAN;
    if ((isWater(bi) && !canSwim) || bi === B.PEAK)
    {
      c.vx *= 0.5;
      c.vy *= 0.5;
      return;
    }
    c.vx = (nx - c.x) / dt;
    c.vy = (ny - c.y) / dt;
    c.x = nx;
    c.y = ny;
  }

  moveTo(c, speed, dt)
  {
    this.moveTowardGoal(c, c.tx, c.ty, speed, dt);
  }

  findFood(c, r)
  {
    let best = null, bv = 0.05;
    const ix = Math.round(c.x), iy = Math.round(c.y);
    for (let dy = -r; dy <= r; dy += 2)
    {
      for (let dx = -r; dx <= r; dx += 2)
      {
        const nx = ix + dx, ny = iy + dy;
        if (!inB(nx, ny)) continue;
        const ti = idx(nx, ny);
        if (!isWater(state.biome[ti]) && state.veg[ti] > bv)
        {
          bv = state.veg[ti];
          best = { x: nx, y: ny };
        }
      }
    }
    return best;
  }

  giveBirth(c)
  {
    const q = c.litterQ || 1;
    c.litterQ = 0;
    const partner = c.matePartner || c.genome;
    const fatherId = c.matePartnerId ?? null;
    let born = 0;
    let firstBaby = null;
    for (let i = 0; i < q; i++)
    {
      if (state.creatures.filter(x => !x.dead).length >= MAX_POP) break;
      const cg = this.breedGenome(c.genome, partner);
      const baby = this.makeCreature(c.sp, c.x + rf(-0.8, 0.8), c.y + rf(-0.8, 0.8), cg, c.gen + 1, false, this.randomSex());
      baby.age = 0;
      baby.hunger = 70;
      baby.thirst = 70;
      baby.energy = 80;
      this.linkBirthParents(baby, c, fatherId);
      lifeStory.recordBorn(baby, c.id, fatherId, baby.sex);
      if (!firstBaby) firstBaby = baby;
      born++;
    }
    c.matePartnerId = null;
    if (born > 0) lifeStory.recordGaveBirth(c, born);
  }

  die(c, cause)
  {
    if (c.dead) return;
    if (cause) c.cause = cause;
    lifeStory.recordDied(c, cause);
    c.dead = true;
    c.gpuNeedsUpload = true;
    state.gpuSimDirtyFromCpu = true;
    const ti = idx(clamp(Math.round(c.x), 0, state.W - 1), clamp(Math.round(c.y), 0, state.H - 1));
    if (!isWater(state.biome[ti]))
    {
      state.veg[ti] = Math.min(state.vegCap[ti], state.veg[ti] + 0.15);
    }
  }

  killAllBySpecies(sp, cause = 'removed')
  {
    let killed = 0;
    for (const c of state.creatures)
    {
      if (c.dead || c.sp !== sp) continue;
      this.die(c, cause);
      killed++;
    }
    if (killed > 0)
    {
      this.rebuildGrid();
      state.vegDirty = true;
    }
    return killed;
  }

  stepNeeds(c, dt)
  {
    const g = c.genome;
    const load = g.size * g.metab;
    c.hunger -= (0.9 * load + (c.state === 'hunt' ? 0.6 : 0)) * dt;
    c.thirst -= 1.0 * load * dt;
    c.energy -= (0.6 * load + (c.vx * c.vx + c.vy * c.vy > 0.02 ? 0.9 : 0)) * dt;
    c.age += dt / 24;
    if (c.mateCd > 0) c.mateCd -= dt;

    const localT = state.temp[idx(clamp(Math.round(c.x), 0, state.W - 1), clamp(Math.round(c.y), 0, state.H - 1))];
    const stress = Math.max(0, Math.abs(localT - g.temp) - g.tol);
    if (stress > 0) c.hp -= stress * 14 * dt;

    if (c.hunger <= 0) { c.hp -= 6 * dt; c.hunger = 0; }
    if (c.thirst <= 0) { c.hp -= 7 * dt; c.thirst = 0; }
    if (c.energy <= 0) c.energy = 0;
    if (c.hunger > 55 && c.thirst > 55 && stress <= 0) c.hp = Math.min(100, c.hp + 4 * dt);

    if (c.hp <= 0)
    {
      if (!c.cause || c.cause === 'exhaustion')
      {
        if (c.hunger <= 0 && c.thirst <= 0) c.cause = 'starvation and dehydration';
        else if (c.hunger <= 0) c.cause = 'starvation';
        else if (c.thirst <= 0) c.cause = 'dehydration';
      }
      this.die(c, c.cause || 'exhaustion');
      return false;
    }
    if (c.age >= g.lifespan) { this.die(c, 'old age'); return false; }

    if (c.pregnant > 0) { c.pregnant -= dt; if (c.pregnant <= 0) this.giveBirth(c); }
    return true;
  }

  runBehaviorDecision(c, dt, options = {})
  {
    return behaviorTree.tick(c, dt, this, options);
  }

  stepCreature(c, dt)
  {
    if (!this.stepNeeds(c, dt)) return;

    behaviorTree.tick(c, dt, this, {
      executeActions: state.simBackend !== 'gpu',
      logLifeStory: state.simBackend !== 'gpu',
    });

    const sp2 = Math.hypot(c.vx, c.vy);
    c.walk += sp2 * dt * 10 + dt * 0.5;
    if (Math.abs(c.vx) > 0.001) c.dir = c.vx > 0 ? 1 : -1;
  }

  findAt(wx, wy)
  {
    let best = null, bd = 1.5;
    for (const c of state.creatures)
    {
      if (c.dead) continue;
      const d = Math.hypot(c.x - wx, c.y - wy);
      if (d < bd) { bd = d; best = c; }
    }
    return best;
  }

  collectVisible(camera)
  {
    const pad = Math.max(30, state.cam.z * 4);
    const minX = camera.s2wX(-pad), maxX = camera.s2wX(camera.canvas.width + pad);
    const minY = camera.s2wY(-pad), maxY = camera.s2wY(camera.canvas.height + pad);
    const vis = [];
    for (const c of state.creatures)
    {
      if (c.dead) continue;
      if (c.x < minX || c.x > maxX || c.y < minY || c.y > maxY) continue;
      vis.push(c);
    }
    vis.sort((a, b) => a.y - b.y);
    return vis;
  }

  stockLife()
  {
    const density = state.cfg.animals;
    const areaScale = Math.max(0.5, state.worldAreaKm2 / 64);
    const scaledBudget = 260 * Math.sqrt(areaScale);
    const budget = Math.min(Math.floor(MAX_POP * 0.45), Math.floor(lerp(0, scaledBudget, density)));
    const plan = {};
    for (const sp of SP_KEYS) plan[sp] = SPECIES[sp].stockWeight ?? (1 / SP_KEYS.length);
    for (const sp of SP_KEYS)
    {
      const n = Math.round(budget * plan[sp]);
      for (let i = 0; i < n; i++)
      {
        const t = this.findSpawnTile(sp);
        if (t) this.makeCreature(sp, t.x, t.y);
      }
    }
    this.log(`🐾 Seeded a new food web with ${state.creatures.filter(c => !c.dead).length} animals.`);
    state.gpuSimDirtyFromCpu = true;
    this.snapAllDisplayPositions();
  }

  pruneDead()
  {
    const onGpu = state.simBackend === 'gpu' && state.gpuSimEnabled;
    const aliveCount = onGpu
      ? (state.gpuTelemetry.aliveCount || state.creatures.filter(c => c && !c.dead).length)
      : state.creatures.filter(c => c && !c.dead).length;
    const deadCount = state.creatures.length - aliveCount;
    const overCapacity = state.creatures.length > MAX_POP * 1.15;
    const deadBloat = deadCount > Math.max(40, aliveCount * 0.08);
    if (!overCapacity && !deadBloat && rng() >= 0.05) return;

    state.creatures = state.creatures.filter(c => c && (!c.dead || c === state.selected));
    if (!onGpu) state.gpuSimDirtyFromCpu = true;
  }
}

export const creatures = new CreatureSystem();
