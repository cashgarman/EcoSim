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

  render()
  {
    const q = quality.config();
    const highlightTier = quality.effectiveHighlight(q.highlight);
    if (state.rendererBackend === 'canvas') this.gpuCanvas.classList.add('hidden');
    else this.gpuCanvas.classList.remove('hidden');

    terrainRenderer.renderStage(camera);
    this.drawTerrainDayNightOverlay();
    const vis = creatures.collectVisible(camera);
    creatureRenderer.clearHighlightOverlay();

    if (state.rendererBackend === 'webgpu')
    {
      if (state.simBackend === 'gpu' && state.gpuSimEnabled)
      {
        const path = this.chooseGpuCreaturePath();
        const preferCanvasDetail = path === 'webgpu_canvas_sprites';
        if (preferCanvasDetail)
        {
          webGpuRenderer.clearOverlay();
          for (const c of vis) creatureRenderer.drawCreature(camera, c, q.detail);
          creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
        }
        else
        {
          const gpuOk = webGpuRenderer.renderGpuBuffer(this.canvas, state.gpuRenderCreatureCount);
          if (!gpuOk)
          {
            state.rendererBackend = 'canvas';
            state.simBackend = 'cpu';
            state.gpuReady = false;
            state.gpuSimEnabled = false;
            this.gpuCanvas.classList.add('hidden');
            for (const c of vis) creatureRenderer.drawCreature(camera, c, q.detail);
            creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
          }
          else
          {
            creatureRenderer.renderHighlightsOverlay(camera, vis, highlightTier);
          }
        }
      }
      else
      {
      const preferCanvasDetail = q.detail >= 2 && state.cam.z > 4.2;
      if (preferCanvasDetail)
      {
        webGpuRenderer.clearOverlay();
        for (const c of vis) creatureRenderer.drawCreature(camera, c, q.detail);
        creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
      }
      else
      {
        const gpuOk = webGpuRenderer.renderCreatures(camera, this.canvas, vis, q.detail, 0);
        if (!gpuOk)
        {
          state.rendererBackend = 'canvas';
          state.gpuReady = false;
          this.gpuCanvas.classList.add('hidden');
          for (const c of vis) creatureRenderer.drawCreature(camera, c, q.detail);
          creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
        }
        else creatureRenderer.renderHighlightsOverlay(camera, vis, highlightTier);
      }
      }
    }
    else
    {
      if (q.detail <= 0) creatureRenderer.drawBatchMarkers(camera, vis);
      else for (const c of vis) creatureRenderer.drawCreature(camera, c, q.detail);
      creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
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
