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
    this._dbgLastAt = 0;
    this._dbgLastScrub = false;
  }

  _dbgLog(hypothesisId, location, message, data, runId = 'initial')
  {
    const now = Date.now();
    const scrub = !!state.scrubActive;
    if (now - this._dbgLastAt < 500 && scrub === this._dbgLastScrub) return;
    this._dbgLastAt = now;
    this._dbgLastScrub = scrub;
    // #region agent log
    fetch('http://127.0.0.1:7380/ingest/1f42d0b3-052e-4f03-9f2a-63f9a93dd687', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Debug-Session-Id': '556f60',
      },
      body: JSON.stringify({
        sessionId: '556f60',
        runId,
        hypothesisId,
        location,
        message,
        data,
        timestamp: now,
      }),
    }).catch(() => {});
    // #endregion
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

  renderScrubCreatures2d(camera, vis, q, highlightTier)
  {
    const path = this.chooseGpuCreaturePath();
    const preferSprites = path === 'webgpu_canvas_sprites' || (q.detail >= 2 && state.cam.z > 4.2);
    if (preferSprites)
    {
      for (const c of vis) creatureRenderer.drawCreature(camera, c, q.detail);
      creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
      return 'scrub-canvas-sprites';
    }

    this.gpuCanvas.classList.remove('hidden');
    const gpuOk = webGpuRenderer.renderCreatures(camera, this.canvas, vis, q.detail, 0);
    if (!gpuOk)
    {
      this.gpuCanvas.classList.add('hidden');
      if (q.detail <= 0 || state.cam.z < 1.8) creatureRenderer.drawBatchMarkers(camera, vis);
      else for (const c of vis) creatureRenderer.drawCreature(camera, c, Math.min(q.detail, 1));
      creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
      return 'scrub-canvas-fallback';
    }

    creatureRenderer.renderHighlightsOverlay(camera, vis, highlightTier);
    return 'scrub-gpu-circles-cpu';
  }

  render()
  {
    const q = quality.config();
    const highlightTier = quality.effectiveHighlight(q.highlight);
    const shouldUseGpuBuffer = state.rendererBackend === 'webgpu';
    let renderBranch = 'unknown';
    this._dbgLog('H1', 'js/render/pipeline.js:render', 'render pipeline decision', {
      scrubActive: state.scrubActive,
      rendererBackend: state.rendererBackend,
      simBackend: state.simBackend,
      gpuSimEnabled: state.gpuSimEnabled,
      shouldUseGpuBuffer,
      gpuCreaturePath: this.gpuCreaturePath,
      detailTier: q.detail,
      camZ: state.cam.z,
    });
    if (shouldUseGpuBuffer && !state.scrubActive) this.gpuCanvas.classList.remove('hidden');
    else if (!shouldUseGpuBuffer) this.gpuCanvas.classList.add('hidden');

    terrainRenderer.renderStage(camera);
    this.drawTerrainDayNightOverlay();
    const vis = creatures.collectVisible(camera);
    creatureRenderer.clearHighlightOverlay();

    if (state.rendererBackend === 'webgpu')
    {
      if (state.scrubActive)
      {
        webGpuRenderer.clearOverlay();
        renderBranch = this.renderScrubCreatures2d(camera, vis, q, highlightTier);
        if (renderBranch === 'scrub-canvas-sprites') this.gpuCanvas.classList.add('hidden');
      }
      else if (state.simBackend === 'gpu' && state.gpuSimEnabled)
      {
        this.gpuCanvas.classList.remove('hidden');
        const path = this.chooseGpuCreaturePath();
        const preferCanvasDetail = path === 'webgpu_canvas_sprites';
        if (preferCanvasDetail)
        {
          renderBranch = 'gpu-sim-canvas-sprites';
          webGpuRenderer.clearOverlay();
          for (const c of vis) creatureRenderer.drawCreature(camera, c, q.detail);
          creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
        }
        else
        {
          renderBranch = 'gpu-sim-gpu-buffer';
          const gpuOk = webGpuRenderer.renderGpuBuffer(this.canvas, state.gpuRenderCreatureCount);
          if (!gpuOk)
          {
            renderBranch = 'gpu-sim-fallback-canvas';
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
      this.gpuCanvas.classList.remove('hidden');
      const preferCanvasDetail = q.detail >= 2 && state.cam.z > 4.2;
      if (preferCanvasDetail)
      {
        renderBranch = 'cpu-canvas-sprites';
        webGpuRenderer.clearOverlay();
        for (const c of vis) creatureRenderer.drawCreature(camera, c, q.detail);
        creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
      }
      else
      {
        renderBranch = 'cpu-gpu-circles';
        const gpuOk = webGpuRenderer.renderCreatures(camera, this.canvas, vis, q.detail, 0);
        if (!gpuOk)
        {
          renderBranch = 'cpu-fallback-canvas';
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
      webGpuRenderer.clearOverlay();
      const detailForRender = state.cam.z < 1.8
        ? 0
        : state.cam.z < 3.5
          ? Math.min(q.detail, 1)
          : q.detail;
      renderBranch = 'canvas-only';
      const drawMode = detailForRender <= 0 ? 'batch-markers' : 'drawCreature';
      this._dbgLog('H2', 'js/render/pipeline.js:canvas-only', 'canvas-only detail', {
        scrubActive: state.scrubActive,
        renderBranch,
        drawMode,
        detailTier: q.detail,
        detailForRender,
        visCount: vis.length,
        camZ: state.cam.z,
      });
      if (detailForRender <= 0) creatureRenderer.drawBatchMarkers(camera, vis);
      else for (const c of vis) creatureRenderer.drawCreature(camera, c, detailForRender);
      creatureRenderer.renderHighlights2d(camera, vis, highlightTier);
    }
    const visSample = vis[0];
    const id1 = state.creatures.find(c => c.id === 1);
    this._dbgLog('H3', 'js/render/pipeline.js:post', 'render branch result', {
      scrubActive: state.scrubActive,
      renderBranch,
      camZ: state.cam.z,
      detailTier: q.detail,
      visCount: vis.length,
      sampleX: visSample?.x ?? null,
      sampleY: visSample?.y ?? null,
      id1X: id1?.x ?? null,
      id1Y: id1?.y ?? null,
    }, state.scrubActive ? 'post-fix-pos2' : 'initial');

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
