import { lerp } from '../utils.js';
import { state } from '../state.js';
import { gpuThrottleMinQualityTier } from '../gpu-throttle.js';

export class QualityController
{
  constructor()
  {
    this.frameMsAvg = 16.7;
    this.tier = 0;
    this.renderDecimation = 1;
    this.frameCounter = 0;
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
    const result = state.lockedSpeciesFromPanel
      ? Math.max(baseHighlight, 2)
      : panelFocus || hasSelection
        ? Math.max(baseHighlight, 1)
        : baseHighlight;
    return result;
  }

  updateTier(frameMs)
  {
    this.frameMsAvg = lerp(this.frameMsAvg, frameMs, 0.12);
    let tier = 0;
    if (this.frameMsAvg > 30) tier = 3;
    else if (this.frameMsAvg > 22) tier = 2;
    else if (this.frameMsAvg > 16.8) tier = 1;
    this.tier = Math.max(tier, gpuThrottleMinQualityTier());
    this.renderDecimation = this.config().decimation;
    if (state.gpuTelemetry) state.gpuTelemetry.qualityTier = this.tier;
  }
}

export const quality = new QualityController();
