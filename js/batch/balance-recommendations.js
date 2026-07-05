import { SP_KEYS, GENE_KEYS, GENE_LABEL } from '../data.js';
import {
  getBehaviorThresholdKeys,
  getBehaviorActionKeys,
} from '../behavior/loader.js';
import { WORLD_SIZE_PRESETS } from '../state.js';
import { scoreFromOutcome } from './metrics.js';

const POP_BAND_BASE = [200, 600];
const POP_BAND_REF_AREA = 64;

function scaledPopBand(sizeKey = 'm')
{
  const area = WORLD_SIZE_PRESETS[sizeKey]?.areaKm2 ?? POP_BAND_REF_AREA;
  const scale = Math.sqrt(area / POP_BAND_REF_AREA);
  return [POP_BAND_BASE[0] * scale, POP_BAND_BASE[1] * scale];
}

function fmtNum(n, decimals = 2)
{
  if (typeof n !== 'number' || !Number.isFinite(n)) return String(n ?? '—');
  const f = Number(n.toFixed(decimals));
  return Number.isInteger(f) ? String(f) : String(f);
}

function pctChange(from, to)
{
  if (from === 0) return to === 0 ? 0 : 100;
  return ((to - from) / Math.abs(from)) * 100;
}

function fmtPct(pct)
{
  const sign = pct >= 0 ? '+' : '';
  return `${sign}${pct.toFixed(0)}%`;
}

function trialSizeKey(trial)
{
  return trial.config?.cfg?.size || trial.config?.size || 'm';
}

export function computeBalanceScore(trial)
{
  const outcome = trial.outcome || '';
  const finalPop = trial.summary?.finalPop ?? 0;
  const initialPop = trial.summary?.initialPop ?? 0;
  const base = scoreFromOutcome(outcome, finalPop, initialPop);
  let bonus = 0;

  const samples = trial.samples || [];
  const lastSample = samples[samples.length - 1];
  const last3 = samples.slice(-3);

  if (lastSample && (lastSample.extinctSpecies || []).length === 0 && outcome === 'stable')
  {
    bonus += 0.15;
  }

  const [lo, hi] = scaledPopBand(trialSizeKey(trial));
  if (finalPop >= lo && finalPop <= hi)
  {
    bonus += 0.10;
  }

  if (last3.length)
  {
    const avgVeg = last3.reduce((a, s) => a + (s.avgVegPct || 0), 0) / last3.length;
    if (avgVeg >= 0.20 && avgVeg <= 0.70)
    {
      bonus += 0.10;
    }
  }

  const peak = trial.summary?.peakPop ?? 0;
  const minPop = trial.summary?.minPop ?? 0;
  if (peak > 0 && minPop / peak >= 0.4)
  {
    bonus += 0.05;
  }

  return base + bonus;
}

export function selectBestTrial(trials)
{
  if (!trials?.length) return null;

  const withScore = trials.map(t => ({ trial: t, balanceScore: t.balanceScore ?? computeBalanceScore(t) }));
  const stable = withScore.filter(x => x.trial.outcome === 'stable');
  if (stable.length)
  {
    stable.sort((a, b) => b.balanceScore - a.balanceScore);
    return stable[0].trial;
  }

  const partial = withScore.filter(x => x.trial.outcome === 'partial_collapse');
  if (partial.length)
  {
    partial.sort((a, b) => b.balanceScore - a.balanceScore);
    return partial[0].trial;
  }

  withScore.sort((a, b) =>
    b.balanceScore - a.balanceScore ||
    (b.trial.score ?? 0) - (a.trial.score ?? 0),
  );
  return withScore[0].trial;
}

function pushDiff(diffs, entry)
{
  if (entry.from === entry.to) return;
  if (Array.isArray(entry.from) && Array.isArray(entry.to))
  {
    if (Math.abs(entry.from[0] - entry.to[0]) < 0.001 && Math.abs(entry.from[1] - entry.to[1]) < 0.001) return;
  }
  else if (typeof entry.from === 'number' && typeof entry.to === 'number')
  {
    if (Math.abs(entry.from - entry.to) < 0.001) return;
  }
  diffs.push(entry);
}

