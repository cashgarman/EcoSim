import { setRngSeed } from './utils.js';
import { SP_KEYS } from './data.js';
import { state, rendererMode } from './state.js';
import { $ } from './dom.js';
import { world } from './world.js';
import { camera } from './camera.js';
import { creatures } from './creatures.js';
import { simulation } from './simulation.js';
import { ui } from './ui.js';
import { inputManager } from './input.js';
import { initToolButtons } from './tools.js';
import { terrainRenderer } from './render/terrain-renderer.js';
import { renderPipeline } from './render/pipeline.js';
import { webGpuRenderer } from './render/webgpu-renderer.js';
import { quality } from './render/quality.js';

export class GameApp
{
  constructor()
  {
    this.last = performance.now();
    this.uiT = 0;
  }

  init()
  {
    setRngSeed(state.SEED);

    const canvas = document.getElementById('world');
    const gpuCanvas = document.getElementById('world-gpu');
    const hlCanvas = document.getElementById('world-hl');

    camera.init(canvas);
    renderPipeline.init(canvas, gpuCanvas, hlCanvas);
    inputManager.init(canvas);

    ui.initPopHistory();
    ui.setFollowToggleHandler(enabled => ui.setFollowMode(enabled));
    creatures.setLogger(msg => ui.log(msg));

    ui.syncLabels();
    ui.initWorldgenSliders();
    ui.initDraggablePanels();
    simulation.updateDayNight();
    ui.syncGraphCanvas();
    ui.applyStatsPanelMode('normal');
    initToolButtons();

    window.deselect = () => ui.deselect();

    addEventListener('resize', () =>
    {
      camera.resize(state.gpuContext, state.gpuDevice, state.gpuCanvasFormat, state.gpuReady);
      ui.syncGraphCanvas();
      ui.drawGraph();
    });
    camera.resize(state.gpuContext, state.gpuDevice, state.gpuCanvasFormat, state.gpuReady);

    gpuCanvas.classList.add('hidden');
    if (rendererMode === 'webgpu_hybrid')
    {
      webGpuRenderer.setup().then(ok =>
      {
        state.rendererBackend = ok ? 'webgpu' : 'canvas';
        if (state.rendererBackend === 'webgpu') gpuCanvas.classList.remove('hidden');
        else gpuCanvas.classList.add('hidden');
      });
    }

    $('generate').addEventListener('click', () => { this.doGenerate(true); });
    $('restock').addEventListener('click', () => { if (state.ready) creatures.stockLife(); });

    this.doGenerate(true);
    requestAnimationFrame(t => this.frame(t));
  }

  doGenerate(fresh)
  {
    state.ready = false;
    $('loading').classList.remove('hidden');
    $('loadmsg').textContent = 'Sculpting terrain…';
    ui.deselect();

    setTimeout(() =>
    {
      world.generate(() =>
      {
        terrainRenderer.bakeTerrain();
        terrainRenderer.bakeVegetation();
      });

      $('loadmsg').textContent = 'Seeding life…';
      state.creatures = [];
      state.generationMax = 1;
      state.nextId = 1;
      for (const k of SP_KEYS) state.popHistory[k] = [];

      creatures.stockLife();
      camera.centerCam();
      state.ready = true;
      state.lastTerrainTipBiome = -1;
      $('loading').classList.add('hidden');
      ui.log(`🌍 New ${state.worldAreaKm2} km² world generated (seed ${state.SEED}).`);
    }, 30);
  }

  frame(now)
  {
    try
    {
      let dt = Math.min(0.05, (now - this.last) / 1000);
      this.last = now;
      const sdt = dt * state.speed;

      if (state.speed > 0 && state.ready)
      {
        state.tGlobal += sdt;
        const steps = Math.min(6, Math.ceil(state.speed));
        const stepDt = sdt / steps;
        for (let i = 0; i < steps; i++) simulation.tick(stepDt);
      }

      if (state.followSelected)
      {
        if (state.selected && !state.selected.dead) camera.followSelected();
        else ui.setFollowMode(false);
      }

      const frameStart = performance.now();
      quality.frameCounter++;
      if (state.ready)
      {
        const shouldRender = (quality.frameCounter % quality.renderDecimation) === 0;
        if (shouldRender) renderPipeline.render();
      }
      quality.updateTier(performance.now() - frameStart);

      const q = quality.config();
      state.vegBakeInterval = (state.W * state.H >= 100000 ? 0.30 : state.W * state.H >= 60000 ? 0.22 : 0.15) * q.vegMul;
      state.vegBakeCd = Math.max(0, state.vegBakeCd - dt);

      this.uiT += dt;
      if (this.uiT > 0.2) { this.uiT = 0; ui.updateUI(); }
    }
    catch (err)
    {
      console.error('Wildlands frame error (recovered):', err);
    }
    finally
    {
      requestAnimationFrame(t => this.frame(t));
    }
  }
}

const app = new GameApp();
app.init();
