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
    this.perfHudEnabled = false;
    this.perfHudLastMs = 16.7;
  }

  config()
  {
    if (this.tier === 0) return { detail: 2, highlight: 2, waterMul: 1, vegMul: 1, decimation: 1 };
    if (this.tier === 1) return { detail: 1, highlight: 1, waterMul: 0.8, vegMul: 1.35, decimation: 1 };
    if (this.tier === 2) return { detail: 1, highlight: 0, waterMul: 0.6, vegMul: 1.8, decimation: 1 };
    return { detail: 0, highlight: 0, waterMul: 0.45, vegMul: 2.3, decimation: 2 };
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
    const simInfo = state.simBackend === 'gpu'
      ? ` | sim: gpu | step: ${(state.gpuTelemetry.simStepMs || 0).toFixed(2)}ms | alive: ${state.gpuTelemetry.aliveCount} | births: ${state.gpuTelemetry.birthCount} | intake: ${state.gpuTelemetry.herbivoreIntake.toFixed(2)}`
      : (state.gpuSimInitReason
        ? ` | sim: cpu (${state.gpuSimInitReason})`
        : ' | sim: cpu');
    hud.textContent = `backend: ${state.rendererBackend} | frame: ${this.perfHudLastMs.toFixed(2)}ms | avg: ${this.frameMsAvg.toFixed(2)}ms | tier: ${tierName} | visible: ${visibleCount}${simInfo}`;
  }

  toggleHud()
  {
    this.perfHudEnabled = !this.perfHudEnabled;
    if (!this.perfHudEnabled) $('perfhud').style.display = 'none';
  }
}

export const quality = new QualityController();
