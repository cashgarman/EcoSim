import { loadSpeciesData } from '../data.js';
import { loadBehaviorLibrary } from '../behavior/loader.js';
import { BatchRunner, parseBatchParams, overridesFromUi } from './runner.js';
import { BalanceUi } from './balance-ui.js';
import { batchReportStore } from './report-store.js';
import { mergeStoredParams, saveStoredFormConfig } from './form-storage.js';
import { buildHistoryDetailElement } from './history-detail.js';
import { buildCampaignDetailElement, buildCampaignRecommendationsElement } from './campaign-detail.js';
import { initPanelResize } from './panel-resize.js';
import { initFormFieldHelp, initGlobalFieldHelpDismiss } from './field-help.js';

class BatchTestApp
{
  constructor()
  {
    this.params = mergeStoredParams(parseBatchParams());
    this.runner = new BatchRunner({
      onProgress: prog => this.updateProgress(prog),
      onReport: report => this.addHistoryRow(report),
      onCampaign: campaign => this.onCampaignComplete(campaign),
      onComplete: report => this.onRunComplete(report),
    });
    this.balanceUi = null;
    this.historySortKey = 'time';
    this.historySortDir = 'desc';
    this._historyReports = [];
    this._historyExpanded = new Set();
    this._historySelected = new Set();
    this._campaign = null;
    this._campaignExpanded = new Set();
    this.campaignSortKey = 'score';
    this.campaignSortDir = 'desc';
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
    initPanelResize();
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

    initFormFieldHelp(sim);
    initFormFieldHelp(fuzz);
    initGlobalFieldHelpDismiss();

    const persistForm = () => saveStoredFormConfig(this.readParamsFromForm());
    sim.addEventListener('change', persistForm);
    sim.addEventListener('input', persistForm);
    fuzz.addEventListener('change', persistForm);
    fuzz.addEventListener('input', persistForm);

    document.getElementById('run-btn').addEventListener('click', () => this.startRun());
    document.getElementById('abort-btn').addEventListener('click', () => this.runner.abort());
    document.getElementById('export-csv-btn').addEventListener('click', () => this.exportCsv());
    document.getElementById('history-archive-btn').addEventListener('click', () => this.archiveSelectedHistory());
    document.getElementById('history-delete-btn').addEventListener('click', () => this.deleteSelectedHistory());
    document.getElementById('history-select-all').addEventListener('change', (e) =>
    {
      this.setAllHistorySelected(e.target.checked);
    });
    document.querySelectorAll('#history-table .sort-header').forEach(btn =>
    {
      btn.addEventListener('click', () => this.setHistorySort(btn.dataset.sort));
    });
  }

  reportTimeMs(report)
  {
    if (report?.startedAt)
    {
      const ms = Date.parse(report.startedAt);
      if (Number.isFinite(ms)) return ms;
    }
    const m = /^batch-(\d+)-/.exec(report?.runId || '');
    return m ? Number(m[1]) : 0;
  }

  formatReportTime(report)
  {
    const ms = this.reportTimeMs(report);
    if (!ms) return '—';
    return new Date(ms).toLocaleString();
  }

  setHistorySort(key)
  {
    if (this.historySortKey === key)
    {
      this.historySortDir = this.historySortDir === 'desc' ? 'asc' : 'desc';
    }
    else
    {
      this.historySortKey = key;
      this.historySortDir = 'desc';
    }
    this.renderHistoryTable();
  }

  historySortValue(report, key)
  {
    switch (key)
    {
      case 'runId': return report.runId || '';
      case 'time': return this.reportTimeMs(report);
      case 'outcome': return report.outcome || '';
      case 'pop': return report.summary?.finalPop ?? 0;
      case 'gen': return report.summary?.generationMax ?? 0;
      case 'sim': return report.config?.simBackend || 'cpu';
      case 'wall': return report.wallMs ?? 0;
      default: return '';
    }
  }

  compareHistoryValues(a, b)
  {
    const na = typeof a === 'number' && Number.isFinite(a);
    const nb = typeof b === 'number' && Number.isFinite(b);
    if (na && nb) return a - b;
    return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
  }

