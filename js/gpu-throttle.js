import { state } from './state.js';

const STORAGE_KEY = 'ecosim-gpu-throttle';

export const GPU_THROTTLE_LEVELS = [
  { level: 0, id: 'off', label: 'Off', hint: 'Default GPU pacing' },
  { level: 1, id: 'light', label: 'Light', hint: 'Slightly fewer readbacks & GPU submits' },
  { level: 2, id: 'balanced', label: 'Balanced', hint: 'Moderate throttling — good for iteration' },
  { level: 3, id: 'heavy', label: 'Heavy', hint: 'Aggressive throttling — reduces freezes' },
  { level: 4, id: 'eco', label: 'Eco', hint: 'Minimal GPU sync — fastest, UI may lag' },
];

const READBACK_MUL = [1, 2, 3.5, 6, 12];
const RENDER_SKIP = [1, 2, 2, 3, 4];
const MIN_QUALITY_TIER = [0, 0, 1, 2, 3];
const READBACK_TICK_MOD = [30, 50, 70, 100, 150];
const BATCH_SYNC_EVERY = [1, 8, 20, 40, 80];

function clampLevel(level)
{
  const n = Math.round(Number(level));
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(4, n));
}

export function loadGpuThrottleFromStorage()
{
  try
  {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw == null || raw === '') return 0;
    return clampLevel(raw);
  }
  catch
  {
    return 0;
  }
}

export function initGpuThrottle()
{
  state.gpuThrottleLevel = loadGpuThrottleFromStorage();
}

export function getGpuThrottleLevel()
{
  return clampLevel(state.gpuThrottleLevel ?? 0);
}

export function setGpuThrottleLevel(level)
{
  const next = clampLevel(level);
  state.gpuThrottleLevel = next;
  try
  {
    localStorage.setItem(STORAGE_KEY, String(next));
  }
  catch { /* ignore */ }
  window.dispatchEvent(new CustomEvent('ecosim-gpu-throttle-change', { detail: { level: next } }));
  return next;
}

export function gpuThrottleLabel(level = getGpuThrottleLevel())
{
  return GPU_THROTTLE_LEVELS[clampLevel(level)]?.label ?? 'Off';
}

export function gpuThrottleReadbackMul(level = getGpuThrottleLevel())
{
  return READBACK_MUL[clampLevel(level)];
}

export function gpuThrottleRenderSkipFrames(level = getGpuThrottleLevel())
{
  return RENDER_SKIP[clampLevel(level)];
}

export function gpuThrottleMinQualityTier(level = getGpuThrottleLevel())
{
  return MIN_QUALITY_TIER[clampLevel(level)];
}

export function gpuThrottleReadbackTickMod(level = getGpuThrottleLevel())
{
  return READBACK_TICK_MOD[clampLevel(level)];
}

export function batchGpuSyncEveryTicks(level = getGpuThrottleLevel())
{
  return BATCH_SYNC_EVERY[clampLevel(level)];
}

export function gpuThrottleDisablesExtrapolation(level = getGpuThrottleLevel())
{
  return clampLevel(level) >= 3;
}

export function shouldSkipGpuRenderFrame(frameCounter, level = getGpuThrottleLevel())
{
  const skip = gpuThrottleRenderSkipFrames(level);
  if (skip <= 1) return false;
  return (frameCounter % skip) !== 0;
}
