import { computeBalanceScore } from './balance-recommendations.js';
import { buildHistoryDetailElement } from './history-detail.js';
import { batchReportStore } from './report-store.js';

function reportTimeMs(report)
{
  if (report?.startedAt)
  {
    const ms = Date.parse(report.startedAt);
    if (Number.isFinite(ms)) return ms;
  }
  const m = /^batch-(\d+)-/.exec(report?.runId || '');
  return m ? Number(m[1]) : 0;
}

function formatReportTime(report)
{
  const ms = reportTimeMs(report);
  if (!ms) return '—';
  return new Date(ms).toLocaleString();
}

function compareValues(a, b)
{
  const na = typeof a === 'number' && Number.isFinite(a);
  const nb = typeof b === 'number' && Number.isFinite(b);
  if (na && nb) return a - b;
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
}

function balanceScoreFor(report)
{
  return report.balanceScore ?? computeBalanceScore(report);
}

export class BalanceRunsTable
{
  constructor(hostEl, options = {})
  {
    this.host = hostEl;
    this.reports = [];
    this.sortKey = 'time';
    this.sortDir = 'desc';
    this._expanded = new Set();
    this._selected = new Set();
    this.onLoadBalance = options.onLoadBalance || null;
    this.onSwitchToDesigner = options.onSwitchToDesigner || null;
    this.onStatus = options.onStatus || null;
    this.onRefresh = options.onRefresh || null;
    this._rankMap = new Map();
    this._mount();
  }

  _mount()
  {
    this.host.innerHTML = '';
    this.host.className = 'balance-runs-host';

    const toolbar = document.createElement('div');
    toolbar.className = 'balance-runs-toolbar';

    this._archiveBtn = document.createElement('button');
    this._archiveBtn.type = 'button';
    this._archiveBtn.className = 'btn';
    this._archiveBtn.textContent = 'Archive';
    this._archiveBtn.disabled = true;
    this._archiveBtn.addEventListener('click', () => this.archiveSelected());

    this._deleteBtn = document.createElement('button');
    this._deleteBtn.type = 'button';
    this._deleteBtn.className = 'btn danger';
    this._deleteBtn.textContent = 'Delete';
    this._deleteBtn.disabled = true;
    this._deleteBtn.addEventListener('click', () => this.deleteSelected());

    this._exportBtn = document.createElement('button');
    this._exportBtn.type = 'button';
    this._exportBtn.className = 'btn';
    this._exportBtn.textContent = 'Export CSV';
    this._exportBtn.addEventListener('click', () => this.exportCsv());

    this._selectionCount = document.createElement('span');
    this._selectionCount.className = 'balance-runs-selection-count';
    this._selectionCount.textContent = '0 selected';

    toolbar.appendChild(this._archiveBtn);
    toolbar.appendChild(this._deleteBtn);
    toolbar.appendChild(this._exportBtn);
    toolbar.appendChild(this._selectionCount);
    this.host.appendChild(toolbar);

    const wrap = document.createElement('div');
    wrap.className = 'batch-table-wrap balance-runs-table-wrap';
    this._table = document.createElement('table');
    this._table.id = 'balance-runs-table';
    this._table.innerHTML = `<thead><tr>
      <th class="history-check-col"><input type="checkbox" class="history-check balance-runs-select-all" aria-label="Select all saved runs"></th>
      <th aria-label="Expand"></th>
      <th><button type="button" class="sort-header" data-sort="rank">Rank <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header active" data-sort="time">Time <span class="sort-indicator">↓</span></button></th>
      <th><button type="button" class="sort-header" data-sort="runId">Run ID <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="outcome">Outcome <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="score">Stability <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="balance">Balance <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="pop">Pop <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="gen">Gen <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="sim">Sim <span class="sort-indicator"></span></button></th>
      <th><button type="button" class="sort-header" data-sort="wall">Wall <span class="sort-indicator"></span></button></th>
      <th></th>
    </tr></thead>`;
    this._tbody = document.createElement('tbody');
    this._table.appendChild(this._tbody);
    wrap.appendChild(this._table);
    this.host.appendChild(wrap);

    this._selectAll = this._table.querySelector('.balance-runs-select-all');
    this._selectAll.addEventListener('change', (e) => this.setAllSelected(e.target.checked));
    this._table.querySelectorAll('.sort-header').forEach(btn =>
    {
      btn.addEventListener('click', () => this.setSort(btn.dataset.sort));
    });
  }

