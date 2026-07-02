import { state, CELL, GPU_SIM_MAX_CREATURES } from '../state.js';
import { SP_KEYS, SPECIES, SPECIES_INDEX, buildGpuSpeciesTables } from '../data.js';

const CELL_CAP = 64;
const CREATURE_STRIDE_VEC4 = 8;
const CREATURE_STRIDE_FLOATS = CREATURE_STRIDE_VEC4 * 4;
const PARAM_FLOATS = 16;
const WORLD_STRIDE = 5;
const COUNTERS_LEN = 8;
const SPECIES_SUM_LEN = SP_KEYS.length * 3;
const PREY_OWNER_BASE = COUNTERS_LEN + SPECIES_SUM_LEN;

export function gpuBehaviorToState(stateCode)
{
  const st = Math.max(0, Math.min(7, Math.round(stateCode || 0)));
  return [
    'wander',
    'flee',
    'thirst',
    'graze',
    'hunt',
    'rest',
    'mate',
    'huntSearch',
  ][st] || 'wander';
}

const GPU_SIM_SHADER = `
const CREATURE_STRIDE: u32 = ${CREATURE_STRIDE_VEC4}u;
const CELL_CAP: u32 = ${CELL_CAP}u;
const WORLD_STRIDE: u32 = ${WORLD_STRIDE}u;
const COUNTERS_LEN: u32 = ${COUNTERS_LEN}u;
const SPECIES_SUM_LEN: u32 = ${SPECIES_SUM_LEN}u;
const PREY_OWNER_BASE: u32 = ${PREY_OWNER_BASE}u;
const POOL_CAP: u32 = ${GPU_SIM_MAX_CREATURES}u;
const FOOD_OWNER_BASE: u32 = PREY_OWNER_BASE + POOL_CAP;

@group(0) @binding(0) var<storage, read_write> creatures: array<vec4<f32>>;
@group(0) @binding(1) var<storage, read_write> worldData: array<f32>;
@group(0) @binding(2) var<storage, read_write> cellCounts: array<atomic<u32>>;
@group(0) @binding(3) var<storage, read_write> cellEntries: array<u32>;
@group(0) @binding(4) var<storage, read_write> simAtomics: array<atomic<u32>>;
@group(0) @binding(5) var<storage, read_write> simLists: array<u32>;
@group(0) @binding(6) var<storage, read> speciesTables: array<vec4<f32>>;
@group(0) @binding(7) var<storage, read_write> renderData: array<f32>;
@group(0) @binding(8) var<uniform> params: array<f32, ${PARAM_FLOATS}>;

fn p_u(index: u32) -> u32
{
  return u32(params[index] + 0.5);
}

fn p_f(index: u32) -> f32
{
  return params[index];
}

fn creatureBase(i: u32) -> u32
{
  return i * CREATURE_STRIDE;
}

fn worldIndex(x: i32, y: i32, w: i32, h: i32) -> u32
{
  let cx = clamp(x, 0, w - 1);
  let cy = clamp(y, 0, h - 1);
  return u32(cy * w + cx);
}

fn worldTempAt(tile: u32) -> f32 { return worldData[tile * WORLD_STRIDE + 0u]; }
fn worldMoistAt(tile: u32) -> f32 { return worldData[tile * WORLD_STRIDE + 1u]; }
fn worldVegAt(tile: u32) -> f32 { return worldData[tile * WORLD_STRIDE + 2u]; }
fn worldVegCapAt(tile: u32) -> f32 { return worldData[tile * WORLD_STRIDE + 3u]; }
fn worldBiomeAt(tile: u32) -> u32 { return u32(worldData[tile * WORLD_STRIDE + 4u]); }
fn setWorldVeg(tile: u32, value: f32) { worldData[tile * WORLD_STRIDE + 2u] = value; }

fn hash11(x: f32) -> f32
{
  return fract(sin(x * 91.345 + 11.791) * 43758.5453);
}

fn hash3(a: f32, b: f32, c: f32) -> f32
{
  return hash11(a * 0.77 + b * 1.11 + c * 1.73);
}

fn pickGlobalTarget(huntsMask: u32, speciesCount: u32) -> vec2<f32>
{
  for (var sp: u32 = 0u; sp < speciesCount; sp++)
  {
    let bit = 1u << sp;
    if ((huntsMask & bit) == 0u) { continue; }
    let si = COUNTERS_LEN + sp * 3u;
    let cnt = atomicLoad(&simAtomics[si + 2u]);
    if (cnt > 0u)
    {
      let sx = f32(atomicLoad(&simAtomics[si + 0u])) / 1024.0;
      let sy = f32(atomicLoad(&simAtomics[si + 1u])) / 1024.0;
      return vec2<f32>(sx / f32(cnt), sy / f32(cnt));
    }
  }
  return vec2<f32>(-1.0, -1.0);
}

@compute @workgroup_size(128)
fn clearCells(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let cellCount = p_u(5u) * p_u(6u);
  let tileCount = p_u(2u) * p_u(3u);
  if (gid.x < cellCount)
  {
    atomicStore(&cellCounts[gid.x], 0u);
  }
  if (gid.x < tileCount)
  {
    atomicStore(&simAtomics[FOOD_OWNER_BASE + gid.x], 0xffffffffu);
  }
  if (gid.x < p_u(4u))
  {
    atomicStore(&simAtomics[PREY_OWNER_BASE + gid.x], 0xffffffffu);
  }
}

@compute @workgroup_size(64)
fn clearCounters(@builtin(global_invocation_id) gid: vec3<u32>)
{
  if (gid.x < COUNTERS_LEN)
  {
    atomicStore(&simAtomics[gid.x], 0u);
  }
  if (gid.x < SPECIES_SUM_LEN)
  {
    atomicStore(&simAtomics[COUNTERS_LEN + gid.x], 0u);
  }
}

@compute @workgroup_size(128)
fn binCreatures(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let i = gid.x;
  let creatureCount = p_u(4u);
  if (i >= creatureCount) { return; }
  let b = creatureBase(i);
  var pv = creatures[b + 0u];
  var mv = creatures[b + 4u];
  if (mv.w < 0.5)
  {
    let dslot = atomicAdd(&simAtomics[1u], 1u);
    simLists[POOL_CAP + dslot] = i;
    return;
  }

  let w = i32(p_u(2u));
  let h = i32(p_u(3u));
  pv.x = clamp(pv.x, 0.5, f32(w) - 1.5);
  pv.y = clamp(pv.y, 0.5, f32(h) - 1.5);
  creatures[b + 0u] = pv;

  let cx = u32(clamp(i32(pv.x / p_f(8u)), 0, i32(p_u(5u)) - 1));
  let cy = u32(clamp(i32(pv.y / p_f(8u)), 0, i32(p_u(6u)) - 1));
  let cellId = cy * p_u(5u) + cx;
  let slot = atomicAdd(&cellCounts[cellId], 1u);
  if (slot < CELL_CAP)
  {
    cellEntries[cellId * CELL_CAP + slot] = i;
  }

  let sp = u32(mv.x + 0.5);
  let si = COUNTERS_LEN + sp * 3u;
  atomicAdd(&simAtomics[si + 0u], u32(pv.x * 1024.0));
  atomicAdd(&simAtomics[si + 1u], u32(pv.y * 1024.0));
  atomicAdd(&simAtomics[si + 2u], 1u);
}

@compute @workgroup_size(128)
fn decideAndClaim(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let i = gid.x;
  let creatureCount = p_u(4u);
  if (i >= creatureCount) { return; }
  let b = creatureBase(i);
  let pv = creatures[b + 0u];
  var nv = creatures[b + 1u];
  var tv = creatures[b + 3u];
  let mv = creatures[b + 4u];
  if (mv.w < 0.5) { return; }

  let sp = u32(mv.x + 0.5);
  let speciesInfo = speciesTables[sp];
  let diet = i32(speciesInfo.x + 0.5);
  let huntsMask = u32(speciesInfo.y + 0.5);
  let preyMask = u32(speciesInfo.z + 0.5);
  let canSwim = speciesInfo.w > 0.5;
  let sense = max(3.0, mv.z);
  let cx = i32(clamp(i32(pv.x / p_f(8u)), 0, i32(p_u(5u)) - 1));
  let cy = i32(clamp(i32(pv.y / p_f(8u)), 0, i32(p_u(6u)) - 1));

  var preyId: u32 = 0xffffffffu;
  var preyDist2 = 1e12;
  var threatId: u32 = 0xffffffffu;
  var threatDist2 = 1e12;
  var mateId: u32 = 0xffffffffu;
  var mateDist2 = 1e12;
  let rr = i32(ceil(sense / p_f(8u)));

  for (var dy = -rr; dy <= rr; dy++)
  {
    for (var dx = -rr; dx <= rr; dx++)
    {
      let ncx = cx + dx;
      let ncy = cy + dy;
      if (ncx < 0 || ncy < 0 || ncx >= i32(p_u(5u)) || ncy >= i32(p_u(6u))) { continue; }
      let cellId = u32(ncy) * p_u(5u) + u32(ncx);
      let count = min(atomicLoad(&cellCounts[cellId]), CELL_CAP);
      for (var j: u32 = 0u; j < count; j++)
      {
        let oi = cellEntries[cellId * CELL_CAP + j];
        if (oi == i) { continue; }
        let ob = creatureBase(oi);
        let op = creatures[ob + 0u];
        let om = creatures[ob + 4u];
        if (om.w < 0.5) { continue; }
        let dd = vec2<f32>(op.x - pv.x, op.y - pv.y);
        let d2 = dot(dd, dd);
        if (d2 > sense * sense) { continue; }
        let osp = u32(om.x + 0.5);
        let bit = 1u << osp;
        if ((huntsMask & bit) != 0u && d2 < preyDist2)
        {
          preyDist2 = d2;
          preyId = oi;
        }
        if ((preyMask & bit) != 0u && d2 < threatDist2)
        {
          threatDist2 = d2;
          threatId = oi;
        }
        if (osp == sp && d2 < mateDist2 && nv.y > 45.0 && nv.z > 45.0)
        {
          mateDist2 = d2;
          mateId = oi;
        }
      }
    }
  }

  var st = 0.0;
  var tx = tv.x;
  var ty = tv.y;
  tv.z = -1.0;

  if (threatId != 0xffffffffu)
  {
    st = 1.0;
    let ob = creatureBase(threatId);
    let op = creatures[ob + 0u];
    let dir = normalize(vec2<f32>(pv.x - op.x, pv.y - op.y) + vec2<f32>(0.0001, 0.0001));
    tx = pv.x + dir.x * 6.0;
    ty = pv.y + dir.y * 6.0;
    tv.z = f32(threatId);
  }
  else if (nv.z < 30.0)
  {
    st = 2.0;
  }
  else if (nv.y < 55.0)
  {
    st = 3.0;
    if (diet >= 1 && preyId != 0xffffffffu)
    {
      let pb = creatureBase(preyId);
      let pp = creatures[pb + 0u];
      tx = pp.x;
      ty = pp.y;
      tv.z = f32(preyId);
      atomicMin(&simAtomics[PREY_OWNER_BASE + preyId], i + 1u);
      st = 4.0;
    }
    else
    {
      let ti = worldIndex(i32(round(pv.x)), i32(round(pv.y)), i32(p_u(2u)), i32(p_u(3u)));
      atomicMin(&simAtomics[FOOD_OWNER_BASE + ti], i + 1u);
      let global = pickGlobalTarget(1u << 0u, p_u(10u));
      if (global.x > 0.0)
      {
        tx = global.x;
        ty = global.y;
      }
    }
  }
  else if (nv.w < 18.0)
  {
    st = 5.0;
  }
  else if (mateId != 0xffffffffu && nv.y > 45.0 && nv.z > 40.0)
  {
    st = 6.0;
    let mb = creatureBase(mateId);
    let mp = creatures[mb + 0u];
    tx = mp.x;
    ty = mp.y;
    tv.z = f32(mateId);
  }
  else if (diet >= 1)
  {
    let global = pickGlobalTarget(huntsMask, p_u(10u));
    if (global.x > 0.0)
    {
      tx = global.x;
      ty = global.y;
      st = 7.0;
    }
  }

  if (st == 0.0)
  {
    let wanderX = hash3(f32(i), p_f(7u), 1.0) * 12.0 - 6.0;
    let wanderY = hash3(f32(i), p_f(7u), 2.0) * 12.0 - 6.0;
    tx = clamp(pv.x + wanderX, 1.0, f32(p_u(2u)) - 2.0);
    ty = clamp(pv.y + wanderY, 1.0, f32(p_u(3u)) - 2.0);
  }

  let nextTile = worldIndex(i32(round(tx)), i32(round(ty)), i32(p_u(2u)), i32(p_u(3u)));
  let bi = worldBiomeAt(nextTile);
  if ((bi <= 2u && !canSwim) || bi == 15u)
  {
    tx = pv.x;
    ty = pv.y;
  }

  tv.x = tx;
  tv.y = ty;
  tv.w = st;
  creatures[b + 3u] = tv;
}

@compute @workgroup_size(128)
fn resolveIntegrate(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let i = gid.x;
  let creatureCount = p_u(4u);
  if (i >= creatureCount) { return; }
  let b = creatureBase(i);
  var pv = creatures[b + 0u];
  var nv = creatures[b + 1u];
  var lv = creatures[b + 2u];
  var tv = creatures[b + 3u];
  var mv = creatures[b + 4u];
  let gv = creatures[b + 5u];
  var sv = creatures[b + 6u];
  if (mv.w < 0.5) { return; }

  let dt = p_f(0u);
  let load = max(0.3, mv.y * gv.y);
  let huntCost = select(0.0, 0.6, tv.w == 4.0);
  nv.y = max(0.0, nv.y - (0.9 * load + huntCost) * dt);
  nv.z = max(0.0, nv.z - 1.0 * load * dt);
  let movingCost = select(0.0, 0.9, abs(pv.z) + abs(pv.w) > 0.1);
  nv.w = max(0.0, nv.w - (0.6 * load + movingCost) * dt);
  lv.x = lv.x + dt / 24.0;
  lv.y = max(4.5, lv.y);

  let tile = worldIndex(i32(round(pv.x)), i32(round(pv.y)), i32(p_u(2u)), i32(p_u(3u)));
  let localT = worldTempAt(tile);
  let stress = max(0.0, abs(localT - gv.z) - gv.w);
  if (stress > 0.0) { nv.x = nv.x - stress * 14.0 * dt; }
  if (nv.y <= 0.0) { nv.x = nv.x - 6.0 * dt; }
  if (nv.z <= 0.0) { nv.x = nv.x - 7.0 * dt; }
  if (nv.y > 55.0 && nv.z > 55.0 && stress <= 0.001) { nv.x = min(100.0, nv.x + 4.0 * dt); }

  if (tv.w == 4.0 && tv.z >= 0.0)
  {
    let preyId = u32(tv.z + 0.5);
    if (preyId < creatureCount && atomicLoad(&simAtomics[PREY_OWNER_BASE + preyId]) == (i + 1u))
    {
      let pb = creatureBase(preyId);
      var pn = creatures[pb + 1u];
      let pp = creatures[pb + 0u];
      let d = distance(vec2<f32>(pv.x, pv.y), vec2<f32>(pp.x, pp.y));
      let chance = 0.10 + sv.y * 0.10;
      let roll = hash3(f32(i), f32(preyId), p_f(7u));
      if (d < mv.y * 0.6 + 0.5 && roll < chance)
      {
        pn.x = pn.x - (30.0 + mv.y * 15.0);
        creatures[pb + 1u] = pn;
        if (pn.x <= 0.0)
        {
          nv.y = min(100.0, nv.y + 50.0);
          nv.w = min(100.0, nv.w + 12.0);
        }
      }
    }
  }

  if (tv.w == 3.0)
  {
    if (atomicLoad(&simAtomics[FOOD_OWNER_BASE + tile]) == (i + 1u))
    {
      let cap = worldVegCapAt(tile);
      let veg = worldVegAt(tile);
      if (cap > 0.02 && veg > 0.04)
      {
        let bite = min(veg, 3.5 * dt);
        setWorldVeg(tile, veg - bite);
        nv.y = min(100.0, nv.y + bite * 26.0);
        atomicAdd(&simAtomics[3u], u32(bite * 1000.0));
      }
    }
  }

  var drinking = false;
  if (tv.w == 2.0)
  {
    let ix = i32(round(pv.x));
    let iy = i32(round(pv.y));
    for (var ddy = -1; ddy <= 1; ddy++)
    {
      for (var ddx = -1; ddx <= 1; ddx++)
      {
        let ti = worldIndex(ix + ddx, iy + ddy, i32(p_u(2u)), i32(p_u(3u)));
        if (worldBiomeAt(ti) <= 2u)
        {
          nv.z = min(100.0, nv.z + 60.0 * dt);
          drinking = true;
        }
      }
    }
  }

  var speed = gv.x;
  if (drinking) { speed = 0.0; }
  if (p_f(1u) < 0.28) { speed = speed * 0.6; }
  if (tv.w == 1.0) { speed = speed * 1.25; }
  if (tv.w == 4.0) { speed = speed * 1.15; }
  if (tv.w == 5.0)
  {
    nv.w = min(100.0, nv.w + 9.0 * dt);
    speed = 0.0;
  }

  let dx = tv.x - pv.x;
  let dy = tv.y - pv.y;
  let d = max(0.001, length(vec2<f32>(dx, dy)));
  let nx = pv.x + dx / d * speed * 2.2 * dt;
  let ny = pv.y + dy / d * speed * 2.2 * dt;
  pv.z = (nx - pv.x) / max(0.0005, dt);
  pv.w = (ny - pv.y) / max(0.0005, dt);
  pv.x = nx;
  pv.y = ny;
  sv.w = sv.w + length(vec2<f32>(pv.z, pv.w)) * dt * 10.0 + dt * 0.5;
  if (abs(pv.z) > 0.001) { creatures[b + 7u].x = select(-1.0, 1.0, pv.z > 0.0); }

  if (nv.x <= 0.0 || lv.x >= lv.y)
  {
    mv.w = 0.0;
  }

  if (nv.y < 5.0 || nv.z < 5.0) { atomicAdd(&simAtomics[4u], 1u); }

  if (mv.w > 0.5 && tv.w == 6.0 && nv.w > 80.0 && nv.y > 75.0 && hash3(f32(i), p_f(7u), 9.0) < 0.0012)
  {
    let bslot = atomicAdd(&simAtomics[2u], 1u);
    simLists[POOL_CAP * 2u + bslot] = i;
  }

  creatures[b + 0u] = pv;
  creatures[b + 1u] = nv;
  creatures[b + 2u] = lv;
  creatures[b + 4u] = mv;
  creatures[b + 6u] = sv;

  if (mv.w > 0.5)
  {
    let aslot = atomicAdd(&simAtomics[0u], 1u);
    simLists[aslot] = i;
  }
  else
  {
    let dslot = atomicAdd(&simAtomics[1u], 1u);
    simLists[POOL_CAP + dslot] = i;
    let ti = worldIndex(i32(round(pv.x)), i32(round(pv.y)), i32(p_u(2u)), i32(p_u(3u)));
    let cap = worldVegCapAt(ti);
    if (cap > 0.02)
    {
      setWorldVeg(ti, min(cap, worldVegAt(ti) + 0.15));
    }
  }
}

@compute @workgroup_size(128)
fn spawnFromBirthQueue(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let i = gid.x;
  let births = atomicLoad(&simAtomics[2u]);
  let deadCount = atomicLoad(&simAtomics[1u]);
  if (i >= births || i >= deadCount) { return; }
  let parent = simLists[POOL_CAP * 2u + i];
  let child = simLists[POOL_CAP + i];
  if (parent >= p_u(4u) || child >= p_u(4u)) { return; }

  let pb = creatureBase(parent);
  let cb = creatureBase(child);
  let pp = creatures[pb + 0u];
  let pl = creatures[pb + 2u];
  let pm = creatures[pb + 4u];
  let pg = creatures[pb + 5u];
  let ps = creatures[pb + 6u];

  let jx = hash3(f32(child), p_f(7u), 11.0) * 1.6 - 0.8;
  let jy = hash3(f32(child), p_f(7u), 12.0) * 1.6 - 0.8;
  creatures[cb + 0u] = vec4<f32>(pp.x + jx, pp.y + jy, 0.0, 0.0);
  creatures[cb + 1u] = vec4<f32>(100.0, 70.0, 70.0, 80.0);
  creatures[cb + 2u] = vec4<f32>(0.0, pl.y, 1.0, 0.0);
  creatures[cb + 3u] = vec4<f32>(pp.x, pp.y, -1.0, 0.0);
  creatures[cb + 4u] = vec4<f32>(pm.x, pm.y, pm.z, 1.0);
  creatures[cb + 5u] = pg;
  creatures[cb + 6u] = vec4<f32>(ps.x, ps.y, ps.z + 1.0, 0.0);
  creatures[cb + 7u] = vec4<f32>(1.0, 0.0, 0.0, 0.0);
}

@compute @workgroup_size(128)
fn growVegetation(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let tile = gid.x;
  let tileCount = p_u(2u) * p_u(3u);
  if (tile >= tileCount) { return; }
  let cap = worldVegCapAt(tile);
  let veg = worldVegAt(tile);
  if (cap > 0.02 && veg < cap)
  {
    setWorldVeg(tile, min(cap, veg + cap * 0.22 * p_f(0u) * p_f(11u) * (0.6 + worldMoistAt(tile))));
  }
}

@compute @workgroup_size(128)
fn composeRenderData(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let i = gid.x;
  let creatureCount = p_u(4u);
  if (i >= creatureCount) { return; }
  let b = creatureBase(i);
  let pv = creatures[b + 0u];
  let mv = creatures[b + 4u];
  let sp = u32(mv.x + 0.5);
  let col = speciesTables[${SP_KEYS.length}u + sp];
  let out = i * 8u;
  renderData[out + 0u] = pv.x;
  renderData[out + 1u] = pv.y;
  renderData[out + 2u] = max(0.7, mv.y);
  renderData[out + 3u] = 0.0;
  renderData[out + 4u] = col.x;
  renderData[out + 5u] = col.y;
  renderData[out + 6u] = col.z;
  renderData[out + 7u] = select(0.0, 0.95, mv.w > 0.5);
}
`;

