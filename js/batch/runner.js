import { BatchHarness } from './harness.js';
import { batchReportStore } from './report-store.js';
import {
  emptyBalanceOverrides,
  decodeBalanceParam,
  buildBalanceConfigSnapshot,
  getCurrentOverrides,
} from './balance-config.js';
import { generateFuzzOverrides, buildCampaignSummary, createFuzzRng } from './fuzzer.js';
import { computeBalanceScore } from './balance-recommendations.js';

export function parseBatchParams(search = window.location.search)
{
  const p = new URLSearchParams(search);
  const num = (key, def) =>
  {
    const v = p.get(key);
    if (v == null || v === '') return def;
    const n = Number(v);
    return Number.isFinite(n) ? n : def;
  };
  const bool = (key, def = false) =>
  {
    const v = p.get(key);
    if (v == null) return def;
    return v === '1' || v === 'true';
  };

  let balanceOverrides = emptyBalanceOverrides();
  if (p.get('balance'))
  {
    try
    {
      balanceOverrides = decodeBalanceParam(p.get('balance'));
    }
    catch (e)
    {
      console.warn('Invalid balance param', e);
    }
  }

  return {
    seed: num('seed', (Math.random() * 1e9) >>> 0),
    size: p.get('size') || 'm',
    days: num('days', 200),
    sampleEvery: num('sampleEvery', 10),
    animals: num('animals', 0.45),
    autoMigration: bool('autoMigration', false),
    sim: p.get('sim') || 'cpu',
    runs: num('runs', 1),
    autostart: bool('autostart', false),
    saveServer: bool('saveServer', true),
    balanceOverrides,
    fuzz: bool('fuzz', false),
    fuzzTrials: num('fuzzTrials', 50),
    fuzzSeed: num('fuzzSeed', (Math.random() * 1e9) >>> 0),
    fuzzIntensity: num('fuzzIntensity', 0.15),
    fuzzScope: p.get('fuzzScope') || 'all',
    fuzzProfile: p.get('fuzzProfile') || 'fast',
  };
}

function applyFuzzProfile(params)
{
  const profile = params.fuzzProfile || 'fast';
  const useGpu = profile.endsWith('-gpu') || params.sim === 'gpu';

  if (profile === 'fast' || profile === 'fast-gpu')
  {
    const urlSize = new URLSearchParams(window.location.search).get('size');
    return {
      ...params,
      sim: useGpu ? 'gpu' : 'cpu',
      fuzzProfile: profile,
      size: params.size === 'm' && !urlSize ? 's' : params.size,
      sampleEvery: params.sampleEvery === 10 ? 20 : params.sampleEvery,
      days: params.days === 200 ? 80 : params.days,
    };
  }

  if (profile === 'deep-gpu')
  {
    return { ...params, sim: 'gpu', fuzzProfile: profile };
  }

  if (profile === 'deep')
  {
    return { ...params, sim: 'cpu', fuzzProfile: profile };
  }

  return params;
}

