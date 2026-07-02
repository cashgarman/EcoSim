import { clamp, lerp } from '../utils.js';
import { SPECIES, SP_KEYS } from '../data.js';
import { state } from '../state.js';
import { creatures } from '../creatures.js';

function hslToRgb(h, s, l)
{
  let r, gg, b;
  if (s === 0) { r = gg = b = l; }
  else
  {
    const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
    const p = 2 * l - q;
    const hue = (t) =>
    {
      if (t < 0) t += 1;
      if (t > 1) t -= 1;
      if (t < 1 / 6) return p + (q - p) * 6 * t;
      if (t < 1 / 2) return q;
      if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
      return p;
    };
    r = hue(h + 1 / 3); gg = hue(h); b = hue(h - 1 / 3);
  }
  return [r * 255 | 0, gg * 255 | 0, b * 255 | 0];
}

export class CreatureRenderer
{
  constructor()
  {
    this.ctx = null;
    this.canvas = null;
    this.hlCtx = null;
    this.hlCanvas = null;
  }

  init(canvas, hlCanvas)
  {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.hlCanvas = hlCanvas || null;
    this.hlCtx = hlCanvas ? hlCanvas.getContext('2d') : null;
  }

  clearHighlightOverlay()
  {
    if (!this.hlCtx || !this.hlCanvas) return;
    this.hlCtx.clearRect(0, 0, this.hlCanvas.width, this.hlCanvas.height);
  }

  renderHighlightsOverlay(camera, vis, highlightTier)
  {
    if (!this.hlCtx || highlightTier <= 0) return;
    const prevCtx = this.ctx;
    const prevCanvas = this.canvas;
    this.ctx = this.hlCtx;
    this.canvas = this.hlCanvas;
    this.renderHighlights2d(camera, vis, highlightTier);
    this.ctx = prevCtx;
    this.canvas = prevCanvas;
  }

  creatureColor(c)
  {
    const g = c.genome, base = SPECIES[c.sp].col;
    const hueCol = hslToRgb(g.hue / 360, 0.5, 0.55);
    return [
      (base[0] * 0.7 + hueCol[0] * 0.3) | 0,
      (base[1] * 0.7 + hueCol[1] * 0.3) | 0,
      (base[2] * 0.7 + hueCol[2] * 0.3) | 0,
    ];
  }

  isPedigreeLit(c)
  {
    const focus = state.selected;
    if (!focus || focus.dead) return false;
    if (c === focus) return true;
    if (focus.parentIds.includes(c.id)) return true;
    if (focus.offspringIds.includes(c.id)) return true;
    return false;
  }

  creatureDrawBrightness(c)
  {
    if (this.isPedigreeLit(c)) return 1;
    return lerp(0.62, 1, state.lightLevel);
  }

  applyCreatureBrightness(c)
  {
    const bright = this.creatureDrawBrightness(c);
    if (bright < 0.999) this.ctx.filter = `brightness(${bright})`;
    return bright;
  }

  drawSelectionGlow(camera, sx, sy, size, strong, phaseSeed, glowRgb)
  {
    const pulse = 0.68 + 0.32 * Math.sin(state.tGlobal * 4.8 + phaseSeed * 0.4);
    const spin = state.tGlobal * (strong ? 2.8 : 2.1) + phaseSeed * 0.3;
    const radius = Math.max(8, size * (strong ? 1.28 : 1.18)) * (0.96 + pulse * 0.08);
    const rgb = glowRgb || (strong ? '242,181,62' : '255,210,120');
    const alphaBase = strong ? 0.72 : 0.52;
    const lineBase = strong ? 2.4 : 1.8;
    const ctx = this.ctx;

    ctx.save();
    ctx.shadowColor = `rgba(${rgb},0.95)`;
    ctx.shadowBlur = strong ? 14 : 10;
    ctx.lineWidth = Math.max(1.4, lineBase + state.cam.z * 0.07);
    ctx.strokeStyle = `rgba(${rgb},${alphaBase})`;
    ctx.beginPath();
    ctx.arc(sx, sy, radius, 0, Math.PI * 2);
    ctx.stroke();
    ctx.shadowBlur = strong ? 9 : 6;
    ctx.lineWidth = Math.max(1.1, (strong ? 2 : 1.5) + state.cam.z * 0.05);
    ctx.strokeStyle = `rgba(${rgb},${strong ? 0.95 : 0.74})`;
    ctx.beginPath();
    ctx.arc(sx, sy, radius * 1.26, spin, spin + Math.PI * 0.82);
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(sx, sy, radius * 1.26, spin + Math.PI, spin + Math.PI + Math.PI * 0.82);
    ctx.stroke();
    ctx.restore();
  }

