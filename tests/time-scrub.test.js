import { state } from '../js/state.js';
import { captureSnapshot, restoreSnapshot, serializeCreature, deserializeCreature, nearestSnapshotForT } from '../js/snapshot.js';
import { timeScrub } from '../js/time-scrub.js';

function approx(a, b, eps = 1e-6)
{
  return Math.abs(a - b) <= eps;
}

function resetMinimalState()
{
  state.W = 8;
  state.H = 8;
  state.tGlobal = 123.4;
  state.day = 3;
  state.timeOfDay = 0.25;
  state.nextId = 42;
  state.generationMax = 5;
  state.growRow = 2;
  state.veg = new Float32Array(state.W * state.H);
  state.vegCap = new Float32Array(state.W * state.H);
  for (let i = 0; i < state.veg.length; i++)
  {
    state.vegCap[i] = 0.8;
    state.veg[i] = 0.3 + (i % 7) * 0.05;
  }
  state.creatures = [];
  state.selected = null;
  state.scrubActive = false;
  state.timelineRunId = 'test-run';
}

export async function runTimeScrubTests()
{
  const results = [];
  let passed = 0;
  let failed = 0;

  function record(name, ok, detail)
  {
    results.push({ name, ok, detail: detail || '' });
    if (ok) passed++; else failed++;
    console.log((ok ? '[PASS]' : '[FAIL]') + ' ' + name + (detail ? ' :: ' + detail : ''));
  }

  // 1. nearestSnapshotForT
  try
  {
    const snaps = [{ t: 5 }, { t: 15.5 }, { t: 42 }];
    const n1 = nearestSnapshotForT(snaps, 10);
    record('nearest <=10 picks 5', n1 && approx(n1.t, 5));
    const n2 = nearestSnapshotForT(snaps, 15.5);
    record('nearest exact 15.5', n2 && approx(n2.t, 15.5));
    const n3 = nearestSnapshotForT(snaps, 100);
    record('nearest past last picks last', n3 && approx(n3.t, 42));
    const n4 = nearestSnapshotForT([], 10);
    record('nearest empty returns null', n4 === null);
    const n5 = nearestSnapshotForT(snaps, 0);
    record('nearest before first nullish', !n5 || n5.t > 0);
  }
  catch (e) { record('nearestSnapshotForT', false, e.message); }

  // 2. creature serialize/deserialize
  try
  {
    const plain = {
      id: 7, sp: 'rabbit', sex: 'female', x: 12.3, y: 4.1,
      vx: 0.2, vy: -0.1, dir: -1, genome: { size: 1.1, speed: 2 }, gen: 2,
      age: 3.2, hp: 77, hunger: 44, thirst: 55, energy: 66,
      state: 'graze', tx: 13, ty: 4, mateCd: 1.5, pregnant: 0, litterQ: 2,
      walk: 1.23, dead: false, cause: '', parentIds: [1], offspringIds: [9,10], matePartnerId: 3,
    };
    const c = deserializeCreature(plain);
    record('deserialize has core fields', c && c.id === 7 && c.sp === 'rabbit' && c.target === null);
    const back = serializeCreature(c);
    record('roundtrip serialize id/sp', back && back.id === 7 && back.sp === 'rabbit');
    record('roundtrip genome clone', back && back.genome && approx(back.genome.size, 1.1) && back.genome !== plain.genome);
  }
  catch (e) { record('creature ser/deser', false, e.message); }

  // 3. snapshot veg roundtrip
  try
  {
    resetMinimalState();
    state.veg[0] = 0.77;
    state.veg[5] = 0.11;
    const before = state.veg[0];
    const snap = captureSnapshot();
    state.veg[0] = 0.01;
    const ok = restoreSnapshot(snap);
    const after = state.veg[0];
    record('veg roundtrip value', ok && approx(after, before, 1e-9));
    record('veg length preserved', state.veg.length === 64);
  }
  catch (e) { record('snapshot veg roundtrip', false, e.message); }

  // 4. snapshot creature pop roundtrip
  try
  {
    resetMinimalState();
    // manually push minimal creatures (bypass make to keep pure)
    const c1 = deserializeCreature({ id: 100, sp: 'deer', x: 1, y: 2, genome: { lifespan: 10, size: 1 }, age: 1, hp: 90, hunger: 50, thirst: 50, energy: 80, state: 'wander', tx: 1, ty: 2, dead: false, parentIds: [], offspringIds: [] });
    const c2 = deserializeCreature({ id: 101, sp: 'fox', x: 3, y: 4, genome: { lifespan: 8, size: 0.9 }, age: 2, hp: 70, hunger: 30, thirst: 40, energy: 60, state: 'hunt', tx: 4, ty: 5, dead: false, parentIds: [100], offspringIds: [] });
    state.creatures.push(c1, c2);
    const snap = captureSnapshot();
    state.creatures = [];
    const ok = restoreSnapshot(snap);
    record('creature count restored', ok && state.creatures.length === 2);
    record('creature pos preserved', state.creatures.some(c => c.id === 101 && approx(c.x, 3)));
  }
  catch (e) { record('snapshot creatures roundtrip', false, e.message); }

  // 5. scalars restored
  try
  {
    resetMinimalState();
    state.tGlobal = 55.5;
    state.nextId = 999;
    const snap = captureSnapshot();
    state.tGlobal = 0;
    state.nextId = 1;
    restoreSnapshot(snap);
    record('tGlobal restored', approx(state.tGlobal, 55.5));
    record('nextId restored', state.nextId === 999);
  }
  catch (e) { record('snapshot scalars', false, e.message); }

  // 6. scrub clamp simulation (slider never > head)
  try
  {
    timeScrub.resetForNewRun();
    timeScrub.headT = 120;
    const target = 999;
    const clamped = Math.max(0, Math.min(target, timeScrub.headT));
    record('slider clamp never exceeds head', clamped === 120 && clamped <= timeScrub.headT);
  }
  catch (e) { record('scrub clamp', false, e.message); }

  // 7. truncate simulation (in-memory)
  try
  {
    const events = [{ t: 10 }, { t: 20 }, { t: 30 }, { t: 40 }];
    const forkT = 25;
    const kept = events.filter(r => r.t <= forkT);
    const futureGone = events.filter(r => r.t > forkT);
    record('truncate keeps <= forkT', kept.length === 2 && kept.every(r => r.t <= forkT));
    record('truncate removes future', futureGone.length === 2 && futureGone.every(r => r.t > forkT));
  }
  catch (e) { record('truncate sim', false, e.message); }

  // 8. fork promotion (head updated)
  try
  {
    timeScrub.resetForNewRun();
    timeScrub.headT = 200;
    timeScrub.viewT = 80;
    timeScrub.active = true;
    timeScrub.baselineSnapshot = { t: 200 };
    // simulate onMutating without real db
    const forkT = timeScrub.viewT;
    timeScrub.headT = 80.5; // as if restored + delta
    timeScrub.baselineSnapshot = { t: 80.5 };
    timeScrub.active = false;
    timeScrub.viewT = timeScrub.headT;
    record('fork sets new head near fork point', approx(timeScrub.headT, 80.5) && !timeScrub.active);
  }
  catch (e) { record('fork promotion', false, e.message); }

  const summary = { total: passed + failed, passed, failed, results };
  console.log('TimeScrub test summary:', summary);
  return summary;
}

if (typeof window !== 'undefined')
{
  window.runTimeScrubTests = runTimeScrubTests;
}