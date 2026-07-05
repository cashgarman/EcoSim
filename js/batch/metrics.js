import { SP_KEYS } from '../data.js';
import { state, idx } from '../state.js';
import { isWater } from '../data.js';

export class BatchMetricsCollector
{
  constructor(options = {})
  {
    this.samples = [];
    this.sampleEveryDays = options.sampleEveryDays ?? 10;
    this.sparseMode = !!options.sparseMode;
    this.startedAt = null;
    this.wallStart = 0;
    this.initialPop = 0;
    this.peakPop = 0;
    this.minPop = Infinity;
    this.extinctAtDay = {};
    this.lastSeenSpecies = new Set(SP_KEYS);
    this.targetDays = options.targetDays ?? 200;
  }

  begin(initialPop, wallStart = null)
  {
    this.startedAt = new Date().toISOString();
    this.wallStart = wallStart ?? performance.now();
    this.initialPop = initialPop;
    this.peakPop = initialPop;
    this.minPop = initialPop;
    this.samples = [];
    this.extinctAtDay = {};
    this.lastSeenSpecies = new Set(SP_KEYS);
    this.captureSample(true);
  }

  aliveCounts()
  {
    const counts = {};
    for (const k of SP_KEYS) counts[k] = 0;
    let total = 0;
    for (const c of state.creatures)
    {
      if (!c || c.dead) continue;
      counts[c.sp] = (counts[c.sp] || 0) + 1;
      total++;
    }
    return { counts, totalAlive: total };
  }

  avgVegPct()
  {
    if (!state.veg || !state.vegCap) return 0;
    let sum = 0;
    let n = 0;
    for (let i = 0; i < state.veg.length; i++)
    {
      const cap = state.vegCap[i];
      if (cap <= 0.02) continue;
      if (state.biome && isWater(state.biome[i])) continue;
      sum += state.veg[i] / Math.max(0.001, cap);
      n++;
    }
    return n ? sum / n : 0;
  }

  captureSample(force = false)
  {
    const day = state.day;
    if (!force && this.samples.length)
    {
      const last = this.samples[this.samples.length - 1];
      if (day - last.day < this.sampleEveryDays) return;
    }

    const { counts, totalAlive } = this.aliveCounts();
    this.peakPop = Math.max(this.peakPop, totalAlive);
    this.minPop = Math.min(this.minPop, totalAlive);

    for (const sp of SP_KEYS)
    {
      if (counts[sp] > 0)
      {
        this.lastSeenSpecies.add(sp);
      }
      else if (this.lastSeenSpecies.has(sp) && this.extinctAtDay[sp] == null)
      {
        this.extinctAtDay[sp] = day;
        this.lastSeenSpecies.delete(sp);
      }
    }

    const alive = state.creatures.filter(c => c && !c.dead);
    const avgNeeds = { hp: 0, hunger: 0, thirst: 0, energy: 0 };
    for (const c of alive)
    {
      avgNeeds.hp += c.hp || 0;
      avgNeeds.hunger += c.hunger || 0;
      avgNeeds.thirst += c.thirst || 0;
      avgNeeds.energy += c.energy || 0;
    }
    const denom = Math.max(1, alive.length);
    avgNeeds.hp /= denom;
    avgNeeds.hunger /= denom;
    avgNeeds.thirst /= denom;
    avgNeeds.energy /= denom;

    const extinctSpecies = SP_KEYS.filter(sp => counts[sp] === 0);

    this.samples.push({
      day,
      t: state.tGlobal,
      counts: { ...counts },
      totalAlive,
      generationMax: state.generationMax,
      avgNeeds,
      avgVegPct: this.avgVegPct(),
      extinctSpecies,
    });
  }

  shouldSampleSparse(day, targetDays)
  {
    if (!this.sparseMode) return false;
    const checkpoints = [0, Math.floor(targetDays * 0.5), targetDays];
    return checkpoints.includes(day);
  }

  classifyOutcome(earlyStop = false)
  {
    const { counts, totalAlive } = this.aliveCounts();
    const extinctSpecies = SP_KEYS.filter(sp => counts[sp] === 0);
    const reachedTarget = state.day >= this.targetDays - 0.001;

    if (earlyStop) return 'total_extinction';
    if (totalAlive === 0) return 'total_extinction';
    if (!reachedTarget) return 'timeout';
    if (extinctSpecies.length === 0) return 'stable';
    if (extinctSpecies.length < SP_KEYS.length) return 'partial_collapse';
    return 'total_extinction';
  }

  stabilityScore(outcome)
  {
    const { totalAlive } = this.aliveCounts();
    return scoreFromOutcome(outcome, totalAlive, this.initialPop);
  }

