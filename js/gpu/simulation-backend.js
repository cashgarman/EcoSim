import { state, CELL, GPU_SIM_MAX_CREATURES } from '../state.js';
import { SP_KEYS, SPECIES, SPECIES_INDEX, buildGpuSpeciesTables } from '../data.js';
import { buildPassMaskUpload } from '../nav.js';
import { quality } from '../render/quality.js';
import { lifeStory } from '../life-story.js';
import { refineDeathCause, inferKillerId } from '../creature-notify.js';
import { effectiveReadbackEveryMs } from '../perf-policy.js';

const CELL_CAP = 64;
const CREATURE_STRIDE_VEC4 = 8;
const CREATURE_STRIDE_FLOATS = CREATURE_STRIDE_VEC4 * 4;
const PARAM_FLOATS = 18;
const WORLD_STRIDE = 6;
const COUNTERS_LEN = 8;

function speciesSumLen()
{
  return SP_KEYS.length * 3;
}

function preyOwnerBase()
{
  return COUNTERS_LEN + speciesSumLen();
}

function speciesMetaBase()
{
  return SP_KEYS.length * 2;
}

import { gpuBehaviorToState, stateToGpuCode } from '../behavior/state-codes.js';

export { gpuBehaviorToState, stateToGpuCode };

function buildGpuSimShader()
{
  const spCount = Math.max(1, SP_KEYS.length);
  const spSumLen = spCount * 3;
  const preyBase = COUNTERS_LEN + spSumLen;
  const metaBase = spCount * 2;
  return `
const CREATURE_STRIDE: u32 = ${CREATURE_STRIDE_VEC4}u;
const CELL_CAP: u32 = ${CELL_CAP}u;
const WORLD_STRIDE: u32 = ${WORLD_STRIDE}u;
const COUNTERS_LEN: u32 = ${COUNTERS_LEN}u;
const SPECIES_SUM_LEN: u32 = ${spSumLen}u;
const PREY_OWNER_BASE: u32 = ${preyBase}u;
const SPECIES_META_BASE: u32 = ${metaBase}u;
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
fn worldPassAt(tile: u32) -> f32 { return worldData[tile * WORLD_STRIDE + 5u]; }
fn setWorldVeg(tile: u32, value: f32) { worldData[tile * WORLD_STRIDE + 2u] = value; }

const NAV_R: i32 = 32;
const NAV_SIDE: u32 = 65u;
const NAV_CELLS: u32 = 4225u;
const DIRECT_PURSUIT_RADIUS: f32 = 4.0;
const PASS_GROUND_BLOCKED: u32 = 1u;

fn tileWalkableAt(wx: i32, wy: i32, w: i32, h: i32, canSwim: bool) -> bool
{
  if (wx < 0 || wy < 0 || wx >= w || wy >= h) { return false; }
  let tile = worldIndex(wx, wy, w, h);
  let bi = worldBiomeAt(tile);
  if (bi == 15u) { return false; }
  if (bi <= 2u) { return canSwim; }
  let passFlags = u32(worldPassAt(tile));
  if ((passFlags & PASS_GROUND_BLOCKED) != 0u) { return false; }
  return true;
}

fn atWaterEdgeXY(px: f32, py: f32, w: i32, h: i32) -> bool
{
  let ix = i32(round(px));
  let iy = i32(round(py));
  for (var ddy = -1; ddy <= 1; ddy++)
  {
    for (var ddx = -1; ddx <= 1; ddx++)
    {
      let ti = worldIndex(ix + ddx, iy + ddy, w, h);
      if (worldBiomeAt(ti) <= 2u) { return true; }
    }
  }
  return false;
}

fn losClear(fx: i32, fy: i32, gx: i32, gy: i32, w: i32, h: i32, canSwim: bool) -> bool
{
  var x = fx;
  var y = fy;
  var dx = abs(gx - fx);
  var dy = abs(gy - fy);
  var sx = select(-1, 1, fx < gx);
  var sy = select(-1, 1, fy < gy);
  var err = dx - dy;
  loop
  {
    if (!tileWalkableAt(x, y, w, h, canSwim)) { return false; }
    if (x == gx && y == gy) { return true; }
    let e2 = err * 2;
    if (e2 > -dy) { err = err - dy; x = x + sx; }
    if (e2 < dx) { err = err + dx; y = y + sy; }
  }
}

fn navCellIndex(cx: i32, cy: i32, side: i32) -> u32
{
  return u32(cy * side + cx);
}

fn octileHeuristic(ax: i32, ay: i32, bx: i32, by: i32) -> i32
{
  let dx = abs(ax - bx);
  let dy = abs(ay - by);
  let mn = min(dx, dy);
  let mx = max(dx, dy);
  return 14 * mn + 10 * (mx - mn);
}

fn navDiagonalBlocked(cx: i32, cy: i32, ddx: i32, ddy: i32, ox: i32, oy: i32, w: i32, h: i32, canSwim: bool) -> bool
{
  if (abs(ddx) + abs(ddy) != 2) { return false; }
  if (!tileWalkableAt(ox + cx + ddx, oy + cy, w, h, canSwim)) { return true; }
  if (!tileWalkableAt(ox + cx, oy + cy + ddy, w, h, canSwim)) { return true; }
  return false;
}

fn planNavWaypoint(
  px: f32, py: f32, goalX: f32, goalY: f32,
  w: i32, h: i32, canSwim: bool, radius: i32,
) -> vec2<f32>
{
  var gx = i32(round(goalX));
  var gy = i32(round(goalY));
  let fx = i32(round(px));
  let fy = i32(round(py));
  let dist = distance(vec2<f32>(px, py), vec2<f32>(goalX, goalY));
  if (dist <= DIRECT_PURSUIT_RADIUS) { return vec2<f32>(goalX, goalY); }
  if (!tileWalkableAt(gx, gy, w, h, canSwim))
  {
    var foundSnap = false;
    var snapX = gx;
    var snapY = gy;
    var bestSnap = 1000000;
    for (var sdy = -8; sdy <= 8; sdy++)
    {
      for (var sdx = -8; sdx <= 8; sdx++)
      {
        let lx = gx + sdx;
        let ly = gy + sdy;
        if (!tileWalkableAt(lx, ly, w, h, canSwim)) { continue; }
        let dd = sdx * sdx + sdy * sdy;
        if (dd < bestSnap)
        {
          bestSnap = dd;
          snapX = lx;
          snapY = ly;
          foundSnap = true;
        }
      }
    }
    if (!foundSnap) { return vec2<f32>(px, py); }
    gx = snapX;
    gy = snapY;
  }
  if (fx == gx && fy == gy) { return vec2<f32>(f32(gx) + 0.5, f32(gy) + 0.5); }
  if (losClear(fx, fy, gx, gy, w, h, canSwim)) { return vec2<f32>(goalX, goalY); }

  let R = min(radius, NAV_R);
  let side = R * 2 + 1;
  var ox = i32(floor((f32(fx) + f32(gx)) * 0.5)) - R;
  var oy = i32(floor((f32(fy) + f32(gy)) * 0.5)) - R;
  let maxOx = max(0, w - side);
  let maxOy = max(0, h - side);
  ox = clamp(ox, 0, maxOx);
  oy = clamp(oy, 0, maxOy);

  let glx = gx - ox;
  let gly = gy - oy;
  let flx = fx - ox;
  let fly = fy - oy;
  if (glx < 0 || gly < 0 || glx >= side || gly >= side) { return vec2<f32>(f32(gx) + 0.5, f32(gy) + 0.5); }
  if (flx < 0 || fly < 0 || flx >= side || fly >= side) { return vec2<f32>(f32(gx) + 0.5, f32(gy) + 0.5); }

  var gScore: array<i32, 4225>;
  var fScore: array<i32, 4225>;
  var parent: array<i32, 4225>;
  var closed: array<i32, 4225>;
  var heapIdx: array<u32, 4225>;
  var heapVal: array<i32, 4225>;
  for (var di: u32 = 0u; di < NAV_CELLS; di++)
  {
    gScore[di] = 2147483647;
    fScore[di] = 2147483647;
    parent[di] = -1;
    closed[di] = 0;
  }

  let start = navCellIndex(flx, fly, side);
  let goal = navCellIndex(glx, gly, side);
  gScore[start] = 0;
  fScore[start] = octileHeuristic(flx, fly, glx, gly);
  var heapSize: u32 = 1u;
  heapIdx[0u] = start;
  heapVal[0u] = fScore[start];

  while (heapSize > 0u)
  {
    let ci = heapIdx[0u];
    let last = heapSize - 1u;
    heapIdx[0u] = heapIdx[last];
    heapVal[0u] = heapVal[last];
    heapSize = last;
    var i = 0u;
    loop
    {
      let l = i * 2u + 1u;
      let r = l + 1u;
      var smallest = i;
      if (l < heapSize && heapVal[l] < heapVal[smallest]) { smallest = l; }
      if (r < heapSize && heapVal[r] < heapVal[smallest]) { smallest = r; }
      if (smallest == i) { break; }
      let ti = heapIdx[i];
      let tv = heapVal[i];
      heapIdx[i] = heapIdx[smallest];
      heapVal[i] = heapVal[smallest];
      heapIdx[smallest] = ti;
      heapVal[smallest] = tv;
      i = smallest;
    }

    if (closed[ci] != 0) { continue; }
    closed[ci] = 1;
    if (ci == goal)
    {
      var step = ci;
      var prev = parent[step];
      while (prev >= 0 && u32(prev) != start)
      {
        step = u32(prev);
        prev = parent[step];
      }
      let sx = i32(step % u32(side));
      let sy = i32(step / u32(side));
      return vec2<f32>(f32(ox + sx) + 0.5, f32(oy + sy) + 0.5);
    }

    let cx = i32(ci % u32(side));
    let cy = i32(ci / u32(side));
    let cg = gScore[ci];
    if (cg >= R * 14) { continue; }

    for (var nd: u32 = 0u; nd < 8u; nd++)
    {
      var ddx = 0;
      var ddy = 0;
      var cost = 10;
      if (nd == 0u) { ddx = 0; ddy = 1; cost = 10; }
      else if (nd == 1u) { ddx = 1; ddy = 0; cost = 10; }
      else if (nd == 2u) { ddx = 0; ddy = -1; cost = 10; }
      else if (nd == 3u) { ddx = -1; ddy = 0; cost = 10; }
      else if (nd == 4u) { ddx = 1; ddy = 1; cost = 14; }
      else if (nd == 5u) { ddx = 1; ddy = -1; cost = 14; }
      else if (nd == 6u) { ddx = -1; ddy = 1; cost = 14; }
      else { ddx = -1; ddy = -1; cost = 14; }
      let nx = cx + ddx;
      let ny = cy + ddy;
      if (nx < 0 || ny < 0 || nx >= side || ny >= side) { continue; }
      let wx = ox + nx;
      let wy = oy + ny;
      if (!tileWalkableAt(wx, wy, w, h, canSwim)) { continue; }
      if (navDiagonalBlocked(cx, cy, ddx, ddy, ox, oy, w, h, canSwim)) { continue; }
      let ni = navCellIndex(nx, ny, side);
      if (closed[ni] != 0) { continue; }
      let tg = cg + cost;
      if (tg >= gScore[ni]) { continue; }
      gScore[ni] = tg;
      parent[ni] = i32(ci);
      let tf = tg + octileHeuristic(nx, ny, glx, gly);
      fScore[ni] = tf;
      heapIdx[heapSize] = ni;
      heapVal[heapSize] = tf;
      var pos = heapSize;
      heapSize = heapSize + 1u;
      while (pos > 0u)
      {
        let p = (pos - 1u) / 2u;
        if (heapVal[p] <= heapVal[pos]) { break; }
        let ti2 = heapIdx[p];
        let tv2 = heapVal[p];
        heapIdx[p] = heapIdx[pos];
        heapVal[p] = heapVal[pos];
        heapIdx[pos] = ti2;
        heapVal[pos] = tv2;
        pos = p;
      }
    }
  }

  var fbScore = 2147483647;
  var fbX = flx;
  var fbY = fly;
  for (var cy2 = 0; cy2 < side; cy2++)
  {
    for (var cx2 = 0; cx2 < side; cx2++)
    {
      let ni = navCellIndex(cx2, cy2, side);
      let g = gScore[ni];
      if (g >= 2147483647) { continue; }
      let md = abs(gx - (ox + cx2)) + abs(gy - (oy + cy2));
      let score = g * 1000 + md;
      if (score < fbScore)
      {
        fbScore = score;
        fbX = cx2;
        fbY = cy2;
      }
    }
  }
  return vec2<f32>(f32(ox + fbX) + 0.5, f32(oy + fbY) + 0.5);
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
  let lv = creatures[b + 2u];
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
          let ol = creatures[ob + 2u];
          let osex = creatures[ob + 7u].y;
          let mySex = creatures[b + 7u].y;
          if (osex != mySex && ol.w <= 0.0 && ol.z <= 0.0 && lv.w <= 0.0 && lv.z <= 0.0)
          {
            mateDist2 = d2;
            mateId = oi;
          }
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
  else if (nv.z < 30.0 || (tv.w == 2.0 && nv.z < 55.0))
  {
    st = 2.0;
    var bestDist2 = 1e12;
    var foundWater = false;
    let searchR = i32(min(24.0, sense + 6.0));
    let px = i32(round(pv.x));
    let py = i32(round(pv.y));
    for (var sdy = -searchR; sdy <= searchR; sdy++)
    {
      for (var sdx = -searchR; sdx <= searchR; sdx++)
      {
        let lx = px + sdx;
        let ly = py + sdy;
        if (lx < 0 || ly < 0 || lx >= i32(p_u(2u)) || ly >= i32(p_u(3u))) { continue; }
        let landTile = worldIndex(lx, ly, i32(p_u(2u)), i32(p_u(3u)));
        let landBi = worldBiomeAt(landTile);
        if (landBi <= 2u || landBi == 15u) { continue; }
        var hasWater = false;
        for (var wdy = -1; wdy <= 1; wdy++)
        {
          for (var wdx = -1; wdx <= 1; wdx++)
          {
            let wt = worldIndex(lx + wdx, ly + wdy, i32(p_u(2u)), i32(p_u(3u)));
            if (worldBiomeAt(wt) <= 2u) { hasWater = true; }
          }
        }
        if (!hasWater) { continue; }
        let dd = f32(sdx) * f32(sdx) + f32(sdy) * f32(sdy);
        if (dd < bestDist2)
        {
          bestDist2 = dd;
          tx = f32(lx) + 0.5;
          ty = f32(ly) + 0.5;
          foundWater = true;
        }
      }
    }
    if (!foundWater)
    {
      let wanderX = hash3(f32(i), p_f(7u), 3.0) * 12.0 - 6.0;
      let wanderY = hash3(f32(i), p_f(7u), 4.0) * 12.0 - 6.0;
      tx = clamp(pv.x + wanderX, 1.0, f32(p_u(2u)) - 2.0);
      ty = clamp(pv.y + wanderY, 1.0, f32(p_u(3u)) - 2.0);
    }
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
  else if (mateId != 0xffffffffu && nv.y > 45.0 && nv.z > 40.0 && lv.w <= 0.0 && lv.z <= 0.0)
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

  tv.x = tx;
  tv.y = ty;
  tv.w = st;
  creatures[b + 3u] = tv;
}

@compute @workgroup_size(128)
fn claimBehaviorTargets(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let i = gid.x;
  let creatureCount = p_u(4u);
  if (i >= creatureCount) { return; }
  let b = creatureBase(i);
  let pv = creatures[b + 0u];
  let tv = creatures[b + 3u];
  let mv = creatures[b + 4u];
  if (mv.w < 0.5) { return; }

  if (tv.w == 4.0 && tv.z >= 0.0)
  {
    let preyId = u32(tv.z + 0.5);
    if (preyId < creatureCount)
    {
      atomicMin(&simAtomics[PREY_OWNER_BASE + preyId], i + 1u);
    }
  }
  if (tv.w == 3.0)
  {
    let tile = worldIndex(i32(round(pv.x)), i32(round(pv.y)), i32(p_u(2u)), i32(p_u(3u)));
    atomicMin(&simAtomics[FOOD_OWNER_BASE + tile], i + 1u);
  }
}

@compute @workgroup_size(128)
fn planNavStep(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let i = gid.x;
  let creatureCount = p_u(4u);
  if (i >= creatureCount) { return; }
  let b = creatureBase(i);
  let pv = creatures[b + 0u];
  var tv = creatures[b + 3u];
  let sv = creatures[b + 6u];
  let mv = creatures[b + 4u];
  var svOut = sv;
  if (mv.w < 0.5) { return; }

  let st = tv.w;
  if (st == 5.0) { return; }
  // Hunt/mate pursuit uses live target positions in resolveIntegrate — skip grid A* here.
  if (st == 4.0 || st == 6.0) { return; }

  let sp = u32(mv.x + 0.5);
  let canSwim = speciesTables[sp].w > 0.5;
  let w = i32(p_u(2u));
  let h = i32(p_u(3u));

  if (st == 2.0 && atWaterEdgeXY(pv.x, pv.y, w, h))
  {
    tv.x = pv.x;
    tv.y = pv.y;
    creatures[b + 3u] = tv;
    return;
  }

  let goalX = sv.x;
  let goalY = sv.y;
  let navR = i32(p_f(13u));
  let replanEvery = max(1u, p_u(14u));
  let shouldPlan = ((p_u(7u) + i) % replanEvery) == 0u;
  if (!shouldPlan) { return; }

  let wp = planNavWaypoint(pv.x, pv.y, goalX, goalY, w, h, canSwim, navR);
  tv.x = wp.x;
  tv.y = wp.y;
  creatures[b + 3u] = tv;
  creatures[b + 6u] = svOut;
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
  if (lv.z > 0.0) { lv.z = max(0.0, lv.z - dt); }
  if (lv.w > 0.0)
  {
    lv.w = lv.w - dt;
    if (lv.w <= 0.0)
    {
      let litter = max(1.0, creatures[b + 7u].w);
      var n = u32(litter);
      for (var bk: u32 = 0u; bk < n; bk++)
      {
        let bslot = atomicAdd(&simAtomics[2u], 1u);
        simLists[POOL_CAP * 2u + bslot] = i;
      }
      creatures[b + 7u].w = 0.0;
    }
  }

  let tile = worldIndex(i32(round(pv.x)), i32(round(pv.y)), i32(p_u(2u)), i32(p_u(3u)));
  let localT = worldTempAt(tile);
  let stress = max(0.0, abs(localT - gv.z) - gv.w);
  if (stress > 0.0) { nv.x = nv.x - stress * 14.0 * dt; }
  if (nv.y <= 0.0) { nv.x = nv.x - 6.0 * dt; }
  if (nv.z <= 0.0) { nv.x = nv.x - 7.0 * dt; }
  if (nv.y > 55.0 && nv.z > 55.0 && stress <= 0.001) { nv.x = min(100.0, nv.x + 4.0 * dt); }

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

  if (tv.w == 6.0 && tv.z >= 0.0)
  {
    let mateId = u32(tv.z + 0.5);
    if (mateId < creatureCount)
    {
      let mb = creatureBase(mateId);
      let mp = creatures[mb + 0u];
      let ml = creatures[mb + 2u];
      let dMate = distance(vec2<f32>(pv.x, pv.y), vec2<f32>(mp.x, mp.y));
      if (dMate < 1.0)
      {
        let mySex = creatures[b + 7u].y;
        let mateSex = creatures[mb + 7u].y;
        if (mySex != mateSex && lv.w <= 0.0 && ml.w <= 0.0 && lv.z <= 0.0 && ml.z <= 0.0)
        {
          var femaleB = b;
          var maleB = mb;
          if (mySex > 0.5) { femaleB = mb; maleB = b; }
          var flv = creatures[femaleB + 2u];
          var mlv = creatures[maleB + 2u];
          let fsp = u32(creatures[femaleB + 4u].x + 0.5);
          let mateCfg = speciesTables[SPECIES_META_BASE + fsp];
          let gestRoll = hash3(f32(femaleB), f32(maleB), p_f(7u));
          let cdRoll = hash3(f32(femaleB), f32(maleB), p_f(7u) + 1.0);
          let gestation = mix(mateCfg.x, mateCfg.y, gestRoll);
          let cd = mix(mateCfg.z, mateCfg.w, cdRoll);
          flv.w = gestation;
          flv.z = cd;
          mlv.z = cd * 0.6;
          let litterGene = creatures[femaleB + 6u].x;
          let litterRoll = hash3(f32(femaleB), p_f(7u), 14.0);
          creatures[femaleB + 7u].w = max(1.0, round(litterGene * mix(0.7, 1.15, litterRoll)));
          creatures[femaleB + 2u] = flv;
          creatures[maleB + 2u] = mlv;
          if (b == femaleB) { nv.w = max(0.0, nv.w - 20.0); }
          else if (b == maleB) { nv.w = max(0.0, nv.w - 12.0); }
        }
      }
    }
  }

  var drinking = false;
  let atEdge = atWaterEdgeXY(pv.x, pv.y, i32(p_u(2u)), i32(p_u(3u)));
  if (tv.w == 2.0 || (atEdge && nv.z < 55.0))
  {
    if (atEdge)
    {
      nv.z = min(100.0, nv.z + 60.0 * dt);
      drinking = true;
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

  if (tv.w == 4.0 && tv.z >= 0.0)
  {
    let preyId = u32(tv.z + 0.5);
    if (preyId < creatureCount)
    {
      let pb = creatureBase(preyId);
      let pm = creatures[pb + 4u];
      if (pm.w > 0.5)
      {
        let pp = creatures[pb + 0u];
        let dPrey = distance(vec2<f32>(pv.x, pv.y), vec2<f32>(pp.x, pp.y));
        let strikeR = mv.y * 0.55 + pm.y * 0.45 + 0.65;
        if (dPrey < strikeR) { speed = 0.0; }
      }
    }
  }

  let sp = u32(mv.x + 0.5);
  let speciesInfo = speciesTables[sp];
  let canSwim = speciesInfo.w > 0.5;

  var moveX = tv.x;
  var moveY = tv.y;
  if (tv.z >= 0.0 && (tv.w == 4.0 || tv.w == 6.0))
  {
    let tgtId = u32(tv.z + 0.5);
    if (tgtId < creatureCount)
    {
      let tb = creatureBase(tgtId);
      let tm = creatures[tb + 4u];
      if (tm.w > 0.5)
      {
        let tp = creatures[tb + 0u];
        moveX = tp.x;
        moveY = tp.y;
      }
    }
  }

  let dx = moveX - pv.x;
  let dy = moveY - pv.y;
  let d = max(0.001, length(vec2<f32>(dx, dy)));
  var nx = pv.x + dx / d * speed * 2.2 * dt;
  var ny = pv.y + dy / d * speed * 2.2 * dt;
  let stepTile = worldIndex(i32(round(nx)), i32(round(ny)), i32(p_u(2u)), i32(p_u(3u)));
  if (!tileWalkableAt(i32(round(nx)), i32(round(ny)), i32(p_u(2u)), i32(p_u(3u)), canSwim))
  {
    nx = pv.x;
    ny = pv.y;
  }
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

  creatures[b + 2u] = lv;
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
fn resolveHuntDamage(@builtin(global_invocation_id) gid: vec3<u32>)
{
  let i = gid.x;
  let creatureCount = p_u(4u);
  if (i >= creatureCount) { return; }
  let b = creatureBase(i);
  let pv = creatures[b + 0u];
  let tv = creatures[b + 3u];
  let mv = creatures[b + 4u];
  if (mv.w < 0.5 || tv.w != 4.0 || tv.z < 0.0) { return; }

  let preyId = u32(tv.z + 0.5);
  if (preyId >= creatureCount) { return; }
  let pb = creatureBase(preyId);
  let pm = creatures[pb + 4u];
  if (pm.w < 0.5) { return; }

  let pp = creatures[pb + 0u];
  let d = distance(vec2<f32>(pv.x, pv.y), vec2<f32>(pp.x, pp.y));
  let strikeR = mv.y * 0.55 + pm.y * 0.45 + 0.65;
  if (d >= strikeR) { return; }

  let agg = creatures[b + 7u].z;
  let chance = 0.35 + agg * 0.35;
  let roll = hash3(f32(i), f32(preyId), p_f(7u) + p_f(0u) * 0.37);
  if (roll >= chance) { return; }

  var pn = creatures[pb + 1u];
  pn.x = pn.x - (30.0 + mv.y * 15.0);
  creatures[pb + 1u] = pn;

  if (pn.x <= 0.0)
  {
    var preyMv = creatures[pb + 4u];
    preyMv.w = 0.0;
    creatures[pb + 4u] = preyMv;
    var nv = creatures[b + 1u];
    nv.y = min(100.0, nv.y + 50.0);
    nv.w = min(100.0, nv.w + 12.0);
    creatures[b + 1u] = nv;
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
  let sexRoll = hash3(f32(child), p_f(7u), 13.0);
  creatures[cb + 6u] = vec4<f32>(ps.x, ps.y, ps.z + 1.0, 0.0);
  creatures[cb + 7u] = vec4<f32>(1.0, select(0.0, 1.0, sexRoll > 0.5), 0.0, 0.0);
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
  let col = speciesTables[${spCount}u + sp];
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
}

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
  const spCount = SP_KEYS.length;
  const packed = new Float32Array(spCount * 12);
  for (let i = 0; i < spCount; i++)
  {
    packed[i * 4 + 0] = table[i * 12 + 0];
    packed[i * 4 + 1] = table[i * 12 + 1];
    packed[i * 4 + 2] = table[i * 12 + 2];
    packed[i * 4 + 3] = table[i * 12 + 3];
    const colorBase = spCount * 4 + i * 4;
    packed[colorBase + 0] = colors[i * 4 + 0];
    packed[colorBase + 1] = colors[i * 4 + 1];
    packed[colorBase + 2] = colors[i * 4 + 2];
    packed[colorBase + 3] = colors[i * 4 + 3];
    const metaBase = spCount * 8 + i * 4;
    packed[metaBase + 0] = table[i * 12 + 8];
    packed[metaBase + 1] = table[i * 12 + 9];
    packed[metaBase + 2] = table[i * 12 + 10];
    packed[metaBase + 3] = table[i * 12 + 11];
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
  data[base + 11] = (state.simBackend === 'gpu' && state.gpuSimEnabled) ? 0 : c.pregnant;
  data[base + 12] = c.tx;
  data[base + 13] = c.ty;
  data[base + 14] = typeof c.gpuTargetSlot === 'number' ? c.gpuTargetSlot : -1;
  data[base + 15] = typeof c.gpuStateCode === 'number' ? c.gpuStateCode : 0;
  data[base + 24] = c.tx;
  data[base + 25] = c.ty;
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
  data[base + 29] = c.sex === 'male' ? 1 : 0;
  data[base + 30] = g.agg;
  data[base + 31] = c.litterQ || 0;
}

export class GpuSimulationBackend
{
  constructor()
  {
    this.initialized = false;
    this.maxCreatures = GPU_SIM_MAX_CREATURES;
    this.tickCounter = 0;
    this.readbackEveryMs = 160;
    this.selectedReadbackEveryMs = 220;
    this.countersReadbackEveryMs = 240;
    this.selectedReadbackPending = false;
    this.selectedReadbackAt = 0;
    this.countersReadbackAt = 0;
    this.countersReadbackPending = false;
    this.worldReadbackPending = false;
    this.worldReadbackEveryTicks = 120;
    this.cpuCreatureIndexById = new Map();
    this._readbackLock = null;
    this._decisionQuad = new Float32Array(4);
    this._goalPair = new Float32Array(2);
    this._targetPair = new Float32Array(2);
    this._paramsScratch = new Float32Array(PARAM_FLOATS);
    this._readbackBootUntil = 0;
    this._readbackPendingSince = 0;
    this._creatureReadbackMapping = false;
    this._gpuLostHandled = false;
  }

  handleGpuDeviceLost(reason, info = null)
  {
    if (this._gpuLostHandled) return;
    this._gpuLostHandled = true;
    state.gpuSimEnabled = false;
    state.simBackend = 'cpu';
    state.rendererBackend = 'canvas';
    state.gpuSimReadbackPending = false;
    this.selectedReadbackPending = false;
    this.countersReadbackPending = false;
    this.worldReadbackPending = false;
    this._creatureReadbackMapping = false;
    this._readbackPendingSince = 0;
    const gpuCanvas = document.getElementById('world-gpu');
    if (gpuCanvas) gpuCanvas.classList.add('hidden');
    console.warn('GPU device lost; fallback to CPU simulation.', reason, info || '');
  }

  creatureReadbackIntervalMs()
  {
    let base = this.readbackEveryMs;
    const rbMs = state.gpuTelemetry?.readbackMs || 0;
    if (rbMs > 2000) base *= 8;
    else if (rbMs > 800) base *= 4;
    else if (rbMs > 400) base *= 2;
    return effectiveReadbackEveryMs(base);
  }

  shouldScheduleCreatureReadback(now)
  {
    if (state.scrubActive || state.batchMode) return false;
    if (this._readbackBootUntil && now < this._readbackBootUntil) return false;
    if (state.gpuSimReadbackPending || this._creatureReadbackMapping) return false;
    if (now - state.gpuSimLastReadbackAt < this.creatureReadbackIntervalMs()) return false;
    if (state.selected && !state.selected.dead) return true;
    if (state.followSelected) return true;
    return (this.tickCounter % 30) === 0;
  }

  recoverStalledReadback(now)
  {
    const rb = state.gpuSimBuffers?.renderReadback;
    if (!state.gpuSimReadbackPending)
    {
      this._readbackPendingSince = 0;
      return;
    }
    if (!this._readbackPendingSince) this._readbackPendingSince = now;
    else if (now - this._readbackPendingSince > 1500)
    {
      state.gpuSimReadbackPending = false;
      this._creatureReadbackMapping = false;
      this._readbackPendingSince = 0;
      if (rb?.mapState === 'mapped') rb.unmap();
    }
  }

  aliveCreaturesForDecisions()
  {
    const mirror = state.gpuSimMirror;
    if (mirror && mirror.length)
    {
      return mirror;
    }
    return state.creatures;
  }

  worldReadbackIntervalTicks()
  {
    const speed = Math.max(1, state.speed || 1);
    return Math.max(this.worldReadbackEveryTicks, Math.floor(this.worldReadbackEveryTicks * speed / 4));
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
      if (!this._dbgDeviceLostHooked && state.gpuDevice?.lost)
      {
        this._dbgDeviceLostHooked = true;
        state.gpuDevice.lost.then(info =>
        {
          this.handleGpuDeviceLost(info?.reason || 'unknown', info?.message || null);
        }).catch(() => {});
      }
      const mod = dev.createShaderModule({ code: buildGpuSimShader() });
      const bindGroupLayout = dev.createBindGroupLayout({ entries: GPU_SIM_BIND_GROUP_LAYOUT });
      const layout = dev.createPipelineLayout({ bindGroupLayouts: [bindGroupLayout] });

      state.gpuSimPipelines = {
        clearCells: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'clearCells' } }),
        clearCounters: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'clearCounters' } }),
        binCreatures: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'binCreatures' } }),
        claimBehaviorTargets: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'claimBehaviorTargets' } }),
        planNavStep: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'planNavStep' } }),
        resolveIntegrate: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'resolveIntegrate' } }),
        resolveHuntDamage: dev.createComputePipeline({ layout, compute: { module: mod, entryPoint: 'resolveHuntDamage' } }),
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
    const simAtomicsBytes = (preyOwnerBase() + this.maxCreatures + tiles) * 4;
    const simListsBytes = this.maxCreatures * 3 * 4;
    const speciesBytes = Math.max(1, SP_KEYS.length) * 12 * 4;
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
    const passFlags = buildPassMaskUpload();
    for (let i = 0; i < tiles; i++)
    {
      const base = i * WORLD_STRIDE;
      packed[base + 0] = state.temp[i];
      packed[base + 1] = state.moist[i];
      packed[base + 2] = state.veg[i];
      packed[base + 3] = state.vegCap[i];
      packed[base + 4] = state.biome[i];
      packed[base + 5] = passFlags[i];
    }
    state.gpuDevice.queue.writeBuffer(state.gpuWorldBuffers.worldData, 0, packed.buffer, packed.byteOffset, packed.byteLength);
  }

  uploadBehaviorDecisions()
  {
    if (!state.gpuSimBuffers || !state.gpuDevice) return;
    const bytesPerCreature = CREATURE_STRIDE_FLOATS * 4;
    const goalPair = this._goalPair;
    const targetPair = this._targetPair;
    const source = this.aliveCreaturesForDecisions();
    for (let i = 0; i < source.length; i++)
    {
      const c = source[i];
      if (!c || c.dead) continue;
      const slot = creatureSlot(c, i);
      if (slot < 0 || slot >= this.maxCreatures) continue;
      const baseByte = slot * bytesPerCreature;
      goalPair[0] = c.tx;
      goalPair[1] = c.ty;
      state.gpuDevice.queue.writeBuffer(
        state.gpuSimBuffers.creatures,
        baseByte + 24 * 4,
        goalPair.buffer,
        goalPair.byteOffset,
        8,
      );
      targetPair[0] = typeof c.gpuTargetSlot === 'number' ? c.gpuTargetSlot : -1;
      targetPair[1] = typeof c.gpuStateCode === 'number' ? c.gpuStateCode : 0;
      state.gpuDevice.queue.writeBuffer(
        state.gpuSimBuffers.creatures,
        baseByte + 14 * 4,
        targetPair.buffer,
        targetPair.byteOffset,
        8,
      );
    }
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
    state.gpuPosSyncAt = performance.now();
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
    this._gpuLostHandled = false;
    if (!this.ensureBuffers()) return false;
    const tileCount = state.W * state.H;
    if (tileCount >= 350000)
    {
      this.readbackEveryMs = 220;
      this.selectedReadbackEveryMs = 520;
      this.countersReadbackEveryMs = 320;
      this.worldReadbackEveryTicks = 360;
    }
    else if (tileCount >= 160000)
    {
      this.readbackEveryMs = 190;
      this.selectedReadbackEveryMs = 360;
      this.countersReadbackEveryMs = 280;
      this.worldReadbackEveryTicks = 220;
    }
    else
    {
      this.readbackEveryMs = 420;
      this.selectedReadbackEveryMs = 280;
      this.countersReadbackEveryMs = 320;
      this.worldReadbackEveryTicks = 200;
    }
    this.countersReadbackAt = 0;
    this._readbackBootUntil = performance.now() + 3500;
    this._readbackPendingSince = 0;
    this._creatureReadbackMapping = false;
    state.gpuSimReadbackPending = false;
    state.gpuTelemetry.readbackMs = 0;
    this.uploadWorld();
    this.uploadCreaturesFromCpu();
    state.gpuSimEnabled = true;
    state.gpuPosSyncAt = performance.now();
    state.simBackend = 'gpu';
    return true;
  }

  writeParams(dt)
  {
    const arr = this._paramsScratch;
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
    const navCfg = quality.config();
    arr[13] = navCfg.navRadius;
    arr[14] = navCfg.navReplanInterval;
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
    const speciesTriples = Math.max(1, speciesSumLen());
    const encoder = dev.createCommandEncoder();
    const pass = encoder.beginComputePass();
    const clearCount = Math.max(cellCount, tileCount, creatureCount, speciesTriples);
    this.dispatch(pass, state.gpuSimPipelines.clearCells, Math.ceil(clearCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.clearCounters, Math.ceil(Math.max(COUNTERS_LEN, speciesTriples) / 64));
    this.dispatch(pass, state.gpuSimPipelines.binCreatures, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.claimBehaviorTargets, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.planNavStep, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.resolveIntegrate, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.resolveHuntDamage, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.spawnFromBirthQueue, Math.ceil(creatureCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.growVegetation, Math.ceil(tileCount / 128));
    this.dispatch(pass, state.gpuSimPipelines.composeRenderData, Math.ceil(creatureCount / 128));
    pass.end();

    const now = performance.now();
    if (!state.batchMode)
    {
      this.recoverStalledReadback(now);
      const readbackEveryMs = this.creatureReadbackIntervalMs();
      const countersRb = state.gpuSimBuffers.countersReadback;
      const countersReady = (now - this.countersReadbackAt) > this.countersReadbackEveryMs;
      const readbackBusy = state.gpuSimReadbackPending || this._creatureReadbackMapping;
      if (!readbackBusy && !this.countersReadbackPending && countersRb && countersRb.mapState === 'unmapped')
      {
        if (countersReady)
        {
          encoder.copyBufferToBuffer(state.gpuSimBuffers.simAtomics, 0, countersRb, 0, 64);
          this.countersReadbackPending = true;
          this.countersReadbackAt = now;
        }
      }
      const renderRb = state.gpuSimBuffers.renderReadback;
      if (
        this.shouldScheduleCreatureReadback(now)
        && renderRb
        && renderRb.mapState === 'unmapped'
      )
      {
        const bytes = state.gpuRenderCreatureCount * CREATURE_STRIDE_FLOATS * 4;
        const copyBytes = roundUp(bytes, 256);
        encoder.copyBufferToBuffer(state.gpuSimBuffers.creatures, 0, renderRb, 0, copyBytes);
        state.gpuSimReadbackPending = true;
        state.gpuSimLastReadbackAt = now;
        this._readbackPendingSince = now;
      }
      const selectedRb = state.gpuSimBuffers.selectedReadback;
      const selectedSlot = state.selected?.gpuSlot;
      const selectedReadbackEligible = (now - state.gpuSimLastReadbackAt) > (readbackEveryMs * 0.65);
      if (
        !readbackBusy
        && !state.scrubActive
        && state.selected
        && typeof selectedSlot === 'number'
        && selectedSlot >= 0
        && selectedSlot < state.gpuRenderCreatureCount
        && selectedReadbackEligible
        && !this.selectedReadbackPending
        && selectedRb
        && selectedRb.mapState === 'unmapped'
        && now - this.selectedReadbackAt > this.selectedReadbackEveryMs
      )
      {
        encoder.copyBufferToBuffer(
          state.gpuSimBuffers.creatures,
          selectedSlot * CREATURE_STRIDE_FLOATS * 4,
          selectedRb,
          0,
          CREATURE_STRIDE_FLOATS * 4,
        );
        this.selectedReadbackPending = true;
        this.selectedReadbackAt = now;
      }
    }
    const cmd = encoder.finish();
    dev.queue.submit([cmd]);

    if (!state.batchMode)
    {
      if (state.gpuSimReadbackPending && !state.scrubActive) this.consumeCreatureReadback();
      else if (this.selectedReadbackPending && !state.scrubActive) this.consumeSelectedReadback();
      else if (this.countersReadbackPending) this.consumeCountersReadback();
    }

    if (
      !state.batchMode
      && !state.scrubActive
      && !state.gpuSimReadbackPending
      && !this._creatureReadbackMapping
      && !this.worldReadbackPending
      && (this.tickCounter % this.worldReadbackIntervalTicks()) === 0
    )
    {
      this.syncWorldBackToCpu();
    }
    return true;
  }

  async waitForBufferUnmapped(rb)
  {
    if (!rb) return;
    const dev = state.gpuDevice;
    for (let i = 0; i < 200; i++)
    {
      if (rb.mapState === 'unmapped') return;
      if (rb.mapState === 'mapped') rb.unmap();
      await dev?.queue?.onSubmittedWorkDone?.();
    }
    throw new Error('GPU readback buffer did not become unmapped');
  }

  async forceCreatureReadback()
  {
    if (!state.gpuSimEnabled || !state.gpuDevice || !state.gpuSimBuffers) return false;
    const run = () => this._forceCreatureReadbackInner();
    this._readbackLock = (this._readbackLock || Promise.resolve()).then(run, run);
    return this._readbackLock;
  }

  async _forceCreatureReadbackInner()
  {
    const rb = state.gpuSimBuffers.renderReadback;
    if (!rb) return false;

    state.gpuSimReadbackPending = false;
    this.countersReadbackPending = false;
    this.selectedReadbackPending = false;

    const dev = state.gpuDevice;
    await this.waitForBufferUnmapped(rb);

    const bytes = state.gpuRenderCreatureCount * CREATURE_STRIDE_FLOATS * 4;
    const copyBytes = roundUp(bytes, 256);
    const encoder = dev.createCommandEncoder();
    encoder.copyBufferToBuffer(state.gpuSimBuffers.creatures, 0, rb, 0, copyBytes);
    dev.queue.submit([encoder.finish()]);
    await dev.queue.onSubmittedWorkDone?.();

    await rb.mapAsync(GPUMapMode.READ);
    const floatData = new Float32Array(rb.getMappedRange());
    this.applyCreatureReadbackFromBuffer(floatData);
    rb.unmap();
    return true;
  }

  consumeCountersReadback()
  {
    const rb = state.gpuSimBuffers?.countersReadback;
    if (!rb || !this.countersReadbackPending || rb.mapState !== 'unmapped') return;
    rb.mapAsync(GPUMapMode.READ).then(() =>
    {
      const arr = new Uint32Array(rb.getMappedRange());
      state.gpuTelemetry.aliveCount = arr[0] || 0;
      state.gpuTelemetry.deadCount = arr[1] || 0;
      state.gpuTelemetry.birthCount = arr[2] || 0;
      state.gpuTelemetry.herbivoreIntake = (arr[3] || 0) / 1000;
      state.gpuTelemetry.starvationRisk = arr[4] || 0;
      rb.unmap();
      this.countersReadbackPending = false;
    }).catch(() =>
    {
      this.handleGpuDeviceLost('counters-readback-map-failed', rb.mapState);
      if (rb.mapState === 'mapped') rb.unmap();
      this.countersReadbackPending = false;
    });
  }

  consumeCreatureReadback()
  {
    if (!state.gpuSimReadbackPending) return;
    if (state.scrubActive)
    {
      state.gpuSimReadbackPending = false;
      return;
    }
    const rb = state.gpuSimBuffers?.renderReadback;
    if (!rb || rb.mapState !== 'unmapped') return;
    if (this._creatureReadbackMapping) return;
    this._creatureReadbackMapping = true;
    const mapStart = performance.now();
    rb.mapAsync(GPUMapMode.READ).then(() =>
    {
      const mapWaitMs = performance.now() - mapStart;
      if (state.scrubActive)
      {
        if (rb.mapState === 'mapped') rb.unmap();
        state.gpuSimReadbackPending = false;
        this._creatureReadbackMapping = false;
        this._readbackPendingSince = 0;
        return;
      }
      const applyStart = performance.now();
      try
      {
        const floatData = new Float32Array(rb.getMappedRange());
        const lightSync = !state.selected && (this.tickCounter % 30) !== 0;
        this.applyCreatureReadbackFromBuffer(floatData, lightSync);
      }
      finally
      {
        if (rb.mapState === 'mapped') rb.unmap();
        state.gpuSimReadbackPending = false;
        this._creatureReadbackMapping = false;
        this._readbackPendingSince = 0;
      }
      const applyMs = performance.now() - applyStart;
      const totalMs = mapWaitMs + applyMs;
      state.gpuTelemetry.readbackMs = state.gpuTelemetry.readbackMs
        ? state.gpuTelemetry.readbackMs * 0.85 + totalMs * 0.15
        : totalMs;
      state.gpuTelemetry.readbackMapMs = mapWaitMs;
      state.gpuTelemetry.readbackApplyMs = applyMs;
    }).catch(() =>
    {
      this.handleGpuDeviceLost('creature-readback-map-failed', rb.mapState);
      if (rb.mapState === 'mapped') rb.unmap();
      state.gpuSimReadbackPending = false;
      this._creatureReadbackMapping = false;
      this._readbackPendingSince = 0;
    });
  }

  applyCreatureReadbackFromBuffer(floatData, lightSync = false)
  {
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
    const pending = [];
    const skipLifeStory = !!state.batchMode || lightSync;
    for (let i = 0; i < Math.min(state.gpuRenderCreatureCount, this.maxCreatures); i++)
    {
      const base = i * CREATURE_STRIDE_FLOATS;
      const spIdx = Math.max(0, Math.min(SP_KEYS.length - 1, Math.round(floatData[base + 16])));
      const aliveFlag = floatData[base + 19] > 0.5;
      let c = slotMap.get(i);
      const isNewGpuCreature = !c;
      const wasDead = !!c?.dead;
      const cpuRepro = (c && state.simBackend === 'gpu' && state.gpuSimEnabled && !state.scrubActive)
        ? {
          pregnant: c.pregnant || 0,
          mateCd: c.mateCd || 0,
          litterQ: c.litterQ || 0,
        }
        : null;
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
        slotMap.set(i, c);
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
      c.gpuTargetSlot = Math.round(floatData[base + 14]);
      c.sp = SP_KEYS[spIdx];
      c.state = gpuBehaviorToState(floatData[base + 15]);
      c.dead = !aliveFlag;
      c.gpuSlot = i;
      c.gpuNeedsUpload = false;
      c.walk = floatData[base + 27];
      c.dir = floatData[base + 28] >= 0 ? 1 : -1;
      c.sex = floatData[base + 29] > 0.5 ? 'male' : 'female';
      c.litterQ = floatData[base + 31] || floatData[base + 30] || 0;
      const gpuGen = Math.max(1, Math.round(floatData[base + 26]) || 1);
      if (isNewGpuCreature || (wasDead && aliveFlag) || c.gen == null || c.gen < 1)
      {
        c.gen = gpuGen;
      }
      if (cpuRepro)
      {
        c.pregnant = cpuRepro.pregnant;
        c.mateCd = cpuRepro.mateCd;
        if (cpuRepro.litterQ > 0) c.litterQ = cpuRepro.litterQ;
      }
      if (!skipLifeStory && !c.lifeStory) lifeStory.initCreature(c);
      pending.push({ c, isNewGpuCreature, aliveFlag, base });
    }

    for (const row of pending)
    {
      const c = row.c;
      const tgtSlot = c.gpuTargetSlot;
      if (typeof tgtSlot === 'number' && tgtSlot >= 0)
      {
        const tgt = slotMap.get(tgtSlot);
        c.target = tgt?.id ?? null;
      }
      else
      {
        c.target = null;
      }
    }

    if (!skipLifeStory)
    {
      for (const row of pending)
      {
        const c = row.c;
        const story = c.lifeStory;
        const wasDead = !!story?.snapshot?.dead;
        if (c.dead)
        {
          if (!wasDead)
          {
            const killerId = inferKillerId(c);
            if (killerId != null)
            {
              c.killedById = killerId;
              c.cause = 'predation';
              const predator = state.creatures.find(x => x.id === killerId);
              if (predator) lifeStory.recordHunted(predator, c);
            }
            else if (!c.cause || c.cause === 'exhaustion')
            {
              c.cause = refineDeathCause(c);
            }
          }
          else if (!c.cause || c.cause === 'exhaustion')
          {
            c.cause = refineDeathCause(c);
          }
        }
        lifeStory.observeFromSnapshot(c, row.isNewGpuCreature);
        if (row.aliveFlag) alive.push(c);
      }
    }
    else
    {
      for (const row of pending)
      {
        if (row.aliveFlag) alive.push(row.c);
        if (row.c.gen > state.generationMax) state.generationMax = row.c.gen;
      }
    }

    state.gpuSimMirror = alive;
    state.gpuTelemetry.poolSize = state.gpuRenderCreatureCount;
    state.gpuTelemetry.creatureArraySize = state.creatures.length;
    state.gpuPosSyncAt = performance.now();
  }

  consumeSelectedReadback()
  {
    if (!this.selectedReadbackPending) return;
    if (state.scrubActive)
    {
      this.selectedReadbackPending = false;
      return;
    }
    const rb = state.gpuSimBuffers?.selectedReadback;
    if (!rb || rb.mapState !== 'unmapped') return;
    rb.mapAsync(GPUMapMode.READ).then(() =>
    {
      if (state.scrubActive)
      {
        if (rb.mapState === 'mapped') rb.unmap();
        this.selectedReadbackPending = false;
        return;
      }
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
      c.sex = floatData[29] > 0.5 ? 'male' : 'female';
      c.litterQ = floatData[30] || 0;
      c.gpuTargetSlot = Math.round(floatData[14]);
      const tgtSlot = c.gpuTargetSlot;
      if (typeof tgtSlot === 'number' && tgtSlot >= 0 && typeof c.gpuSlot === 'number')
      {
        const tgt = state.creatures.find(x => x.gpuSlot === tgtSlot && !x.dead);
        c.target = tgt?.id ?? null;
      }
      else
      {
        c.target = null;
      }
      if (c.dead)
      {
        const story = c.lifeStory || lifeStory.initCreature(c);
        const wasDead = !!story.snapshot?.dead;
        if (!wasDead)
        {
          const killerId = inferKillerId(c);
          if (killerId != null)
          {
            c.killedById = killerId;
            c.cause = 'predation';
            const predator = state.creatures.find(x => x.id === killerId);
            if (predator) lifeStory.recordHunted(predator, c);
          }
          else if (!c.cause || c.cause === 'exhaustion')
          {
            c.cause = refineDeathCause(c);
          }
        }
        else if (!c.cause || c.cause === 'exhaustion')
        {
          c.cause = refineDeathCause(c);
        }
      }
      lifeStory.observeFromSnapshot(c, false);
      state.gpuPosSyncAt = performance.now();
      rb.unmap();
      this.selectedReadbackPending = false;
    }).catch(() =>
    {
      this.handleGpuDeviceLost('selected-readback-map-failed', rb.mapState);
      if (rb.mapState === 'mapped') rb.unmap();
      this.selectedReadbackPending = false;
    });
  }

  syncWorldBackToCpu()
  {
    if (!state.gpuWorldBuffers || !state.gpuDevice || this.worldReadbackPending) return;
    const bytes = state.W * state.H * WORLD_STRIDE * 4;
    if (!state.gpuSimReadbackBuffer || state.gpuSimReadbackBuffer.size !== roundUp(bytes, 256))
    {
      state.gpuSimReadbackBuffer = state.gpuDevice.createBuffer({
        size: roundUp(bytes, 256),
        usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
      });
    }
    if (state.gpuSimReadbackBuffer.mapState !== 'unmapped') return;
    const enc = state.gpuDevice.createCommandEncoder();
    enc.copyBufferToBuffer(state.gpuWorldBuffers.worldData, 0, state.gpuSimReadbackBuffer, 0, bytes);
    const dev = state.gpuDevice;
    dev.queue.submit([enc.finish()]);
    this.worldReadbackPending = true;
    const rb = state.gpuSimReadbackBuffer;
    rb.mapAsync(GPUMapMode.READ).then(() =>
    {
      if (!rb)
      {
        this.worldReadbackPending = false;
        return;
      }
      const packed = new Float32Array(rb.getMappedRange());
      let stock = 0;
      let vegChanged = false;
      for (let i = 0; i < state.veg.length; i++)
      {
        const nextVeg = packed[i * WORLD_STRIDE + 2];
        if (!vegChanged && Math.abs(state.veg[i] - nextVeg) > 0.002)
        {
          vegChanged = true;
        }
        state.veg[i] = nextVeg;
        stock += nextVeg;
      }
      state.gpuTelemetry.vegetationStock = stock;
      if (vegChanged) state.vegDirty = true;
      rb.unmap();
      this.worldReadbackPending = false;
    }).catch(() =>
    {
      this.handleGpuDeviceLost('world-readback-map-failed', rb?.mapState || null);
      if (rb && rb.mapState === 'mapped') rb.unmap();
      this.worldReadbackPending = false;
    });
  }
}

export const gpuSimulationBackend = new GpuSimulationBackend();
