import { loadSpeciesData } from '../data.js';
import { loadBehaviorLibrary } from '../behavior/loader.js';
import { BatchRunner, parseBatchParams, overridesFromUi } from './runner.js';
import { BalanceUi } from './balance-ui.js';
import { batchReportStore } from './report-store.js';

class BatchTestApp
{
  constructor()
  {
    this.params = parseBatchParams();
    this.runner = new BatchRunner({
      onProgress: prog => this.updateProgress(prog),
      onReport: report => this.addHistoryRow(report),
      onCampaign: campaign => this.onCampaignComplete(campaign),
      onComplete: report => this.onRunComplete(report),
    });
    this.balanceUi = null;
  }

  async init()
  {
    await loadSpeciesData();
    await loadBehaviorLibrary();

    this.balanceUi = new BalanceUi(document.getElementById('balance-panel'));
    if (this.params.balanceOverrides)
    {
      this.balanceUi.setOverrides(this.params.balanceOverrides);
    }
    else
    {
      this.balanceUi.render();
    }

    this.bindForm();
    await this.refreshHistory();

    if (this.params.autostart)
    {
      this.startRun();
    }
  }

  bindForm()
  {
    const sim = document.getElementById('sim-config');
    const fuzz = document.getElementById('fuzz-config');
    sim.seed.value = this.params.seed;
    sim.size.value = this.params.size;
    sim.days.value = this.params.days;
    sim.sampleEvery.value = this.params.sampleEvery;
    sim.animals.value = this.params.animals;
    sim.autoMigration.checked = this.params.autoMigration;
    sim.sim.value = this.params.sim;
    sim.runs.value = this.params.runs;
    fuzz.fuzz.checked = this.params.fuzz;
    fuzz.fuzzTrials.value = this.params.fuzzTrials;
    fuzz.fuzzSeed.value = this.params.fuzzSeed;
    fuzz.fuzzIntensity.value = this.params.fuzzIntensity;
    fuzz.fuzzScope.value = this.params.fuzzScope;
    fuzz.fuzzProfile.value = this.params.fuzzProfile;

    document.getElementById('run-btn').addEventListener('click', () => this.startRun());
    document.getElementById('abort-btn').addEventListener('click', () => this.runner.abort());
    document.getElementById('export-csv-btn').addEventListener('click', () => this.exportCsv());
  }

  readParamsFromForm()
  {
    const sim = document.getElementById('sim-config');
    const fuzz = document.getElementById('fuzz-config');
    return {
      ...this.params,
      seed: Number(sim.seed.value) || this.params.seed,
      size: sim.size.value,
      days: Number(sim.days.value) || 200,
      sampleEvery: Number(sim.sampleEvery.value) || 10,
      animals: Number(sim.animals.value) || 0.45,
      autoMigration: sim.autoMigration.checked,
      sim: sim.sim.value,
      runs: Number(sim.runs.value) || 1,
      saveServer: true,
      balanceOverrides: overridesFromUi(this.balanceUi),
      fuzz: fuzz.fuzz.checked,
      fuzzTrials: Number(fuzz.fuzzTrials.value) || 50,
      fuzzSeed: Number(fuzz.fuzzSeed.value) || this.params.fuzzSeed,
      fuzzIntensity: Number(fuzz.fuzzIntensity.value) || 0.15,
      fuzzScope: fuzz.fuzzScope.value || 'all',
      fuzzProfile: fuzz.fuzzProfile.value || 'fast',
    };
  }

  setStatus(text, done = false)
  {
    const el = document.getElementById('batch-status');
    el.textContent = text;
    el.dataset.done = done ? '1' : '0';
    if (done && window.__BATCH_PROGRESS__)
    {
      window.__BATCH_PROGRESS__.phase = 'done';
    }
  }

  updateProgress(prog)
  {
    const parts = [];
    if (prog.mode === 'fuzz')
    {
      parts.push(`Trial ${prog.trialIndex + 1}/${prog.trialTotal}`);
    }
    else if (prog.runTotal > 1)
    {
      parts.push(`Run ${prog.runIndex + 1}/${prog.runTotal}`);
    }
    parts.push(`Day ${prog.day}/${prog.targetDays}`);
    parts.push(`Pop ${prog.totalAlive}`);
    parts.push(`Gen ${prog.generationMax}`);
    if (prog.simBackend === 'gpu') parts.push('GPU');
    parts.push(`${(prog.wallMs / 1000).toFixed(1)}s`);
    this.setStatus(parts.join(' · '));

    window.__BATCH_PROGRESS__ = {
      phase: 'running',
      mode: prog.mode || 'single',
      day: prog.day,
      targetDays: prog.targetDays,
      totalAlive: prog.totalAlive,
      generationMax: prog.generationMax,
      wallMs: prog.wallMs,
      trialIndex: prog.trialIndex ?? 0,
      trialTotal: prog.trialTotal ?? 1,
      runIndex: prog.runIndex ?? 0,
      runTotal: prog.runTotal ?? 1,
      message: parts.join(' · '),
    };
  }