  setReports(reports)
  {
    this.reports = reports || [];
    const visible = new Set(this.reports.map(r => r.runId));
    for (const runId of this._selected)
    {
      if (!visible.has(runId)) this._selected.delete(runId);
    }
    for (const runId of this._expanded)
    {
      if (!visible.has(runId)) this._expanded.delete(runId);
    }
    this._recomputeRanks();
    this.render();
  }

  upsertReport(report)
  {
    const i = this.reports.findIndex(r => r.runId === report.runId);
    if (i >= 0) this.reports[i] = report;
    else this.reports.push(report);
    this._recomputeRanks();
    this.render();
  }

  _recomputeRanks()
  {
    this._rankMap = new Map();
    const rankable = this.reports.filter(r => !r.archived);
    const sorted = [...rankable].sort((a, b) =>
    {
      const ba = balanceScoreFor(a);
      const bb = balanceScoreFor(b);
      if (bb !== ba) return bb - ba;
      const sa = a.score ?? 0;
      const sb = b.score ?? 0;
      if (sb !== sa) return sb - sa;
      const pa = a.summary?.finalPop ?? 0;
      const pb = b.summary?.finalPop ?? 0;
      if (pb !== pa) return pb - pa;
      return reportTimeMs(b) - reportTimeMs(a);
    });
    sorted.forEach((r, i) => this._rankMap.set(r.runId, i + 1));
  }

  rankFor(report)
  {
    if (report.archived) return '—';
    const rank = this._rankMap.get(report.runId);
    return rank != null ? String(rank) : '—';
  }

  setSort(key)
  {
    if (this.sortKey === key)
    {
      this.sortDir = this.sortDir === 'desc' ? 'asc' : 'desc';
    }
    else
    {
      this.sortKey = key;
      this.sortDir = 'desc';
    }
    this.render();
  }

  sortValue(report, key)
  {
    switch (key)
    {
      case 'rank':
      {
        const rank = this._rankMap.get(report.runId);
        return rank != null ? rank : Number.MAX_SAFE_INTEGER;
      }
      case 'runId': return report.runId || '';
      case 'time': return reportTimeMs(report);
      case 'outcome': return report.outcome || '';
      case 'score': return report.score ?? 0;
      case 'balance': return balanceScoreFor(report);
      case 'pop': return report.summary?.finalPop ?? 0;
      case 'gen': return report.summary?.generationMax ?? 0;
      case 'sim': return report.config?.simBackend || 'cpu';
      case 'wall': return report.wallMs ?? 0;
      default: return '';
    }
  }

  sortedReports()
  {
    const dir = this.sortDir === 'asc' ? 1 : -1;
    const key = this.sortKey;
    return [...this.reports].sort((a, b) =>
    {
      const cmp = compareValues(this.sortValue(a, key), this.sortValue(b, key));
      if (cmp !== 0) return cmp * dir;
      return compareValues(reportTimeMs(a), reportTimeMs(b)) * -1;
    });
  }

  _updateSortHeaders()
  {
    this._table.querySelectorAll('.sort-header').forEach(btn =>
    {
      const active = btn.dataset.sort === this.sortKey;
      btn.classList.toggle('active', active);
      const ind = btn.querySelector('.sort-indicator');
      if (ind) ind.textContent = active ? (this.sortDir === 'asc' ? '↑' : '↓') : '';
    });
  }

  toggleExpand(runId)
  {
    if (this._expanded.has(runId)) this._expanded.delete(runId);
    else this._expanded.add(runId);
    this.render();
  }

  toggleSelect(runId, selected)
  {
    if (selected) this._selected.add(runId);
    else this._selected.delete(runId);
    this._updateBulkUi();
  }

  setAllSelected(selected)
  {
    this._selected.clear();
    if (selected)
    {
      for (const report of this.reports)
      {
        this._selected.add(report.runId);
      }
    }
    this.render();
  }

  getSelectedIds()
  {
    return [...this._selected];
  }

  _updateBulkUi()
  {
    const count = this._selected.size;
    this._archiveBtn.disabled = count === 0;
    this._deleteBtn.disabled = count === 0;
    this._selectionCount.textContent = `${count} selected`;
    const visible = this.reports.length;
    this._selectAll.checked = visible > 0 && count === visible;
    this._selectAll.indeterminate = count > 0 && count < visible;
  }

