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
| 1D — CPU nav replan | `perf/phase-1d-cpu-nav-replan` | In progress |
| 2 — Incremental grid | `perf/phase-2-incremental-grid` | Pending |
| 3A — Batch GPU upload | `perf/phase-3a-batch-upload` | Pending |
| 3B — GPU perception | `perf/phase-3b-gpu-perception` | Pending |
| Validation | `perf/validation` | Pending |

---

## Phase 0 — Profiler instrumentation

**Branch:** `perf/phase-0-scopes`

**Problem:** `behavior.tick` showed ~15ms unscoped self-time; goal resolution (`resolveGoals`) was invisible in the CPU/GPU detail panel.

**Changes:**
- [`js/behavior/executor.js`](js/behavior/executor.js): wrap `resolveGoals` in `behavior.resolveGoals` with per-goal child scopes (`nearestWater`, `wander`, etc.); wrap `applyDecisionWithContext` in `behavior.applyDecision`.
- [`js/behavior/index.js`](js/behavior/index.js): wrap GPU `tickDecisionOnly` in `behavior.tick`; extract `_observeLifeStory` scoped as `behavior.lifeStory`.

**Acceptance:** With CPU/GPU detail panel open (F2 + CPU/GPU toggle), `behavior.tick` self-time should be near zero; `behavior.resolveGoals.nearestWater` should dominate when many creatures are thirsty.

---

## Phase 1A — Water distance field

**Branch:** `perf/phase-1a-water-field`

**Problem:** `nearestWater` goal scanned up to 48-tile radius per thirsty creature per tick (~9,400 tile checks each).

**Changes:**
- [`js/state.js`](js/state.js): `waterDist` Float32Array on state.
- [`js/nav.js`](js/nav.js): `buildWaterDistanceField()` (multi-source BFS from shoreline), `waterEdgeGoalFromField()` (gradient descent with fallback to radial scan).
- [`js/world.js`](js/world.js): build field after biome generation.
- [`js/behavior/executor.js`](js/behavior/executor.js): thirst goals use field lookup first.

**Acceptance:** `behavior.resolveGoals.nearestWater` drops from dominant cost to negligible; thirsty creatures still reach shore (batch parity).

---

## Phase 1B — Goal replan cache

**Branch:** `perf/phase-1b-goal-cache`

**Problem:** `resolveGoals` ran every tick even when behavior node and targets were unchanged (especially `wander` / `rest` / stable `graze`).

**Changes:**
- [`js/behavior/executor.js`](js/behavior/executor.js): `shouldReuseGoals()` skips `resolveGoals` when BT node + state match, targets remain valid, and staggered replan phase (quality `navReplanInterval`) has not fired.

**Acceptance:** Large drop in `behavior.resolveGoals.wander` call cost; hunt/flee/mate still replan on interval or target loss.

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
- [`js/creatures.js`](js/creatures.js): `moveTowardGoal` mirrors GPU `navReplanInterval` — reuses cached waypoint unless goal changes, direct pursuit, or staggered replan phase.

---

## Phase 2 — Incremental spatial hash

**Branch:** `perf/phase-2-incremental-grid`

**Changes:**
- [`js/creatures.js`](js/creatures.js): `syncGrid()` moves creatures between buckets; `insertIntoGrid` / `removeFromGrid` on spawn/death; per-tick `syncGrid` replaces full rebuild.
- [`js/simulation.js`](js/simulation.js): tick uses `syncGrid`.
- [`js/snapshot.js`](js/snapshot.js): full `rebuildGrid` after snapshot restore.

---

## Phase 3A — Batch GPU behavior upload

*(Pending)*

---

## Phase 3B — GPU decideAndClaim perception

*(Pending)*

---

## Validation

*(Pending)*