  async startRun()
  {
    if (this.runner.isRunning()) return;
    const params = this.readParamsFromForm();
    window.__BATCH_PROGRESS__ = {
      phase: 'starting',
      mode: params.fuzz ? 'fuzz' : (params.runs > 1 ? 'sequential' : 'single'),
      day: 0,
      targetDays: params.days,
      totalAlive: 0,
      generationMax: 0,
      wallMs: 0,
      trialIndex: 0,
      trialTotal: params.fuzz ? params.fuzzTrials : 1,
      runIndex: 0,
      runTotal: params.runs,
      message: 'Starting…',
    };
    window.__BATCH_COMPLETE__ = null;
    window.__FUZZ_CAMPAIGN_COMPLETE__ = null;
    this.setStatus('Starting…');
    document.getElementById('run-btn').disabled = true;
    try
    {
      await this.runner.start(params);
      this.setStatus('Done', true);
    }
    catch (err)
    {
      console.error(err);
      if (window.__BATCH_PROGRESS__) window.__BATCH_PROGRESS__.phase = 'error';
      this.setStatus(`Error: ${err.message}`, true);
    }
    finally
    {
      document.getElementById('run-btn').disabled = false;
      await this.refreshHistory();
    }
  }

  onRunComplete(report)
  {
    this.setStatus(`Done — ${report.outcome} (pop ${report.summary.finalPop})`, true);
  }

  onCampaignComplete(campaign)
  {
    const top = campaign.ranked[0];
    this.setStatus(
      `Fuzz done — ${campaign.histogram.stable} stable / ${campaign.fuzzTrials} trials` +
      (top ? ` · best pop ${top.finalPop}` : ''),
      true,
    );
    this.renderCampaign(campaign);
  }

  renderCampaign(campaign)
  {
    const el = document.getElementById('campaign-results');
    el.innerHTML = '';
    const h = document.createElement('h3');
    h.textContent = `Campaign ${campaign.campaignId} (${campaign.trialsPerMinute?.toFixed(1)} trials/min)`;
    el.appendChild(h);
    const table = document.createElement('table');
    table.innerHTML = '<thead><tr><th>Score</th><th>Outcome</th><th>Pop</th><th>Gen</th><th></th></tr></thead>';
    const tbody = document.createElement('tbody');
    for (const row of campaign.ranked.slice(0, 10))
    {
      const tr = document.createElement('tr');
      tr.innerHTML = `<td>${row.score?.toFixed(2)}</td><td>${row.outcome}</td><td>${row.finalPop}</td><td>${row.generationMax}</td><td></td>`;
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.textContent = 'Load config';
      btn.addEventListener('click', () =>
      {
        if (row.balanceConfig)
        {
          this.balanceUi.setOverrides({
            speciesOverrides: row.balanceConfig.speciesOverrides || {},
            behaviorLibraryOverrides: row.balanceConfig.behaviorLibraryOverrides || {},
            behaviorSpeciesOverrides: row.balanceConfig.behaviorSpeciesOverrides || {},
          });
        }
      });
      tr.lastElementChild.appendChild(btn);
      tbody.appendChild(tr);
    }
    table.appendChild(tbody);
    el.appendChild(table);
  }

  addHistoryRow(report)
  {
    const tbody = document.querySelector('#history-table tbody');
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${report.runId}</td><td>${report.outcome}</td><td>${report.summary?.finalPop ?? 0}</td><td>${report.summary?.generationMax ?? 0}</td><td>${report.config?.simBackend ?? 'cpu'}</td><td>${(report.wallMs / 1000).toFixed(1)}s</td><td></td>`;
    const dl = document.createElement('button');
    dl.type = 'button';
    dl.textContent = 'JSON';
    dl.addEventListener('click', () =>
    {
      const blob = new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `${report.runId}.json`;
      a.click();
    });
    tr.lastElementChild.appendChild(dl);
    tbody.prepend(tr);
  }

  async refreshHistory()
  {
    const reports = await batchReportStore.listReports(30);
    const tbody = document.querySelector('#history-table tbody');
    tbody.innerHTML = '';
    for (const report of reports)
    {
      this.addHistoryRow(report);
    }
  }

  async exportCsv()
  {
    const reports = await batchReportStore.listReports(200);
    const lines = ['runId,outcome,score,finalPop,generationMax,seed,hungerGraze,rabbitLifespan'];
    for (const r of reports)
    {
      const hg = r.balanceConfig?.effective?.behavior?.rabbit?.thresholds?.hungerGraze ?? '';
      const life = r.balanceConfig?.effective?.species?.rabbit?.base?.lifespan ?? '';
      lines.push([
        r.runId,
        r.outcome,
        r.score ?? '',
        r.summary?.finalPop ?? '',
        r.summary?.generationMax ?? '',
        r.config?.seed ?? '',
        hg,
        life,
      ].join(','));
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'batch-reports.csv';
    a.click();
  }
}

const app = new BatchTestApp();
app.init().catch(err =>
{
  console.error('Batch test init failed:', err);
  document.getElementById('batch-status').textContent = `Init error: ${err.message}`;
});

window.batchTestApp = app;
