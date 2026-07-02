import { state, CELL, GPU_SIM_MAX_CREATURES } from '../state.js';
import { SP_KEYS, SPECIES, SPECIES_INDEX, buildGpuSpeciesTables } from '../data.js';

const CELL_CAP = 64;
const CREATURE_STRIDE_VEC4 = 8;
const PARAM_FLOATS = 16;

const GPU_SIM_SHADER = `
const CREATURE_STRIDE: u32 = ${CREATURE_STRIDE_VEC4}u;
const CELL_CAP: u32 = ${CELL_CAP}u;

@group(0) @binding(0) var<storage, read_write> creatures: array<vec4<f32>>;
@group(0) @binding(1) var<storage, read_write> worldTemp: array<f32>;
@group(0) @binding(2) var<storage, read_write> worldMoist: array<f32>;
@group(0) @binding(3) var<storage, read_write> worldVeg: array<f32>;
@group(0) @binding(4) var<storage, read> worldVegCap: array<f32>;
@group(0) @binding(5) var<storage, read> worldBiome: array<u32>;
@group(0) @binding(6) var<storage, read_write> cellCounts: array<atomic<u32>>;
@group(0) @binding(7) var<storage, read_write> cellEntries: array<u32>;
@group(0) @binding(8) var<storage, read_write> preyOwner: array<atomic<u32>>;
@group(0) @binding(9) var<storage, read_write> foodOwner: array<atomic<u32>>;
@group(0) @binding(10) var<storage, read_write> aliveIndices: array<u32>;
@group(0) @binding(11) var<storage, read_write> deadIndices: array<u32>;
@group(0) @binding(12) var<storage, read_write> birthParents: array<u32>;
@group(0) @binding(13) var<storage, read_write> counters: array<atomic<u32>>;
@group(0) @binding(14) var<storage, read_write> speciesSums: array<atomic<u32>>;
@group(0) @binding(15) var<storage, read> speciesMeta: array<vec4<f32>>;
@group(0) @binding(16) var<storage, read> speciesColor: array<vec4<f32>>;
@group(0) @binding(17) var<storage, read_write> renderData: array<f32>;
@group(0) @binding(18) var<uniform> params: array<f32, ${PARAM_FLOATS}>;

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
    let si = sp * 3u;
    let cnt = atomicLoad(&speciesSums[si + 2u]);
    if (cnt > 0u)
    {
      let sx = f32(atomicLoad(&speciesSums[si + 0u])) / 1024.0;
      let sy = f32(atomicLoad(&speciesSums[si + 1u])) / 1024.0;
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
    atomicStore(&foodOwner[gid.x], 0xffffffffu);
  }
  if (gid.x < p_u(4u))
  {
    atomicStore(&preyOwner[gid.x], 0xffffffffu);
  }
}

@compute @workgroup_size(64)
fn clearCounters(@builtin(global_invocation_id) gid: vec3<u32>)
{
  if (gid.x < 8u)
  {
    atomicStore(&counters[gid.x], 0u);
  }
  let speciesLen = p_u(10u) * 3u;
  if (gid.x < speciesLen)
  {
    atomicStore(&speciesSums[gid.x], 0u);
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
    let dslot = atomicAdd(&counters[1u], 1u);
    deadIndices[dslot] = i;
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
  let si = sp * 3u;
  atomicAdd(&speciesSums[si + 0u], u32(pv.x * 1024.0));
  atomicAdd(&speciesSums[si + 1u], u32(pv.y * 1024.0));
  atomicAdd(&speciesSums[si + 2u], 1u);
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
  let gv = creatures[b + 5u];
  if (mv.w < 0.5) { return; }

  let sp = u32(mv.x + 0.5);
  let speciesInfo = speciesMeta[sp];
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

  var st = 0.0; // wander
  var tx = tv.x;
  var ty = tv.y;
  tv.z = -1.0;

  if (threatId != 0xffffffffu)
  {
    st = 1.0; // flee
    let ob = creatureBase(threatId);
    let op = creatures[ob + 0u];
    let dir = normalize(vec2<f32>(pv.x - op.x, pv.y - op.y) + vec2<f32>(0.0001, 0.0001));
    tx = pv.x + dir.x * 6.0;
    ty = pv.y + dir.y * 6.0;
    tv.z = f32(threatId);
  }
  else if (nv.z < 30.0)
  {
    st = 2.0; // thirst
  }
  else if (nv.y < 55.0)
  {
    st = 3.0; // forage/hunt
    if (diet >= 1 && preyId != 0xffffffffu)
    {
      let pb = creatureBase(preyId);
      let pp = creatures[pb + 0u];
      tx = pp.x;
      ty = pp.y;
      tv.z = f32(preyId);
      atomicMin(&preyOwner[preyId], i + 1u);
      st = 4.0; // hunt
    }
    else
    {
      let ti = worldIndex(i32(round(pv.x)), i32(round(pv.y)), i32(p_u(2u)), i32(p_u(3u)));
      atomicMin(&foodOwner[ti], i + 1u);
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
    st = 5.0; // rest
  }
  else if (mateId != 0xffffffffu && nv.y > 45.0 && nv.z > 40.0)
  {
    st = 6.0; // mate
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
      st = 7.0; // hunt search
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
  let bi = worldBiome[nextTile];
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
  let localT = worldTemp[tile];
  let stress = max(0.0, abs(localT - gv.z) - gv.w);
  if (stress > 0.0) { nv.x = nv.x - stress * 14.0 * dt; }
  if (nv.y <= 0.0) { nv.x = nv.x - 6.0 * dt; }
  if (nv.z <= 0.0) { nv.x = nv.x - 7.0 * dt; }
  if (nv.y > 55.0 && nv.z > 55.0 && stress <= 0.001) { nv.x = min(100.0, nv.x + 4.0 * dt); }

  if (tv.w == 4.0 && tv.z >= 0.0)
  {
    let preyId = u32(tv.z + 0.5);
    if (preyId < creatureCount && atomicLoad(&preyOwner[preyId]) == (i + 1u))
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
    if (atomicLoad(&foodOwner[tile]) == (i + 1u))
    {
      let cap = worldVegCap[tile];
      if (cap > 0.02 && worldVeg[tile] > 0.04)
      {
        let bite = min(worldVeg[tile], 3.5 * dt);
        worldVeg[tile] = worldVeg[tile] - bite;
        nv.y = min(100.0, nv.y + bite * 26.0);
        atomicAdd(&counters[3u], u32(bite * 1000.0));
      }
    }
  }

  var speed = gv.x;
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

  if (nv.y < 5.0 || nv.z < 5.0) { atomicAdd(&counters[4u], 1u); }

  if (mv.w > 0.5 && tv.w == 6.0 && nv.w > 80.0 && nv.y > 75.0 && hash3(f32(i), p_f(7u), 9.0) < 0.0012)
  {
    let bslot = atomicAdd(&counters[2u], 1u);
    birthParents[bslot] = i;
  }

  creatures[b + 0u] = pv;
  creatures[b + 1u] = nv;
  creatures[b + 2u] = lv;
  creatures[b + 4u] = mv;
  creatures[b + 6u] = sv;

  if (mv.w > 0.5)
  {
    let aslot = atomicAdd(&counters[0u], 1u);
    aliveIndices[aslot] = i;
  }
  else
  {
    let dslot = atomicAdd(&counters[1u], 1u);
    deadIndices[dslot] = i;
    let ti = worldIndex(i32(round(pv.x)), i32(round(pv.y)), i32(p_u(2u)), i32(p_u(3u)));
    let cap = worldVegCap[ti];
    if (cap > 0.02) { worldVeg[ti] = min(cap, worldVeg[ti] + 0.15); }
  }
}

@compute @workgroup_size(128)
fn spawnFromBirthQueue(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let i = gid.x;
  let births = atomicLoad(&counters[2u]);
  let deadCount = atomicLoad(&counters[1u]);
  if (i >= births || i >= deadCount) { return; }
  let parent = birthParents[i];
  let child = deadIndices[i];
  if (parent >= p_u(4u) || child >= p_u(4u)) { return; }

  let pb = creatureBase(parent);
  let cb = creatureBase(child);
  let pp = creatures[pb + 0u];
  let pn = creatures[pb + 1u];
  let pl = creatures[pb + 2u];
  let pt = creatures[pb + 3u];
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
  let cap = worldVegCap[tile];
  if (cap > 0.02 && worldVeg[tile] < cap)
  {
    worldVeg[tile] = min(cap, worldVeg[tile] + cap * 0.22 * p_f(0u) * p_f(11u) * (0.6 + worldMoist[tile]));
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
  let col = speciesColor[sp];
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
  return device.createBuffer({
    size,
    usage,
  });
}

function roundUp(value, align)
{
  return Math.ceil(value / align) * align;
}

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

export class GpuSimulationBackend
{
  constructor()
  {
    this.initialized = false;
    this.maxCreatures = GPU_SIM_MAX_CREATURES;
    this.tickCounter = 0;
    this.readbackEveryMs = 220;
    this.cpuCreatureIndexById = new Map();
  }

  init()
  {
    if (!state.gpuDevice) return false;
    const dev = state.gpuDevice;
    const maxStorage = dev.limits?.maxStorageBuffersPerShaderStage ?? 0;
    if (maxStorage < 19)
    {
      this.initialized = false;
      return false;
    }
    try
    {
      const mod = dev.createShaderModule({ code: GPU_SIM_SHADER });
      const bindGroupLayout = dev.createBindGroupLayout({
      entries: [
        { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 3, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 4, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
        { binding: 5, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
        { binding: 6, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 7, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 8, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 9, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 10, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 11, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 12, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 13, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 14, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 15, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
        { binding: 16, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
        { binding: 17, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
        { binding: 18, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
      ],
    });
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
      return true;
    }
    catch (err)
    {
      this.initialized = false;
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

    const creatureVec4Count = this.maxCreatures * CREATURE_STRIDE_VEC4;
    const creatureBytes = creatureVec4Count * 16;
    const uintCreatureBytes = this.maxCreatures * 4;
    const cellCountsBytes = cells * 4;
    const cellEntriesBytes = cells * CELL_CAP * 4;
    const tilesBytes = tiles * 4;
    const speciesTriplesBytes = SP_KEYS.length * 3 * 4;
    const renderBytes = this.maxCreatures * 8 * 4;
    const countersBytes = 64;

    state.gpuWorldBuffers = {
      temp: createStorageBuffer(dev, tilesBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST),
      moist: createStorageBuffer(dev, tilesBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST),
      veg: createStorageBuffer(dev, tilesBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC),
      vegCap: createStorageBuffer(dev, tilesBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST),
      biome: createStorageBuffer(dev, tilesBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST),
    };

    state.gpuSimBuffers = {
      creatures: createStorageBuffer(dev, creatureBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC),
      cellCounts: createStorageBuffer(dev, cellCountsBytes, GPUBufferUsage.STORAGE),
      cellEntries: createStorageBuffer(dev, cellEntriesBytes, GPUBufferUsage.STORAGE),
      preyOwner: createStorageBuffer(dev, uintCreatureBytes, GPUBufferUsage.STORAGE),
      foodOwner: createStorageBuffer(dev, tilesBytes, GPUBufferUsage.STORAGE),
      aliveIndices: createStorageBuffer(dev, uintCreatureBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC),
      deadIndices: createStorageBuffer(dev, uintCreatureBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC),
      birthParents: createStorageBuffer(dev, uintCreatureBytes, GPUBufferUsage.STORAGE),
      counters: createStorageBuffer(dev, countersBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC),
      speciesSums: createStorageBuffer(dev, speciesTriplesBytes, GPUBufferUsage.STORAGE),
      renderData: createStorageBuffer(dev, renderBytes, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC),
      renderReadback: createStorageBuffer(dev, roundUp(renderBytes, 256), GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ),
      countersReadback: createStorageBuffer(dev, 256, GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ),
    };
    state.gpuCreatureBuffer = state.gpuSimBuffers.renderData;
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
    state.gpuRenderCreatureCount = 0;
    state.gpuSimParamBuffer = dev.createBuffer({
      size: roundUp(PARAM_FLOATS * 4, 16),
      usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });

    const { table, colors } = buildGpuSpeciesTables();
    state.gpuSpeciesTable = createStorageBuffer(dev, table.byteLength, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST);
    state.gpuSpeciesColorTable = createStorageBuffer(dev, colors.byteLength, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST);
    dev.queue.writeBuffer(state.gpuSpeciesTable, 0, table.buffer, table.byteOffset, table.byteLength);
    dev.queue.writeBuffer(state.gpuSpeciesColorTable, 0, colors.buffer, colors.byteOffset, colors.byteLength);

    const bgLayout = state.gpuSimPipelines.clearCells.getBindGroupLayout(0);
    state.gpuSimBindGroups = {
      main: dev.createBindGroup({
        layout: bgLayout,
        entries: [
          { binding: 0, resource: { buffer: state.gpuSimBuffers.creatures } },
          { binding: 1, resource: { buffer: state.gpuWorldBuffers.temp } },
          { binding: 2, resource: { buffer: state.gpuWorldBuffers.moist } },
          { binding: 3, resource: { buffer: state.gpuWorldBuffers.veg } },
          { binding: 4, resource: { buffer: state.gpuWorldBuffers.vegCap } },
          { binding: 5, resource: { buffer: state.gpuWorldBuffers.biome } },
          { binding: 6, resource: { buffer: state.gpuSimBuffers.cellCounts } },
          { binding: 7, resource: { buffer: state.gpuSimBuffers.cellEntries } },
          { binding: 8, resource: { buffer: state.gpuSimBuffers.preyOwner } },
          { binding: 9, resource: { buffer: state.gpuSimBuffers.foodOwner } },
          { binding: 10, resource: { buffer: state.gpuSimBuffers.aliveIndices } },
          { binding: 11, resource: { buffer: state.gpuSimBuffers.deadIndices } },
          { binding: 12, resource: { buffer: state.gpuSimBuffers.birthParents } },
          { binding: 13, resource: { buffer: state.gpuSimBuffers.counters } },
          { binding: 14, resource: { buffer: state.gpuSimBuffers.speciesSums } },
          { binding: 15, resource: { buffer: state.gpuSpeciesTable } },
          { binding: 16, resource: { buffer: state.gpuSpeciesColorTable } },
          { binding: 17, resource: { buffer: state.gpuSimBuffers.renderData } },
          { binding: 18, resource: { buffer: state.gpuSimParamBuffer } },
        ],
      }),
    };
    return true;
  }

  uploadWorld()
  {
    if (!state.gpuWorldBuffers || !state.gpuDevice) return;
    const dev = state.gpuDevice;
    const biomeU32 = new Uint32Array(state.biome.length);
    for (let i = 0; i < state.biome.length; i++) biomeU32[i] = state.biome[i];
    dev.queue.writeBuffer(state.gpuWorldBuffers.temp, 0, state.temp.buffer, state.temp.byteOffset, state.temp.byteLength);
    dev.queue.writeBuffer(state.gpuWorldBuffers.moist, 0, state.moist.buffer, state.moist.byteOffset, state.moist.byteLength);
    dev.queue.writeBuffer(state.gpuWorldBuffers.veg, 0, state.veg.buffer, state.veg.byteOffset, state.veg.byteLength);
    dev.queue.writeBuffer(state.gpuWorldBuffers.vegCap, 0, state.vegCap.buffer, state.vegCap.byteOffset, state.vegCap.byteLength);
    dev.queue.writeBuffer(state.gpuWorldBuffers.biome, 0, biomeU32.buffer, biomeU32.byteOffset, biomeU32.byteLength);
  }

  uploadCreaturesFromCpu()
  {
    if (!state.gpuSimBuffers || !state.gpuDevice) return;
    const data = new Float32Array(this.maxCreatures * CREATURE_STRIDE_VEC4 * 4);
    const cpu = state.creatures;
    this.cpuCreatureIndexById.clear();
    for (let i = 0; i < this.maxCreatures; i++)
    {
      const c = cpu[i];
      const base = i * CREATURE_STRIDE_VEC4 * 4;
      if (!c)
      {
        data[base + 16 + 3] = 0;
        continue;
      }
      this.cpuCreatureIndexById.set(c.id, i);
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
    state.gpuDevice.queue.writeBuffer(state.gpuSimBuffers.creatures, 0, data.buffer, data.byteOffset, data.byteLength);
    state.gpuRenderCreatureCount = Math.min(cpu.length, this.maxCreatures);
    state.gpuSimDirtyFromCpu = false;
  }

  setupForCurrentWorld()
  {
    if (!this.initialized) return false;
    this.ensureBuffers();
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
    if (!state.gpuSimEnabled || !state.gpuDevice || !state.gpuSimPipelines) return false;
    if (state.gpuSimDirtyFromCpu) this.uploadCreaturesFromCpu();
    this.tickCounter++;
    this.writeParams(dt);
    const dev = state.gpuDevice;
    const creatureCount = Math.max(1, state.gpuRenderCreatureCount);
    const tileCount = Math.max(1, state.W * state.H);
    const cellCount = Math.max(1, state.gpuSimCellCols * state.gpuSimCellRows);
    const speciesTriples = Math.max(1, SP_KEYS.length * 3);
    const clearCount = Math.max(cellCount, tileCount, creatureCount, speciesTriples);

    const encoder = dev.createCommandEncoder();
    const pass = encoder.beginComputePass();
    this.dispatch(pass, state.gpuSimPipelines.clearCells, Math.ceil(clearCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.clearCounters, Math.ceil(Math.max(8, speciesTriples) / 64));
    this.dispatch(pass, state.gpuSimPipelines.binCreatures, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.decideAndClaim, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.resolveIntegrate, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.spawnFromBirthQueue, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.growVegetation, Math.ceil(tileCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.composeRenderData, Math.ceil(creatureCount / 128));
    pass.end();

    encoder.copyBufferToBuffer(state.gpuSimBuffers.counters, 0, state.gpuSimBuffers.countersReadback, 0, 64);
    const now = performance.now();
    if (!state.gpuSimReadbackPending && now - state.gpuSimLastReadbackAt > this.readbackEveryMs)
    {
      const bytes = state.gpuRenderCreatureCount * CREATURE_STRIDE_VEC4 * 16;
      const copyBytes = roundUp(bytes, 256);
      encoder.copyBufferToBuffer(state.gpuSimBuffers.creatures, 0, state.gpuSimBuffers.renderReadback, 0, copyBytes);
      state.gpuSimReadbackPending = true;
      state.gpuSimLastReadbackAt = now;
    }
    dev.queue.submit([encoder.finish()]);

    this.consumeCountersReadback();
    this.consumeCreatureReadback();
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
      state.gpuRenderCreatureCount = Math.max(state.gpuRenderCreatureCount, state.gpuTelemetry.aliveCount);
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
      state.gpuTelemetry.vegetationStock = 0;
      for (let i = 0; i < Math.min(state.gpuRenderCreatureCount, this.maxCreatures); i++)
      {
        const base = i * CREATURE_STRIDE_VEC4 * 4;
        const spIdx = Math.max(0, Math.min(SP_KEYS.length - 1, Math.round(floatData[base + 16])));
        const aliveFlag = floatData[base + 19] > 0.5;
        let c = state.creatures[i];
        if (!c)
        {
          const sp = SP_KEYS[spIdx];
          c = {
            id: 1000000 + i,
            sp,
            genome: { ...SPECIES[sp].base },
            parentIds: [],
            offspringIds: [],
          };
          state.creatures[i] = c;
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
        c.state = 'gpu';
        c.dead = !aliveFlag;
        c.walk = floatData[base + 27];
        c.dir = floatData[base + 28] >= 0 ? 1 : -1;
        if (aliveFlag) alive.push(c);
      }
      state.creatures = state.creatures.slice(0, state.gpuRenderCreatureCount);
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

  syncWorldBackToCpu()
  {
    if (!state.gpuWorldBuffers || !state.gpuDevice) return;
    const bytes = state.veg.byteLength;
    if (!state.gpuSimReadbackBuffer || state.gpuSimReadbackBuffer.size !== roundUp(bytes, 256))
    {
      state.gpuSimReadbackBuffer = state.gpuDevice.createBuffer({
        size: roundUp(bytes, 256),
        usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
      });
    }
    const enc = state.gpuDevice.createCommandEncoder();
    enc.copyBufferToBuffer(state.gpuWorldBuffers.veg, 0, state.gpuSimReadbackBuffer, 0, bytes);
    state.gpuDevice.queue.submit([enc.finish()]);
    state.gpuSimReadbackBuffer.mapAsync(GPUMapMode.READ).then(() =>
    {
      const v = new Float32Array(state.gpuSimReadbackBuffer.getMappedRange());
      state.veg.set(v.subarray(0, state.veg.length));
      let stock = 0;
      for (let i = 0; i < state.veg.length; i++) stock += state.veg[i];
      state.gpuTelemetry.vegetationStock = stock;
      state.gpuSimReadbackBuffer.unmap();
    }).catch(() =>
    {
      if (state.gpuSimReadbackBuffer.mapState === 'mapped') state.gpuSimReadbackBuffer.unmap();
    });
  }
}

export const gpuSimulationBackend = new GpuSimulationBackend();
