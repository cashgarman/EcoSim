import { explainStabilityScore } from './metrics.js';
import { diffOverrides, formatTweaks } from './balance-recommendations.js';
import { SPECIES } from '../data.js';

function esc(text)
{
  return String(text ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

function addKv(parent, rows)
{
  const dl = document.createElement('dl');
  dl.className = 'campaign-score-kv';
  for (const [k, v] of rows)
  {
    if (v == null || v === '') continue;
    const dt = document.createElement('dt');
    dt.textContent = k;
    const dd = document.createElement('dd');
    dd.textContent = String(v);
    dl.appendChild(dt);
    dl.appendChild(dd);
  }
  parent.appendChild(dl);
}

function buildTweakList(tweaks, className = 'campaign-tweak-list')
{
  const wrap = document.createElement('div');
  if (!tweaks?.length)
  {
    const empty = document.createElement('p');
    empty.className = 'campaign-tweak-empty';
    empty.textContent = 'No parameter changes vs baseline.';
    wrap.appendChild(empty);
    return wrap;
  }

  const groups = {
    species: 'Species',
    behavior_global: 'Global behavior',
    behavior_species: 'Per-species behavior',
  };
  const byCat = {};
  for (const t of tweaks)
  {
    if (!byCat[t.category]) byCat[t.category] = [];
    byCat[t.category].push(t);
  }

  for (const cat of ['species', 'behavior_global', 'behavior_species'])
  {
    const items = byCat[cat];
    if (!items?.length) continue;
    const h = document.createElement('h5');
    h.className = 'campaign-detail-subtitle';
    h.textContent = groups[cat];
    wrap.appendChild(h);
    const ul = document.createElement('ul');
    ul.className = className;
    for (const t of items)
    {
      const li = document.createElement('li');
      li.textContent = t.text;
      if (t.reason) li.title = t.reason;
      ul.appendChild(li);
    }
    wrap.appendChild(ul);
  }
  return wrap;
}

export function buildCampaignDetailElement(row, options = {})
{
  const root = document.createElement('div');
  root.className = 'campaign-detail';

  const explain = explainStabilityScore(row);

  const tier = document.createElement('p');
  tier.className = 'campaign-score-tier';
  tier.innerHTML = `<strong>${esc(explain.tier)}</strong>`;
  root.appendChild(tier);

  const reason = document.createElement('p');
  reason.className = 'campaign-score-reason';
  reason.textContent = explain.reason;
  root.appendChild(reason);

  addKv(root, [
    ['Score', explain.score.toFixed(2)],
    ['Balance score', row.balanceScore != null ? row.balanceScore.toFixed(2) : '—'],
    ['Outcome', row.outcome],
    ['Initial pop', explain.initialPop],
    ['Final pop', explain.finalPop],
    ['30% retention floor', explain.popFloor != null ? Math.ceil(explain.popFloor) : '—'],
    ['Peak pop', explain.peakPop],
    ['Min pop', explain.minPop],
    ['Target days', explain.targetDays],
    ['Final day', explain.finalDay],
    ['Reached target', explain.reachedTarget == null ? '—' : (explain.reachedTarget ? 'yes' : 'no')],
    ['Collapse day', explain.collapseDay],
    ['Dominant species', explain.dominantSpecies],
    ['Wall time', row.wallMs != null ? `${(row.wallMs / 1000).toFixed(1)}s` : null],
    ['Run ID', row.runId],
  ]);

  const extinct = row.extinctAtDay || {};
  const extinctKeys = Object.keys(extinct);
  if (extinctKeys.length)
  {
    const h = document.createElement('h5');
    h.className = 'campaign-detail-subtitle';
    h.textContent = 'Extinctions';
    root.appendChild(h);
    const ul = document.createElement('ul');
    ul.className = 'campaign-extinct-list';
    for (const sp of extinctKeys)
    {
      const li = document.createElement('li');
      li.textContent = `${sp} — day ${extinct[sp]}`;
      ul.appendChild(li);
    }
    root.appendChild(ul);
  }

  if (options.baselineBalanceConfig && row.balanceConfig)
  {
    const diffs = diffOverrides(options.baselineBalanceConfig, row.balanceConfig);
    const speciesLabels = {};
    for (const sp of Object.keys(SPECIES))
    {
      speciesLabels[sp] = `${SPECIES[sp].emoji} ${SPECIES[sp].label}`;
    }
    const tweaks = formatTweaks(diffs, row, { speciesLabels });
    if (tweaks.length)
    {
      const h = document.createElement('h5');
      h.className = 'campaign-detail-subtitle';
      h.textContent = 'Tweaks vs baseline';
      root.appendChild(h);
      root.appendChild(buildTweakList(tweaks));
    }
  }

  const rules = document.createElement('p');
  rules.className = 'campaign-score-rules';
  rules.textContent = 'Scoring: 1.0 stable (all species, pop ≥30% initial) · 0.5 partial collapse · 0.35 timeout with survivors · 0.0 extinction or failure.';
  root.appendChild(rules);

  return root;
}

export function buildCampaignRecommendationsElement(recommendations, handlers = {})
{
  const root = document.createElement('div');
  root.className = 'campaign-recommendations';

  const head = document.createElement('div');
  head.className = 'campaign-rec-head';
  const title = document.createElement('h4');
  title.textContent = 'Recommended balance tweaks';
  head.appendChild(title);

  const badge = document.createElement('span');
  badge.className = `campaign-rec-confidence campaign-rec-confidence-${recommendations.confidence || 'low'}`;
  badge.textContent = recommendations.confidence === 'high'
    ? 'High confidence'
    : recommendations.confidence === 'partial'
      ? 'Partial stability'
      : 'Low confidence';
  head.appendChild(badge);
  root.appendChild(head);

  const summary = document.createElement('p');
  summary.className = 'campaign-rec-summary';
  summary.textContent = recommendations.summary || '';
  root.appendChild(summary);

  if (recommendations.sourceRunId)
  {
    const src = document.createElement('p');
    src.className = 'campaign-rec-source';
    src.textContent = `Source trial: ${recommendations.sourceRunId}`;
    root.appendChild(src);
  }

  root.appendChild(buildTweakList(recommendations.tweaks || [], 'campaign-rec-tweak-list'));

  const actions = document.createElement('div');
  actions.className = 'campaign-rec-actions';

  const applyBtn = document.createElement('button');
  applyBtn.type = 'button';
  applyBtn.className = 'btn gold';
  applyBtn.textContent = 'Apply recommended config';
  applyBtn.addEventListener('click', () =>
  {
    if (handlers.onApply) handlers.onApply(recommendations);
  });
  actions.appendChild(applyBtn);

  if (handlers.onApplyAndValidate)
  {
    const validateBtn = document.createElement('button');
    validateBtn.type = 'button';
    validateBtn.className = 'btn';
    validateBtn.textContent = 'Apply & validate';
    validateBtn.addEventListener('click', () => handlers.onApplyAndValidate(recommendations));
    actions.appendChild(validateBtn);
  }

  root.appendChild(actions);
  return root;
}
