import { setRngSeed } from '../utils.js';
import { SP_KEYS } from '../data.js';
import { state } from '../state.js';
import { world } from '../world.js';
import { creatures } from '../creatures.js';
import { simulation } from '../simulation.js';
import { BatchMetricsCollector } from './metrics.js';
import {
  loadBaseData,
  applyBalanceOverrides,
  buildBalanceConfigSnapshot,
  emptyBalanceOverrides,
} from './balance-config.js';
import {
  initBatchGpu,
  setupBatchGpuWorld,
  syncBatchGpuState,
  teardownBatchGpu,
} from './gpu-setup.js';

export class BatchHarness
{
  constructor()
  {
    this.metrics = null;
    this.runConfig = null;
    this.balanceOverrides = emptyBalanceOverrides();
    this._abort = false;
    this._gpuRequested = false;
    this._gpuFailReason = null;
  }

  abort()
  {
    this._abort = true;
  }

  async init(config = {})
  {
    this.runConfig = { ...config };
    this.balanceOverrides = config.balanceOverrides || emptyBalanceOverrides();
    this._abort = false;
    this._gpuRequested = config.simBackend === 'gpu';
    this._gpuFailReason = null;

    await loadBaseData();
    applyBalanceOverrides(this.balanceOverrides);

    state.batchMode = true;
    state.batchConfig = {
      autoMigration: !!config.autoMigration,
      simBackend: config.simBackend || 'cpu',
    };

    state.simBackend = this._gpuRequested ? 'gpu' : 'cpu';
    state.gpuSimEnabled = false;
    state.gpuSimInitPending = false;

    state.cfg = {
      sea: config.sea ?? 0.46,
      temp: config.temp ?? 0.5,
      moist: config.moist ?? 0.5,
      relief: config.relief ?? 0.6,
      animals: config.animals ?? 0.45,
      size: config.size || 'm',
    };

    state.SEED = config.seed ?? ((Math.random() * 1e9) >>> 0);
    setRngSeed(state.SEED);

    creatures.setLogger(null);
    creatures.setNotifyFn(null);
  }

  async generateWorld()
  {
    state.tGlobal = 0;
    state.day = 0;
    state.timeOfDay = 0.3;
    state.migrantTimer = 0;
    state.generationMax = 1;
    state.nextId = 1;
    state.creatures = [];
    state.grid.clear();
    state.ready = true;

    world.generate(() => {});
    creatures.stockLife();

    if (this._gpuRequested)
    {
      const gpuInit = await initBatchGpu();
      if (!gpuInit.ok)
      {
        this._gpuFailReason = gpuInit.reason;
        throw new Error(`GPU backend unavailable (${gpuInit.reason})`);
      }
      const gpuSetup = await setupBatchGpuWorld();
      if (!gpuSetup.ok)
      {
        this._gpuFailReason = gpuSetup.reason;
        throw new Error(`GPU world setup failed (${gpuSetup.reason})`);
      }
      await syncBatchGpuState();
    }

    const initialPop = state.creatures.filter(c => !c.dead).length;
    return initialPop;
  }

  async syncGpuMetrics()
  {
    if (!state.gpuSimEnabled) return;
    await syncBatchGpuState();
  }

  async run(options = {}, onProgress = null)
  {
    const targetDays = options.targetDays ?? this.runConfig.targetDays ?? 200;
    const sampleEveryDays = options.sampleEveryDays ?? this.runConfig.sampleEveryDays ?? 10;
    const tickDt = options.tickDt ?? 0.5;
    const maxWallMs = options.maxWallMs ?? 120000;
    const earlyStopDay = options.earlyStopDay ?? null;
    const sparseMode = !!options.sparseMode;
    const onGpu = state.gpuSimEnabled;

    const initialPop = state.creatures.filter(c => !c.dead).length;
    this.metrics = new BatchMetricsCollector({ sampleEveryDays, targetDays, sparseMode });
    this.metrics.begin(initialPop, options.wallStart);

    let ticksSinceYield = 0;
    const yieldEvery = onGpu ? 20 : 40;

    while (state.day < targetDays && !this._abort)
    {
      state.tGlobal += tickDt;
      simulation.tick(tickDt);
      state.day = Math.floor(state.tGlobal / 40);

      if (onGpu) await this.syncGpuMetrics();

      if (sparseMode)
      {
        if (this.metrics.shouldSampleSparse(state.day, targetDays))
        {
          this.metrics.captureSample(true);
        }
      }
      else
      {
        this.metrics.captureSample(false);
      }

      const { totalAlive } = this.metrics.aliveCounts();
      if (earlyStopDay != null && state.day >= earlyStopDay && totalAlive === 0)
      {
        break;
      }

      if (performance.now() - this.metrics.wallStart > maxWallMs)
      {
        break;
      }

      ticksSinceYield++;
      if (ticksSinceYield >= yieldEvery)
      {
        ticksSinceYield = 0;
        if (onProgress)
        {
          onProgress({
            day: state.day,
            targetDays,
            totalAlive,
            generationMax: state.generationMax,
            wallMs: performance.now() - this.metrics.wallStart,
            simBackend: onGpu ? 'gpu' : 'cpu',
          });
        }
        await new Promise(r => setTimeout(r, 0));
      }
    }

    if (onGpu) await this.syncGpuMetrics();
    this.metrics.captureSample(true);
    const earlyStop = earlyStopDay != null && state.day < targetDays && this.metrics.aliveCounts().totalAlive === 0;
    const timedOut = performance.now() - this.metrics.wallStart > maxWallMs && state.day < targetDays;
    let outcome = this.metrics.classifyOutcome(earlyStop);
    if (timedOut && outcome !== 'total_extinction') outcome = 'timeout';

    return {
      outcome,
      earlyStop,
      timedOut,
    };
  }

  getReport(runId, runMeta = {})
  {
    const runConfig = {
      seed: state.SEED,
      size: state.cfg.size,
      cfg: { ...state.cfg },
      targetDays: this.runConfig?.targetDays,
      sampleEveryDays: this.runConfig?.sampleEveryDays,
      simBackend: state.gpuSimEnabled ? 'gpu' : 'cpu',
      gpuRequested: this._gpuRequested,
      autoMigration: !!state.batchConfig?.autoMigration,
      fuzzProfile: this.runConfig?.fuzzProfile,
      ...runMeta,
    };
    if (this._gpuFailReason) runConfig.gpuFailReason = this._gpuFailReason;
    const balanceConfig = buildBalanceConfigSnapshot(this.balanceOverrides);
    return this.metrics.buildReport(runConfig, balanceConfig, {
      runId,
      outcome: runMeta.outcome,
      earlyStop: runMeta.earlyStop,
    });
  }

  teardown()
  {
    teardownBatchGpu();
    state.batchMode = false;
    state.batchConfig = null;
    state.ready = false;
  }
}
