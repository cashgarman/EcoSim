# Simulation Performance Optimization — Phase Log

Tracking branch-per-phase work to cut CPU sim frame time from ~22ms (577 creatures) toward playable MAX_POP 6000 at 30+ FPS.

## Git workflow

Each phase is implemented on its own branch. Before starting the next phase: commit, return to the integration branch, then `/create-branch <next-phase-name>`.

| Phase | Branch | Status |
|-------|--------|--------|
| 0 — Profiler scopes | `perf/phase-0-scopes` | In progress |
| 1A — Water distance field | `perf/phase-1a-water-field` | Pending |
| 1B — Goal replan cache | `perf/phase-1b-goal-cache` | Pending |
| 1C — Alloc-free perception | `perf/phase-1c-perception` | Pending |
| 1D — CPU nav replan | `perf/phase-1d-cpu-nav-replan` | Pending |
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

*(Pending)*

---

## Phase 1B — Goal replan cache

*(Pending)*

---

## Phase 1C — Allocation-free perception

*(Pending)*

---

## Phase 1D — CPU nav replan interval

*(Pending)*

---

## Phase 2 — Incremental spatial hash

*(Pending)*

---

## Phase 3A — Batch GPU behavior upload

*(Pending)*

---

## Phase 3B — GPU decideAndClaim perception

*(Pending)*

---

## Validation

*(Pending)*