  drawCreatureHighlight(camera, c, sx, sy, size)
  {
    let drawnCount = 0;
    const isLockedSpecies = state.lockedSpeciesFromPanel === c.sp;
    const isHoveredSpecies = state.hoveredGraphSpecies === c.sp;
    if ((isLockedSpecies || isHoveredSpecies) && state.selected !== c)
    {
      const rgb = isLockedSpecies ? '242,181,62' : '87,184,232';
      this.drawSelectionGlow(camera, sx, sy, size, false, c.id + 13, rgb);
      drawnCount++;
    }
    if (state.selected === c)
    {
      this.drawSelectionGlow(camera, sx, sy, size, true, c.id + 37, '242,181,62');
      drawnCount++;
    }
    return drawnCount;
  }

  drawCreatureMarker(c, sx, sy, s)
  {
    const col = this.creatureColor(c);
    this.ctx.save();
    this.applyCreatureBrightness(c);
    this.ctx.fillStyle = `rgb(${col[0]},${col[1]},${col[2]})`;
    const m = Math.max(2, s * 0.45);
    this.ctx.fillRect(sx - m * 0.5, sy - m * 0.5, m, m);
    this.ctx.restore();
  }

  drawCreature(camera, c, detailTier)
  {
    const sx = camera.w2sX(c.x), sy = camera.w2sY(c.y);
    const s = Math.max(2.5, state.cam.z * 0.9 * creatures.eSize(c));
    if (sx < -30 || sy < -30 || sx > this.canvas.width + 30 || sy > this.canvas.height + 30) return;

    this.ctx.save();
    this.applyCreatureBrightness(c);

    if (detailTier <= 0 || state.cam.z < 1.8)
    {
      const col = this.creatureColor(c);
      this.ctx.fillStyle = `rgb(${col[0]},${col[1]},${col[2]})`;
      const m = Math.max(2, s * 0.45);
      this.ctx.fillRect(sx - m * 0.5, sy - m * 0.5, m, m);
      this.ctx.restore();
      return;
    }
    if (detailTier === 1 && state.cam.z < 3.5)
    {
      const col = this.creatureColor(c);
      this.ctx.fillStyle = `rgb(${col[0]},${col[1]},${col[2]})`;
      const body = Math.max(2.2, s * 0.55);
      this.ctx.fillRect(sx - body * 0.5, sy - body * 0.4, body, body * 0.8);
      this.ctx.restore();
      return;
    }

    const col = this.creatureColor(c);
    const rgb = `rgb(${col[0]},${col[1]},${col[2]})`;
    const dk = `rgb(${col[0] * 0.6 | 0},${col[1] * 0.6 | 0},${col[2] * 0.6 | 0})`;
    const moving = Math.hypot(c.vx, c.vy) > 0.02;
    const legSw = moving ? Math.sin(c.walk) * s * 0.28 : 0;
    const shape = SPECIES[c.sp].shape;
    const ctx = this.ctx;
    ctx.save();
    ctx.translate(sx, sy);
    ctx.scale(c.dir, 1);
    const bob = moving ? Math.abs(Math.sin(c.walk)) * s * 0.06 : 0;

    if (shape === 'bird')
    {
      const flap = Math.sin(c.walk * 1.4) * s * 0.5;
      ctx.fillStyle = dk;
      ctx.fillRect(-s * 0.1, -flap - s * 0.1, s * 0.9, s * 0.22);
      ctx.fillRect(-s * 0.8, flap - s * 0.1, s * 0.9, s * 0.22);
      ctx.fillStyle = rgb;
      ctx.fillRect(-s * 0.28, -s * 0.25 - bob, s * 0.56, s * 0.5);
      ctx.fillStyle = '#f2c23a';
      ctx.fillRect(s * 0.28, -s * 0.12 - bob, s * 0.2, s * 0.14);
    }
    else
    {
      ctx.fillStyle = dk;
      ctx.fillRect(-s * 0.32, s * 0.1 - legSw, s * 0.16, s * 0.4 + legSw);
      ctx.fillRect(s * 0.16, s * 0.1 + legSw, s * 0.16, s * 0.4 - legSw);
      if (shape === 'tall' || shape === 'stocky')
      {
        ctx.fillRect(-s * 0.1, s * 0.1 + legSw * 0.6, s * 0.14, s * 0.4 - legSw * 0.6);
        ctx.fillRect(s * 0.02, s * 0.1 - legSw * 0.6, s * 0.14, s * 0.4 + legSw * 0.6);
      }
      const bl = shape === 'tall' ? 0.78 : shape === 'stocky' ? 0.95 : 0.7;
      const bh = shape === 'tall' ? 0.5 : shape === 'stocky' ? 0.62 : 0.5;
      ctx.fillStyle = rgb;
      ctx.fillRect(-s * bl / 2, -s * bh / 2 - bob, s * bl, s * bh);
      ctx.fillRect(s * (bl / 2 - 0.12), -s * 0.5 - bob, s * 0.42, s * 0.42);
      if (shape === 'small')
      {
        ctx.fillStyle = dk;
        ctx.fillRect(s * (bl / 2 + 0.05), -s * 0.85 - bob, s * 0.12, s * 0.4);
        ctx.fillRect(s * (bl / 2 + 0.22), -s * 0.85 - bob, s * 0.12, s * 0.4);
      }
      if (c.sp === 'deer')
      {
        ctx.fillStyle = dk;
        ctx.fillRect(s * (bl / 2 + 0.15), -s * 0.95 - bob, s * 0.06, s * 0.35);
      }
      ctx.fillStyle = dk;
      ctx.fillRect(-s * (bl / 2 + 0.15), -s * 0.3 - bob, s * 0.18, s * 0.3);
      ctx.fillStyle = '#111';
      ctx.fillRect(s * (bl / 2 + 0.14), -s * 0.4 - bob, s * 0.08, s * 0.08);
    }
    if (!creatures.isAdult(c))
    {
      ctx.fillStyle = 'rgba(255,255,255,.6)';
      ctx.fillRect(-s * 0.05, -s * 0.9 - bob, s * 0.1, s * 0.1);
    }
    ctx.restore();

    if (state.cam.z > 6)
    {
      const em = { flee: '❗', thirst: '💧', graze: '🌱', hunt: '🎯', mate: '❤️', rest: '💤' }[c.state];
      if (em && c.state !== 'wander')
      {
        ctx.filter = 'none';
        ctx.font = `${Math.max(6, s * 0.9)}px serif`;
        ctx.fillText(em, sx - s * 0.4, sy - s * 0.9);
      }
    }
    this.ctx.restore();
  }

