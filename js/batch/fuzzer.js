import { SP_KEYS, GENE_KEYS, GENE_RANGE } from '../data.js';
import {
  getBehaviorLibraryDefaults,
  getBehaviorThresholdKeys,
  getBehaviorActionKeys,
} from '../behavior/loader.js';
import { getBaseSpeciesData } from '../data.js';
import { clamp, setRngSeed, rng } from '../utils.js';
import { mergeOverrides, emptyBalanceOverrides } from './balance-config.js';
import { computeBalanceScore, buildCampaignRecommendations } from './balance-recommendations.js';

function gauss()
{
  let u = 0;
  let v = 0;
  while (u === 0) u = rng();
  while (v === 0) v = rng();
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
}

function perturbValue(value, intensity, min, max)
{
  const delta = value * gauss() * intensity;
  return clamp(value + delta, min, max);
}

function perturbRange(range, intensity, minBound, maxBound)
{
  let a = perturbValue(range[0], intensity, minBound, maxBound);
  let b = perturbValue(range[1], intensity, minBound, maxBound);
  if (a > b) [a, b] = [b, a];
  return [a, b];
}

function enforceThresholdOrder(thresholds)
{
  if (thresholds.thirstExit <= thresholds.thirstUrgent)
  {
    thresholds.thirstExit = thresholds.thirstUrgent + 5;
  }
  if (thresholds.hungerHunt <= thresholds.hungerGraze)
  {
    thresholds.hungerHunt = thresholds.hungerGraze + 5;
  }
  return thresholds;
}

function parseScope(scopeStr)
{
  if (!scopeStr || scopeStr === 'all')
  {
    return { species: true, behaviorThresholds: true, behaviorActions: true };
  }
  const parts = scopeStr.split(',').map(s => s.trim());
  return {
    species: parts.includes('species'),
    behaviorThresholds: parts.includes('behaviorThresholds'),
    behaviorActions: parts.includes('behaviorActions'),
  };
}

export function generateFuzzOverrides(baseline, options = {})
{
  const intensity = options.intensity ?? 0.15;
  const scope = parseScope(options.scope || 'all');
  const baseSpecies = getBaseSpeciesData();
  const baseLibrary = getBehaviorLibraryDefaults();
  const patch = emptyBalanceOverrides();

  if (scope.species && baseSpecies)
  {
    for (const sp of SP_KEYS)
    {
      const src = baseSpecies[sp];
      if (!src) continue;
      const spPatch = { base: {} };
      for (const gene of GENE_KEYS)
      {
        if (gene === 'hue') continue;
        const range = GENE_RANGE[gene];
        spPatch.base[gene] = perturbValue(src.base[gene], intensity, range[0], range[1]);
      }
      spPatch.gestationSec = perturbRange(src.gestationSec, intensity, 1, 12);
      spPatch.mateCooldownSec = perturbRange(src.mateCooldownSec, intensity, 1, 15);
      spPatch.stockWeight = clamp(perturbValue(src.stockWeight, intensity, 0.01, 0.6), 0.01, 0.6);
      patch.speciesOverrides[sp] = spPatch;
    }
  }

  if (scope.behaviorThresholds && baseLibrary?.thresholds)
  {
    patch.behaviorLibraryOverrides.thresholds = {};
    for (const key of getBehaviorThresholdKeys())
    {
      patch.behaviorLibraryOverrides.thresholds[key] = clamp(
        perturbValue(baseLibrary.thresholds[key], intensity, 5, 95),
        5,
        95,
      );
    }
    enforceThresholdOrder(patch.behaviorLibraryOverrides.thresholds);
  }

  if (scope.behaviorActions && baseLibrary?.actions)
  {
    patch.behaviorLibraryOverrides.actions = {};
    for (const key of getBehaviorActionKeys())
    {
      const baseVal = baseLibrary.actions[key]?.speedMult ?? 1;
      patch.behaviorLibraryOverrides.actions[key] = {
        speedMult: clamp(perturbValue(baseVal, intensity, 0.3, 2.0), 0.3, 2.0),
      };
    }
  }

  return mergeOverrides(baseline, patch);
}

export function buildCampaignSummary(trials, options = {})
{
  const histogram = { stable: 0, partial_collapse: 0, total_extinction: 0, timeout: 0 };
  for (const t of trials)
  {
    histogram[t.outcome] = (histogram[t.outcome] || 0) + 1;
  }

  const scoredTrials = trials.map(t => ({
    ...t,
    balanceScore: t.balanceScore ?? computeBalanceScore(t),
  }));

  const ranked = [...scoredTrials]
    .sort((a, b) =>
      (b.balanceScore || 0) - (a.balanceScore || 0) ||
      (b.score || 0) - (a.score || 0) ||
      (b.summary?.finalPop || 0) - (a.summary?.finalPop || 0),
    )
    .slice(0, options.topN ?? trials.length)
    .map(t => ({
      runId: t.runId,
      score: t.score,
      balanceScore: t.balanceScore,
      outcome: t.outcome,
      finalPop: t.summary?.finalPop ?? 0,
      generationMax: t.summary?.generationMax ?? 0,
      initialPop: t.summary?.initialPop ?? 0,
      peakPop: t.summary?.peakPop ?? 0,
      minPop: t.summary?.minPop ?? 0,
      finalDay: t.summary?.finalDay,
      collapseDay: t.summary?.collapseDay,
      dominantSpecies: t.summary?.dominantSpecies,
      targetDays: t.config?.targetDays,
      wallMs: t.wallMs,
      extinctAtDay: { ...(t.summary?.extinctAtDay || {}) },
      extinctSpecies: Object.keys(t.summary?.extinctAtDay || {}),
      balanceConfig: t.balanceConfig,
    }));

  const wallMsTotal = trials.reduce((a, t) => a + (t.wallMs || 0), 0);
  const trialsPerMinute = wallMsTotal > 0 ? (trials.length / (wallMsTotal / 60000)) : 0;

  const campaign = {
    campaignId: options.campaignId || `fuzz-${Date.now()}`,
    fuzzSeed: options.fuzzSeed,
    fuzzTrials: trials.length,
    fuzzIntensity: options.fuzzIntensity,
    fuzzProfile: options.fuzzProfile,
    fuzzScope: options.fuzzScope,
    baselineBalanceConfig: options.baselineBalanceConfig,
    wallMsTotal,
    trialsPerMinute,
    ranked,
    histogram,
    trials: trials.map(t => t.runId),
    trialsFull: scoredTrials,
  };

  campaign.recommendations = buildCampaignRecommendations(campaign);
  return campaign;
}

export function createFuzzRng(seed)
{
  setRngSeed(seed >>> 0);
}
