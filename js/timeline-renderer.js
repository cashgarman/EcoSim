import { lightLevelFromTimeOfDay } from './utils.js';
import { timeOfDayAtSimT } from './timeline-viewport.js';
import { effectiveSnapshotIntervalSec } from './perf-policy.js';

const NIGHT_RGB = [20, 26, 51];
const DAY_RGB = [106, 138, 176];

function lightToRgb(light)
{
  const t = clamp01(light);
  return [
    Math.round(NIGHT_RGB[0] + (DAY_RGB[0] - NIGHT_RGB[0]) * t),
    Math.round(NIGHT_RGB[1] + (DAY_RGB[1] - NIGHT_RGB[1]) * t),
    Math.round(NIGHT_RGB[2] + (DAY_RGB[2] - NIGHT_RGB[2]) * t),
  ];
}

function clamp01(v)
{
  return v < 0 ? 0 : v > 1 ? 1 : v;
}

export function renderTimelineDayNight(canvas, viewport, originTimeOfDay = 0.3)
{
  if (!canvas || !viewport) return;
  const track = canvas.parentElement;
  if (!track) return;
  const cssW = Math.max(1, track.clientWidth);
  const cssH = Math.max(1, track.clientHeight);
  const dpr = window.devicePixelRatio || 1;
  const pxW = Math.max(1, Math.round(cssW * dpr));
  const pxH = Math.max(1, Math.round(cssH * dpr));
  if (canvas.width !== pxW || canvas.height !== pxH)
  {
    canvas.width = pxW;
    canvas.height = pxH;
    canvas.style.width = `${cssW}px`;
    canvas.style.height = `${cssH}px`;
  }
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  const image = ctx.createImageData(pxW, pxH);
  const data = image.data;
  for (let x = 0; x < pxW; x++)
  {
    const ratio = pxW > 1 ? x / (pxW - 1) : 0;
    const t = viewport.ratioToT(ratio);
    const tod = timeOfDayAtSimT(t, originTimeOfDay);
    const light = lightLevelFromTimeOfDay(tod);
    const rgb = lightToRgb(light);
    for (let y = 0; y < pxH; y++)
    {
      const i = (y * pxW + x) * 4;
      data[i] = rgb[0];
      data[i + 1] = rgb[1];
      data[i + 2] = rgb[2];
      data[i + 3] = 255;
    }
  }
  ctx.putImageData(image, 0, 0);
}

export function buildVisibleTickTimes(viewport, trackWidthPx)
{
  const interval = effectiveSnapshotIntervalSec();
  if (!viewport || interval <= 0) return [];
  const lo = viewport.startT;
  const hi = viewport.endT;
  if (hi <= lo + 1e-6) return [];
  let firstTick = Math.ceil(lo / interval - 1e-9) * interval;
  if (firstTick < 0) firstTick = 0;
  const ticks = [];
  const maxTicks = trackWidthPx > 0 ? Math.max(8, Math.floor(trackWidthPx / 2)) : 120;
  let t = firstTick;
  while (t <= hi + 0.001)
  {
    ticks.push(t);
    t += interval;
  }
  if (ticks.length <= maxTicks) return ticks;
  const group = Math.ceil(ticks.length / maxTicks);
  const sampled = [];
  for (let i = 0; i < ticks.length; i += group) sampled.push(ticks[i]);
  return sampled;
}
