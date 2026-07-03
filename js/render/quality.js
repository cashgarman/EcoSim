import { lerp } from '../utils.js';
import { state } from '../state.js';
import { $ } from '../dom.js';

export class QualityController
{
  constructor()
  {
    this.frameMsAvg = 16.7;
    this.tier = 0;
    this.renderDecimation = 1;
    this.frameCounter = 0;
    this.perfHudEnabled = true;
    this.perfHudLastMs = 16.7;
  }

  config()
  {
    if (this.tier === 0) return { detail: 2, highlight: 2, waterMul: 1, vegMul: 1, decimation: 1, navRadius: 64, navReplanInterval: 8 };
    if (this.tier === 1) return { detail: 1, highlight: 1, waterMul: 0.8, vegMul: 1.35, decimation: 1, navRadius: 48, navReplanInterval: 8 };
    if (this.tier === 2) return { detail: 1, highlight: 0, waterMul: 0.6, vegMul: 1.8, decimation: 1, navRadius: 48, navReplanInterval: 12 };
    return { detail: 0, highlight: 0, waterMul: 0.45, vegMul: 2.3, decimation: 2, navRadius: 32, navReplanInterval: 16 };
  }

  effectiveHighlight(baseHighlight)
  {
    const panelFocus = state.hoveredGraphSpecies || state.lockedSpeciesFromPanel;
    const hasSelection = state.selected && !state.selected.dead;
    if (panelFocus || hasSelection) return Math.max(baseHighlight, 1);
    return baseHighlight;
  }

  updateTier(frameMs)
  {
    this.perfHudLastMs = frameMs;
    this.frameMsAvg = lerp(this.frameMsAvg, frameMs, 0.12);
    if (this.frameMsAvg > 30) this.tier = 3;
    else if (this.frameMsAvg > 22) this.tier = 2;
    else if (this.frameMsAvg > 16.8) this.tier = 1;
    else this.tier = 0;
    this.renderDecimation = this.config().decimation;
    if (state.gpuTelemetry) state.gpuTelemetry.qualityTier = this.tier;
  }

  updateHud(visibleCount)
  {
    const hud = $('perfhud');
    if (!this.perfHudEnabled)
    {
      hud.style.display = 'none';
      return;
    }
    hud.style.display = 'block';
    const tierName = ['high', 'medium', 'low', 'emergency'][this.tier] || 'high';
    const gpuSim = state.simBackend === 'gpu' && state.gpuSimEnabled;
    hud.classList.toggle('gpu-sim', gpuSim);
    $('perf-backend').textContent = state.rendererBackend || 'canvas';
    $('perf-frame').textContent = `${this.perfHudLastMs.toFixed(2)}ms`;
    $('perf-avg').textContent = `${this.frameMsAvg.toFixed(2)}ms`;
    $('perf-tier').textContent = tierName;
    $('perf-visible').textContent = String(visibleCount);
    const simEl = $('perf-sim');
    if (gpuSim)
    {
      simEl.textContent = 'gpu';
      $('perf-step').textContent = `${(state.gpuTelemetry.simStepMs || 0).toFixed(2)}ms`;
      $('perf-alive').textContent = String(state.gpuTelemetry.aliveCount ?? 0);
      $('perf-births').textContent = String(state.gpuTelemetry.birthCount ?? 0);
      $('perf-intake').textContent = (state.gpuTelemetry.herbivoreIntake ?? 0).toFixed(2);
      const poolEl = $('perf-pool');
      if (poolEl) poolEl.textContent = String(state.gpuTelemetry.poolSize ?? state.gpuRenderCreatureCount ?? 0);
      const arrEl = $('perf-arr');
      if (arrEl) arrEl.textContent = String(state.gpuTelemetry.creatureArraySize ?? state.creatures.length);
      const rbEl = $('perf-readback');
      if (rbEl) rbEl.textContent = `${(state.gpuTelemetry.readbackMs || 0).toFixed(2)}ms`;
      const dropEl = $('perf-drops');
      if (dropEl) dropEl.textContent = String(state.gpuTelemetry.droppedTimelineWrites ?? 0);
    }
    else
    {
      simEl.textContent = state.gpuSimInitReason
        ? `cpu (${state.gpuSimInitReason})`
        : 'cpu';
    }
  }

  toggleHud()
  {
    this.perfHudEnabled = !this.perfHudEnabled;
    if (!this.perfHudEnabled) $('perfhud').style.display = 'none';
  }
}

export const quality = new QualityController();
