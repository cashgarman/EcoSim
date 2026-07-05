import { clamp, lerp, hashN } from '../utils.js';
import { B, BIOME_INFO, isWater } from '../data.js';
import { state, idx, inB } from '../state.js';
import { quality } from './quality.js';

function shade(c, m)
{
  return [clamp(c[0] * m, 0, 255) | 0, clamp(c[1] * m, 0, 255) | 0, clamp(c[2] * m, 0, 255) | 0];
}

function wavyWavePhase(px, py, f, period, tileOff, wobbleAmp, wobbleFreq, tileNoise)
{
  const dirX = 1, dirY = 0.52;
  const along = px * dirX + py * dirY;
  const cross = px * (-dirY) + py * dirX;
  const frontCurve = Math.sin(cross * wobbleFreq + f * 0.07) * wobbleAmp;
  return (along + frontCurve + tileNoise + tileOff + f) % period;
}

function waveBandFromPhase(pos, period, power)
{
  return Math.pow(Math.cos(pos / period * 2 * Math.PI) * 0.5 + 0.5, power);
}

function openWaterShimmer(px, py, f, depthPeriod, depthAmp, tileOff, patch, noise1)
{
  const pos = wavyWavePhase(px, py, f, depthPeriod, tileOff, 5.5, 0.072, noise1);
  return waveBandFromPhase(pos, depthPeriod, 4) * patch * depthAmp;
}

export class TerrainRenderer
{
  constructor()
  {
    this.ctx = null;
    this.canvas = null;
    this._scrubVegBakeAt = 0;
  }

  init(canvas)
  {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.ctx.imageSmoothingEnabled = false;
  }

  bakeTerrain()
  {
    const { W, H, TX, elev, biome } = state;
    state.terrC = document.createElement('canvas');
    state.terrC.width = W * TX;
    state.terrC.height = H * TX;
    state.tctx = state.terrC.getContext('2d');
    state.waterC = document.createElement('canvas');
    state.waterC.width = W;
    state.waterC.height = H;
    state.wctx = state.waterC.getContext('2d');

    const img = state.tctx.createImageData(W * TX, H * TX), d = img.data;
    for (let y = 0; y < H; y++)
    {
      for (let x = 0; x < W; x++)
      {
        const b = biome[idx(x, y)], info = BIOME_INFO[b], e = elev[idx(x, y)];
        for (let sy = 0; sy < TX; sy++)
        {
          for (let sx = 0; sx < TX; sx++)
          {
            const px = x * TX + sx, py = y * TX + sy, h = hashN(px, py, 7);
            let c = info.col.slice();
            const shd = 0.9 + (e - 0.5) * 0.28 + (h - 0.5) * 0.14;
            c = shade(c, shd);
            if (b === B.FOREST || b === B.RAINFOREST || b === B.TAIGA)
            {
              if (h > 0.7) c = shade(info.col, 0.78);
              else if (h < 0.15) c = shade(info.col, 1.12);
            }
            else if (b === B.DESERT || b === B.BEACH || b === B.SAVANNA)
            {
              if (h > 0.9) c = shade(info.col, 0.9);
              if (h < 0.05) c = shade(info.col, 1.1);
            }
            else if (b === B.GRASS || b === B.SHRUB)
            {
              if (h > 0.85) c = shade(info.col, 0.85);
            }
            else if (b === B.MOUNTAIN)
            {
              if (h > 0.7) c = shade(info.col, 1.15);
              if (h < 0.3) c = shade(info.col, 0.82);
            }
            else if (b === B.SNOW || b === B.PEAK)
            {
              if (h > 0.85) c = [210, 222, 232];
            }
            const o = (py * W * TX + px) * 4;
            d[o] = c[0]; d[o + 1] = c[1]; d[o + 2] = c[2]; d[o + 3] = 255;
          }
        }
      }
    }
    state.tctx.putImageData(img, 0, 0);
  }

  deepTerrainColor(wx, wy)
  {
    const info = BIOME_INFO[B.DEEP];
    const h = hashN(wx * state.TX, wy * state.TX, 7);
    let e = state.cfg.sea - 0.12;
    if (state.elev && state.biome)
    {
      let ex = clamp(Math.floor(wx), 0, state.W - 1);
      let ey = clamp(Math.floor(wy), 0, state.H - 1);
      if (wx < 0) ex = 0;
      else if (wx >= state.W) ex = state.W - 1;
      if (wy < 0) ey = 0;
      else if (wy >= state.H) ey = state.H - 1;
      if (isWater(state.biome[idx(ex, ey)])) e = state.elev[idx(ex, ey)];
    }
    const shd = 0.9 + (e - 0.5) * 0.28 + (h - 0.5) * 0.14;
    return shade(info.col, shd);
  }

