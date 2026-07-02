import { B, isWater } from './data.js';
import { state, idx, inB } from './state.js';

export const PASS_GROUND_BLOCKED = 1;

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

const NAV_DIRS = [
  [0, 1], [1, 0], [0, -1], [-1, 0],
  [1, 1], [1, -1], [-1, 1], [-1, -1],
];

export function planGridStep(x, y, goalX, goalY, canSwim, radius = 48)
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
  const dist = new Int16Array(cells);
  dist.fill(-1);
  const q = new Int32Array(cells);
  let qh = 0, qt = 0;

  const gi = gly * side + glx;
  dist[gi] = 0;
  q[qt++] = gi;

  while (qh < qt)
  {
    const ci = q[qh++];
    const cx = ci % side, cy = (ci / side) | 0;
    const cd = dist[ci];
    if (cd >= R) continue;
    for (const [ddx, ddy] of NAV_DIRS)
    {
      const nx = cx + ddx, ny = cy + ddy;
      if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;
      const ni = ny * side + nx;
      if (dist[ni] >= 0) continue;
      const wx = ox + nx, wy = oy + ny;
      if (!inB(wx, wy) || !isTileWalkable(wx, wy, canSwim)) continue;
      dist[ni] = cd + 1;
      q[qt++] = ni;
    }
  }

  const fi = fly * side + flx;
  if (dist[fi] >= 0)
  {
    let bestStep = null, bestD = dist[fi];
    for (const [ddx, ddy] of NAV_DIRS)
    {
      const nx = flx + ddx, ny = fly + ddy;
      if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;
      const ni = ny * side + nx;
      const nd = dist[ni];
      if (nd >= 0 && nd < bestD)
      {
        bestD = nd;
        bestStep = { x: ox + nx + 0.5, y: oy + ny + 0.5 };
      }
    }
    if (bestStep) return bestStep;
  }

  let fallback = null, fbScore = 1e9;
  for (let cy = 0; cy < side; cy++)
  {
    for (let cx = 0; cx < side; cx++)
    {
      const ni = cy * side + cx;
      if (dist[ni] < 0) continue;
      const md = Math.abs(gx - (ox + cx)) + Math.abs(gy - (oy + cy));
      const score = dist[ni] * 1000 + md;
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