  buildSummary(outcome)
  {
    const { counts, totalAlive } = this.aliveCounts();
    let dominantSpecies = null;
    let dominantCount = 0;
    for (const sp of SP_KEYS)
    {
      if (counts[sp] > dominantCount)
      {
        dominantCount = counts[sp];
        dominantSpecies = sp;
      }
    }

    let collapseDay = null;
    if (outcome === 'total_extinction')
    {
      collapseDay = state.day;
    }
    else if (Object.keys(this.extinctAtDay).length)
    {
      collapseDay = Math.min(...Object.values(this.extinctAtDay));
    }

    return {
      finalDay: state.day,
      initialPop: this.initialPop,
      targetDays: this.targetDays,
      peakPop: this.peakPop,
      minPop: this.minPop === Infinity ? 0 : this.minPop,
      finalPop: totalAlive,
      generationMax: state.generationMax,
      extinctAtDay: { ...this.extinctAtDay },
      dominantSpecies,
      collapseDay,
      finalCounts: { ...counts },
    };
  }

  buildReport(runConfig, balanceConfig, options = {})
  {
    const outcome = options.outcome || this.classifyOutcome(options.earlyStop);
    const wallMs = performance.now() - this.wallStart;
    return {
      runId: options.runId || `batch-${Date.now()}`,
      startedAt: this.startedAt,
      finishedAt: new Date().toISOString(),
      wallMs,
      config: { ...runConfig },
      balanceConfig,
      outcome,
      score: this.stabilityScore(outcome),
      summary: this.buildSummary(outcome),
      samples: this.samples,
    };
  }
}

export function scoreFromOutcome(outcome, finalPop, initialPop = 0)
{
  if (outcome === 'stable' && finalPop >= initialPop * 0.3) return 1.0;
  if (outcome === 'partial_collapse') return 0.5;
  if (outcome === 'timeout' && finalPop > 0) return 0.35;
  return 0.0;
}

export function explainStabilityScore(row = {})
{
  const outcome = row.outcome || '';
  const finalPop = row.finalPop ?? row.summary?.finalPop ?? 0;
  const initialPop = row.initialPop ?? row.summary?.initialPop ?? 0;
  const targetDays = row.targetDays ?? row.config?.targetDays ?? null;
  const finalDay = row.finalDay ?? row.summary?.finalDay ?? null;
  const score = row.score ?? scoreFromOutcome(outcome, finalPop, initialPop);
  const popFloor = initialPop > 0 ? initialPop * 0.3 : null;
  const reachedTarget = targetDays != null && finalDay != null && finalDay >= targetDays - 0.001;

  let tier = '0.0 — failure';
  let reason = `Outcome "${outcome}" maps to score 0.0.`;

  if (outcome === 'stable' && finalPop >= (popFloor ?? 0))
  {
    tier = '1.0 — stable ecosystem';
    reason = `All species survived through day ${targetDays ?? '?'}. Final pop ${finalPop}`
      + (initialPop ? ` (≥30% of initial ${initialPop}, floor ${Math.ceil(popFloor)})` : '') + '.';
  }
  else if (outcome === 'partial_collapse')
  {
    tier = '0.5 — partial collapse';
    const extinct = row.extinctSpecies?.length
      ? row.extinctSpecies.join(', ')
      : Object.keys(row.extinctAtDay || row.summary?.extinctAtDay || {}).join(', ') || 'unknown';
    reason = `Reached target window but species went extinct: ${extinct}. Final pop ${finalPop}.`;
  }
  else if (outcome === 'timeout' && finalPop > 0)
  {
    tier = '0.35 — timeout with survivors';
    reason = `Simulation stopped before day ${targetDays ?? '?'} (ended day ${finalDay ?? '?'}). ${finalPop} creatures remain.`;
  }
  else if (outcome === 'total_extinction')
  {
    reason = 'All creatures died before the run finished.';
  }
  else if (outcome === 'stable' && popFloor != null && finalPop < popFloor)
  {
    reason = `Outcome stable but final pop ${finalPop} is below the 30% retention floor (${Math.ceil(popFloor)}).`;
  }

  return {
    score,
    tier,
    reason,
    popFloor,
    reachedTarget,
    initialPop,
    finalPop,
    targetDays,
    finalDay,
    peakPop: row.peakPop ?? row.summary?.peakPop,
    minPop: row.minPop ?? row.summary?.minPop,
    collapseDay: row.collapseDay ?? row.summary?.collapseDay,
    dominantSpecies: row.dominantSpecies ?? row.summary?.dominantSpecies,
    wallMs: row.wallMs,
  };
}
