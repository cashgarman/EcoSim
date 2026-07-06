import { setRngSeed } from './utils.js';
import { SP_KEYS, loadSpeciesData } from './data.js';
import { loadBehaviorLibrary } from './behavior/loader.js';
import { state, rendererMode, simulationMode } from './state.js';
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
import { gpuSimulationBackend } from './gpu/simulation-backend.js';
import { lifeStory } from './life-story.js';
import { timelineDb } from './timeline-db.js';
import { timeScrub } from './time-scrub.js';
import { captureSnapshot } from './snapshot.js';
import { loadTimelineConfig } from './config.js';
import { effectiveSnapshotIntervalSec } from './perf-policy.js';
import { perfProfiler } from './perf-profiler.js';
import { initSpeciesStats } from './species-stats.js';
import { applyPanelLayout } from './panel-layout.js';
import { initGpuThrottleUi } from './gpu-throttle-ui.js';
import { shouldSkipGpuRenderFrame } from './gpu-throttle.js';
import {
  loadBalanceFromStorage,
  applyBalanceOverrides,
  decodeBalanceParam,
  hasActiveOverrides,
  emptyBalanceOverrides,
  saveBalanceToStorage,
} from './batch/balance-config.js';

function resolveBalanceOverrides()
{
  const params = new URLSearchParams(window.location.search);
  const encoded = params.get('balance');
  if (encoded) return decodeBalanceParam(encoded);
  return loadBalanceFromStorage();
}

export class GameApp
{
  constructor()
  {
    this.last = performance.now();
    this.uiT = 0;
  }

