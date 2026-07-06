import { B, isWater } from './data.js';
import { state, idx, inB } from './state.js';
import { perfProfiler } from './perf-profiler.js';

export const PASS_GROUND_BLOCKED = 1;

const NAV_DIRS = [
  [0, 1, 10], [1, 0, 10], [0, -1, 10], [-1, 0, 10],
  [1, 1, 14], [1, -1, 14], [-1, 1, 14], [-1, -1, 14],
];

let astarGScore = null;
let astarFScore = null;
let astarParent = null;
let astarClosed = null;
let astarHeapIdx = null;
let astarHeapVal = null;

function ensureAstarBuffers(cells)
{
  if (!astarGScore || astarGScore.length < cells)
  {
    astarGScore = new Int32Array(cells);
    astarFScore = new Int32Array(cells);
    astarParent = new Int32Array(cells);
    astarClosed = new Uint8Array(cells);
    astarHeapIdx = new Int32Array(cells);
    astarHeapVal = new Int32Array(cells);
  }
}

function octileHeuristic(ax, ay, bx, by)
{
  const dx = Math.abs(ax - bx);
  const dy = Math.abs(ay - by);
  const mn = Math.min(dx, dy);
  const mx = Math.max(dx, dy);
  return 14 * mn + 10 * (mx - mn);
}

function heapPush(heapSize, cellIdx, fScore)
{
  let i = heapSize;
  astarHeapIdx[i] = cellIdx;
  astarHeapVal[i] = fScore;
  while (i > 0)
  {
    const p = (i - 1) >> 1;
    if (astarHeapVal[p] <= astarHeapVal[i]) break;
    const ti = astarHeapIdx[p];
    const tv = astarHeapVal[p];
    astarHeapIdx[p] = astarHeapIdx[i];
    astarHeapVal[p] = astarHeapVal[i];
    astarHeapIdx[i] = ti;
    astarHeapVal[i] = tv;
    i = p;
  }
  return heapSize + 1;
}

function heapPop(heapSize)
{
  const cellIdx = astarHeapIdx[0];
  const last = heapSize - 1;
  astarHeapIdx[0] = astarHeapIdx[last];
  astarHeapVal[0] = astarHeapVal[last];
  let i = 0;
  while (true)
  {
    const l = i * 2 + 1;
    const r = l + 1;
    let smallest = i;
    if (l < last && astarHeapVal[l] < astarHeapVal[smallest]) smallest = l;
    if (r < last && astarHeapVal[r] < astarHeapVal[smallest]) smallest = r;
    if (smallest === i) break;
    const ti = astarHeapIdx[i];
    const tv = astarHeapVal[i];
    astarHeapIdx[i] = astarHeapIdx[smallest];
    astarHeapVal[i] = astarHeapVal[smallest];
    astarHeapIdx[smallest] = ti;
    astarHeapVal[smallest] = tv;
    i = smallest;
  }
  return { cellIdx, heapSize: last };
}

function diagonalBlocked(cx, cy, ddx, ddy, ox, oy, canSwim)
{
  if (Math.abs(ddx) + Math.abs(ddy) !== 2) return false;
  const wx1 = ox + cx + ddx;
  const wy1 = oy + cy + ddy;
  const wx2 = ox + cx;
  const wy2 = oy + cy;
  if (!isTileWalkable(wx1, wy2, canSwim)) return true;
  if (!isTileWalkable(wx2, wy1, canSwim)) return true;
  return false;
}

export function isBiomePassable(biomeId, canSwim)
{
  if (biomeId === B.PEAK) return false;
  if (isWater(biomeId)) return canSwim;
  return true;
}

export function tilePassFlags(tileIndex)
{
  if (tileIndex < 0 || tileIndex >= state.biome.length) return PASS_GROUND_BLOCKED;
  const b = state.biome[tileIndex];
  let flags = 0;
  if (isWater(b) || b === B.PEAK) flags |= PASS_GROUND_BLOCKED;
  if (state.passMask && (state.passMask[tileIndex] & PASS_GROUND_BLOCKED)) flags |= PASS_GROUND_BLOCKED;
  return flags;
}

export function isTileWalkable(tx, ty, canSwim)
{
  if (!inB(tx, ty)) return false;
  const ti = idx(tx, ty);
  const b = state.biome[ti];
  if (b === B.PEAK) return false;
  if (isWater(b)) return canSwim;
  if (state.passMask && (state.passMask[ti] & PASS_GROUND_BLOCKED)) return false;
  return true;
}

export function atWaterEdge(x, y)
{
  const ix = Math.round(x), iy = Math.round(y);
  for (let dy = -1; dy <= 1; dy++)
  {
    for (let dx = -1; dx <= 1; dx++)
    {
      const nx = ix + dx, ny = iy + dy;
      if (inB(nx, ny) && isWater(state.biome[idx(nx, ny)])) return true;
    }
  }
  return false;
}

