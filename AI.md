# Wildlands EcoSim — AI / Behavior Tree Reference

> **Scope:** the JSON-driven behavior tree (BT) system that drives every creature's decisions in the Godot/C# port ([`EcoSim.Core`](godot/WildlandsEcoSim/EcoSim.Core/)) and its visual editor/observe panel. For the legacy web port's equivalent JS system, see [`AGENTS.md`](AGENTS.md) § *AI behavior trees (CPU)*. For the rest of the Godot codebase, see [`GODOT.md`](GODOT.md).
>
> **Update this file whenever the tree shape, node vocabulary, interrupt-tier rules, or editor behavior changes.**

## Quick reference

| Item | Location |
|------|----------|
| Node/condition/action vocabulary | [`data/behaviors/schema.json`](data/behaviors/schema.json) |
| Shared thresholds, conditions, actions, diet templates | [`data/behaviors/library.json`](data/behaviors/library.json) |
| Per-species behavior file | [`data/behaviors/{species}.json`](data/behaviors/) (`deer`, `rabbit`, `elk`, `mouse`, `beaver`, `wolf`, `fox`, `hawk`, `owl`, `bear`, `boar`) |
| Compile JSON → tree | [`BehaviorCompiler.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorCompiler.cs) |
| Validate JSON | [`BehaviorValidator.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorValidator.cs) |
| Load/recompile all species | [`BehaviorLibrary.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorLibrary.cs) |
| Evaluate tree each tick | [`BehaviorEvaluator.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorEvaluator.cs) |
| Decide / commit-vs-interrupt / tick | [`BehaviorTree.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorTree.cs) |
| Interrupt-tier rules | [`BehaviorPriority.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorPriority.cs) |
| Goal resolution + action side effects | [`BehaviorExecutor.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorExecutor.cs) |
| Perception / condition ops | `BehaviorContextBuilder.cs`, `ConditionEvaluator.cs` (same folder) |
| Editor ↔ compiled-tree bridge | [`BehaviorGraphAdapter.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorGraphAdapter.cs), [`BehaviorGraphLayout.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorGraphLayout.cs) |
| Editor document model / save | [`BtEditorDocument.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BtEditorDocument.cs), [`BtSpeciesSerializer.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BtSpeciesSerializer.cs), [`BtEditSaveService.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BtEditSaveService.cs) |
| In-game editor/observe panel | [`scripts/ui/BtEditorPanel.cs`](godot/WildlandsEcoSim/scripts/ui/BtEditorPanel.cs), `BtGraphCanvas.cs`, `BtInspectorPane.cs`, `BtBlackboardPane.cs` |
| Tests | [`tests/EcoSim.Core.Tests/BehaviorCompilerTests.cs`](tests/EcoSim.Core.Tests/BehaviorCompilerTests.cs), `BehaviorInterruptTests.cs`, `CoreParityTests.cs` (`BehaviorLibraryTests`) |

---

## Node vocabulary

