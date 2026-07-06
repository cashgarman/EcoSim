# Simulation Performance Optimization — Phase Log

Tracking branch-per-phase work to cut CPU sim frame time from ~22ms (577 creatures) toward playable MAX_POP 6000 at 30+ FPS.

## Git workflow

Each phase is implemented on its own branch. Before starting the next phase: commit, return to the integration branch, then `/create-branch <next-phase-name>`.

| Phase | Branch | Status |
|-------|--------|--------|
| 0 — Profiler scopes | `perf/phase-0-scopes` | Done |
| 1A — Water distance field | `perf/phase-1a-water-field` | Done |
| 1B — Goal replan cache | `perf/phase-1b-goal-cache` | Done |
| 1C — Alloc-free perception | `perf/phase-1c-perception` | Done |
| 1D — CPU nav replan | `perf/phase-1d-cpu-nav-replan` | Done |
| 2 — Incremental grid | `perf/phase-2-incremental-grid` | Done |
| 3A — Batch GPU upload | `perf/phase-3a-batch-upload` | Done |
| 3B — GPU perception | `perf/phase-3b-gpu-perception` | Deferred |
| Validation | `perf/validation` | Done |

Integration branch: `simulation-optimization` (merge `perf/validation` when ready).

---

## Phase 0 — Profiler instrumentation

**Branch:** `perf/phase-0-scopes`

**Problem:** `behavior.tick` showed ~15ms unscoped self-time; goal resolution (`resolveGoals`) was invisible in the CPU/GPU detail panel.

**Changes:**
- [`js/behavior/executor.js`](js/behavior/executor.js): wrap `resolveGoals` in `behavior.resolveGoals` with per-goal child scopes; wrap `applyDecisionWithContext` in `behavior.applyDecision`.
- [`js/behavior/index.js`](js/behavior/index.js): wrap GPU `tickDecisionOnly` in `behavior.tick`; extract `_observeLifeStory` scoped as `behavior.lifeStory`.

---

## Phase 1A — Water distance field

**Branch:** `perf/phase-1a-water-field`

**Problem:** `nearestWater` goal scanned up to 48-tile radius per thirsty creature per tick.

**Changes:**
- [`js/state.js`](js/state.js): `waterDist` Float32Array.
- [`js/nav.js`](js/nav.js): `buildWaterDistanceField()`, `waterEdgeGoalFromField()`.
- [`js/world.js`](js/world.js): build field after biome generation.
- [`js/behavior/executor.js`](js/behavior/executor.js): thirst goals use field lookup first.

---

## Phase 1B — Goal replan cache

**Branch:** `perf/phase-1b-goal-cache`

**Changes:**
- [`js/behavior/executor.js`](js/behavior/executor.js): `shouldReuseGoals()` skips `resolveGoals` when BT node + state match and targets remain valid.

---

## Phase 1C — Allocation-free perception

**Branch:** `perf/phase-1c-perception`

**Changes:**
- [`js/data.js`](js/data.js): `attachSpeciesMasks()` on load/override.
- [`js/creatures.js`](js/creatures.js): reusable `_nearbyScratch`, `simPosInto`, optional `nearby(c,r,out)`.
- [`js/behavior/context.js`](js/behavior/context.js): bitmask threat/prey checks, squared distance in scan.

---

## Phase 1D — CPU nav replan interval

**Branch:** `perf/phase-1d-cpu-nav-replan`

**Changes:**
- [`js/creatures.js`](js/creatures.js): `moveTowardGoal` mirrors GPU `navReplanInterval`.

---

## Phase 2 — Incremental spatial hash

**Branch:** `perf/phase-2-incremental-grid`

**Changes:**
- [`js/creatures.js`](js/creatures.js): `syncGrid()`, `insertIntoGrid`, `removeFromGrid`.
- [`js/simulation.js`](js/simulation.js): per-tick `syncGrid`.
- [`js/snapshot.js`](js/snapshot.js): `rebuildGrid` after snapshot restore.

---

## Phase 3A — Batch GPU behavior upload

**Branch:** `perf/phase-3a-batch-upload`

**Changes:**
- [`js/gpu/simulation-backend.js`](js/gpu/simulation-backend.js): `behaviorBatch` staging buffer, single `writeBuffer`, `applyBehaviorBatch` compute scatter pass.

---

## Phase 3B — GPU decideAndClaim perception

**Status:** Deferred. Wiring `decideAndClaim` requires reordering the tick (GPU bin before CPU BT) or a lightweight perception readback path. Existing WGSL in [`js/gpu/simulation-backend.js`](js/gpu/simulation-backend.js) is ready for a follow-up branch.

---

## Validation

**Branch:** `perf/validation`

**Manual checks:**
1. Serve app (`python serve.py`), generate world, open F2 profiler + CPU/GPU detail.
2. Confirm `behavior.resolveGoals.nearestWater` is no longer dominant; `behavior.tick` self-time near zero.
3. Run batch smoke: `python scripts/run_batch.py --days 30 --size s --runs 1` (CPU path).

**Expected impact (577 creatures, CPU sim):** sim frame ~22ms → ~5–10ms depending on thirsty/wander mix.