export function diffOverrides(baselineConfig, candidateConfig)
{
  const diffs = [];
  const baseEff = baselineConfig?.effective || {};
  const candEff = candidateConfig?.effective || {};
  const baseSpecies = baseEff.species || {};
  const candSpecies = candEff.species || {};
  const baseBeh = baseEff.behavior || {};
  const candBeh = candEff.behavior || {};

  for (const sp of SP_KEYS)
  {
    const bSp = baseSpecies[sp];
    const cSp = candSpecies[sp];
    if (!bSp || !cSp) continue;

    for (const gene of GENE_KEYS)
    {
      if (gene === 'hue') continue;
      const from = bSp.base?.[gene];
      const to = cSp.base?.[gene];
      if (from == null || to == null) continue;
      pushDiff(diffs, {
        category: 'species',
        target: sp,
        field: gene,
        label: GENE_LABEL[gene] || gene,
        from,
        to,
      });
    }

    for (const field of ['gestationSec', 'mateCooldownSec'])
    {
      const from = bSp[field];
      const to = cSp[field];
      if (!from || !to) continue;
      pushDiff(diffs, {
        category: 'species',
        target: sp,
        field,
        label: field,
        from: [...from],
        to: [...to],
      });
    }

    const fromW = bSp.stockWeight;
    const toW = cSp.stockWeight;
    if (fromW != null && toW != null)
    {
      pushDiff(diffs, {
        category: 'species',
        target: sp,
        field: 'stockWeight',
        label: 'stockWeight',
        from: fromW,
        to: toW,
      });
    }
  }

  const refSp = SP_KEYS[0];
  const refBase = baseBeh[refSp];
  const refCand = candBeh[refSp];

  if (refBase && refCand)
  {
    for (const key of getBehaviorThresholdKeys())
    {
      const from = refBase.thresholds?.[key];
      const to = refCand.thresholds?.[key];
      if (from == null || to == null) continue;
      pushDiff(diffs, {
        category: 'behavior_global',
        target: 'library',
        field: key,
        label: key,
        from,
        to,
      });
    }

    for (const key of getBehaviorActionKeys())
    {
      const from = refBase.actions?.[key]?.speedMult;
      const to = refCand.actions?.[key]?.speedMult;
      if (from == null || to == null) continue;
      pushDiff(diffs, {
        category: 'behavior_global',
        target: 'library',
        field: `${key} speedMult`,
        label: `${key} speedMult`,
        from,
        to,
      });
    }
  }

  const speciesBehOverrides = candidateConfig?.behaviorSpeciesOverrides || {};
  for (const sp of SP_KEYS)
  {
    const bSp = baseBeh[sp];
    const cSp = candBeh[sp];
    if (!bSp || !cSp) continue;
    const hasSpeciesPatch = speciesBehOverrides[sp] &&
      (Object.keys(speciesBehOverrides[sp].thresholds || {}).length ||
        Object.keys(speciesBehOverrides[sp].actions || {}).length);

    for (const key of getBehaviorThresholdKeys())
    {
      const from = bSp.thresholds?.[key];
      const to = cSp.thresholds?.[key];
      if (from == null || to == null) continue;
      const globalFrom = refBase?.thresholds?.[key];
      const globalTo = refCand?.thresholds?.[key];
      if (!hasSpeciesPatch && globalFrom === from && globalTo === to) continue;
      if (sp === refSp && !hasSpeciesPatch) continue;
      pushDiff(diffs, {
        category: 'behavior_species',
        target: sp,
        field: key,
        label: key,
        from,
        to,
      });
    }

    for (const key of getBehaviorActionKeys())
    {
      const from = bSp.actions?.[key]?.speedMult;
      const to = cSp.actions?.[key]?.speedMult;
      if (from == null || to == null) continue;
      const globalFrom = refBase?.actions?.[key]?.speedMult;
      const globalTo = refCand?.actions?.[key]?.speedMult;
      if (!hasSpeciesPatch && globalFrom === from && globalTo === to) continue;
      if (sp === refSp && !hasSpeciesPatch) continue;
      pushDiff(diffs, {
        category: 'behavior_species',
        target: sp,
        field: `${key} speedMult`,
        label: `${key} speedMult`,
        from,
        to,
      });
    }
  }

  return diffs;
}