  sampleDeepWaterPixel(wx, wy, f)
  {
    const info = BIOME_INFO[B.DEEP];
    const depthAmp = 0.48;
    const depthPeriod = 56;
    const th = hashN(wx, wy, 3);
    const tileOff = Math.floor(th * depthPeriod);
    const patch = 0.8 + 0.2 * hashN(wx, wy, 43);
    const noise1 = (hashN(wx, wy, 17) - 0.5) * 2.75;
    const px = wx * state.TX + 1, py = wy * state.TX + 1;
    let a = openWaterShimmer(px, py, f, depthPeriod, depthAmp, tileOff, patch, noise1);
    const slowPos = wavyWavePhase(px, py, f * 0.65, depthPeriod * 1.15, Math.floor(th * depthPeriod * 1.1), 4.2, 0.06, (hashN(wx, wy, 31) - 0.5) * 2.1);
    a = Math.max(a, waveBandFromPhase(slowPos, depthPeriod * 1.15, 3) * 0.22);
    const waterCol = [info.col[0] + 38, info.col[1] + 34, info.col[2] + 28];
    return { waterCol, a };
  }

  sampleDeepWaterOpaque(wx, wy, f)
  {
    const { waterCol, a } = this.sampleDeepWaterPixel(wx, wy, f);
    const terrCol = this.deepTerrainColor(wx, wy);
    return [
      lerp(terrCol[0], waterCol[0], a) | 0,
      lerp(terrCol[1], waterCol[1], a) | 0,
      lerp(terrCol[2], waterCol[2], a) | 0,
    ];
  }

  bakeWater(frameTime)
  {
    const q = quality.config();
    const baseWaterFps = state.W * state.H >= 100000 ? 3 : state.W * state.H >= 60000 ? 5 : 8;
    const waterFps = Math.max(2, Math.round(baseWaterFps * q.waterMul));
    const prevStep = state.waterFrameAt;
    const rawStep = Math.floor(frameTime * waterFps);
    const fStep = prevStep >= 0 ? Math.max(prevStep, rawStep) : rawStep;
    if (fStep === state.waterFrameAt) return;
    state.waterFrameAt = fStep;
    const f = fStep;
    const t = frameTime;
    const { W, H, wctx, biome } = state;
    wctx.clearRect(0, 0, W, H);
    const img = wctx.createImageData(W, H), d = img.data;
    const period = 48;

    for (let y = 0; y < H; y++)
    {
      for (let x = 0; x < W; x++)
      {
        const b = biome[idx(x, y)];
        if (!isWater(b)) continue;
        const info = BIOME_INFO[b];
        const isDeep = b === B.DEEP;
        const isShallow = b === B.OCEAN || b === B.LAKE;
        let lx = 0, ly = 0;
        for (let dy = -1; dy <= 1; dy++)
        {
          for (let dx = -1; dx <= 1; dx++)
          {
            if (!dx && !dy) continue;
            const ax = x + dx, ay = y + dy;
            if (inB(ax, ay) && !isWater(biome[idx(ax, ay)])) { lx -= dx; ly -= dy; }
          }
        }
        const landLen = Math.hypot(lx, ly);
        const nearLand = landLen > 0.01;
        let snx = 0.707, sny = 0.707, stx = -0.707, sty = 0.707;
        if (nearLand) { snx = lx / landLen; sny = ly / landLen; stx = -sny; sty = snx; }
        const shoreStrength = nearLand ? clamp(landLen / 3, 0, 1) : 0;
        const depthAmp = isDeep ? 0.48 : isShallow ? 0.62 : 0.54;
        const depthPeriod = isDeep ? 56 : isShallow ? 42 : period;
        const th = hashN(x, y, 3);
        const tileOff = Math.floor(th * depthPeriod);
        const patch = 0.8 + 0.2 * hashN(x, y, 43);
        const noise1 = (hashN(x, y, 17) - 0.5) * 2.75;
        const px = x * state.TX + 1, py = y * state.TX + 1;
        let a, col;
        if (isDeep)
        {
          const deep = this.sampleDeepWaterPixel(x, y, f);
          a = deep.a;
          col = deep.waterCol;
        }
        else
        {
          a = openWaterShimmer(px, py, f, depthPeriod, depthAmp, tileOff, patch, noise1);
          col = [info.col[0] + 50, info.col[1] + 45, info.col[2] + 35];
          if (isShallow)
          {
            const fastPos = wavyWavePhase(px, py, f * 1.12, depthPeriod * 0.78, Math.floor(th * depthPeriod * 0.85), 6, 0.085, (hashN(x, y, 37) - 0.5) * 3);
            a = Math.max(a, waveBandFromPhase(fastPos, depthPeriod * 0.78, 2.5) * 0.26);
          }
        }
        if (nearLand)
        {
          const along = px * stx + py * sty;
          const wavePhase = along * 0.18 - t * 1.15;
          const crest = Math.pow(Math.sin(wavePhase) * 0.5 + 0.5, 2.2);
          const ripple = Math.pow(Math.sin(wavePhase * 0.55 + px * snx * 0.12 - py * sny * 0.08 - t * 0.65) * 0.5 + 0.5, 2.5);
          const foam = crest * 0.55 + ripple * 0.45;
          const foamA = 0.18 + foam * (0.35 + shoreStrength * 0.4);
          if (foamA > a)
          {
            a = foamA;
            const mix = foam * shoreStrength;
            col = [
              lerp(info.col[0] + 30, 228, mix),
              lerp(info.col[1] + 28, 244, mix),
              lerp(info.col[2] + 22, 252, mix),
            ];
          }
        }
        const o = idx(x, y) * 4;
        d[o] = col[0] | 0; d[o + 1] = col[1] | 0; d[o + 2] = col[2] | 0; d[o + 3] = (a * 255) | 0;
      }
    }
    wctx.putImageData(img, 0, 0);
  }