  render()
  {
    if (!this._tbody) return;
    this._updateSortHeaders();
    this._tbody.innerHTML = '';

    if (!this.reports.length)
    {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 13;
      td.className = 'balance-runs-empty';
      td.textContent = 'No saved runs yet. Complete a batch run to see results here.';
      tr.appendChild(td);
      this._tbody.appendChild(tr);
      this._updateBulkUi();
      return;
    }

    for (const report of this.sortedReports())
    {
      const expanded = this._expanded.has(report.runId);
      const selected = this._selected.has(report.runId);
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
        this.toggleSelect(report.runId, e.target.checked);
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
        this.toggleExpand(report.runId);
      });
      toggleTd.appendChild(toggle);
      tr.appendChild(toggleTd);

      const bs = balanceScoreFor(report);
      const cells = [
        this.rankFor(report),
        formatReportTime(report),
        report.runId,
        report.outcome,
        report.score != null ? report.score.toFixed(2) : '—',
        Number.isFinite(bs) ? bs.toFixed(2) : '—',
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
      actionsTd.className = 'balance-runs-actions';

      const loadBtn = document.createElement('button');
      loadBtn.type = 'button';
      loadBtn.className = 'btn';
      loadBtn.textContent = 'Load config';
      loadBtn.addEventListener('click', (e) =>
      {
        e.stopPropagation();
        if (this.onLoadBalance) this.onLoadBalance(report);
        if (this.onSwitchToDesigner) this.onSwitchToDesigner();
      });
      actionsTd.appendChild(loadBtn);

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
        this.toggleExpand(report.runId);
      });
      this._tbody.appendChild(tr);

      if (expanded)
      {
        const detailTr = document.createElement('tr');
        detailTr.className = 'history-detail-row';
        const detailTd = document.createElement('td');
        detailTd.colSpan = 13;
        detailTd.appendChild(buildHistoryDetailElement(report, {
          onLoadBalance: (r) =>
          {
            if (this.onLoadBalance) this.onLoadBalance(r);
            if (this.onSwitchToDesigner) this.onSwitchToDesigner();
          },
        }));
        detailTr.appendChild(detailTd);
        this._tbody.appendChild(detailTr);
      }
    }
    this._updateBulkUi();
  }

  async archiveSelected()
  {
    const runIds = this.getSelectedIds();
    if (!runIds.length) return;
    const archived = await batchReportStore.archiveReports(runIds);
    for (const runId of runIds)
    {
      this._selected.delete(runId);
      this._expanded.delete(runId);
    }
    if (this.onRefresh) await this.onRefresh();
    else
    {
      this.reports = this.reports.filter(r => !runIds.includes(r.runId));
      this._recomputeRanks();
      this.render();
    }
    if (this.onStatus) this.onStatus(`Archived ${archived} run${archived === 1 ? '' : 's'}.`, true);
    return archived;
  }

  async deleteSelected()
  {
    const runIds = this.getSelectedIds();
    if (!runIds.length) return;
    const noun = runIds.length === 1 ? 'this run' : `${runIds.length} runs`;
    if (!window.confirm(`Permanently delete ${noun} from saved runs? This cannot be undone.`)) return;
    await batchReportStore.deleteReports(runIds);
    for (const runId of runIds)
    {
      this._selected.delete(runId);
      this._expanded.delete(runId);
      const i = this.reports.findIndex(r => r.runId === runId);
      if (i >= 0) this.reports.splice(i, 1);
    }
    this._recomputeRanks();
    this.render();
    if (this.onStatus) this.onStatus(`Deleted ${runIds.length} run${runIds.length === 1 ? '' : 's'}.`, true);
  }

  async exportCsv()
  {
    const reports = await batchReportStore.listReports(200);
    const lines = ['runId,startedAt,outcome,score,balanceScore,finalPop,generationMax,seed,hungerGraze,rabbitLifespan'];
    for (const r of reports)
    {
      const hg = r.balanceConfig?.effective?.behavior?.rabbit?.thresholds?.hungerGraze ?? '';
      const life = r.balanceConfig?.effective?.species?.rabbit?.base?.lifespan ?? '';
      const bs = r.balanceScore ?? computeBalanceScore(r);
      lines.push([
        r.runId,
        r.startedAt || '',
        r.outcome,
        r.score ?? '',
        Number.isFinite(bs) ? bs.toFixed(3) : '',
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
