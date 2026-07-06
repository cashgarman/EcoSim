import { state } from './state.js';
import { lifeStory } from './life-story.js';
import { perfProfiler } from './perf-profiler.js';
import { creatures } from './creatures.js';

export function captureSnapshot()
{
  return perfProfiler.scope('snapshot.capture', () => _captureSnapshot());
}

function _captureSnapshot()
{
  const runId = state.timelineRunId || '';
  const t = state.tGlobal;
  // Snapshot living creatures at this moment. Keep a dead selected if present for inspector continuity.
  let toSnap = state.creatures.filter(c => !c.dead);
  if (state.selected && state.selected.dead && !toSnap.some(c => c.id === state.selected.id))
  {
    toSnap = toSnap.concat([state.selected]);
  }
  const creaturesPlain = toSnap.map(serializeCreature);
  const snap = {
    runId,
    t,
    day: state.day,
    timeOfDay: state.timeOfDay,
    nextId: state.nextId,
    generationMax: state.generationMax,
    world: {
      W: state.W,
      H: state.H,
      growRow: state.growRow,
      veg: state.veg ? Array.from(state.veg) : [],
      vegCap: state.vegCap ? Array.from(state.vegCap) : [],
    },
    creatures: creaturesPlain,
  };
  return snap;
}

function serializeLifeStory(c)
{
  const story = c.lifeStory;
  if (!story || !story.events?.length) return null;
  return {
    events: story.events.map(ev => ({ ...ev })),
    committedState: story.committedState ?? null,
    committedNodeId: story.committedNodeId ?? null,
    committedSince: story.committedSince ?? 0,
    pendingState: story.pendingState ?? null,
    pendingNodeId: story.pendingNodeId ?? null,
    pendingSince: story.pendingSince ?? 0,
    stageFlags: { ...story.stageFlags },
    actionMarks: { ...story.actionMarks },
    snapshot: story.snapshot ? { ...story.snapshot } : {},
    nextSeq: story.nextSeq ?? 1,
    deathRecorded: !!story.deathRecorded,
  };
}

function restoreLifeStory(c, data)
{
  if (!data)
  {
    lifeStory.initCreature(c);
    return;
  }
  c.lifeStory = {
    events: Array.isArray(data.events) ? data.events.map(ev => ({ ...ev })) : [],
    committedState: data.committedState ?? null,
    committedNodeId: data.committedNodeId ?? null,
    committedSince: data.committedSince ?? 0,
    pendingState: data.pendingState ?? null,
    pendingNodeId: data.pendingNodeId ?? null,
    pendingSince: data.pendingSince ?? 0,
    stageFlags: { adult: false, elder: false, ...(data.stageFlags || {}) },
    actionMarks: { ...(data.actionMarks || {}) },
    snapshot: { ...(data.snapshot || {}) },
    nextSeq: data.nextSeq ?? 1,
    deathRecorded: !!data.deathRecorded,
  };
}

export function serializeCreature(c)
{
  if (!c) return null;
  return {
    id: c.id,
    sp: c.sp,
    sex: c.sex,
    x: c.x,
    y: c.y,
    vx: c.vx || 0,
    vy: c.vy || 0,
    dir: c.dir || 1,
    genome: c.genome ? { ...c.genome } : {},
    gen: c.gen || 1,
    age: c.age || 0,
    hp: c.hp != null ? c.hp : 100,
    hunger: c.hunger != null ? c.hunger : 0,
    thirst: c.thirst != null ? c.thirst : 0,
    energy: c.energy != null ? c.energy : 0,
    state: c.state || 'wander',
    tx: c.tx != null ? c.tx : c.x,
    ty: c.ty != null ? c.ty : c.y,
    mateCd: c.mateCd || 0,
    pregnant: c.pregnant || 0,
    litterQ: c.litterQ || 0,
    walk: c.walk || 0,
    dead: !!c.dead,
    cause: c.cause || '',
    parentIds: c.parentIds ? [...c.parentIds] : [],
    offspringIds: c.offspringIds ? [...c.offspringIds] : [],
    matePartnerId: c.matePartnerId != null ? c.matePartnerId : null,
    lifeStory: serializeLifeStory(c),
  };
}

