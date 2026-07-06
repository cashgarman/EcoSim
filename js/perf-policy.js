import { state } from './state.js';
import { gpuThrottleReadbackMul } from './gpu-throttle.js';

const MILESTONE_EVENT_KINDS = new Set([
  'appeared',
  'born',
  'mated',
  'gaveBirth',
  'hunted',
  'preyedOn',
  'died',
  'stage',
]);

export function effectiveSnapshotIntervalSec()
{
  return Math.max(0.5, state.snapshotIntervalSec || 10);
}

export function effectiveHeartbeatIntervalSec()
{
  const base = Math.max(1, state.heartbeatIntervalSec || 5);
  const speed = Math.max(1, state.speed || 1);
  if (speed < 4) return base;
  return base * Math.max(1, speed / 3);
}

export function shouldRunBehaviorThisSubstep(substep, substepCount)
{
  const speed = Math.max(1, state.speed || 1);
  if (speed < 5) return true;
  if (!Number.isFinite(substep) || !Number.isFinite(substepCount) || substepCount <= 0) return true;
  return substep >= substepCount - 1;
}

export function effectiveReadbackEveryMs(baseMs)
{
  if (state.batchMode) return 0;
  const speed = Math.max(1, state.speed || 1);
  const tier = state.gpuTelemetry?.qualityTier ?? 0;
  const tierMul = tier >= 3 ? 1.8 : tier >= 2 ? 1.35 : 1;
  const throttleMul = gpuThrottleReadbackMul();
  return baseMs * Math.max(1, speed / 3) * tierMul * throttleMul;
}

export function effectiveScrubTickRefreshMs()
{
  return 800;
}

export function shouldPersistCreatureEvent(kind, creatureId = null)
{
  const speed = Math.max(1, state.speed || 1);
  if (speed < 5) return true;
  if (MILESTONE_EVENT_KINDS.has(kind)) return true;
  if (
    creatureId != null
    && state.selected
    && !state.selected.dead
    && state.selected.id === creatureId
  )
  {
    return true;
  }
  return false;
}

export function timelineWritePressure()
{
  const speed = Math.max(1, state.speed || 1);
  if (speed >= 8) return 'high';
  if (speed >= 5) return 'medium';
  return 'low';
}
