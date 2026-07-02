import { rng, ri, rf, gauss, clamp, lerp } from './utils.js';
import { B, SPECIES, SP_KEYS, GENE_KEYS, GENE_RANGE, isWater, sampleGestation, sampleMateCooldown, sexSymbol } from './data.js';
import { state, MAX_POP, idx, inB, gkey, CELL } from './state.js';
import {
  atWaterEdge,
  nearestWaterEdgeTarget,
  planGridStep,
  snapWalkableGoal,
  pickRandomWalkableTile,
} from './nav.js';
import { quality } from './render/quality.js';
import { lifeStory } from './life-story.js';

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
    };
    lifeStory.initCreature(c);
    state.creatures.push(c);
    if ((gen || 1) > state.generationMax) state.generationMax = gen || 1;
    state.gpuSimDirtyFromCpu = true;
    if (recordStory) lifeStory.recordAppeared(c, 'spawned');
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
    if (firstBaby)
    {
      const S = SPECIES[c.sp];
      this.notify(
        `${S.emoji} ${S.label} ${sexSymbol(firstBaby.sex)} born <b>(gen ${firstBaby.gen})</b>`,
        firstBaby.id,
      );
    }
  }

  die(c, cause)
  {
    if (c.dead) return;
    lifeStory.recordDied(c, cause);
    c.dead = true;
    c.gpuNeedsUpload = true;
    state.gpuSimDirtyFromCpu = true;
    c.cause = cause;
    const ti = idx(clamp(Math.round(c.x), 0, state.W - 1), clamp(Math.round(c.y), 0, state.H - 1));
    if (!isWater(state.biome[ti]))
    {
      state.veg[ti] = Math.min(state.vegCap[ti], state.veg[ti] + 0.15);
    }
  }

  stepCreature(c, dt)
  {
    const S = SPECIES[c.sp], g = c.genome;
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

    if (c.hp <= 0) { this.die(c, c.cause || 'exhaustion'); return; }
    if (c.age >= g.lifespan) { this.die(c, 'old age'); return; }

    if (c.pregnant > 0) { c.pregnant -= dt; if (c.pregnant <= 0) this.giveBirth(c); }

    const senseR = g.sense;
    const neigh = this.nearby(c, senseR);
    let threat = null, tdist = 1e9, prey = null, pdist = 1e9, mate = null, mdist = 1e9;

    for (const o of neigh)
    {
      const d = Math.hypot(o.x - c.x, o.y - c.y);
      if (S.preyOf && S.preyOf.includes(o.sp) && this.eSize(o) > this.eSize(c) * 0.7)
      {
        if (d < tdist) { tdist = d; threat = o; }
      }
      if (S.hunts && S.hunts.includes(o.sp) && this.eSize(o) < this.eSize(c) * 1.25)
      {
        if (d < pdist) { pdist = d; prey = o; }
      }
      if (o.sp === c.sp && o.sex !== c.sex && this.isAdult(o) && this.isAdult(c)
        && o.mateCd <= 0 && c.mateCd <= 0
        && o.pregnant <= 0 && c.pregnant <= 0 && o.energy > 45 && c.energy > 45)
      {
        if (d < mdist) { mdist = d; mate = o; }
      }
    }

    let st = 'wander';
    if (threat) st = 'flee';
    else if (c.thirst < 30 || (c.state === 'thirst' && c.thirst < 55)) st = 'thirst';
    else if (c.hunger < 55) st = (S.diet === 0 ? 'graze' : (prey ? 'hunt' : (S.diet === 2 ? 'graze' : 'huntSearch')));
    else if (c.energy < 18) st = 'rest';
    else if (mate && c.pregnant <= 0 && c.hunger > 45 && c.thirst > 40) st = 'mate';
    else if (S.diet >= 1 && prey && c.hunger < 75) st = 'hunt';
    else st = 'wander';
    c.state = st;
    c.target = null;

    let speed = g.speed;
    if (!this.isAdult(c)) speed *= 0.8;
    if (state.isNight) { speed *= 0.6; if (st === 'wander' && c.energy < 75) st = 'rest'; }

    const canSwim = SPECIES[c.sp].shape === 'bird';

    const applySnappedGoal = (gx, gy) =>
    {
      const sn = snapWalkableGoal(Math.round(gx), Math.round(gy), canSwim, 8);
      if (sn) { c.tx = sn.x + 0.5; c.ty = sn.y + 0.5; }
      else { c.tx = gx; c.ty = gy; }
    };

    switch (st)
    {
      case 'flee':
      {
        if (atWaterEdge(c.x, c.y) && c.thirst < 55)
        {
          c.thirst = Math.min(100, c.thirst + 60 * dt);
          c.vx *= 0.7;
          c.vy *= 0.7;
          break;
        }
        c.target = threat ? threat.id : null;
        const a = Math.atan2(c.y - threat.y, c.x - threat.x);
        applySnappedGoal(c.x + Math.cos(a) * 6, c.y + Math.sin(a) * 6);
        this.moveTo(c, speed * 1.25, dt);
        break;
      }
      case 'thirst':
      {
        if (atWaterEdge(c.x, c.y)) { c.thirst = Math.min(100, c.thirst + 60 * dt); }
        else
        {
          const w = nearestWaterEdgeTarget(c.x, c.y, senseR + 6);
          if (w) { c.tx = w.x; c.ty = w.y; }
          else this.wander(c);
          this.moveTo(c, speed, dt);
        }
        break;
      }
      case 'graze':
      {
        const ti = idx(clamp(Math.round(c.x), 0, state.W - 1), clamp(Math.round(c.y), 0, state.H - 1));
        if (state.veg[ti] > 0.04)
        {
          const bite = Math.min(state.veg[ti], 3.5 * dt);
          state.veg[ti] -= bite;
          c.hunger = Math.min(100, c.hunger + bite * 26);
          state.vegDirty = true;
        }
        else
        {
          const t = this.findFood(c, senseR);
          if (t) applySnappedGoal(t.x, t.y);
          else this.wander(c);
          this.moveTo(c, speed, dt);
        }
        break;
      }
      case 'huntSearch':
        this.wander(c);
        this.moveTo(c, speed, dt);
        break;
      case 'hunt':
      {
        if (prey)
        {
          applySnappedGoal(prey.x, prey.y);
          c.target = prey.id;
          this.moveTo(c, speed * 1.15, dt);
          if (pdist < this.eSize(c) * 0.6 + 0.5)
          {
            if (rng() < (0.10 + g.agg * 0.10))
            {
              prey.hp -= 30 + g.size * 15;
              prey.cause = 'predation';
              if (prey.hp <= 0)
              {
                c.hunger = Math.min(100, c.hunger + 50);
                c.energy = Math.min(100, c.energy + 12);
                lifeStory.recordHunted(c, prey);
              }
            }
          }
        }
        else this.wander(c);
        this.moveTo(c, speed, dt);
        break;
      }
      case 'rest':
        c.energy = Math.min(100, c.energy + 9 * dt);
        c.vx *= 0.8;
        c.vy *= 0.8;
        break;
      case 'mate':
      {
        if (mate)
        {
          c.target = mate.id;
          applySnappedGoal(mate.x, mate.y);
          this.moveTo(c, speed, dt);
          if (mdist < 1.0)
          {
            const female = c.sex === 'female' ? c : mate;
            const male = c.sex === 'male' ? c : mate;
            if (female.sex === 'female' && male.sex === 'male'
              && female.pregnant <= 0 && female.mateCd <= 0 && male.mateCd <= 0)
            {
              female.pregnant = sampleGestation(c.sp);
              female.matePartner = male.genome;
              female.matePartnerId = male.id;
              female.litterQ = Math.max(1, Math.round(g.litter * rf(0.7, 1.15)));
              const cd = sampleMateCooldown(c.sp);
              female.mateCd = cd;
              male.mateCd = cd * 0.6;
              female.energy -= 20;
              male.energy -= 12;
              lifeStory.recordMated(female, male.id, male.sp, female.sex, male.sex);
              lifeStory.recordMated(male, female.id, female.sp, male.sex, female.sex);
              const S = SPECIES[c.sp];
              this.notify(
                `${S.emoji} ${S.label} ${sexSymbol(female.sex)} mated with ${sexSymbol(male.sex)} ${S.label.toLowerCase()}`,
                female.id,
              );
            }
          }
        }
        else this.wander(c);
        break;
      }
      default:
        this.wander(c);
        this.moveTo(c, speed * 0.7, dt);
    }

    if (state.simBackend !== 'gpu')
    {
      lifeStory.observeDecision(c, c.state, c.target);
      lifeStory.observeAge(c);
    }

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
  }

  pruneDead()
  {
    if (state.simBackend === 'gpu' && state.gpuSimEnabled)
    {
      return;
    }
    if (state.creatures.length > MAX_POP * 1.3 || rng() < 0.05)
    {
      state.creatures = state.creatures.filter(c => !c.dead || c === state.selected);
      state.gpuSimDirtyFromCpu = true;
    }
  }
}

export const creatures = new CreatureSystem();