export function deserializeCreature(p)
{
  if (!p) return null;
  const c = {
    id: p.id,
    sp: p.sp,
    sex: p.sex,
    x: p.x,
    y: p.y,
    vx: p.vx || 0,
    vy: p.vy || 0,
    dir: p.dir || 1,
    genome: p.genome ? { ...p.genome } : {},
    gen: p.gen || 1,
    age: p.age || 0,
    hp: p.hp != null ? p.hp : 100,
    hunger: p.hunger != null ? p.hunger : 0,
    thirst: p.thirst != null ? p.thirst : 0,
    energy: p.energy != null ? p.energy : 0,
    state: p.state || 'wander',
    tx: p.tx != null ? p.tx : p.x,
    ty: p.ty != null ? p.ty : p.y,
    target: null,
    matePartner: null,
    matePartnerId: p.matePartnerId != null ? p.matePartnerId : null,
    mateCd: p.mateCd || 0,
    pregnant: p.pregnant || 0,
    litterQ: p.litterQ || 0,
    walk: p.walk || 0,
    dead: !!p.dead,
    cause: p.cause || '',
    parentIds: p.parentIds ? [...p.parentIds] : [],
    offspringIds: p.offspringIds ? [...p.offspringIds] : [],
    gpuSlot: -1,
    gpuNeedsUpload: true,
    rx: p.x,
    ry: p.y,
  };
  restoreLifeStory(c, p.lifeStory);
  return c;
}

export function restoreSnapshot(snapOrRow, options = {})
{
  const { clearSelection = true, preserveDisplay = false } = options;
  const snap = (snapOrRow && snapOrRow.snapshot) ? snapOrRow.snapshot : snapOrRow;
  if (!snap) return false;

  const prevDisplay = preserveDisplay ? new Map() : null;
  if (prevDisplay)
  {
    for (const c of state.creatures)
    {
      if (!c || c.dead || typeof c.rx !== 'number') continue;
      prevDisplay.set(c.id, { rx: c.rx, ry: c.ry });
    }
  }

  // scalars
  if (typeof snap.t === 'number') state.tGlobal = snap.t;
  if (typeof snap.day === 'number') state.day = snap.day;
  if (typeof snap.timeOfDay === 'number') state.timeOfDay = snap.timeOfDay;
  if (typeof snap.nextId === 'number') state.nextId = snap.nextId;
  if (typeof snap.generationMax === 'number') state.generationMax = snap.generationMax;

  // world mutable state
  if (snap.world)
  {
    const w = snap.world;
    if (typeof w.W === 'number') state.W = w.W;
    if (typeof w.H === 'number') state.H = w.H;
    if (typeof w.growRow === 'number') state.growRow = w.growRow;
    if (Array.isArray(w.veg))
    {
      state.veg = new Float32Array(w.veg);
    }
    if (Array.isArray(w.vegCap))
    {
      state.vegCap = new Float32Array(w.vegCap);
    }
  }

  // creatures
  state.creatures = [];
  if (Array.isArray(snap.creatures))
  {
    for (const p of snap.creatures)
    {
      const c = deserializeCreature(p);
      if (!c) continue;
      const prev = prevDisplay?.get(c.id);
      if (prev)
      {
        c.rx = prev.rx;
        c.ry = prev.ry;
      }
      state.creatures.push(c);
    }
  }

  // mark for redraw / gpu resync
  state.vegDirty = true;
  state.gpuSimDirtyFromCpu = true;

  if (clearSelection)
  {
    state.selected = null;
    state.followSelected = false;
  }

  creatures.rebuildGrid();
  return true;
}

export function reconcileSelectionAfterRestore(preserve)
{
  if (!preserve) return { rebound: false, reason: 'no-preserve' };
  const { selectedId, wasFollowing } = preserve;
  if (selectedId == null) return { rebound: false, reason: 'no-id' };

  for (const c of state.creatures)
  {
    if (c.id !== selectedId) continue;
    if (!c.dead)
    {
      state.selected = c;
      state.followSelected = !!wasFollowing;
      return { rebound: true, alive: true };
    }
    if (wasFollowing)
    {
      state.selected = null;
      state.followSelected = false;
      return { rebound: false, reason: 'dead-following' };
    }
    state.selected = c;
    state.followSelected = false;
    return { rebound: true, alive: false };
  }

  state.selected = null;
  state.followSelected = false;
  return { rebound: false, reason: 'missing' };
}

export function nearestSnapshotForT(snaps, t)
{
  if (!Array.isArray(snaps) || snaps.length === 0) return null;
  if (t == null || !Number.isFinite(t)) t = Number.MAX_SAFE_INTEGER;
  let best = null;
  let bestT = -Infinity;
  for (const s of snaps)
  {
    const rowT = (s && typeof s.t === 'number') ? s.t : null;
    const innerT = (s && s.snapshot && typeof s.snapshot.t === 'number') ? s.snapshot.t : null;
    const st = rowT != null ? rowT : innerT;
    if (st != null && st <= t && st > bestT)
    {
      bestT = st;
      best = s;
    }
  }
  return best;
}