  upsertHistoryReport(report)
  {
    const i = this._historyReports.findIndex(r => r.runId === report.runId);
    if (i >= 0) this._historyReports[i] = report;
    else this._historyReports.push(report);
    this.renderHistoryTable();
  }

  sortHistoryReports(reports)
  {
    const dir = this.historySortDir === 'asc' ? 1 : -1;
    const key = this.historySortKey;
    return [...reports].sort((a, b) =>
    {
      const cmp = this.compareHistoryValues(
        this.historySortValue(a, key),
        this.historySortValue(b, key),
      );
      if (cmp !== 0) return cmp * dir;
      return this.compareHistoryValues(this.reportTimeMs(a), this.reportTimeMs(b)) * -1;
    });
  }

  updateHistorySortHeaders()
  {
    document.querySelectorAll('#history-table .sort-header').forEach(btn =>
    {
      const active = btn.dataset.sort === this.historySortKey;
      btn.classList.toggle('active', active);
      const ind = btn.querySelector('.sort-indicator');
      if (ind) ind.textContent = active ? (this.historySortDir === 'asc' ? '↑' : '↓') : '';
    });
  }

  toggleHistoryExpand(runId)
  {
    if (this._historyExpanded.has(runId)) this._historyExpanded.delete(runId);
    else this._historyExpanded.add(runId);
    this.renderHistoryTable();
  }

  toggleHistorySelect(runId, selected)
  {
    if (selected) this._historySelected.add(runId);
    else this._historySelected.delete(runId);
    this.updateHistoryBulkUi();
  }

  setAllHistorySelected(selected)
  {
    this._historySelected.clear();
    if (selected)
    {
      for (const report of this._historyReports)
      {
        this._historySelected.add(report.runId);
      }
    }
    this.renderHistoryTable();
  }

  getSelectedHistoryIds()
  {
    return [...this._historySelected];
  }

  updateHistoryBulkUi()
  {
    const count = this._historySelected.size;
    const archiveBtn = document.getElementById('history-archive-btn');
    const deleteBtn = document.getElementById('history-delete-btn');
    const countEl = document.getElementById('history-selection-count');
    const selectAll = document.getElementById('history-select-all');
    if (archiveBtn) archiveBtn.disabled = count === 0;
    if (deleteBtn) deleteBtn.disabled = count === 0;
    if (countEl) countEl.textContent = `${count} selected`;
    if (selectAll)
    {
      const visible = this._historyReports.length;
      selectAll.checked = visible > 0 && count === visible;
      selectAll.indeterminate = count > 0 && count < visible;
    }
  }

  async archiveSelectedHistory()
  {
    const runIds = this.getSelectedHistoryIds();
    if (!runIds.length) return;
    const archived = await batchReportStore.archiveReports(runIds);
    for (const runId of runIds)
    {
      this._historySelected.delete(runId);
      this._historyExpanded.delete(runId);
    }
    await this.refreshHistory();
    this.setStatus(`Archived ${archived} run${archived === 1 ? '' : 's'}.`, true);
  }

  async deleteSelectedHistory()
  {
    const runIds = this.getSelectedHistoryIds();
    if (!runIds.length) return;
    const noun = runIds.length === 1 ? 'this run' : `${runIds.length} runs`;
    if (!window.confirm(`Permanently delete ${noun} from history? This cannot be undone.`)) return;
    await batchReportStore.deleteReports(runIds);
    for (const runId of runIds)
    {
      this._historySelected.delete(runId);
      this._historyExpanded.delete(runId);
    }
    await this.refreshHistory();
    this.setStatus(`Deleted ${runIds.length} run${runIds.length === 1 ? '' : 's'}.`, true);
  }

  loadBalanceFromReport(report)
  {
    const balance = report.balanceConfig;
    if (!balance) return;
    this.balanceUi.setOverrides({
      speciesOverrides: balance.speciesOverrides || {},
      behaviorLibraryOverrides: balance.behaviorLibraryOverrides || {},
      behaviorSpeciesOverrides: balance.behaviorSpeciesOverrides || {},
    });
  }