export function lineOfSightClear(x0, y0, x1, y1, canSwim)
{
  let x = Math.round(x0), y = Math.round(y0);
  const tx = Math.round(x1), ty = Math.round(y1);
  let dx = Math.abs(tx - x), dy = Math.abs(ty - y);
  let sx = x < tx ? 1 : -1, sy = y < ty ? 1 : -1;
  let err = dx - dy;
  while (true)
  {
    if (!isTileWalkable(x, y, canSwim)) return false;
    if (x === tx && y === ty) return true;
    const e2 = err * 2;
    if (e2 > -dy) { err -= dy; x += sx; }
    if (e2 < dx) { err += dx; y += sy; }
  }
}

export function snapWalkableGoal(gx, gy, canSwim, radius = 8)
{
  if (isTileWalkable(gx, gy, canSwim)) return { x: gx, y: gy };
  let best = null, bd = radius * radius + 1;
  for (let dy = -radius; dy <= radius; dy++)
  {
    for (let dx = -radius; dx <= radius; dx++)
    {
      const lx = gx + dx, ly = gy + dy;
      if (!inB(lx, ly) || !isTileWalkable(lx, ly, canSwim)) continue;
      const d = dx * dx + dy * dy;
      if (d < bd) { bd = d; best = { x: lx, y: ly }; }
    }
  }
  return best;
}

export const DIRECT_PURSUIT_RADIUS = 4;

/** Live float goal — snap only when the entity tile is impassable. */
export function unsnappedWalkableGoal(gx, gy, canSwim)
{
  const tx = Math.round(gx);
  const ty = Math.round(gy);
  if (isTileWalkable(tx, ty, canSwim)) return { x: gx, y: gy };
  const sn = snapWalkableGoal(tx, ty, canSwim, 8);
  if (sn) return { x: sn.x + 0.5, y: sn.y + 0.5 };
  return { x: gx, y: gy };
}

/**
 * Pick movement target for this tick: direct pursuit when close/LOS, else one A* grid step.
 */
export function resolveMovementTarget(px, py, goalX, goalY, canSwim, radius = 48, opts = {})
{
  const direct = opts.direct === true;
  const directRadius = opts.directRadius ?? DIRECT_PURSUIT_RADIUS;
  const dist = Math.hypot(goalX - px, goalY - py);

  if (direct && dist <= directRadius) return { x: goalX, y: goalY };

  const fx = Math.round(px);
  const fy = Math.round(py);
  const gx = Math.round(goalX);
  const gy = Math.round(goalY);
  if (lineOfSightClear(fx, fy, gx, gy, canSwim)) return { x: goalX, y: goalY };

  return planGridStep(px, py, goalX, goalY, canSwim, radius);
}

export const WATER_SEEK_RADIUS_MIN = 48;

export function waterSeekRadius(senseR)
{
  return Math.max(senseR + 6, WATER_SEEK_RADIUS_MIN);
}

export function nearestWaterEdgeTarget(x, y, r)
{
  let best = null, bd = r * r;
  const ix = Math.round(x), iy = Math.round(y);
  for (let dy = -r; dy <= r; dy++)
  {
    for (let dx = -r; dx <= r; dx++)
    {
      const lx = ix + dx, ly = iy + dy;
      if (!inB(lx, ly)) continue;
      if (!isTileWalkable(lx, ly, false)) continue;
      let nearWater = false;
      for (let wy = -1; wy <= 1; wy++)
      {
        for (let wx = -1; wx <= 1; wx++)
        {
          const nx = lx + wx, ny = ly + wy;
          if (inB(nx, ny) && isWater(state.biome[idx(nx, ny)]))
          {
            nearWater = true;
            break;
          }
        }
        if (nearWater) break;
      }
      if (!nearWater) continue;
      const d = dx * dx + dy * dy;
      if (d < bd) { bd = d; best = { x: lx + 0.5, y: ly + 0.5 }; }
    }
  }
  return best;
}

export function pickRandomWalkableTile(cx, cy, spread, canSwim)
{
  for (let tries = 0; tries < 12; tries++)
  {
    const lx = Math.round(cx + (Math.random() * 2 - 1) * spread);
    const ly = Math.round(cy + (Math.random() * 2 - 1) * spread);
    if (inB(lx, ly) && isTileWalkable(lx, ly, canSwim))
    {
      return { x: lx + 0.5, y: ly + 0.5 };
    }
  }
  return { x: cx, y: cy };
}

export function planGridStep(x, y, goalX, goalY, canSwim, radius = 48)
{
  return perfProfiler.scope('nav.planGridStep', () => _planGridStep(x, y, goalX, goalY, canSwim, radius));
}

