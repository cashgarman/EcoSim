import { explainStabilityScore } from './metrics.js';

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

export function buildCampaignDetailElement(row)
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

  const rules = document.createElement('p');
  rules.className = 'campaign-score-rules';
  rules.textContent = 'Scoring: 1.0 stable (all species, pop ≥30% initial) · 0.5 partial collapse · 0.35 timeout with survivors · 0.0 extinction or failure.';
  root.appendChild(rules);

  return root;
}