  applyRecommendedConfig(recommendations)
  {
    if (!recommendations?.overrides) return;
    this.balanceUi.setOverrides(recommendations.overrides);
    this.setStatus('Recommended balance config applied to tuning panel.', true);
  }

  async applyRecommendedAndValidate(recommendations)
  {
    this.applyRecommendedConfig(recommendations);
    const sim = document.getElementById('sim-config');
    if (sim?.fuzz) sim.fuzz.checked = false;
    const params = this.readParamsFromForm();
    params.fuzz = false;
    params.runs = 1;
    if (params.fuzzProfile?.includes('deep') || params.days < 120)
    {
      params.days = Math.max(params.days, 120);
      if (sim?.days) sim.days.value = String(params.days);
    }
    document.getElementById('run-btn').disabled = true;
    this.setStatus('Validating recommended config…');
    try
    {
      await this.runner.start(params);
      this.setStatus('Validation run complete.', true);
    }
    catch (err)
    {
      console.error(err);
      this.setStatus(`Validation error: ${err.message}`, true);
    }
    finally
    {
      document.getElementById('run-btn').disabled = false;
      await this.refreshHistory();
    }
  }

  renderHistoryTable()
  {
    this.updateHistorySortHeaders();

    const tbody = document.querySelector('#history-table tbody');
    tbody.innerHTML = '';
    const sorted = this.sortHistoryReports(this._historyReports);
    for (const report of sorted)
    {
      const expanded = this._historyExpanded.has(report.runId);
      const selected = this._historySelected.has(report.runId);
      const tr = document.createElement('tr');
      tr.className = 'history-row'
        + (expanded ? ' expanded' : '')
        + (selected ? ' selected' : '');

      const checkTd = document.createElement('td');
      checkTd.className = 'history-check-col';
      const checkbox = document.createElement('input');
      checkbox.type = 'checkbox';
      checkbox.className = 'history-check';
      checkbox.checked = selected;
      checkbox.setAttribute('aria-label', `Select ${report.runId}`);
      checkbox.addEventListener('click', (e) => e.stopPropagation());
      checkbox.addEventListener('change', (e) =>
      {
        e.stopPropagation();
        this.toggleHistorySelect(report.runId, e.target.checked);
        tr.classList.toggle('selected', e.target.checked);
      });
      checkTd.appendChild(checkbox);
      tr.appendChild(checkTd);

      const toggleTd = document.createElement('td');
      const toggle = document.createElement('button');
      toggle.type = 'button';
      toggle.className = 'expand-btn btn';
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      toggle.setAttribute('aria-label', expanded ? 'Collapse run details' : 'Expand run details');
      toggle.textContent = expanded ? '▼' : '▶';
      toggle.addEventListener('click', (e) =>
      {
        e.stopPropagation();
        this.toggleHistoryExpand(report.runId);
      });
      toggleTd.appendChild(toggle);
      tr.appendChild(toggleTd);

      const cells = [
        report.runId,
        this.formatReportTime(report),
        report.outcome,
        report.summary?.finalPop ?? 0,
        report.summary?.generationMax ?? 0,
        report.config?.simBackend ?? 'cpu',
        `${(report.wallMs / 1000).toFixed(1)}s`,
      ];
      for (const text of cells)
      {
        const td = document.createElement('td');
        td.textContent = String(text);
        tr.appendChild(td);
      }

      const actionsTd = document.createElement('td');
      const dl = document.createElement('button');
      dl.type = 'button';
      dl.className = 'btn';
      dl.textContent = 'JSON';
      dl.addEventListener('click', (e) =>
      {
        e.stopPropagation();
        const blob = new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = `${report.runId}.json`;
        a.click();
      });
      actionsTd.appendChild(dl);
      tr.appendChild(actionsTd);

      tr.addEventListener('click', (e) =>
      {
        if (e.target.closest('button, input, label, a')) return;
        this.toggleHistoryExpand(report.runId);
      });
      tbody.appendChild(tr);

      if (expanded)
      {
        const detailTr = document.createElement('tr');
        detailTr.className = 'history-detail-row';
        const detailTd = document.createElement('td');
        detailTd.colSpan = 10;
        detailTd.appendChild(buildHistoryDetailElement(report, {
          onLoadBalance: (r) => this.loadBalanceFromReport(r),
        }));
        detailTr.appendChild(detailTd);
        tbody.appendChild(detailTr);
      }
    }
    this.updateHistoryBulkUi();
  }