  renderHighlights2d(camera, vis, highlightTier)
  {
    if (highlightTier <= 0) return;
    const ctx = this.ctx;
    for (const c of vis)
    {
      const sx = camera.w2sX(c.x), sy = camera.w2sY(c.y);
      const s = Math.max(2.5, state.cam.z * 0.9 * creatures.eSize(c));
      if (sx < -30 || sy < -30 || sx > this.canvas.width + 30 || sy > this.canvas.height + 30) continue;
      if (highlightTier === 1)
      {
        const isLockedSpecies = state.lockedSpeciesFromPanel === c.sp;
        const isHoveredSpecies = state.hoveredGraphSpecies === c.sp;
        if (isLockedSpecies || isHoveredSpecies || state.selected === c)
        {
          const rgb = state.selected === c || isLockedSpecies ? '242,181,62' : '87,184,232';
          ctx.strokeStyle = `rgba(${rgb},0.9)`;
          ctx.lineWidth = Math.max(1.1, state.cam.z * 0.08);
          ctx.beginPath();
          ctx.arc(sx, sy, Math.max(6, s * 1.05), 0, Math.PI * 2);
          ctx.stroke();
        }
        continue;
      }
      this.drawCreatureHighlight(camera, c, sx, sy, s);
    }
  }

  drawBatchMarkers(camera, vis)
  {
    const nightBright = lerp(0.62, 1, state.lightLevel);
    const buckets = { normal: [], lit: [] };
    for (const c of vis)
    {
      if (this.isPedigreeLit(c)) buckets.lit.push(c);
      else buckets.normal.push(c);
    }
    const drawBucket = (list, bright) =>
    {
      if (!list.length) return;
      const groups = {};
      for (const c of list)
      {
        if (!groups[c.sp]) groups[c.sp] = [];
        groups[c.sp].push(c);
      }
      for (const sp of SP_KEYS)
      {
        const arr = groups[sp];
        if (!arr || arr.length === 0) continue;
        const col = SPECIES[sp].col;
        this.ctx.save();
        if (bright < 0.999) this.ctx.filter = `brightness(${bright})`;
        this.ctx.fillStyle = `rgb(${col[0]},${col[1]},${col[2]})`;
        for (const c of arr)
        {
          const sx = camera.w2sX(c.x), sy = camera.w2sY(c.y);
          const s = Math.max(2, state.cam.z * 0.45 * creatures.eSize(c));
          this.ctx.fillRect(sx - s * 0.5, sy - s * 0.5, s, s);
        }
        this.ctx.restore();
      }
    };
    drawBucket(buckets.normal, nightBright);
    drawBucket(buckets.lit, 1);
  }

