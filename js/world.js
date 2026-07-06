import { setRngSeed, ri, rf, clamp, fbm } from './utils.js';
import { B, BIOME_INFO, isWater } from './data.js';
import { state, WORLD_SIZE_PRESETS, idx, inB } from './state.js';
import { buildWaterDistanceField } from './nav.js';

export class World
{
  biomeAt(e, t, m)
  {
    const { cfg } = state;
    if (e < cfg.sea - 0.09) return B.DEEP;
    if (e < cfg.sea) return B.OCEAN;
    if (e < cfg.sea + 0.015) return B.BEACH;
    if (e > 0.86) return e > 0.93 ? B.PEAK : (t < 0.35 ? B.SNOW : B.MOUNTAIN);
    if (t < 0.22) return m < 0.5 ? B.TUNDRA : B.SNOW;
    if (t < 0.42) return m < 0.35 ? B.TUNDRA : B.TAIGA;
    if (t < 0.72)
    {
      if (m < 0.22) return B.GRASS;
      if (m < 0.5) return B.GRASS;
      if (m < 0.78) return B.FOREST;
      return B.SWAMP;
    }
    if (m < 0.25) return B.DESERT;
    if (m < 0.5) return B.SAVANNA;
    if (m < 0.78) return B.FOREST;
    return B.RAINFOREST;
  }

  computeLandBounds()
  {
    const { W, H, biome } = state;
    let minX = W, minY = H, maxX = 0, maxY = 0, found = false;
    for (let y = 0; y < H; y++)
    {
      for (let x = 0; x < W; x++)
      {
        if (isWater(biome[idx(x, y)])) continue;
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
        found = true;
      }
    }
    if (!found) { minX = 0; minY = 0; maxX = W; maxY = H; }
    else { maxX += 1; maxY += 1; }
    state.landBounds = { minX, minY, maxX, maxY };
  }

  generate(onTerrainBaked)
  {
    setRngSeed(state.SEED);
    const sz = WORLD_SIZE_PRESETS[state.cfg.size];
    state.worldAreaKm2 = sz.areaKm2;
    const sideTiles = Math.round(sz.sideKm * sz.tilesPerKm);
    state.W = sideTiles;
    state.H = sideTiles;
    state.worldKmPerTile = sz.sideKm / sideTiles;
    state.TX = state.W * state.H >= 100000 ? 1 : state.W * state.H >= 50000 ? 2 : 3;
    state.growStride = clamp(state.H / 40, 4, 12);
    state.vegBakeInterval = state.W * state.H >= 100000 ? 0.30 : state.W * state.H >= 60000 ? 0.22 : 0.15;

    const n = state.W * state.H;
    state.elev = new Float32Array(n);
    state.temp = new Float32Array(n);
    state.moist = new Float32Array(n);
    state.biome = new Uint8Array(n);
    state.veg = new Float32Array(n);
    state.vegCap = new Float32Array(n);
    state.passMask = new Uint8Array(n);

    const { cfg } = state;
    const eS = ri(1, 9999), mS = ri(1, 9999), tS = ri(1, 9999), lS = ri(1, 9999);
    const relief = cfg.relief;

    for (let y = 0; y < state.H; y++)
    {
      for (let x = 0; x < state.W; x++)
      {
        let e = fbm(x, y, eS, 5, 0.028, 0.5);
        const cx = (x / state.W - 0.5), cy = (y / state.H - 0.5);
        const radial = Math.sqrt(cx * cx + cy * cy) * 1.9;
        const falloff = Math.pow(clamp(radial, 0, 1), 2.2) * 0.34;
        e = e * (0.7 + relief * 0.5) + 0.34 - falloff;
        e += (fbm(x, y, eS + 31, 3, 0.09, 0.5) - 0.5) * 0.15 * relief;
        e = clamp(e, 0, 1);

        let m = fbm(x, y, mS, 4, 0.04, 0.55) * 0.7 + cfg.moist * 0.4;
        const lat = 1 - Math.abs(y / state.H - 0.5) * 2;
        let t = lat * 0.7 + cfg.temp * 0.5 - Math.max(0, e - cfg.sea) * 0.55 + (fbm(x, y, tS, 3, 0.05, 0.5) - 0.5) * 0.25;
        t = clamp(t, 0, 1);
        m = clamp(m, 0, 1);

        state.elev[idx(x, y)] = e;
        state.temp[idx(x, y)] = t;
        state.moist[idx(x, y)] = m;

        let b = this.biomeAt(e, t, m);
        if (!isWater(b) && e < 0.62)
        {
          const lake = fbm(x, y, lS, 3, 0.07, 0.5);
          if (lake > 0.72) { b = B.LAKE; }
        }
        state.biome[idx(x, y)] = b;
        const cap = BIOME_INFO[b].veg;
        state.vegCap[idx(x, y)] = cap;
        state.veg[idx(x, y)] = cap * rf(0.4, 1.0);
      }
    }

    this.computeLandBounds();
    buildWaterDistanceField();
    state.infWaterKey = '';
    onTerrainBaked();
  }

  growVegetation(dt)
  {
    const { W, H, growRow, growStride, veg, vegCap, moist } = state;
    const y = growRow;
    for (let x = 0; x < W; x++)
    {
      const ti = idx(x, y);
      const cap = vegCap[ti];
      if (cap > 0.02 && veg[ti] < cap)
      {
        veg[ti] = Math.min(cap, veg[ti] + cap * 0.22 * dt * growStride * (0.6 + moist[ti]));
      }
    }
    state.growRow = (growRow + 1) % H;
    if (state.growRow === 0) state.vegDirty = true;
  }
}

export const world = new World();