  addHistoryRow(report)
  {
    this.upsertHistoryReport(report);
  }

  readParamsFromForm()
  {
    const sim = document.getElementById('sim-config');
    const fuzz = document.getElementById('fuzz-config');
    const params = {
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
    saveStoredFormConfig(params);
    return params;
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
    this._campaign = campaign;
    const top = campaign.ranked[0];
    this.setStatus(
      `Fuzz done — ${campaign.histogram.stable} stable / ${campaign.fuzzTrials} trials` +
      (top ? ` · best pop ${top.finalPop}` : ''),
      true,
    );
    this.renderCampaign();
  }

  campaignSortValue(row, key)
  {
    switch (key)
    {
      case 'score': return row.score ?? 0;
      case 'balance': return row.balanceScore ?? 0;
      case 'outcome': return row.outcome || '';
      case 'pop': return row.finalPop ?? 0;
      case 'gen': return row.generationMax ?? 0;
      default: return '';
    }
  }

  sortCampaignRows(rows)
  {
    const dir = this.campaignSortDir === 'asc' ? 1 : -1;
    const key = this.campaignSortKey;
    return [...rows].sort((a, b) =>
    {
      const cmp = this.compareHistoryValues(
        this.campaignSortValue(a, key),
        this.campaignSortValue(b, key),
      );
      if (cmp !== 0) return cmp * dir;
      return this.compareHistoryValues(a.score ?? 0, b.score ?? 0) * -1;
    });
  }

  setCampaignSort(key)
  {
    if (this.campaignSortKey === key)
    {
      this.campaignSortDir = this.campaignSortDir === 'desc' ? 'asc' : 'desc';
    }
    else
    {
      this.campaignSortKey = key;
      this.campaignSortDir = 'desc';
    }
    this.renderCampaign();
  }

  toggleCampaignExpand(runId)
  {
    if (this._campaignExpanded.has(runId)) this._campaignExpanded.delete(runId);
    else this._campaignExpanded.add(runId);
    this.renderCampaign();
  }

  updateCampaignSortHeaders()
  {
    document.querySelectorAll('#campaign-table .sort-header').forEach(btn =>
    {
      const active = btn.dataset.sort === this.campaignSortKey;
      btn.classList.toggle('active', active);
      const ind = btn.querySelector('.sort-indicator');
      if (ind) ind.textContent = active ? (this.campaignSortDir === 'asc' ? '↑' : '↓') : '';
    });
  }

  renderCampaign()
  {
    const el = document.getElementById('campaign-results');
    el.innerHTML = '';
    const campaign = this._campaign;
    if (!campaign?.ranked?.length)
    {
      el.innerHTML = '<p class="empty">Fuzz campaign rankings appear here.</p>';
      return;
    }

    const h = document.createElement('h3');
    h.textContent = `Campaign ${campaign.campaignId} (${campaign.trialsPerMinute?.toFixed(1)} trials/min)`;
    el.appendChild(h);

    const hist = campaign.histogram || {};
    const histLine = document.createElement('p');
    histLine.className = 'campaign-histogram';
    histLine.textContent = `stable ${hist.stable ?? 0} · partial ${hist.partial_collapse ?? 0} · extinct ${hist.total_extinction ?? 0} · timeout ${hist.timeout ?? 0}`;
    el.appendChild(histLine);

    if (campaign.recommendations)
    {
      el.appendChild(buildCampaignRecommendationsElement(campaign.recommendations, {
        onApply: rec => this.applyRecommendedConfig(rec),
        onApplyAndValidate: rec => this.applyRecommendedAndValidate(rec),
      }));
    }

    const wrap = document.createElement('div');
    wrap.className = 'batch-table-wrap campaign-table-wrap';
    const table = document.createElement('table');
    table.id = 'campaign-table';
    table.innerHTML = `<thead><tr>
      <th aria-label="Expand"></th>
      <th><button type="button" class="sort-header" data-sort="score">Score <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="balance">Balance <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="outcome">Outcome <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="pop">Pop <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="gen">Gen <span class="sort-indicator"></span></button></th>
      <th></th>
    </tr></thead>`;
    table.querySelectorAll('.sort-header').forEach(btn =>
    {
      btn.addEventListener('click', () => this.setCampaignSort(btn.dataset.sort));
    });

    const tbody = document.createElement('tbody');
    const sorted = this.sortCampaignRows(campaign.ranked);
    for (const row of sorted)
    {
      const expanded = this._campaignExpanded.has(row.runId);
      const tr = document.createElement('tr');
      tr.className = 'campaign-row' + (expanded ? ' expanded' : '');

      const toggleTd = document.createElement('td');
      const toggle = document.createElement('button');
      toggle.type = 'button';
      toggle.className = 'expand-btn btn';
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      toggle.textContent = expanded ? '▼' : '▶';
      toggle.addEventListener('click', (e) =>
      {
        e.stopPropagation();
        this.toggleCampaignExpand(row.runId);
      });
      toggleTd.appendChild(toggle);
      tr.appendChild(toggleTd);

      const cells = [
        row.score?.toFixed(2) ?? '0.00',
        row.balanceScore != null ? row.balanceScore.toFixed(2) : '—',
        row.outcome,
        row.finalPop,
        row.generationMax,
      ];
      for (const text of cells)
      {
        const td = document.createElement('td');
        td.textContent = String(text);
        tr.appendChild(td);
      }

      const actionsTd = document.createElement('td');
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'btn';
      btn.textContent = 'Load config';
      btn.addEventListener('click', (e) =>
      {
        e.stopPropagation();
        if (row.balanceConfig)
        {
          this.balanceUi.setOverrides({
            speciesOverrides: row.balanceConfig.speciesOverrides || {},
            behaviorLibraryOverrides: row.balanceConfig.behaviorLibraryOverrides || {},
            behaviorSpeciesOverrides: row.balanceConfig.behaviorSpeciesOverrides || {},
          });
        }
      });
      actionsTd.appendChild(btn);
      tr.appendChild(actionsTd);

      tr.addEventListener('click', (e) =>
      {
        if (e.target.closest('button')) return;
        this.toggleCampaignExpand(row.runId);
      });
      tbody.appendChild(tr);

      if (expanded)
      {
        const detailTr = document.createElement('tr');
        detailTr.className = 'campaign-detail-row';
        const detailTd = document.createElement('td');
        detailTd.colSpan = 7;
        detailTd.appendChild(buildCampaignDetailElement(row, {
          baselineBalanceConfig: campaign.baselineBalanceConfig,
        }));
        detailTr.appendChild(detailTd);
        tbody.appendChild(detailTr);
      }
    }

    table.appendChild(tbody);
    wrap.appendChild(table);
    el.appendChild(wrap);
    this.updateCampaignSortHeaders();
  }

  async refreshHistory()
  {
    this._historyReports = await batchReportStore.listReports(30);
    const visible = new Set(this._historyReports.map(r => r.runId));
    for (const runId of this._historySelected)
    {
      if (!visible.has(runId)) this._historySelected.delete(runId);
    }
    this.renderHistoryTable();
  }

  async exportCsv()
  {
    const reports = await batchReportStore.listReports(200);
    const lines = ['runId,startedAt,outcome,score,finalPop,generationMax,seed,hungerGraze,rabbitLifespan'];
    for (const r of reports)
    {
      const hg = r.balanceConfig?.effective?.behavior?.rabbit?.thresholds?.hungerGraze ?? '';
      const life = r.balanceConfig?.effective?.species?.rabbit?.base?.lifespan ?? '';
      lines.push([
        r.runId,
        r.startedAt || '',
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