  async init()
  {
    setRngSeed(state.SEED);
    const timelineConfig = await loadTimelineConfig();
    if (timelineConfig && typeof timelineConfig.snapshotIntervalSec === 'number')
    {
      state.snapshotIntervalSec = Math.max(0.5, timelineConfig.snapshotIntervalSec);
    }
    $('loading').classList.remove('hidden');
    $('loadmsg').textContent = 'Loading species…';
    await loadSpeciesData();
    await loadBehaviorLibrary();
    this.applyStoredBalanceOverrides();

    const canvas = document.getElementById('world');
    const gpuCanvas = document.getElementById('world-gpu');
    const hlCanvas = document.getElementById('world-hl');

    camera.init(canvas);
    renderPipeline.init(canvas, gpuCanvas, hlCanvas);
    inputManager.init(canvas);

    ui.initPopHistory();
    initSpeciesStats();
    ui.initInspectTabs();
    ui.initEventLogClicks();
    ui.initSpeciesRowMenu();
    ui.setFollowToggleHandler(enabled => ui.setFollowMode(enabled));
    creatures.setLogger(msg => ui.log(msg));
    creatures.setNotifyFn((html, creatureId) => ui.notifyCreatureEvent(html, creatureId));
    lifeStory.setEventNotify((kind, c, partnerId) =>
    {
      ui.notifyCreatureLifeEvent(kind, c, partnerId);
    });

    ui.syncLabels();
    ui.initWorldgenSliders();
    ui.initBalanceTuningControls();
    ui.initDraggablePanels();
    ui.initPanelCollapse();
    ui.initProfilerPanel();
    ui.initTimelineDbViewer();
    ui.initTimeScrubber();
    initGpuThrottleUi();
    timeScrub.setAfterRestoreCallback(p =>
    {
      simulation.updateDayNight();
      ui.reconcileSelectionAfterScrub(p);
    });
    simulation.updateDayNight();
    ui.syncGraphCanvas();
    ui.applyStatsPanelMode('normal');
    initToolButtons();

    window.deselect = () => ui.deselect();
    window.lifeStory = lifeStory;
    window.runGpuBenchmark = (seconds = 12) =>
    {
      const samples = [];
      const started = performance.now();
      return new Promise(resolve =>
      {
        const timer = setInterval(() =>
        {
          samples.push({
            t: (performance.now() - started) / 1000,
            frameMs: quality.frameMsAvg,
            simStepMs: state.gpuTelemetry.simStepMs || 0,
            alive: state.gpuTelemetry.aliveCount || state.creatures.filter(c => !c.dead).length,
            intake: state.gpuTelemetry.herbivoreIntake || 0,
          });
          if ((performance.now() - started) / 1000 >= seconds)
          {
            clearInterval(timer);
            const avgFrame = samples.reduce((a, s) => a + s.frameMs, 0) / Math.max(1, samples.length);
            const avgStep = samples.reduce((a, s) => a + s.simStepMs, 0) / Math.max(1, samples.length);
            const maxAlive = samples.reduce((m, s) => Math.max(m, s.alive), 0);
            const summary = { seconds, avgFrame, avgStep, maxAlive, samples };
            console.log('EcoSim GPU benchmark summary', summary);
            ui.log(`Benchmark ${seconds}s: avg frame ${avgFrame.toFixed(2)}ms, avg sim ${avgStep.toFixed(2)}ms, peak alive ${maxAlive}.`);
            resolve(summary);
          }
        }, 250);
      });
    };

    addEventListener('resize', () =>
    {
      camera.resize(state.gpuContext, state.gpuDevice, state.gpuCanvasFormat, state.gpuReady);
      applyPanelLayout();
      ui.syncGraphCanvas();
      ui.drawGraph();
      ui.positionProfilerDetailPanel();
      ui.reclampProfilerDetailPanel();
    });
    camera.resize(state.gpuContext, state.gpuDevice, state.gpuCanvasFormat, state.gpuReady);

    gpuCanvas.classList.add('hidden');
    if (rendererMode === 'webgpu_hybrid')
    {
      if (simulationMode === 'gpu_hybrid') state.gpuSimInitPending = true;
      webGpuRenderer.setup().then(ok =>
      {
        state.rendererBackend = ok ? 'webgpu' : 'canvas';
        state.simBackend = 'cpu';
        state.gpuSimEnabled = false;
        if (ok && simulationMode === 'gpu_hybrid')
        {
          let inited = false;
          try
          {
            inited = gpuSimulationBackend.init();
          }
          catch (err)
          {
            inited = false;
          }
          if (inited && state.ready && state.W > 0 && state.H > 0 && state.veg)
          {
            const setupOk = gpuSimulationBackend.setupForCurrentWorld();
            if (!setupOk)
            {
              state.simBackend = 'cpu';
              state.gpuSimEnabled = false;
              if (!state.gpuSimInitReason) state.gpuSimInitReason = 'setup-failed';
            }
          }
          else if (inited)
          {
            // GPU sim is enabled after setupForCurrentWorld() in doGenerate().
          }
          else if (!state.gpuSimInitReason)
          {
            state.gpuSimInitReason = 'init-failed';
          }
        }
        state.gpuSimInitPending = false;
        if (state.rendererBackend === 'webgpu') gpuCanvas.classList.remove('hidden');
        else gpuCanvas.classList.add('hidden');
      });
    }

    $('generate').addEventListener('click', () => { this.doGenerate(true); });
    $('restock').addEventListener('click', () => { if (state.ready) creatures.stockLife(); });

    this.doGenerate(true);
    requestAnimationFrame(t => this.frame(t));
  }

  applyStoredBalanceOverrides()
  {
    const overrides = resolveBalanceOverrides();
    if (hasActiveOverrides(overrides))
    {
      applyBalanceOverrides(overrides);
    }
    ui.updateBalanceTuningBanner(overrides);
    return overrides;
  }

