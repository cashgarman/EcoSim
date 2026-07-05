import { state } from '../state.js';
import { gpuSimulationBackend } from '../gpu/simulation-backend.js';

let batchGpuInitPromise = null;

/**
 * Minimal WebGPU device init for headless batch sim (no render pipeline).
 */
export async function initBatchGpu(canvas = null)
{
  if (state.gpuDevice && gpuSimulationBackend.initialized)
  {
    return { ok: true, reason: 'already-ready' };
  }

  if (batchGpuInitPromise) return batchGpuInitPromise;

  batchGpuInitPromise = (async () =>
  {
    if (!navigator.gpu)
    {
      return { ok: false, reason: 'no-webgpu' };
    }

    const el = canvas || document.getElementById('batch-gpu-canvas');
    if (!el)
    {
      return { ok: false, reason: 'no-canvas' };
    }

    try
    {
      let adapter = await navigator.gpu.requestAdapter();
      if (!adapter)
      {
        return { ok: false, reason: 'no-adapter' };
      }

      if (!state.gpuDevice)
      {
        state.gpuDevice = await adapter.requestDevice();
      }

      if (!state.gpuContext)
      {
        state.gpuContext = el.getContext('webgpu');
        if (!state.gpuContext)
        {
          return { ok: false, reason: 'no-context' };
        }
        state.gpuCanvasFormat = navigator.gpu.getPreferredCanvasFormat();
        state.gpuContext.configure({
          device: state.gpuDevice,
          format: state.gpuCanvasFormat,
          alphaMode: 'premultiplied',
        });
      }

      state.gpuReady = true;
      if (!gpuSimulationBackend.init())
      {
        return { ok: false, reason: 'sim-init-failed' };
      }

      return { ok: true, reason: 'ready' };
    }
    catch (err)
    {
      console.warn('Batch GPU init failed', err);
      const reason = err?.message || 'init-error';
      return { ok: false, reason };
    }
  })();

  const result = await batchGpuInitPromise;
  if (!result.ok) batchGpuInitPromise = null;
  return result;
}

export function configureBatchGpuReadback()
{
  gpuSimulationBackend.readbackEveryMs = 0;
  gpuSimulationBackend.selectedReadbackEveryMs = 999999;
  gpuSimulationBackend.countersReadbackEveryMs = 0;
  gpuSimulationBackend.worldReadbackEveryTicks = 999999;
}

export async function setupBatchGpuWorld()
{
  configureBatchGpuReadback();
  const ok = gpuSimulationBackend.setupForCurrentWorld();
  if (!ok)
  {
    state.gpuSimEnabled = false;
    state.simBackend = 'cpu';
    return { ok: false, reason: 'setup-failed' };
  }
  return { ok: true };
}

export async function syncBatchGpuState()
{
  if (!state.gpuSimEnabled) return false;
  return gpuSimulationBackend.forceCreatureReadback();
}

export function teardownBatchGpu()
{
  state.gpuSimEnabled = false;
  if (state.batchConfig?.simBackend === 'gpu')
  {
    state.simBackend = 'gpu';
  }
  else
  {
    state.simBackend = 'cpu';
  }
}