  drawInfiniteOcean(camera)
  {
    this.bakeWater(state.tGlobal);
    const f = state.waterFrameAt;
    if (f < 0) return;

    const { cam } = state;
    const pad = 6;
    const x0 = Math.floor(cam.x - pad);
    const y0 = Math.floor(cam.y - pad);
    const x1 = Math.ceil(cam.x + this.canvas.width / cam.z + pad);
    const y1 = Math.ceil(cam.y + this.canvas.height / cam.z + pad);
    const iw = x1 - x0, ih = y1 - y0;
    if (iw <= 0 || ih <= 0) return;

    const key = `${x0},${y0},${iw},${ih},${f},${state.W},${state.H},${state.TX}`;
    if (key !== state.infWaterKey)
    {
      state.infWaterKey = key;
      if (!state.infWaterC || state.infWaterC.width !== iw || state.infWaterC.height !== ih)
      {
        state.infWaterC = document.createElement('canvas');
        state.infWaterC.width = iw;
        state.infWaterC.height = ih;
        state.infWaterCtx = state.infWaterC.getContext('2d');
      }
      const img = state.infWaterCtx.createImageData(iw, ih), d = img.data;
      for (let j = 0; j < ih; j++)
      {
        for (let i = 0; i < iw; i++)
        {
          const wx = x0 + i, wy = y0 + j;
          const o = (j * iw + i) * 4;
          if (wx >= 0 && wx < state.W && wy >= 0 && wy < state.H) { d[o + 3] = 0; continue; }
          const rgb = this.sampleDeepWaterOpaque(wx, wy, f);
          d[o] = rgb[0] | 0; d[o + 1] = rgb[1] | 0; d[o + 2] = rgb[2] | 0; d[o + 3] = 255;
        }
      }
      state.infWaterCtx.putImageData(img, 0, 0);
    }

    const ctx = this.ctx;
    ctx.save();
    ctx.setTransform(cam.z, 0, 0, cam.z, -cam.x * cam.z, -cam.y * cam.z);
    ctx.drawImage(state.infWaterC, x0, y0, iw, ih);
    ctx.restore();
  }

  bakeVegetation()
  {
    const { W, H, biome, veg, vegCap } = state;
    if (!state.vegC)
    {
      state.vegC = document.createElement('canvas');
      state.vegC.width = W;
      state.vegC.height = H;
      state.vgctx = state.vegC.getContext('2d');
    }
    const img = state.vgctx.createImageData(W, H), d = img.data;
    for (let y = 0; y < H; y++)
    {
      for (let x = 0; x < W; x++)
      {
        const b = biome[idx(x, y)];
        const o = idx(x, y) * 4;
        if (isWater(b) || vegCap[idx(x, y)] < 0.02) { d[o + 3] = 0; continue; }
        const v = veg[idx(x, y)] / Math.max(0.02, vegCap[idx(x, y)]);
        const g = lerp(70, 150, v), r = lerp(120, 40, v) * 0.6, bl = lerp(60, 40, v);
        d[o] = r | 0; d[o + 1] = g | 0; d[o + 2] = bl | 0; d[o + 3] = (v * 120) | 0;
      }
    }
    state.vgctx.putImageData(img, 0, 0);
    state.vegDirty = false;
  }

  renderStage(camera)
  {
    this.drawInfiniteOcean(camera);
    const ctx = this.ctx;
    ctx.imageSmoothingEnabled = false;
    const { cam, W, H } = state;
    const ox = -cam.x * cam.z, oy = -cam.y * cam.z;
    if (state.terrC) ctx.drawImage(state.terrC, ox, oy, W * cam.z, H * cam.z);
    if (state.vegC)
    {
      if (state.vegDirty && state.vegRedrawOK && state.vegBakeCd <= 0)
      {
        const scrubBlocked = state.scrubActive
          && (performance.now() - this._scrubVegBakeAt) < 320;
        if (!scrubBlocked)
        {
          this.bakeVegetation();
          state.vegBakeCd = state.vegBakeInterval;
          if (state.scrubActive) this._scrubVegBakeAt = performance.now();
        }
      }
      ctx.drawImage(state.vegC, ox, oy, W * cam.z, H * cam.z);
    }
    this.bakeWater(state.tGlobal);
    if (state.waterC) ctx.drawImage(state.waterC, ox, oy, W * cam.z, H * cam.z);
  }
}

export const terrainRenderer = new TerrainRenderer();