function _planGridStep(x, y, goalX, goalY, canSwim, radius = 48)
{
  const R = Math.max(8, Math.min(64, radius | 0));
  let gx = Math.round(goalX), gy = Math.round(goalY);
  const snapped = snapWalkableGoal(gx, gy, canSwim, 8);
  if (snapped) { gx = snapped.x; gy = snapped.y; }
  else return null;

  const fx = Math.round(x), fy = Math.round(y);
  if (fx === gx && fy === gy) return { x: gx + 0.5, y: gy + 0.5 };
  if (Math.hypot(gx - fx, gy - fy) < 1.5) return { x: gx + 0.5, y: gy + 0.5 };
  if (lineOfSightClear(fx, fy, gx, gy, canSwim)) return { x: goalX, y: goalY };

  const side = R * 2 + 1;
  const ox = clampWindowOrigin(Math.floor((fx + gx) * 0.5) - R, state.W - side);
  const oy = clampWindowOrigin(Math.floor((fy + gy) * 0.5) - R, state.H - side);
  const glx = gx - ox, gly = gy - oy;
  const flx = fx - ox, fly = fy - oy;
  if (glx < 0 || gly < 0 || glx >= side || gly >= side) return { x: gx + 0.5, y: gy + 0.5 };
  if (flx < 0 || fly < 0 || flx >= side || fly >= side) return { x: gx + 0.5, y: gy + 0.5 };

  const cells = side * side;
  ensureAstarBuffers(cells);
  astarGScore.fill(2147483647, 0, cells);
  astarFScore.fill(2147483647, 0, cells);
  astarParent.fill(-1, 0, cells);
  astarClosed.fill(0, 0, cells);

  const start = fly * side + flx;
  const goal = gly * side + glx;
  astarGScore[start] = 0;
  astarFScore[start] = octileHeuristic(flx, fly, glx, gly);
  let heapSize = heapPush(0, start, astarFScore[start]);

  while (heapSize > 0)
  {
    const popped = heapPop(heapSize);
    heapSize = popped.heapSize;
    const ci = popped.cellIdx;
    if (astarClosed[ci]) continue;
    astarClosed[ci] = 1;

    if (ci === goal)
    {
      let step = ci;
      let prev = astarParent[step];
      while (prev >= 0 && prev !== start)
      {
        step = prev;
        prev = astarParent[step];
      }
      const sx = step % side;
      const sy = (step / side) | 0;
      return { x: ox + sx + 0.5, y: oy + sy + 0.5 };
    }

    const cx = ci % side;
    const cy = (ci / side) | 0;
    const cg = astarGScore[ci];
    if (cg >= R * 14) continue;

    for (const [ddx, ddy, cost] of NAV_DIRS)
    {
      const nx = cx + ddx;
      const ny = cy + ddy;
      if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;
      const wx = ox + nx;
      const wy = oy + ny;
      if (!inB(wx, wy) || !isTileWalkable(wx, wy, canSwim)) continue;
      if (diagonalBlocked(cx, cy, ddx, ddy, ox, oy, canSwim)) continue;

      const ni = ny * side + nx;
      if (astarClosed[ni]) continue;
      const tg = cg + cost;
      if (tg >= astarGScore[ni]) continue;

      astarGScore[ni] = tg;
      astarParent[ni] = ci;
      const tf = tg + octileHeuristic(nx, ny, glx, gly);
      astarFScore[ni] = tf;
      heapSize = heapPush(heapSize, ni, tf);
    }
  }

  let fallback = null;
  let fbScore = 2147483647;
  for (let cy = 0; cy < side; cy++)
  {
    for (let cx = 0; cx < side; cx++)
    {
      const ni = cy * side + cx;
      const g = astarGScore[ni];
      if (g >= 2147483647) continue;
      const md = Math.abs(gx - (ox + cx)) + Math.abs(gy - (oy + cy));
      const score = g * 1000 + md;
      if (score < fbScore)
      {
        fbScore = score;
        fallback = { x: ox + cx + 0.5, y: oy + cy + 0.5 };
      }
    }
  }
  return fallback;
}

function clampWindowOrigin(origin, maxOrigin)
{
  if (origin < 0) return 0;
  if (origin > maxOrigin) return Math.max(0, maxOrigin);
  return origin;
}

export function buildPassMaskUpload()
{
  const n = state.W * state.H;
  const packed = new Float32Array(n);
  for (let i = 0; i < n; i++)
  {
    packed[i] = tilePassFlags(i);
  }
  return packed;
}

export function setTilePassBlocked(x, y, blocked = true)
{
  if (!inB(x, y) || !state.passMask) return;
  const ti = idx(x, y);
  if (blocked) state.passMask[ti] |= PASS_GROUND_BLOCKED;
  else state.passMask[ti] &= ~PASS_GROUND_BLOCKED;
}