  doGenerate(fresh)
  {
    state.ready = false;
    state.tGlobal = 0;
    state.day = 0;
    state.timeOfDay = 0.3;
    state.migrantTimer = 0;
    state.heartbeatNextAt = state.heartbeatIntervalSec;
    state.lastSnapshotAt = 0;
    timeScrub.resetForNewRun();
    $('loading').classList.remove('hidden');
    $('loadmsg').textContent = 'Sculpting terrain…';
    ui.deselect();

    setTimeout(() =>
    {
      const runMeta = {
        runId: `run-${Date.now()}-${Math.floor(Math.random() * 1e6)}`,
        seed: state.SEED,
        worldConfig: { ...state.cfg },
        worldAreaKm2: state.worldAreaKm2,
        timeOfDayOrigin: state.timeOfDay,
      };
      state.timeOfDayOrigin = state.timeOfDay;

      const finishGenerate = () =>
      {
        this.applyStoredBalanceOverrides();

        world.generate(() =>
        {
          terrainRenderer.bakeTerrain();
          terrainRenderer.bakeVegetation();
        });

        $('loadmsg').textContent = 'Seeding life…';
        state.creatures = [];
        state.grid.clear();
        state.generationMax = 1;
        state.nextId = 1;
        for (const k of SP_KEYS) state.popHistory[k] = [];
        initSpeciesStats();

        creatures.stockLife();
        try
        {
          const initSnap = captureSnapshot();
          timelineDb.appendSnapshot({ t: initSnap.t, day: initSnap.day, snapshot: initSnap });
          state.lastSnapshotAt = state.tGlobal;
        }
        catch (e)
        {
          // initial snapshot optional
        }
        if (simulationMode === 'gpu_hybrid' && gpuSimulationBackend.initialized)
        {
          try
          {
            const setupOk = gpuSimulationBackend.setupForCurrentWorld();
            if (!setupOk)
            {
              state.simBackend = 'cpu';
              state.gpuSimEnabled = false;
              if (!state.gpuSimInitReason) state.gpuSimInitReason = 'setup-failed';
            }
          }
          catch (err)
          {
            state.simBackend = 'cpu';
            state.gpuSimEnabled = false;
          }
        }
        camera.centerCam();
        state.ready = true;
        state.lastTerrainTipBiome = -1;
        $('loading').classList.add('hidden');
        ui.log(`🌍 New ${state.worldAreaKm2} km² world generated (seed ${state.SEED}).`);
        ui.refreshTimelineDbView(true);
        const settleScrub = async () =>
        {
          try
          {
            await timeScrub.goToPresent();
            ui.updateScrubLabels();
            ui.updatePauseIndicator();
          }
          catch (err)
          {
            console.warn('Failed to settle scrub state:', err);
          }
        };
        settleScrub().finally(() => timeScrub.persistState());
      };

      timelineDb.initTimelineDb(runMeta).then(async () =>
      {
        try
        {
          const meta = await timelineDb.getRunMeta();
          if (meta && typeof meta.timeOfDayOrigin === 'number')
          {
            state.timeOfDayOrigin = meta.timeOfDayOrigin;
          }
        }
        catch (e) {}
      }).catch(err =>
      {
        console.warn('Timeline DB init failed:', err);
      }).finally(() =>
      {
        finishGenerate();
      });
    }, 30);
  }