Defined in [`schema.json`](data/behaviors/schema.json) and mirrored in the `BehaviorNodeType` enum ([`BehaviorModels.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorModels.cs)):

| Node type | JSON `"type"` | Semantics |
|-----------|---------------|-----------|
| **Selector** | `"selector"` | Tries each child in order; first child that succeeds (returns an action) wins. Used for both priority dispatch and named tier groups (see below) — there is no separate "group" node type. |
| **Sequence** | `"sequence"` | All children must succeed in order; the sequence's result is its last child's action. Used for `condition, condition…, action` branches. |
| **Condition** | `"conditionRef"` (a bare string leaf, e.g. `"HasThreat"`) | Reference to a named entry in the merged `conditions` dict; evaluated by `ConditionEvaluator`. |
| **Action** | `"actionRef"` (a bare string leaf, e.g. `"Wander"`) | Reference to a named entry in the merged `actions` dict; terminal — sets state/goal/speed/interrupt tier. |

There is currently **no Parallel, Decorator, Inverter, or Cooldown node** — the compiler, evaluator, and editor only understand these four. "Layering" the tree (see below) is done entirely with nested `selector`/`sequence`, not new node kinds.

Every composite (`selector`/`sequence`) may carry an optional `"id"` (a *branch id*, e.g. `"thirst_branch"`, `"survival_needs"`) used for: patch anchors, the BT editor's node title/tooltip, and life-story/debug labeling. It is **not** required to be unique globally — uniqueness is enforced on the compiler-generated `Uid`, not the author-facing `id`.

---

## Templates, `extends`, and per-species overrides

Every species behaves as one of **three diet archetypes**, whose full trees live once in `library.json`'s `"trees"` key:

| Template | Species | Extra branches vs. herbivore |
|----------|---------|-------------------------------|
| `herbivore_prey` | deer, rabbit, elk, mouse, beaver | — |
| `carnivore` | wolf, fox, hawk, owl | hunting + stalking |
| `omnivore` | bear, boar | hunting + stalking **and** grazing fallback |

A species file (`data/behaviors/{species}.json`) is now a thin delta on top of its template:

```json
{
  "extends": "carnivore",
  "thresholds": { "restEnergy": 15 },
  "actions": { "HuntNearby": { "speedMult": 1.25 } }
}
```

- **`extends`** — which template's tree to compile against.
- **`thresholds`** — per-species overrides of the shared threshold numbers (e.g. a faster-tiring wolf lowers `restEnergy`). Anything omitted falls back to `library.json`'s top-level `thresholds`.
- **`conditions` / `actions`** — optional **partial** overrides, deep-merged field-by-field onto the library's definition (via [`DeepMerge.Merge`](godot/WildlandsEcoSim/EcoSim.Core/Util/DeepMerge.cs)) — a species only needs to specify the field it's tuning (e.g. rabbit overrides just `Flee.speedMult`), not the whole action object.
- **`root`** — if present instead of `extends`, the species is **fully self-contained**: its own inlined tree, bypassing templates entirely. This is what the visual editor writes when you save (see *Editor* below) — editing and saving a template-based species **detaches** it into a self-contained `root` file. That's expected, not a bug.
- **`tree`** (`{ "remove": [...], "insertBefore": {...}, "insertAfter": {...} }`) — structural patch by branch-id anchor, applied to the template's **top-level** children only (`BehaviorCompiler.ApplyTreePatches`). **No species currently uses this** — all 11 are pure `extends` + threshold/action deltas, because every species within a diet archetype is structurally identical.

  > **Known limitation:** patch anchors are validated recursively (`BehaviorValidator.CollectAnchors` walks the whole tree), but `ApplyTreePatches` only searches the root's immediate children. If a future species needs to insert/remove a branch *inside* a nested tier group (e.g. add a species-only branch under `survival_needs`), `ApplyTreePatches` will need to recurse to find the anchor — it doesn't today.

`BehaviorCompiler.Compile()` handles both paths uniformly: merge `library` + species `thresholds`/`conditions`/`actions`, then resolve either the inlined `root` or `library.Trees[extends]` (+ patches) into a `BehaviorTreeNode` tree with stable, deterministic `Uid`s per node (`{behaviorKey}/{templateName}/...`).

---

## The layered tree shape

Each diet template is a **4-tier vertical hierarchy** instead of one flat wide selector, mirroring the game's existing interrupt-tier taxonomy (`schema.json` → `interruptTiers`):

```
root (selector)
├── threat_response (selector)         — tier 0, Critical — always preempts
│    ├── flee_drink_branch (sequence)  — flee while pausing to drink at shore
│    └── flee_branch (sequence)        — flee a threat
├── survival_needs (selector)          — tier 1, Survival
│    ├── thirst_branch (sequence)
│    ├── hunger_response (selector)    — carnivore/omnivore only: sub-layer for hunting variants
│    │    ├── hunt_branch (sequence)        — urgent hunt (prey nearby + hungerUrgent)
│    │    ├── graze_branch (sequence)       — omnivore only: no prey visible, graze instead
│    │    └── moderate_hunt_branch (sequence) — opportunistic hunt while only moderately hungry
│    ├── graze_branch (sequence)       — herbivore: single branch, no sub-layer needed
│    └── rest_branch (sequence)
├── discretionary (selector)           — tier 2
│    ├── mate_branch (sequence)
│    └── stalk_branch (sequence)       — carnivore/omnivore only
└── ambient (selector)                 — tier 3, default idle
     ├── night_rest_branch (sequence)
     └── Wander (action leaf)
```

This replaced a single flat `selector` with 7–11 sibling branches at depth 1 (the shape every species used before this restructure). Key points:

- **Group ids are addressable/labelable**, not just visual sugar — `threat_response`, `survival_needs`, `hunger_response`, `discretionary`, `ambient` are ordinary `selector` nodes with an `"id"`, resolved and rendered exactly like any other node.
- **Tier order is now authoritative for branch order.** The one deliberate behavior change from the old flat list: `moderate_hunt_branch` (a tier-1/Survival action) now outranks `mate_branch` (tier-2/Discretionary) for carnivores and omnivores — previously it was listed after mate, which contradicted the documented tier semantics. A moderately-hungry wolf/bear with visible prey now hunts before mating.
- Herbivores were already tier-monotonic in the old flat order, so their restructuring is pure reorganization with **no behavior change**.
- All condition/action leaf refs, ids, and thresholds are untouched — only grouping wrappers were introduced, using the existing `selector`/`sequence` vocabulary (see *Node vocabulary* — no new node types were needed).
- The compiler/evaluator/validator/layout/editor code is fully generic recursion over `Children` — depth was never hardcoded anywhere, so no runtime code changes were required to support the deeper shape (only the JSON data changed, plus a small editor rendering improvement — see *Editor* below).

---

## Interrupt tiers & commit logic

Tree *shape* only decides which action wins when the tree is evaluated fresh. Whether a creature actually **switches** to that action this tick is a separate, orthogonal system: [`BehaviorPriority.cs`](godot/WildlandsEcoSim/EcoSim.Core/Behavior/BehaviorPriority.cs) + `BehaviorTree.ShouldApplyDecision`.

| Tier | Label | Meaning |
|------|-------|---------|
| 0 | Critical | Flee — always preempts everything, no dwell |
| 1 | Survival | Thirst, hunger (hunt/graze), rest |
| 2 | Discretionary | Mate, stalk — interruptible by survival, has anti-flicker dwell |
| 3 | Ambient | Wander — the default fallback |

- `interruptTier` is a field on each **action** definition (`library.json` → `actions`), not on the tree structure — the same action (e.g. `HuntNearby`) keeps one tier regardless of which branch/tier-group triggered it.
- `ShouldApplyDecision` (`BehaviorTree.cs:86`): a lower-tier proposed action always preempts the current one; an "urgent need" (`hunger`/`thirst`/`energy` below their `*Urgent` threshold, `BehaviorPriority.IsUrgentNeed`) also bypasses discretionary commits; same-tier ≥2 actions must dwell for `max(minCommitSec, SameTierDwellSec=0.3s)` before re-selecting, to avoid flicker.
- **Need hysteresis:** conditions like `Thirsty`, `HungryHerbivore`/`HungryCarnivore*`, `Exhausted` use `*BelowOrState` ops (`ConditionEvaluator`) — enter at the `Urgent` threshold, don't exit until the higher `Exit` threshold, so a creature that starts drinking doesn't immediately stop the instant it crosses 30% thirst.

---

## Compile & evaluate pipeline

1. **`BehaviorLibrary.Load(catalog)`** — reads `library.json` once, then for each species in `SpeciesCatalog` loads `data/behaviors/{behaviorKey}.json`, validates it (`BehaviorValidator.Validate`), and compiles it (`BehaviorCompiler.Compile`) into a `BehaviorConfig` (merged thresholds/conditions/actions + a `BehaviorTreeNode Root` + the original `SourceTree` JSON for editor round-trips). `RecompileAll` / `RecompileSpecies` support hot-reload from the BT editor without restarting.
2. **Per tick, `BehaviorTree.Decide(creature, creatures)`** — builds a `BehaviorContext` (perception: nearby threat/prey/mate, sense radius, thresholds) via `BehaviorContextBuilder.Build`, then calls `BehaviorEvaluator.EvaluateTree(state, cfg.Root, ctx)`.
3. **`BehaviorEvaluator`** walks the compiled tree recursively: `Selector` returns its first child's action that succeeds; `Sequence` requires every child to succeed and returns the last action; `Condition` evaluates via `ConditionEvaluator.Evaluate`; `Action` is terminal. `EvaluateTreeWithTrace` is the same walk instrumented to emit a `BehaviorTraceStep` per visited node (pass/fail/selected/skipped) — this trace is what powers the BT editor's live "eval sweep" and committed-path glow.
4. **`BehaviorTree.Tick`** — takes the proposed decision, asks `ShouldApplyDecision` whether to switch or keep the currently-committed action, then calls `BehaviorExecutor.ApplyDecision` (sets `creature.State`/`BtNodeId`/`BtBranchUid`/`BtAction`, resolves a movement goal via `BehaviorExecutor.ResolveGoals`) and `BehaviorExecutor.ApplyActionEffects` (per-state side effects: drink, graze bite, hunt strike/damage, rest healing, mate consummation, movement).
5. **Goals** (`ResolveGoals` switch on the action's `"goal"` field): `hold`, `awayFromThreat`, `nearestWater`, `bestFoodOrWander`, `chasePrey`, `approachMate`, `wander`/`randomWalkable` — each resolves a target position (and optionally a `TargetId`) using `Navigation`/`CreatureSystem` helpers; goals are cached and reused for a few ticks (`ShouldReuseGoals`) to avoid replanning every frame.

---

## Editor & live observe panel

The three-pane visual BT editor (`BtEditorPanel` — Blackboard | Graph | Inspector) is also the **live observe view**: selecting a creature in-game feeds `BehaviorTree.EvaluateWithTrace` / `PeekDecision` into the same canvas.

| Component | Role |
|-----------|------|
| `BtEditorPanel.cs` | Top-level panel; loads a locked species' compiled tree, feeds live trace for a selected creature, wires save |
| `BtGraphCanvas.cs` | Node-graph rendering: colored cards by node type, orthogonal edge routing, committed-path glow (ancestor walk from the trace's winning `Uid`), eval-sweep animation, drag/reparent/wrap editing |
| `BtInspectorPane.cs` | Right-hand property editor for the selected node (type, branch id, condition op/params, action fields) |
| `BtBlackboardPane.cs` | Left pane — threshold key/value editor |
| `BehaviorGraphAdapter.cs` | Converts a compiled `BehaviorConfig` ↔ the editor's mutable `BtEditorDocument` (nodes + child-id lists) and ↔ a flat nodes/edges document for auto-layout |
| `BehaviorGraphLayout.cs` | Reingold–Tilford-style recursive auto-layout (`NodeWidth=248`, `NodeHeight=92`, `HGap=34`, `VGap=104`); fully depth-agnostic — a deeper/narrower tree just gets more rows, not a different algorithm |
| `BtSpeciesSerializer.cs` / `BtEditSaveService.cs` | Serializes the edited document back to a **self-contained** `{ "root": ..., "thresholds": ..., "conditions": ..., "actions": ... }` species JSON and writes it to disk |

**Node titles/tooltips for named groups:** `BtGraphCanvas.HeaderTitle()` shows a selector's `RefId` (branch id) as its header when set (e.g. `SURVIVAL NEEDS`), falling back to the generic `SELECTOR` only for the anonymous root; `NodeDescMap` (same file) has hover-tooltip entries for every tier/group id (`threat_response`, `survival_needs`, `hunger_response`, `discretionary`, `ambient`) mirroring `schema.json`'s tier labels/descriptions.

**Editing caveat:** saving any species through the editor always writes a fully self-contained `root` (`BtSpeciesSerializer.ToSpeciesJson`), even if it started as `extends`-based. This is intentional ("detach on edit"), but means hand-authored template sharing and in-editor edits don't currently coexist — editing a templated species and saving permanently expands it back to a standalone file.

---

## Species catalog (current)

| Species | Extends | Threshold deltas | Action deltas |
|---------|---------|------------------|----------------|
| deer | `herbivore_prey` | — | — |
| rabbit | `herbivore_prey` | `restEnergy: 20` | `Flee.speedMult: 1.35` |
| elk | `herbivore_prey` | — | `Flee.speedMult: 1.4` |
| mouse | `herbivore_prey` | `thirstUrgent: 35` | `Wander.speedMult: 0.65` |
| beaver | `herbivore_prey` | — | — |
| wolf | `carnivore` | `restEnergy: 15` | — |
| fox | `carnivore` | — | `HuntNearby.speedMult: 1.25` |
| hawk | `carnivore` | — | `HuntNearby.speedMult: 1.25` |
| owl | `carnivore` | — | `HuntNearby.speedMult: 1.2` |
| bear | `omnivore` | `restEnergy: 15` | — |
| boar | `omnivore` | `restEnergy: 14` | `Wander.speedMult: 0.6` |

All other thresholds/actions come straight from `library.json`'s defaults via the diet template.

---

## Extension guide

**Tune an existing species** — edit its `thresholds`/`actions` overrides in `data/behaviors/{species}.json`; no tree changes needed.

**Add a species to an existing diet archetype** — new `data/behaviors/{species}.json` with `"extends": "<diet>"` + whatever numeric/action deltas it needs; register it in `data/species.json` with a `"behavior"` key pointing at the file stem.

**Add a branch to a tier** (e.g. a new discretionary behavior) — add the condition/action defs to `library.json`'s shared `conditions`/`actions`, then add a `sequence` under the right tier selector in the relevant template(s) in `library.json`'s `"trees"`. Order within a tier matters (first success wins); keep the tier boundaries intact.

**Add a new tier or sub-layer** — add a new named `selector` under `root` (or nested inside an existing tier), give it an `"id"`, and add a `NodeDescMap` entry in `BtGraphCanvas.cs` so it renders with a real title/tooltip instead of generic `SELECTOR`. No compiler/evaluator changes needed — grouping is just nested `selector`/`sequence`.

**Add a genuinely new node kind** (Parallel, Decorator/Inverter, Cooldown) — bigger lift: new `BehaviorNodeType` case (`BehaviorModels.cs`), new `BehaviorEvaluator` branch, new `schema.json` entry, new `BtGraphCanvas` rendering + add-node menu item, new `BtInspectorPane` fields. Not needed for pure re-layering.

---

## Tests

| File | Covers |
|------|--------|
| `BehaviorCompilerTests.cs` | `extends`+patch resolution, self-contained `root` compile, deterministic/explicit uids, editor round-trip (`ToEditorDocument` → `ToSpeciesJson` → recompile), `AllSpeciesBehaviorFiles_ValidateClean` (every species file validates against the schema/library) |
| `BehaviorInterruptTests.cs` | Tier-based interrupt/commit behavior (mate interrupted by hunger, wander interrupted by thirst, stalk interrupted by urgent energy, flee always wins, same-tier wander dwell) |
| `CoreParityTests.cs` (`BehaviorLibraryTests`) | Full `BehaviorLibrary.Load` compiles a `BehaviorConfig` for every species in the real catalog |

Run with `dotnet test EcoSim.sln` (or the `tests/EcoSim.Core.Tests` project directly).

---

*Last synced with codebase: 2026-07-09 (flat → layered vertical-tier restructure; templates revived via `extends`).*
