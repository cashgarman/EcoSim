import { clamp, lerp } from '../utils.js';
import { state } from '../state.js';
import { creatures } from '../creatures.js';
import { camera } from '../camera.js';
import { ui } from '../ui.js';
import { effects } from '../fx.js';
import { toolRadius } from '../tools.js';
import { quality } from './quality.js';
import { terrainRenderer } from './terrain-renderer.js';
import { creatureRenderer } from './creature-renderer.js';
import { webGpuRenderer } from './webgpu-renderer.js';

export class RenderPipeline
{
  constructor()
  {
    this.canvas = null;
    this.gpuCanvas = null;
    this.hlCanvas = null;
    this.gpuCreaturePath = 'webgpu_circles';
  }

  chooseGpuCreaturePath()
  {
    const prev = this.gpuCreaturePath;
    let next = prev;
    const z = state.cam.z;
    const enterSprites = z > 4.6;
    const leaveSprites = z < 3.9;

    if (prev === 'webgpu_canvas_sprites')
    {
      if (leaveSprites) next = 'webgpu_circles';
    }
    else
    {
      if (enterSprites) next = 'webgpu_canvas_sprites';
    }

    this.gpuCreaturePath = next;
    return next;
  }

  resolveLodPlan(q)
  {
    const z = state.cam.z;
    const detailTier = q.detail;
    const lowDetail = detailTier <= 0 || z < 1.8;
    const mediumDetail = !lowDetail && (detailTier <= 1 || z < 3.5);
    const path = this.chooseGpuCreaturePath();
    const preferSprites = detailTier >= 2 && path === 'webgpu_canvas_sprites';

    let mode = 'circles';
    if (lowDetail) mode = 'markers';
    else if (preferSprites) mode = 'sprites';
    else if (mediumDetail) mode = 'simple';

    const circleDetail = mode === 'markers'
      ? 0
      : mode === 'simple'
        ? 1
        : 2;
    const gpuSizeScale = circleDetail <= 0
      ? 0.55
      : circleDetail === 1
        ? 0.75
        : 1.0;

    return {
      mode,
      circleDetail,
      canvasDetail: mode === 'markers' ? 0 : mode === 'simple' ? 1 : 2,
      gpuSizeScale,
    };
  }

  resolveCreatureAuthority()
  {
    if (state.scrubActive) return 'cpu_snapshot';
    if (state.simBackend === 'gpu' && state.gpuSimEnabled) return 'gpu_buffer';
    return 'cpu_live';
  }

  init(canvas, gpuCanvas, hlCanvas)
  {
    this.canvas = canvas;
    this.gpuCanvas = gpuCanvas;
    this.hlCanvas = hlCanvas;
    terrainRenderer.init(canvas);
    creatureRenderer.init(canvas, hlCanvas);
    webGpuRenderer.init(gpuCanvas);
  }

  drawTerrainDayNightOverlay()
  {
    const ctx = creatureRenderer.ctx;
    const darkness = 1 - state.lightLevel;
    if (darkness <= 0.015) return;
    const twilight = clamp(1 - Math.abs(state.timeOfDay - 0.5) * 2.2, 0, 1);
    const warm = (1 - twilight) * darkness * 0.35;
    const nr = lerp(20, 90, warm) | 0, ng = lerp(28, 55, warm) | 0, nb = lerp(70, 35, warm) | 0;
    ctx.fillStyle = `rgba(${nr},${ng},${nb},${darkness * 0.68})`;
    ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);
  }

  renderOverlayStage()
  {
    effects.draw(creatureRenderer.ctx, camera, 1 / 60);
    const ctx = creatureRenderer.ctx;
    if (state.tool !== 'inspect')
    {
      ctx.strokeStyle = 'rgba(255,255,255,.5)';
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.arc(state.mouseX, state.mouseY, toolRadius() * state.cam.z, 0, 6.28);
      ctx.stroke();
    }
  }

  renderCanvasByLod(camera, vis, lodPlan, highlightTier)
  {
    if (lodPlan.mode === 'markers')
    {
      creatureRenderer.drawBatchMarkers(camera, vis);
    }
    else
    {
      for (const c of vis) creatureRenderer.drawCreature(camera, c, lodPlan.canvasDetail);
    }
    creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
  }

  renderWebGpuByLod(camera, vis, lodPlan, highlightTier, authority)
  {
    if (lodPlan.mode === 'sprites')
    {
      webGpuRenderer.clearOverlay();
      this.gpuCanvas.classList.add('hidden');
      this.renderCanvasByLod(camera, vis, lodPlan, highlightTier);
      return `${authority}-canvas-sprites`;
    }

    this.gpuCanvas.classList.remove('hidden');
    let gpuOk = false;
    if (authority === 'gpu_buffer')
    {
      gpuOk = webGpuRenderer.renderGpuBuffer(this.canvas, state.gpuRenderCreatureCount, lodPlan.gpuSizeScale);
    }
    else
    {
      gpuOk = webGpuRenderer.renderCreatures(camera, this.canvas, vis, lodPlan.circleDetail, 0);
    }

    if (!gpuOk)
    {
      this.gpuCanvas.classList.add('hidden');
      this.renderCanvasByLod(camera, vis, lodPlan, highlightTier);
      return `${authority}-canvas-fallback`;
    }

    creatureRenderer.renderHighlightsOverlay(camera, vis, highlightTier);
    return `${authority}-gpu-circles`;
  }

  render()
  {
    const q = quality.config();
    const highlightTier = quality.effectiveHighlight(q.highlight);
    const shouldUseGpu = state.rendererBackend === 'webgpu';
    const authority = this.resolveCreatureAuthority();
    const lodPlan = this.resolveLodPlan(q);
    let renderBranch = 'unknown';

    terrainRenderer.renderStage(camera);
    this.drawTerrainDayNightOverlay();
    const vis = creatures.collectVisible(camera);
    creatureRenderer.clearHighlightOverlay();

    if (shouldUseGpu)
    {
      renderBranch = this.renderWebGpuByLod(camera, vis, lodPlan, highlightTier, authority);
    }
    else
    {
      this.gpuCanvas.classList.add('hidden');
      webGpuRenderer.clearOverlay();
      renderBranch = 'canvas-only';
      this.renderCanvasByLod(camera, vis, lodPlan, highlightTier);
    }

    if (state.selected && !state.selected.dead)
    {
      creatureRenderer.drawSelectedTargetLine(camera, state.selected);
      creatureRenderer.drawFollowPedigreeLines(camera, state.selected);
    }

    ui.updateCreatureTooltip(state.mouseX, state.mouseY);
    this.renderOverlayStage();
    quality.updateHud(vis.length);
  }
}

export const renderPipeline = new RenderPipeline();