  frame(now)
  {
    const instrument = perfProfiler.isInstrumentationActive();
    if (instrument) perfProfiler.beginFrame();
    if (instrument) perfProfiler.begin('frame');
    const frameT0 = performance.now();
    try
    {
      let dt = Math.min(0.05, (now - this.last) / 1000);
      this.last = now;
      const sdt = dt * state.speed;

      const viewingPast = timeScrub.isViewingPast();
      state.scrubActive = viewingPast;

      if (state.speed > 0 && state.ready && !viewingPast)
      {
        state.tGlobal += sdt;
        const steps = Math.min(6, Math.ceil(state.speed));
        if (instrument) perfProfiler.setMeta('substepCount', steps);
        const stepDt = sdt / steps;
        if (instrument) perfProfiler.begin('frame.sim');
        const simT0 = performance.now();
        for (let i = 0; i < steps; i++)
        {
          simulation.tick(stepDt, { substep: i, substepCount: steps });
        }
        if (instrument) perfProfiler.end('frame.sim');
        perfProfiler.record('sim', performance.now() - simT0);
      }
      else
      {
        if (instrument) perfProfiler.setMeta('substepCount', 0);
        perfProfiler.record('sim', 0);
      }

      if (!viewingPast)
      {
        timeScrub.noteLiveAdvance();

        const snapInterval = effectiveSnapshotIntervalSec();
        const lastAt = state.lastSnapshotAt || 0;
        if (state.tGlobal >= lastAt + snapInterval - 1e-6)
        {
          const bucketT = Math.floor(state.tGlobal / snapInterval) * snapInterval;
          if (bucketT > lastAt + 1e-6)
          {
            state.lastSnapshotAt = bucketT;
            if (instrument) perfProfiler.begin('frame.snapshot');
            const snapT0 = instrument ? performance.now() : 0;
            try
            {
              const snap = captureSnapshot();
              const row = { t: snap.t, day: snap.day, snapshot: snap };
              timelineDb.appendSnapshot(row);
              timeScrub.cacheSnapshotRow(row);
              ui.invalidateScrubTicks();
              ui.renderTimeline(true);
            }
            catch (e)
            {
              // snapshot capture failure should not break sim
            }
            if (instrument)
            {
              perfProfiler.end('frame.snapshot');
              perfProfiler.record('snapshot', performance.now() - snapT0);
            }
          }
        }
      }

      if (state.ready)
      {
        if (instrument) perfProfiler.begin('frame.displaySmooth');
        const smoothT0 = instrument ? performance.now() : 0;
        creatures.advanceDisplayPositions(dt);
        if (instrument)
        {
          perfProfiler.end('frame.displaySmooth');
          perfProfiler.record('displaySmooth', performance.now() - smoothT0);
        }
      }

      if (state.followSelected)
      {
        if (state.selected && !state.selected.dead) camera.followSelected(dt);
        else if (state.scrubActive) ui.clearCreatureSelection();
        else ui.deselect();
      }
      else if (state.selected && state.selected.dead)
      {
        if (state.scrubActive) ui.clearCreatureSelection();
        else ui.deselect();
      }

      quality.frameCounter++;
      if (instrument) perfProfiler.begin('frame.render');
      const renderT0 = performance.now();
      if (state.ready)
      {
        const skipDecimationForWebGpu = state.rendererBackend === 'webgpu' && !state.scrubActive;
        const throttleSkip = skipDecimationForWebGpu && shouldSkipGpuRenderFrame(quality.frameCounter);
        const shouldRender = state.scrubActive
          ? true
          : throttleSkip
            ? false
          : skipDecimationForWebGpu
            ? true
            : (quality.frameCounter % quality.renderDecimation) === 0;
        if (shouldRender) renderPipeline.render();
      }
      if (instrument) perfProfiler.end('frame.render');
      perfProfiler.record('render', performance.now() - renderT0);

      const q = quality.config();
      state.vegBakeInterval = (state.W * state.H >= 100000 ? 0.30 : state.W * state.H >= 60000 ? 0.22 : 0.15) * q.vegMul;
      state.vegBakeCd = Math.max(0, state.vegBakeCd - dt);

      this.uiT += dt;
      if (this.uiT > 0.2)
      {
        this.uiT = 0;
        if (instrument) perfProfiler.begin('frame.ui');
        const uiT0 = instrument ? performance.now() : 0;
        ui.updateUI();
        if (instrument)
        {
          perfProfiler.end('frame.ui');
          perfProfiler.record('ui', performance.now() - uiT0);
        }
      }
    }
    catch (err)
    {
      console.error('Wildlands frame error (recovered):', err);
    }
    finally
    {
      if (instrument) perfProfiler.end('frame');
      const frameTotal = performance.now() - frameT0;
      perfProfiler.endFrame(frameTotal);
      quality.updateTier(frameTotal);
      requestAnimationFrame(t => this.frame(t));
    }
  }
}

const app = new GameApp();
if (window.__wildlandsAppStarted)
{
  console.warn('Wildlands: duplicate app boot skipped.');
}
else
{
  window.__wildlandsAppStarted = true;
  app.init().catch(err =>
  {
    console.error('Wildlands boot failed:', err);
    $('loadmsg').textContent = 'Startup failed — hard-refresh the page (Ctrl+Shift+R).';
  });
}
