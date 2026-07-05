import { SPECIES, SP_KEYS } from './data.js';
import { inferKillerId, refineDeathCause } from './creature-notify.js';
import { state } from './state.js';

const BIRTH_WINDOW_SEC = 40;

const speciesStats = {};

function emptyEntry()
{
  return {
    totalBorn: 0,
    totalDied: 0,
    deathsByKey: {},
    birthTimes: [],
  };
}

export function initSpeciesStats()
{
  for (const k of SP_KEYS)
  {
    speciesStats[k] = emptyEntry();
  }
}

function pruneBirthTimes(entry, tGlobal)
{
  const cutoff = tGlobal - BIRTH_WINDOW_SEC;
  const times = entry.birthTimes;
  let i = 0;
  while (i < times.length && times[i] < cutoff) i++;
  if (i > 0) entry.birthTimes = times.slice(i);
}

export function recordSpeciesBirth(sp, tGlobal)
{
  if (!sp || !speciesStats[sp]) return;
  const entry = speciesStats[sp];
  entry.totalBorn++;
  entry.birthTimes.push(tGlobal);
  pruneBirthTimes(entry, tGlobal);
}

function deathKeyFor(c)
{
  const killerId = inferKillerId(c);
  if (killerId != null)
  {
    const killer = state.creatures.find(x => x.id === killerId);
    const killerSp = killer?.sp;
    if (killerSp && SPECIES[killerSp]) return `predation:${killerSp}`;
    const story = c.lifeStory;
    if (story?.events)
    {
      for (let i = story.events.length - 1; i >= 0; i--)
      {
        const ev = story.events[i];
        if (ev.kind === 'preyedOn' && ev.targetSp) return `predation:${ev.targetSp}`;
      }
    }
    return 'predation:unknown';
  }
  const cause = c.cause && c.cause !== 'exhaustion' ? c.cause : refineDeathCause(c);
  return cause || 'unknown';
}

export function recordSpeciesDeath(c)
{
  if (!c?.sp || !speciesStats[c.sp]) return;
  const entry = speciesStats[c.sp];
  entry.totalDied++;
  const key = deathKeyFor(c);
  entry.deathsByKey[key] = (entry.deathsByKey[key] || 0) + 1;
}

export function deathCauseDisplay(key)
{
  if (key.startsWith('predation:'))
  {
    const sp = key.slice('predation:'.length);
    if (sp === 'unknown') return { icon: '🦴', label: 'Killed by predator' };
    const S = SPECIES[sp];
    if (S) return { icon: S.emoji, label: `Killed by ${S.label}` };
    return { icon: '🦴', label: `Killed by ${sp}` };
  }
  switch (key)
  {
    case 'starvation': return { icon: '🍖', label: 'Starvation' };
    case 'dehydration': return { icon: '💧', label: 'Thirst' };
    case 'starvation and dehydration': return { icon: '🍖💧', label: 'Starvation & thirst' };
    case 'old age': return { icon: '⏳', label: 'Old age' };
    case 'exhaustion': return { icon: '😴', label: 'Exhaustion' };
    case 'meteor': return { icon: '☄️', label: 'Meteor' };
    case 'removed': return { icon: '✕', label: 'Removed' };
    case 'predation': return { icon: '🦴', label: 'Predation' };
    default: return { icon: '❓', label: key || 'Unknown' };
  }
}

export function getSpeciesStats(sp)
{
  const entry = speciesStats[sp] || emptyEntry();
  const tGlobal = state.tGlobal;
  pruneBirthTimes(entry, tGlobal);
  const birthRate = entry.birthTimes.length;
  const deathRows = Object.entries(entry.deathsByKey)
    .map(([key, count]) => ({ key, count, ...deathCauseDisplay(key) }))
    .sort((a, b) => b.count - a.count);
  return {
    totalBorn: entry.totalBorn,
    totalDied: entry.totalDied,
    birthRate,
    deathRows,
  };
}