function trialReason(trial)
{
  const outcome = trial.outcome || '';
  if (outcome === 'stable')
  {
    const extinct = Object.keys(trial.summary?.extinctAtDay || {});
    if (!extinct.length) return 'stable trial, all species survived';
    return 'stable trial';
  }
  if (outcome === 'partial_collapse')
  {
    const extinct = Object.keys(trial.summary?.extinctAtDay || {}).join(', ') || 'unknown';
    return `partial collapse — extinct: ${extinct}`;
  }
  if (outcome === 'timeout') return 'timeout with survivors';
  return 'best available trial (no stable config found)';
}

export function formatTweaks(diffs, trial, options = {})
{
  const speciesLabels = options.speciesLabels || {};
  const reason = trial ? trialReason(trial) : '';

  return diffs.map(d =>
  {
    const spLabel = speciesLabels[d.target] || d.target;
    let text;
    let pct = null;

    if (Array.isArray(d.from) && Array.isArray(d.to))
    {
      text = `${spLabel} · ${d.label}: [${fmtNum(d.from[0])}, ${fmtNum(d.from[1])}] → [${fmtNum(d.to[0])}, ${fmtNum(d.to[1])}]`;
    }
    else
    {
      pct = pctChange(d.from, d.to);
      const prefix = d.category === 'behavior_global' ? 'Global' : spLabel;
      text = `${prefix} · ${d.label}: ${fmtNum(d.from)} → ${fmtNum(d.to)} (${fmtPct(pct)})`;
    }

    return {
      category: d.category,
      target: d.target,
      label: d.label,
      from: d.from,
      to: d.to,
      pct,
      text,
      reason,
    };
  });
}

function confidenceForTrial(trial)
{
  if (trial.outcome === 'stable') return 'high';
  if (trial.outcome === 'partial_collapse') return 'partial';
  return 'low';
}

function summaryForTrial(trial, tweakCount)
{
  const pop = trial.summary?.finalPop ?? 0;
  const score = (trial.balanceScore ?? computeBalanceScore(trial)).toFixed(2);
  if (trial.outcome === 'stable')
  {
    return `Stable ecosystem (balance score ${score}, pop ${pop}) — ${tweakCount} suggested tweak${tweakCount === 1 ? '' : 's'} from best trial.`;
  }
  if (trial.outcome === 'partial_collapse')
  {
    return `Best effort — partial collapse (score ${score}, pop ${pop}). ${tweakCount} tweaks may improve balance; re-validate with a longer run.`;
  }
  return `No fully stable trial found. Showing ${tweakCount} tweaks from the least-bad run (score ${score}) — expect limited improvement.`;
}

export function extractOverrideBuckets(balanceConfig)
{
  return {
    speciesOverrides: JSON.parse(JSON.stringify(balanceConfig?.speciesOverrides || {})),
    behaviorLibraryOverrides: JSON.parse(JSON.stringify(balanceConfig?.behaviorLibraryOverrides || {})),
    behaviorSpeciesOverrides: JSON.parse(JSON.stringify(balanceConfig?.behaviorSpeciesOverrides || {})),
  };
}

export function buildCampaignRecommendations(campaign)
{
  const trials = campaign.trialsFull || campaign.ranked || [];
  const baseline = campaign.baselineBalanceConfig || {};
  const best = selectBestTrial(trials);

  if (!best?.balanceConfig)
  {
    return {
      sourceRunId: null,
      confidence: 'low',
      summary: 'No trial data available for recommendations.',
      tweaks: [],
      overrides: extractOverrideBuckets(null),
    };
  }

  const diffs = diffOverrides(baseline, best.balanceConfig);
  const tweaks = formatTweaks(diffs, best);
  const confidence = confidenceForTrial(best);

  return {
    sourceRunId: best.runId,
    balanceScore: best.balanceScore ?? computeBalanceScore(best),
    confidence,
    summary: summaryForTrial(best, tweaks.length),
    tweaks,
    overrides: extractOverrideBuckets(best.balanceConfig),
  };
}