function createStorageBuffer(device, size, usage)
{
  return device.createBuffer({ size, usage });
}

function roundUp(value, align)
{
  return Math.ceil(value / align) * align;
}

const GPU_SIM_BIND_GROUP_LAYOUT = [
  { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
  { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
  { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
  { binding: 3, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
  { binding: 4, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
  { binding: 5, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
  { binding: 6, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
  { binding: 7, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
  { binding: 8, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
];

function gEncodeState(c)
{
  const isAlive = c.dead ? 0 : 1;
  const sp = SPECIES_INDEX[c.sp] ?? 0;
  return {
    sp,
    size: c.genome.size,
    sense: c.genome.sense,
    alive: isAlive,
    speed: c.genome.speed,
    metab: c.genome.metab,
    temp: c.genome.temp,
    tol: c.genome.tol,
    litter: c.genome.litter,
    agg: c.genome.agg,
    gen: c.gen,
    dir: c.dir ?? 1,
  };
}

function buildSpeciesTableBuffer()
{
  const { table, colors } = buildGpuSpeciesTables();
  const packed = new Float32Array(SP_KEYS.length * 8);
  for (let i = 0; i < SP_KEYS.length; i++)
  {
    packed[i * 4 + 0] = table[i * 8 + 0];
    packed[i * 4 + 1] = table[i * 8 + 1];
    packed[i * 4 + 2] = table[i * 8 + 2];
    packed[i * 4 + 3] = table[i * 8 + 3];
    const colorBase = SP_KEYS.length * 4 + i * 4;
    packed[colorBase + 0] = colors[i * 4 + 0];
    packed[colorBase + 1] = colors[i * 4 + 1];
    packed[colorBase + 2] = colors[i * 4 + 2];
    packed[colorBase + 3] = colors[i * 4 + 3];
  }
  return packed;
}

function creatureSlot(c, fallback)
{
  if (typeof c?.gpuSlot === 'number' && c.gpuSlot >= 0)
  {
    return c.gpuSlot | 0;
  }
  return fallback | 0;
}

function writeCreatureToArray(data, base, c)
{
  const g = gEncodeState(c);
  data[base + 0] = c.x;
  data[base + 1] = c.y;
  data[base + 2] = c.vx;
  data[base + 3] = c.vy;
  data[base + 4] = c.hp;
  data[base + 5] = c.hunger;
  data[base + 6] = c.thirst;
  data[base + 7] = c.energy;
  data[base + 8] = c.age;
  data[base + 9] = c.genome.lifespan;
  data[base + 10] = c.mateCd;
  data[base + 11] = c.pregnant;
  data[base + 12] = c.tx;
  data[base + 13] = c.ty;
  data[base + 14] = -1;
  data[base + 15] = 0;
  data[base + 16] = g.sp;
  data[base + 17] = g.size;
  data[base + 18] = g.sense;
  data[base + 19] = g.alive;
  data[base + 20] = g.speed;
  data[base + 21] = g.metab;
  data[base + 22] = g.temp;
  data[base + 23] = g.tol;
  data[base + 24] = g.litter;
  data[base + 25] = g.agg;
  data[base + 26] = g.gen;
  data[base + 27] = c.walk || 0;
  data[base + 28] = g.dir;
}

export class GpuSimulationBackend
{
  constructor()
  {
    this.initialized = false;
    this.maxCreatures = GPU_SIM_MAX_CREATURES;
    this.tickCounter = 0;
    this.readbackEveryMs = 75;
    this.selectedReadbackEveryMs = 40;
    this.selectedReadbackPending = false;
    this.selectedReadbackAt = 0;
    this.cpuCreatureIndexById = new Map();
  }

  init()
  {
    if (!state.gpuDevice) return false;
    const dev = state.gpuDevice;
    const maxStorage = dev.limits?.maxStorageBuffersPerShaderStage ?? 8;
    if (maxStorage < 8)
    {
      this.initialized = false;
      state.gpuSimInitReason = 'storage-limit';
      return false;
    }
    try
    {
      const mod = dev.createShaderModule({ code: GPU_SIM_SHADER });
      const bindGroupLayout = dev.createBindGroupLayout({ entries: GPU_SIM_BIND_GROUP_LAYOUT });
      const layout = dev.createPipelineLayout({ bindGroupLayouts: [bindGroupLayout] });

      state.gpuSimPipelines = {
        clearCells: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'clearCells' } }),
        clearCounters: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'clearCounters' } }),
        binCreatures: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'binCreatures' } }),
        decideAndClaim: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'decideAndClaim' } }),
        resolveIntegrate: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'resolveIntegrate' } }),
        spawnFromBirthQueue: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'spawnFromBirthQueue' } }),
        growVegetation: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'growVegetation' } }),
        composeRenderData: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'composeRenderData' } }),
      };
      this.initialized = true;
      state.gpuSimInitReason = '';
      return true;
    }
    catch (err)
    {
      console.warn('GPU simulation init failed, using CPU simulation', err);
      this.initialized = false;
      state.gpuSimInitReason = 'shader-init';
      return false;
    }
  }

  ensureBuffers()
  {
    if (!this.initialized || !state.gpuDevice) return false;
    const dev = state.gpuDevice;
    const w = state.W;
    const h = state.H;
    const tiles = w * h;
    const cellsX = Math.ceil(w / CELL);
    const cellsY = Math.ceil(h / CELL);
    const cells = cellsX * cellsY;
    state.gpuSimCellCols = cellsX;
    state.gpuSimCellRows = cellsY;

    const creatureBytes = this.maxCreatures * CREATURE_STRIDE_VEC4 * 16;
    const worldBytes = tiles * WORLD_STRIDE * 4;
    const cellCountsBytes = cells * 4;
    const cellEntriesBytes = cells * CELL_CAP * 4;
    const simAtomicsBytes = (PREY_OWNER_BASE + this.maxCreatures + tiles) * 4;
    const simListsBytes = this.maxCreatures * 3 * 4;
    const speciesBytes = SP_KEYS.length * 8 * 4;
    const renderBytes = this.maxCreatures * 8 * 4;

    state.gpuWorldBuffers = {
      worldData: createStorageBuffer(dev, worldBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC),
    };

    state.gpuSimBuffers = {
      creatures: createStorageBuffer(dev, creatureBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC),
      cellCounts: createStorageBuffer(dev, cellCountsBytes, GPUBufferUsage.STORAGE),
      cellEntries: createStorageBuffer(dev, cellEntriesBytes, GPUBufferUsage.STORAGE),
      simAtomics: createStorageBuffer(dev, simAtomicsBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC),
      simLists: createStorageBuffer(dev, simListsBytes, GPUBufferUsage.STORAGE),
      speciesTables: createStorageBuffer(dev, speciesBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST),
      renderData: createStorageBuffer(dev, renderBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC),
      renderReadback: createStorageBuffer(dev, roundUp(renderBytes, 256), GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ),
      countersReadback: createStorageBuffer(dev, 256, GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ),
      selectedReadback: createStorageBuffer(dev, 256, GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ),
    };
    state.gpuCreatureBuffer = state.gpuSimBuffers.renderData;
    state.gpuSimParamBuffer = dev.createBuffer({
      size: roundUp(PARAM_FLOATS * 4, 16),
      usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });

    const speciesPacked = buildSpeciesTableBuffer();
    dev.queue.writeBuffer(state.gpuSimBuffers.speciesTables, 0, speciesPacked.buffer, speciesPacked.byteOffset, speciesPacked.byteLength);

    if (state.gpuPipeline && state.gpuUniformBuffer)
    {
      state.gpuBindGroup = dev.createBindGroup({
        layout: state.gpuPipeline.getBindGroupLayout(0),
        entries: [
          { binding: 0, resource: { buffer: state.gpuCreatureBuffer } },
          { binding: 1, resource: { buffer: state.gpuUniformBuffer } },
        ],
      });
    }

    const bgLayout = state.gpuSimPipelines.clearCells.getBindGroupLayout(0);
    state.gpuSimBindGroups = {
      main: dev.createBindGroup({
        layout: bgLayout,
        entries: [
          { binding: 0, resource: { buffer: state.gpuSimBuffers.creatures } },
          { binding: 1, resource: { buffer: state.gpuWorldBuffers.worldData } },
          { binding: 2, resource: { buffer: state.gpuSimBuffers.cellCounts } },
          { binding: 3, resource: { buffer: state.gpuSimBuffers.cellEntries } },
          { binding: 4, resource: { buffer: state.gpuSimBuffers.simAtomics } },
          { binding: 5, resource: { buffer: state.gpuSimBuffers.simLists } },
          { binding: 6, resource: { buffer: state.gpuSimBuffers.speciesTables } },
          { binding: 7, resource: { buffer: state.gpuSimBuffers.renderData } },
          { binding: 8, resource: { buffer: state.gpuSimParamBuffer } },
        ],
      }),
    };
    return true;
  }

  uploadWorld()
  {
    if (!state.gpuWorldBuffers || !state.gpuDevice) return;
    const tiles = state.W * state.H;
    const packed = new Float32Array(tiles * WORLD_STRIDE);
    for (let i = 0; i < tiles; i++)
    {
      const base = i * WORLD_STRIDE;
      packed[base + 0] = state.temp[i];
      packed[base + 1] = state.moist[i];
      packed[base + 2] = state.veg[i];
      packed[base + 3] = state.vegCap[i];
      packed[base + 4] = state.biome[i];
    }
    state.gpuDevice.queue.writeBuffer(state.gpuWorldBuffers.worldData, 0, packed.buffer, packed.byteOffset, packed.byteLength);
  }

  uploadCreaturesFromCpu()
  {
    if (!state.gpuSimBuffers || !state.gpuDevice) return;
    const data = new Float32Array(this.maxCreatures * CREATURE_STRIDE_FLOATS);
    const cpu = state.creatures;
    let poolCount = 0;
    this.cpuCreatureIndexById.clear();
    for (let i = 0; i < cpu.length; i++)
    {
      const c = cpu[i];
      if (!c) continue;
      const slot = creatureSlot(c, i);
      if (slot < 0 || slot >= this.maxCreatures)
      {
        continue;
      }
      c.gpuSlot = slot;
      const base = slot * CREATURE_STRIDE_FLOATS;
      poolCount = Math.max(poolCount, slot + 1);
      this.cpuCreatureIndexById.set(c.id, slot);
      writeCreatureToArray(data, base, c);
      c.gpuNeedsUpload = false;
    }
    state.gpuDevice.queue.writeBuffer(state.gpuSimBuffers.creatures, 0, data.buffer, data.byteOffset, data.byteLength);
    state.gpuRenderCreatureCount = poolCount;
    state.gpuSimDirtyFromCpu = false;
  }

  uploadDirtyCreaturesFromCpu()
  {
    if (!state.gpuSimBuffers || !state.gpuDevice) return;
    let poolCount = state.gpuRenderCreatureCount;
    for (let i = 0; i < state.creatures.length; i++)
    {
      const c = state.creatures[i];
      if (!c) continue;
      if (!c.gpuNeedsUpload && typeof c.gpuSlot === 'number') continue;
      const slot = creatureSlot(c, i);
      if (slot < 0 || slot >= this.maxCreatures) continue;
      c.gpuSlot = slot;
      poolCount = Math.max(poolCount, slot + 1);
      const packed = new Float32Array(CREATURE_STRIDE_FLOATS);
      writeCreatureToArray(packed, 0, c);
      state.gpuDevice.queue.writeBuffer(
        state.gpuSimBuffers.creatures,
        slot * CREATURE_STRIDE_FLOATS * 4,
        packed.buffer,
        packed.byteOffset,
        packed.byteLength,
      );
      c.gpuNeedsUpload = false;
    }
    state.gpuRenderCreatureCount = poolCount;
    state.gpuSimDirtyFromCpu = false;
  }

  setupForCurrentWorld()
  {
    if (!this.initialized) return false;
    if (!this.ensureBuffers()) return false;
    this.uploadWorld();
    this.uploadCreaturesFromCpu();
    state.gpuSimEnabled = true;
    state.simBackend = 'gpu';
    return true;
  }

  writeParams(dt)
  {
    const arr = new Float32Array(PARAM_FLOATS);
    arr[0] = dt;
    arr[1] = state.lightLevel;
    arr[2] = state.W;
    arr[3] = state.H;
    arr[4] = state.gpuRenderCreatureCount;
    arr[5] = state.gpuSimCellCols;
    arr[6] = state.gpuSimCellRows;
    arr[7] = this.tickCounter;
    arr[8] = CELL;
    arr[9] = CELL_CAP;
    arr[10] = SP_KEYS.length;
    arr[11] = state.growStride;
    arr[12] = state.growRow;
    state.gpuDevice.queue.writeBuffer(state.gpuSimParamBuffer, 0, arr.buffer, arr.byteOffset, arr.byteLength);
  }

  dispatch(pass, pipeline, workgroups)
  {
    pass.setPipeline(pipeline);
    pass.setBindGroup(0, state.gpuSimBindGroups.main);
    pass.dispatchWorkgroups(workgroups);
  }

  step(dt)
  {
    if (!state.gpuSimEnabled || !state.gpuDevice || !state.gpuSimPipelines || !state.gpuSimBindGroups?.main) return false;
    if (state.gpuSimDirtyFromCpu) this.uploadDirtyCreaturesFromCpu();
    this.tickCounter++;
    this.writeParams(dt);
    const dev = state.gpuDevice;
    const creatureCount = Math.max(1, state.gpuRenderCreatureCount);
    const tileCount = Math.max(1, state.W * state.H);
    const cellCount = Math.max(1, state.gpuSimCellCols * state.gpuSimCellRows);
    const speciesTriples = Math.max(1, SPECIES_SUM_LEN);

    const encoder = dev.createCommandEncoder();
    const pass = encoder.beginComputePass();
    const clearCount = Math.max(cellCount, tileCount, creatureCount, speciesTriples);
    this.dispatch(pass, state.gpuSimPipelines.clearCells, Math.ceil(clearCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.clearCounters, Math.ceil(Math.max(COUNTERS_LEN, speciesTriples) / 64));
    this.dispatch(pass, state.gpuSimPipelines.binCreatures, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.decideAndClaim, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.resolveIntegrate, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.spawnFromBirthQueue, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.growVegetation, Math.ceil(tileCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.composeRenderData, Math.ceil(creatureCount / 128));
    pass.end();

    encoder.copyBufferToBuffer(state.gpuSimBuffers.simAtomics, 0, state.gpuSimBuffers.countersReadback, 0, 64);
    const now = performance.now();
    if (!state.gpuSimReadbackPending && now - state.gpuSimLastReadbackAt > this.readbackEveryMs)
    {
      const bytes = state.gpuRenderCreatureCount * CREATURE_STRIDE_FLOATS * 4;
      const copyBytes = roundUp(bytes, 256);
      encoder.copyBufferToBuffer(state.gpuSimBuffers.creatures, 0, state.gpuSimBuffers.renderReadback, 0, copyBytes);
      state.gpuSimReadbackPending = true;
      state.gpuSimLastReadbackAt = now;
    }
    const selectedSlot = state.selected?.gpuSlot;
    if (
      state.selected
      && typeof selectedSlot === 'number'
      && selectedSlot >= 0
      && selectedSlot < state.gpuRenderCreatureCount
      && !this.selectedReadbackPending
      && now - this.selectedReadbackAt > this.selectedReadbackEveryMs
    )
    {
      encoder.copyBufferToBuffer(
        state.gpuSimBuffers.creatures,
        selectedSlot * CREATURE_STRIDE_FLOATS * 4,
        state.gpuSimBuffers.selectedReadback,
        0,
        CREATURE_STRIDE_FLOATS * 4,
      );
      this.selectedReadbackPending = true;
      this.selectedReadbackAt = now;
    }
    dev.queue.submit([encoder.finish()]);

    this.consumeCountersReadback();
    this.consumeCreatureReadback();
    this.consumeSelectedReadback();
    if ((this.tickCounter % 18) === 0) this.syncWorldBackToCpu();
    return true;
  }

  consumeCountersReadback()
  {
    const rb = state.gpuSimBuffers?.countersReadback;
    if (!rb || rb.mapState !== 'unmapped') return;
    rb.mapAsync(GPUMapMode.READ).then(() =>
    {
      const arr = new Uint32Array(rb.getMappedRange());
      state.gpuTelemetry.aliveCount = arr[0] || 0;
      state.gpuTelemetry.deadCount = arr[1] || 0;
      state.gpuTelemetry.birthCount = arr[2] || 0;
      state.gpuTelemetry.herbivoreIntake = (arr[3] || 0) / 1000;
      state.gpuTelemetry.starvationRisk = arr[4] || 0;
      rb.unmap();
    }).catch(() =>
    {
      if (rb.mapState === 'mapped') rb.unmap();
    });
  }

  consumeCreatureReadback()
  {
    if (!state.gpuSimReadbackPending) return;
    const rb = state.gpuSimBuffers?.renderReadback;
    if (!rb || rb.mapState !== 'unmapped') return;
    rb.mapAsync(GPUMapMode.READ).then(() =>
    {
      const floatData = new Float32Array(rb.getMappedRange());
      const alive = [];
      const slotMap = new Map();
      for (const c of state.creatures)
      {
        if (!c) continue;
        if (typeof c.gpuSlot === 'number' && c.gpuSlot >= 0)
        {
          slotMap.set(c.gpuSlot, c);
        }
      }
      for (let i = 0; i < Math.min(state.gpuRenderCreatureCount, this.maxCreatures); i++)
      {
        const base = i * CREATURE_STRIDE_FLOATS;
        const spIdx = Math.max(0, Math.min(SP_KEYS.length - 1, Math.round(floatData[base + 16])));
        const aliveFlag = floatData[base + 19] > 0.5;
        let c = slotMap.get(i);
        if (!c)
        {
          if (!aliveFlag) continue;
          const sp = SP_KEYS[spIdx];
          c = {
            id: 1000000 + i,
            sp,
            genome: { ...SPECIES[sp].base },
            parentIds: [],
            offspringIds: [],
            gpuSlot: i,
            gpuNeedsUpload: false,
          };
          state.creatures.push(c);
        }
        c.x = floatData[base + 0];
        c.y = floatData[base + 1];
        c.vx = floatData[base + 2];
        c.vy = floatData[base + 3];
        c.hp = floatData[base + 4];
        c.hunger = floatData[base + 5];
        c.thirst = floatData[base + 6];
        c.energy = floatData[base + 7];
        c.age = floatData[base + 8];
        c.mateCd = floatData[base + 10];
        c.pregnant = floatData[base + 11];
        c.tx = floatData[base + 12];
        c.ty = floatData[base + 13];
        c.sp = SP_KEYS[spIdx];
        c.state = gpuBehaviorToState(floatData[base + 15]);
        c.dead = !aliveFlag;
        c.gpuSlot = i;
        c.gpuNeedsUpload = false;
        c.walk = floatData[base + 27];
        c.dir = floatData[base + 28] >= 0 ? 1 : -1;
        if (c.dead && !c.cause)
        {
          c.cause = c.age >= c.genome.lifespan ? 'old age' : 'exhaustion';
        }
        if (aliveFlag) alive.push(c);
      }
      state.gpuSimMirror = alive;
      rb.unmap();
      state.gpuSimReadbackPending = false;
      state.vegDirty = true;
    }).catch(() =>
    {
      if (rb.mapState === 'mapped') rb.unmap();
      state.gpuSimReadbackPending = false;
    });
  }

  consumeSelectedReadback()
  {
    if (!this.selectedReadbackPending) return;
    const rb = state.gpuSimBuffers?.selectedReadback;
    if (!rb || rb.mapState !== 'unmapped') return;
    rb.mapAsync(GPUMapMode.READ).then(() =>
    {
      const c = state.selected;
      if (!c)
      {
        rb.unmap();
        this.selectedReadbackPending = false;
        return;
      }
      const floatData = new Float32Array(rb.getMappedRange());
      const spIdx = Math.max(0, Math.min(SP_KEYS.length - 1, Math.round(floatData[16])));
      const aliveFlag = floatData[19] > 0.5;
      c.x = floatData[0];
      c.y = floatData[1];
      c.vx = floatData[2];
      c.vy = floatData[3];
      c.hp = floatData[4];
      c.hunger = floatData[5];
      c.thirst = floatData[6];
      c.energy = floatData[7];
      c.age = floatData[8];
      c.mateCd = floatData[10];
      c.pregnant = floatData[11];
      c.tx = floatData[12];
      c.ty = floatData[13];
      c.sp = SP_KEYS[spIdx];
      c.state = gpuBehaviorToState(floatData[15]);
      c.dead = !aliveFlag;
      c.walk = floatData[27];
      c.dir = floatData[28] >= 0 ? 1 : -1;
      if (c.dead && !c.cause)
      {
        c.cause = c.age >= c.genome.lifespan ? 'old age' : 'exhaustion';
      }
      rb.unmap();
      this.selectedReadbackPending = false;
    }).catch(() =>
    {
      if (rb.mapState === 'mapped') rb.unmap();
      this.selectedReadbackPending = false;
    });
  }

  syncWorldBackToCpu()
  {
    if (!state.gpuWorldBuffers || !state.gpuDevice) return;
    const bytes = state.W * state.H * WORLD_STRIDE * 4;
    if (!state.gpuSimReadbackBuffer || state.gpuSimReadbackBuffer.size !== roundUp(bytes, 256))
    {
      state.gpuSimReadbackBuffer = state.gpuDevice.createBuffer({
        size: roundUp(bytes, 256),
        usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
      });
    }
    const enc = state.gpuDevice.createCommandEncoder();
    enc.copyBufferToBuffer(state.gpuWorldBuffers.worldData, 0, state.gpuSimReadbackBuffer, 0, bytes);
    state.gpuDevice.queue.submit([enc.finish()]);
    state.gpuSimReadbackBuffer.mapAsync(GPUMapMode.READ).then(() =>
    {
      const packed = new Float32Array(state.gpuSimReadbackBuffer.getMappedRange());
      let stock = 0;
      for (let i = 0; i < state.veg.length; i++)
      {
        state.veg[i] = packed[i * WORLD_STRIDE + 2];
        stock += state.veg[i];
      }
      state.gpuTelemetry.vegetationStock = stock;
      state.gpuSimReadbackBuffer.unmap();
    }).catch(() =>
    {
      if (state.gpuSimReadbackBuffer.mapState === 'mapped') state.gpuSimReadbackBuffer.unmap();
    });
  }
}

export const gpuSimulationBackend = new GpuSimulationBackend();