function makeRunId(prefix = 'batch')
{
  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}`;
}

export class BatchRunner
{
  constructor(callbacks = {})
  {
    this.callbacks = callbacks;
    this._harness = new BatchHarness();
    this._running = false;
  }

  isRunning()
  {
    return this._running;
  }

  abort()
  {
    this._harness.abort();
  }

  async runSingle(params, overrides, onProgress)
  {
    const wallStart = performance.now();
    const runConfig = {
      seed: params.seed,
      size: params.size,
      targetDays: params.days,
      sampleEveryDays: params.sampleEvery,
      animals: params.animals,
      autoMigration: params.autoMigration,
      simBackend: params.sim,
      fuzzProfile: params.fuzzProfile,
      balanceOverrides: overrides,
    };

    const sparseMode = (params.fuzzProfile === 'fast' || params.fuzzProfile === 'fast-gpu') && params.fuzz;
    await this._harness.init(runConfig);
    await this._harness.generateWorld();

    const runMeta = await this._harness.run({
      targetDays: params.days,
      sampleEveryDays: params.sampleEvery,
      sparseMode,
      earlyStopDay: (params.fuzzProfile === 'fast' || params.fuzzProfile === 'fast-gpu') ? 20 : null,
      maxWallMs: params.sim === 'gpu' ? 180000 : 120000,
      wallStart,
    }, onProgress);

    const runId = makeRunId();
    const report = this._harness.getReport(runId, {
      outcome: runMeta.outcome,
      earlyStop: runMeta.earlyStop,
    });
    this._harness.teardown();
    return report;
  }

  async runSequential(params, onProgress)
  {
    const reports = [];
    for (let i = 0; i < params.runs; i++)
    {
      const runParams = { ...params, seed: params.seed + i };
      const report = await this.runSingle(runParams, params.balanceOverrides, prog =>
      {
        if (onProgress) onProgress({ ...prog, runIndex: i, runTotal: params.runs, mode: 'sequential' });
      });
      reports.push(report);
      if (params.saveServer) await batchReportStore.postToServer(report);
      await batchReportStore.saveReport(report);
      if (this.callbacks.onReport) this.callbacks.onReport(report);
    }
    return reports;
  }

  async runFuzzCampaign(params, onProgress)
  {
    const fuzzParams = applyFuzzProfile({ ...params, fuzz: true });
    const baseline = params.balanceOverrides || emptyBalanceOverrides();
    const trials = [];
    createFuzzRng(fuzzParams.fuzzSeed);

    for (let i = 0; i < fuzzParams.fuzzTrials; i++)
    {
      if (!this._running) break;
      const trialOverrides = generateFuzzOverrides(baseline, {
        intensity: fuzzParams.fuzzIntensity,
        scope: fuzzParams.fuzzScope,
      });
      const trialParams = { ...fuzzParams, seed: fuzzParams.seed + i };
      const report = await this.runSingle(trialParams, trialOverrides, prog =>
      {
        if (onProgress)
        {
          onProgress({
            ...prog,
            trialIndex: i,
            trialTotal: fuzzParams.fuzzTrials,
            mode: 'fuzz',
          });
        }
      });
      report.balanceScore = computeBalanceScore(report);
      trials.push(report);
      if (fuzzParams.saveServer) await batchReportStore.postToServer(report);
      await batchReportStore.saveReport(report);
      if (this.callbacks.onReport) this.callbacks.onReport(report);
    }

    const campaign = buildCampaignSummary(trials, {
      campaignId: makeRunId('fuzz'),
      fuzzSeed: fuzzParams.fuzzSeed,
      fuzzIntensity: fuzzParams.fuzzIntensity,
      fuzzProfile: fuzzParams.fuzzProfile,
      fuzzScope: fuzzParams.fuzzScope,
      baselineBalanceConfig: buildBalanceConfigSnapshot(baseline),
      topN: 10,
    });
    campaign.startedAt = new Date().toISOString();
    if (fuzzParams.saveServer) await batchReportStore.postToServer(campaign);
    await batchReportStore.saveCampaign(campaign);
    return campaign;
  }

  async start(params)
  {
    if (this._running) return null;
    this._running = true;
    window.__BATCH_COMPLETE__ = null;
    window.__FUZZ_CAMPAIGN_COMPLETE__ = null;

    try
    {
      if (params.fuzz)
      {
        const campaign = await this.runFuzzCampaign(params, this.callbacks.onProgress);
        window.__FUZZ_CAMPAIGN_COMPLETE__ = campaign;
        if (this.callbacks.onCampaign) this.callbacks.onCampaign(campaign);
        return campaign;
      }

      const reports = params.runs > 1
        ? await this.runSequential(params, this.callbacks.onProgress)
        : [await this.runSingle(params, params.balanceOverrides, this.callbacks.onProgress)];

      const last = reports[reports.length - 1];
      if (params.saveServer && params.runs === 1) await batchReportStore.postToServer(last);
      if (params.runs === 1) await batchReportStore.saveReport(last);
      window.__BATCH_COMPLETE__ = last;
      if (this.callbacks.onComplete) this.callbacks.onComplete(last);
      return last;
    }
    finally
    {
      this._running = false;
    }
  }
}

export function overridesFromUi(balanceUi)
{
  return balanceUi ? balanceUi.getOverrides() : getCurrentOverrides();
}
