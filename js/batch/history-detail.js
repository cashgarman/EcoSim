import { SP_KEYS, SPECIES, GENE_LABEL } from '../data.js';

function esc(text)
{
  return String(text ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

function flattenLeaves(obj, prefix = '', out = [])
{
  if (obj == null || typeof obj !== 'object' || Array.isArray(obj))
  {
    if (prefix) out.push({ path: prefix, value: obj });
    return out;
  }
  const keys = Object.keys(obj);
  if (!keys.length)
  {
    if (prefix) out.push({ path: prefix, value: '{}' });
    return out;
  }
  for (const key of keys)
  {
    const next = prefix ? `${prefix}.${key}` : key;
    flattenLeaves(obj[key], next, out);
  }
  return out;
}

function hasOverrideContent(obj)
{
  return flattenLeaves(obj).length > 0;
}

function addSection(root, title)
{
  const h = document.createElement('h4');
  h.className = 'history-detail-title';
  h.textContent = title;
  root.appendChild(h);
  const body = document.createElement('div');
  body.className = 'history-detail-block';
  root.appendChild(body);
  return body;
}

function addSubheading(parent, title)
{
  const h = document.createElement('h5');
  h.className = 'history-species-subtitle';
  h.textContent = title;
  parent.appendChild(h);
}

function addKvGrid(parent, rows)
{
  const dl = document.createElement('dl');
  dl.className = 'history-kv';
  let added = false;
  for (const [k, v] of rows)
  {
    if (v == null || v === '') continue;
    const dt = document.createElement('dt');
    dt.textContent = k;
    const dd = document.createElement('dd');
    dd.textContent = v;
    dl.appendChild(dt);
    dl.appendChild(dd);
    added = true;
  }
  if (added) parent.appendChild(dl);
  return added;
}

function addChangeList(parent, overrides, { emptyText } = {})
{
  const leaves = flattenLeaves(overrides);
  if (!leaves.length)
  {
    if (emptyText)
    {
      const p = document.createElement('p');
      p.className = 'history-detail-empty';
      p.textContent = emptyText;
      parent.appendChild(p);
    }
    return false;
  }
  const ul = document.createElement('ul');
  ul.className = 'history-change-list';
  for (const { path, value } of leaves)
  {
    const li = document.createElement('li');
    li.innerHTML = `<code>${esc(path)}</code> = <span>${esc(formatValue(value))}</span>`;
    ul.appendChild(li);
  }
  parent.appendChild(ul);
  return true;
}

function addCollapsible(parent, { summary, open = false, className = 'history-species' }, buildBody)
{
  const details = document.createElement('details');
  details.className = className;
  if (open) details.open = true;
  const sum = document.createElement('summary');
  sum.innerHTML = summary;
  details.appendChild(sum);
  const body = document.createElement('div');
  body.className = 'history-species-body';
  buildBody(body);
  details.appendChild(body);
  parent.appendChild(details);
  return details;
}

function formatValue(value)
{
  if (value == null) return '—';
  if (typeof value === 'number') return Number.isInteger(value) ? String(value) : value.toFixed(4).replace(/\.?0+$/, '');
  if (typeof value === 'boolean') return value ? 'yes' : 'no';
  if (Array.isArray(value)) return value.join(', ');
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

function formatCfg(cfg = {})
{
  return [
    ['Sea level', cfg.sea],
    ['Temperature', cfg.temp],
    ['Moisture', cfg.moist],
    ['Relief', cfg.relief],
    ['Animals density', cfg.animals],
  ];
}

function dietLabel(diet)
{
  if (diet === 0) return 'Herbivore';
  if (diet === 1) return 'Carnivore';
  if (diet === 2) return 'Omnivore';
  return formatValue(diet);
}

function speciesDisplayName(sp)
{
  const meta = SPECIES[sp];
  if (!meta) return sp;
  const emoji = meta.emoji ? `${meta.emoji} ` : '';
  return `${emoji}${meta.label || sp}`;
}

function speciesSummaryLine(sp, summary, speciesOverrides, behaviorOverrides)
{
  const finalCount = summary.finalCounts?.[sp] ?? 0;
  const extinctDay = summary.extinctAtDay?.[sp];
  const changed = hasOverrideContent(speciesOverrides) || hasOverrideContent(behaviorOverrides);
  const parts = [
    `<span class="history-species-name">${esc(speciesDisplayName(sp))}</span>`,
    `<span class="history-species-meta">${finalCount} final</span>`,
  ];
  if (extinctDay != null) parts.push(`<span class="history-species-meta extinct">extinct day ${extinctDay}</span>`);
  if (changed) parts.push('<span class="history-species-meta changed">modified</span>');
  return parts.join('');
}

function buildSpeciesBody(body, sp, { summary, speciesSnap, behaviorSnap, speciesOverrides, behaviorOverrides })
{
  addSubheading(body, 'Population');
  addKvGrid(body, [
    ['Final count', summary.finalCounts?.[sp] ?? 0],
    ['Extinct at day', summary.extinctAtDay?.[sp] != null ? summary.extinctAtDay[sp] : '—'],
  ]);

  const hasSpeciesOverrides = hasOverrideContent(speciesOverrides);
  const hasBehaviorOverrides = hasOverrideContent(behaviorOverrides);
  if (hasSpeciesOverrides || hasBehaviorOverrides)
  {
    addSubheading(body, 'Overrides');
    if (hasSpeciesOverrides) addChangeList(body, speciesOverrides);
    if (hasBehaviorOverrides)
    {
      if (hasSpeciesOverrides) addSubheading(body, 'Behavior overrides');
      addChangeList(body, behaviorOverrides);
    }
  }

  if (speciesSnap)
  {
    addSubheading(body, 'Genes');
    const geneRows = speciesSnap.base
      ? Object.entries(speciesSnap.base).map(([k, v]) => [GENE_LABEL[k] || k, formatValue(v)])
      : [];
    addKvGrid(body, geneRows);

    addSubheading(body, 'Life cycle');
    addKvGrid(body, [
      ['Diet', dietLabel(speciesSnap.diet)],
      ['Stock weight', speciesSnap.stockWeight],
      ['Gestation (s)', speciesSnap.gestationSec ? speciesSnap.gestationSec.join('–') : null],
      ['Mate cooldown (s)', speciesSnap.mateCooldownSec ? speciesSnap.mateCooldownSec.join('–') : null],
      ['Hunts', speciesSnap.hunts?.length ? speciesSnap.hunts.join(', ') : null],
      ['Prey of', speciesSnap.preyOf?.length ? speciesSnap.preyOf.join(', ') : null],
    ]);
  }

  if (behaviorSnap)
  {
    addSubheading(body, 'Behavior thresholds');
    const threshRows = behaviorSnap.thresholds
      ? Object.entries(behaviorSnap.thresholds).map(([k, v]) => [k, formatValue(v)])
      : [];
    if (!addKvGrid(body, threshRows))
    {
      const p = document.createElement('p');
      p.className = 'history-detail-empty';
      p.textContent = 'No threshold data.';
      body.appendChild(p);
    }

    addSubheading(body, 'Behavior actions');
    const actionRows = behaviorSnap.actions
      ? Object.entries(behaviorSnap.actions).map(([k, v]) => [k, `speed×${formatValue(v.speedMult)}`])
      : [];
    if (!addKvGrid(body, actionRows))
    {
      const p = document.createElement('p');
      p.className = 'history-detail-empty';
      p.textContent = 'No action data.';
      body.appendChild(p);
    }
  }
}

export function buildHistoryDetailElement(report, { onLoadBalance } = {})
{
  const root = document.createElement('div');
  root.className = 'history-detail';

  const cfg = report.config || {};
  const worldCfg = cfg.cfg || {};
  const summary = report.summary || {};
  const balance = report.balanceConfig || {};
  const effective = balance.effective || {};

  const runSec = addSection(root, 'Run settings');
  addKvGrid(runSec, [
    ['Seed', cfg.seed],
    ['World size', cfg.size],
    ['Target days', cfg.targetDays],
    ['Sample every (days)', cfg.sampleEveryDays],
    ['Sim backend', cfg.simBackend],
    ['GPU requested', cfg.gpuRequested],
    ['Auto migration', cfg.autoMigration],
    ['Fuzz profile', cfg.fuzzProfile],
    ['Score', report.score != null ? report.score.toFixed(2) : null],
    ['Wall time', report.wallMs != null ? `${(report.wallMs / 1000).toFixed(1)}s` : null],
    ['Started', report.startedAt ? new Date(report.startedAt).toLocaleString() : null],
    ['Finished', report.finishedAt ? new Date(report.finishedAt).toLocaleString() : null],
  ]);

  const worldSec = addSection(root, 'World generation');
  addKvGrid(worldSec, formatCfg(worldCfg));

  const outcomeSec = addSection(root, 'Outcome');
  addKvGrid(outcomeSec, [
    ['Outcome', report.outcome],
    ['Final population', summary.finalPop],
    ['Peak population', summary.peakPop],
    ['Min population', summary.minPop],
    ['Max generation', summary.generationMax],
    ['Dominant species', summary.dominantSpecies ? speciesDisplayName(summary.dominantSpecies) : null],
    ['Collapse day', summary.collapseDay],
    ['Final day', summary.finalDay],
  ]);

  const animalsSec = addSection(root, 'Animals');
  const speciesBehOverrides = balance.behaviorSpeciesOverrides || {};

  for (const sp of SP_KEYS)
  {
    const speciesSnap = effective.species?.[sp];
    const behaviorSnap = effective.behavior?.[sp];
    const speciesOverrides = balance.speciesOverrides?.[sp];
    const behaviorOverrides = speciesBehOverrides[sp];
    const hasData = speciesSnap || behaviorSnap || summary.finalCounts?.[sp] != null
      || hasOverrideContent(speciesOverrides) || hasOverrideContent(behaviorOverrides);
    if (!hasData) continue;

    addCollapsible(animalsSec, {
      summary: speciesSummaryLine(sp, summary, speciesOverrides, behaviorOverrides),
    }, (body) =>
    {
      buildSpeciesBody(body, sp, {
        summary,
        speciesSnap,
        behaviorSnap,
        speciesOverrides,
        behaviorOverrides,
      });
    });
  }

  if (hasOverrideContent(balance.behaviorLibraryOverrides))
  {
    addCollapsible(animalsSec, {
      summary: '<span class="history-species-name">Global behavior</span><span class="history-species-meta changed">modified</span>',
      className: 'history-species history-species-global',
    }, (body) =>
    {
      addSubheading(body, 'Library overrides');
      addChangeList(body, balance.behaviorLibraryOverrides);
    });
  }

  if (!animalsSec.children.length)
  {
    const p = document.createElement('p');
    p.className = 'history-detail-empty';
    p.textContent = 'No per-species data recorded for this run.';
    animalsSec.appendChild(p);
  }

  const tools = document.createElement('div');
  tools.className = 'history-detail-tools';
  if (onLoadBalance && (
    hasOverrideContent(balance.speciesOverrides)
    || hasOverrideContent(balance.behaviorLibraryOverrides)
    || hasOverrideContent(balance.behaviorSpeciesOverrides)))
  {
    const loadBtn = document.createElement('button');
    loadBtn.type = 'button';
    loadBtn.className = 'btn';
    loadBtn.textContent = 'Load balance into panel';
    loadBtn.addEventListener('click', () => onLoadBalance(report));
    tools.appendChild(loadBtn);
  }
  const jsonBtn = document.createElement('button');
  jsonBtn.type = 'button';
  jsonBtn.className = 'btn';
  jsonBtn.textContent = 'Download JSON';
  jsonBtn.addEventListener('click', () =>
  {
    const blob = new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `${report.runId}.json`;
    a.click();
  });
  tools.appendChild(jsonBtn);
  root.appendChild(tools);

  return root;
}