  drawAnimatedPedigreeLine(camera, fromX, fromY, toX, toY, rgb, phaseSeed)
  {
    const sx1 = camera.w2sX(fromX), sy1 = camera.w2sY(fromY);
    const sx2 = camera.w2sX(toX), sy2 = camera.w2sY(toY);
    const dashLen = Math.max(4, state.cam.z * 0.18);
    const gapLen = Math.max(3, state.cam.z * 0.12);
    const offset = (state.tGlobal * 42 + phaseSeed) % (dashLen + gapLen);
    const ctx = this.ctx;

    ctx.save();
    ctx.strokeStyle = `rgba(${rgb},0.88)`;
    ctx.lineWidth = Math.max(1.2, state.cam.z * 0.07);
    ctx.setLineDash([dashLen, gapLen]);
    ctx.lineDashOffset = -offset;
    ctx.beginPath();
    ctx.moveTo(sx1, sy1);
    ctx.lineTo(sx2, sy2);
    ctx.stroke();
    ctx.restore();
  }

  targetLineColorForState(st)
  {
    return {
      hunt: '245,102,72',
      flee: '255,220,108',
      mate: '236,124,214',
    }[st] || '220,220,220';
  }

  drawSelectedTargetLine(camera, focus)
  {
    if (!focus || focus.dead || focus.target == null) return;
    const target = creatures.getById(focus.target);
    if (!target || target.dead) return;
    const ctx = this.ctx;
    const sx1 = camera.w2sX(focus.x), sy1 = camera.w2sY(focus.y);
    const sx2 = camera.w2sX(target.x), sy2 = camera.w2sY(target.y);
    const rgb = this.targetLineColorForState(focus.state);

    ctx.save();
    ctx.strokeStyle = `rgba(${rgb},0.98)`;
    ctx.lineWidth = Math.max(1.4, state.cam.z * 0.09);
    ctx.shadowColor = `rgba(${rgb},0.45)`;
    ctx.shadowBlur = 5;
    ctx.beginPath();
    ctx.moveTo(sx1, sy1);
    ctx.lineTo(sx2, sy2);
    ctx.stroke();
    ctx.restore();
  }

  drawFollowPedigreeLines(camera, focus)
  {
    if (!focus || focus.dead) return;
    for (const parentId of focus.parentIds)
    {
      const parent = creatures.getById(parentId);
      if (!parent) continue;
      this.drawAnimatedPedigreeLine(camera, focus.x, focus.y, parent.x, parent.y, '255,220,60', focus.id * 17 + parentId * 3);
    }
    for (const childId of focus.offspringIds)
    {
      const child = creatures.getById(childId);
      if (!child) continue;
      this.drawAnimatedPedigreeLine(camera, focus.x, focus.y, child.x, child.y, '87,184,232', focus.id * 23 + childId * 5);
    }
  }
}

export const creatureRenderer = new CreatureRenderer();
