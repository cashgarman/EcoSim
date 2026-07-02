# Wildlands — Game Design Document (Preliminary)

> **Status:** Draft v0.1 — design intent only; not yet implemented.  
> **Companion docs:** `AGENTS.md` (simulation architecture), `js/data.js` (species/biomes).  
> **Last updated:** 2026-07-02

---

## Elevator Pitch

**Wildlands** is a pixel-art ecosystem steward game built on a living simulation. You don't direct animals like units — you **observe, intervene sparingly, and keep the food web in balance** across procedurally generated biomes. Same seed, same world rules; your choices (when to rain, what to spawn, when to let nature run) determine whether the land thrives or collapses.

Think *Reus* meets *SimEarth* with a chill retro UI: watch populations breathe on the graph, follow a lineage through generations, and spend limited **Essence** to nudge fate before extinction cascades.

---

## Design Pillars

| Pillar | Meaning |
|--------|---------|
| **Living world first** | The sim is the star. Game systems wrap it, never replace it. |
| **Observation is gameplay** | Inspecting creatures, reading the pop graph, and following a bloodline are core verbs — not optional fluff. |
| **Scarcity creates decisions** | Unlimited god-powers turn the sim into a toy. Essence, cooldowns, and scenario constraints force tradeoffs. |
| **Failure is readable** | Players should see *why* wolves died out (veg crash → rabbit boom → collapse), not get opaque "Game Over." |
| **Seeds are content** | Procedural worlds + fixed scenario seeds = infinite levels without hand-authored maps. |

---

## Player Fantasy

You are the **Steward** of a wild region — a spirit, ranger, or caretaker with partial influence over weather and life, but no direct control over animal AI. The ecosystem runs whether you act or not. Your job is to **guide** it toward health under pressure: droughts, predator swings, climate mismatch, and your own mistakes.

Emotional hooks:
- Relief when a near-extinct species rebounds after a well-timed rain.
- Tension when the graph shows oscillation turning into a death spiral.
- Attachment when following one creature's lineage across generations.

---

## Game Modes

### Creative (current sandbox)

**Purpose:** Exploration, streaming, learning the sim, stress-free play.

| Rule | Behavior |
|------|----------|
| Worldgen | Full access to seed, size, sea/temp/moist/relief/animals sliders |
| Tools | Free, unlimited (once toolbar is exposed) |
| Restock | Available anytime |
| Migrants | On (sim's built-in recovery) |
| Objectives | None |
| Speed | 0–10× as today |

Creative remains the default entry point. No regression to current behavior.

### Challenge (new — primary "game" mode)

**Purpose:** Structured runs with goals, costs, and consequences.

| Rule | Behavior |
|------|----------|
| Worldgen | From **scenario** definition (fixed or constrained sliders) |
| Tools | Cost **Essence**; some have cooldowns |
| Restock | Disabled or costs heavily |
| Migrants | Configurable per scenario/difficulty (see Difficulty) |
| Objectives | Explicit win/fail checks each day |
| Speed | Capped at 5× (optional) to reduce "speed through danger" |

### Daily Seed (future)

Same scenario template, new seed each calendar day. Async score comparison ("how did you do on today's island?"). No server required — share seed + score string.

---

## Core Loop (Challenge Mode)

```mermaid
flowchart LR
  A[Scenario start] --> B[Observe metrics]
  B --> C{Threat or opportunity?}
  C -->|Yes| D[Spend Essence / pick tool]
  C -->|No| E[Wait / adjust speed]
  D --> F[Sim advances]
  E --> F
  F --> G{Day tick}
  G --> H[Evaluate objectives]
  H -->|Continue| B
  H -->|Win| I[Results screen]
  H -->|Lose| J[Fail screen + recap]
```

1. **Setup** — Load scenario: world cfg, starting populations, objective list, Essence budget, tool unlocks.
2. **Observe** — Pop graph, species rows, inspector, terrain tooltip, hovered-creature state tooltip, event log.
3. **Intervene** — Select tool, click map; Essence deducted if allowed.
4. **Simulate** — Day/night, AI, veg growth, optional scheduled events.
5. **Judge** — End-of-day (or continuous) objective evaluation.
6. **Resolve** — Win, lose, or star rating on timeout.

---

## Resources: Essence

**Essence** is the Steward's intervention currency. It converts "infinite sandbox" into meaningful choices.

### Properties (proposed defaults)

| Property | Value |
|----------|-------|
| Starting pool | Scenario-defined (e.g. 80–120) |
| Max pool | 150 (upgradeable in meta mode later) |
| Passive regen | `0.5 + healthBonus` per sim-day |
| Health bonus | Derived from ecosystem health index (see Scoring) |

### Tool costs (initial tuning)

| Tool | Essence | Cooldown | Notes |
|------|---------|----------|-------|
| Inspect | 0 | — | Always free |
| Rain (radius 4) | 15 | 8 s | Refill veg in area |
| Drought (radius 4) | 10 | 5 s | Strip veg — risky, cheap for puzzle scenarios |
| Spawn herbivore | 25 | — | rabbit, deer, boar |
| Spawn predator | 40 | — | fox, wolf, hawk |
| Cull (radius 1.5) | 8 | 3 s | Surgical population trim |
| Meteor (radius 3) | 35 | 25 s | Emergency / puzzle tool; scorches veg |

Costs apply in `js/tools.js` → `applyTool()` via a new `GameRules.canUseTool(tool)` / `GameRules.spendEssence(amount)` gate. Creative mode bypasses the gate.

### Regen formula (draft)

```
healthIndex = avg(vegPercent/100, speciesPresence, popStability)  // 0..1
essenceRegenPerDay = 0.5 + healthIndex * 2.0
```

Reward good stewardship; starve reckless meteor spam.

---

## Objectives

Objectives are data-driven checks run after each sim-day (or every N seconds at high speed). Stored in scenario config.

### Objective types

| Type | Example | Pass condition |
|------|---------|----------------|
| `surviveDays` | "Last 30 days" | `day >= 30` |
| `speciesMin` | "Keep ≥2 wolves" | `counts.wolf >= 2` for whole run or rolling window |
| `speciesMax` | "Control rabbit boom" | `counts.rabbit <= 80` |
| `vegAvg` | "Restore the land" | avg veg ≥ 55% |
| `generation` | "Evolve the line" | `generationMax >= 8` |
| `populationBand` | "Balance" | total pop in [200, 600] |
| `toolBudget` | "Minimal intervention" | essence spent ≤ 50 |
| `noExtinctions` | "Full web" | no species at 0 for >3 consecutive days |

### Win / lose

- **Win:** All required objectives complete.
- **Lose:** Any hard-fail objective triggers (e.g. species extinct, veg < 10% for 5 days, Essence cannot afford required action — soft fail only if scenario says so).
- **Partial success:** Star rating when win condition is "survive X days" with optional bonus objectives.

### Star rating (example)

| Stars | Condition |
|-------|-----------|
| ★ | Survived to target day |
| ★★ | No species extinct + avg veg ≥ 45% |
| ★★★ | All bonus objectives + essence spent ≤ budget |

---

## Scenarios

Scenarios live in a new export `SCENARIOS` in `js/data.js` (or `js/scenarios.js`). Each scenario is a self-contained "level."

### Scenario schema (draft)

```js
{
  id: 'island_ark',
  title: 'Island Ark',
  description: 'A small island. Keep rabbits and foxes coexisting for 40 days.',
  seed: 42857,                    // fixed; omit for random
  cfg: { size: 's', sea: 0.52, temp: 0.5, moist: 0.55, relief: 0.5, animals: 0.35 },
  startingCounts: { rabbit: 24, fox: 6 },  // optional override after stockLife
  essenceStart: 90,
  migrants: 'delayed',              // 'on' | 'off' | 'delayed'
  toolsEnabled: ['inspect', 'rain', 'spawn-rabbit', 'cull'],
  objectives: [
    { type: 'surviveDays', days: 40, required: true },
    { type: 'speciesMin', species: 'rabbit', min: 4, required: true },
    { type: 'speciesMin', species: 'fox', min: 2, required: true },
    { type: 'vegAvg', min: 35, required: false, label: 'Healthy undergrowth' },
  ],
  events: [
    { day: 15, type: 'drought', duration: 8, severity: 1.5 },
  ],
}
```

### Launch scenarios (content plan)

| ID | Fantasy | Twist |
|----|---------|-------|
| `tutorial_grove` | Learn inspect + rain | Forgiving migrants, 20 days |
| `island_ark` | Classic predator–prey | Small map, no spawns except budget |
| `rewilding` | Wolves extinct at start | Must graze veg up before wolf spawn unlocks |
| `cold_spring` | Low temp world | Deer need high `tol`; climate stress visible in inspector |
| `boar_wrecking_ball` | Too many omnivores | Cull + rain management |
| `hands_off` | No spawn/meteor tools | Pure observation + rain/drought only |

---

## Scheduled Events

Events inject drama without breaking sim determinism (seed + event table).

| Event | Effect |
|-------|--------|
| `drought` | Veg growth ×0.3; optional ambient veg decay |
| `heavy_rain` | Free partial veg refill in random biome band |
| `wildfire` | Meteor-like veg clear in random land patch (no Essence cost) |
| `migration_herd` | One-time spawn of scenario species at edge |
| `disease` | Temporary hp drain for one species |
| `cold_snap` | Global temp shift for N days |

**Hook:** `Simulation.tick()` in `js/simulation.js` — after day increment, `GameEvents.update(day)`.

Event announcements use existing log pipeline in `js/ui.js` / `creatures.log()`.

---

## Difficulty & Migrants

The sim already re-seeds extinct species via `Simulation.tick()` migrant logic (`js/simulation.js`). For a game, this must be a **difficulty knob**, not a hidden crutch.

| Setting | Migrants | Essence regen | Event severity |
|---------|----------|---------------|----------------|
| Easy | On (default) | ×1.25 | ×0.75 |
| Normal | Delayed (first after day 20) | ×1.0 | ×1.0 |
| Hard | Off | ×0.85 | ×1.25 |

When migrants are off, extinction is a fail unless the scenario allows local loss (e.g. "hawks optional").

---

## Scoring & Metrics

### Run score (for Daily Seed / leaderboards)

```
score = Math.floor(
  daysSurvived * 100
  + biodiversityBonus * 50
  + starObjectives * 200
  - essenceSpent * 0.5
  - extinctionCount * 300
)
```

**Biodiversity bonus:** +1 per species present above minimum threshold at run end.

### Ecosystem health index (UI + regen)

Computed each UI tick from data already in `js/ui.js`:

- `vegPercent` — average vegetation / cap
- `speciesPresence` — fraction of species with count ≥ 2
- `popStability` — inverse of max single-step drop in total pop from `popHistory`

Display as a small meter in Challenge mode (new UI element).

---

## UI Additions (Challenge Mode)

| Element | Placement | Purpose |
|---------|-----------|---------|
| **Mode selector** | Title / gen panel | Creative vs Challenge vs (future) Daily |
| **Scenario picker** | Gen panel or modal | List from `SCENARIOS` |
| **Objectives panel** | Left or right stack | Checklist + progress bars |
| **Essence bar** | Top bar | Current / max + regen hint |
| **Toolbar** | Bottom center | Restore `#toolbar` DOM; disable locked tools |
| **Results overlay** | Full screen | Win/lose, score, stars, graph snapshot |
| **Tool cooldown pips** | On toolbar buttons | Small radial or numeric cooldown |

Existing panels remain: World Generator (collapsed in Challenge), Ecosystem stats, Inspector, event log.

Map readability overlays in current build:
- Terrain tile tooltip (bottom-right) with biome name/color.
- Hovered-creature tooltip above sprite showing current behavior state.
- Selected-creature relation lines: solid line to active behavior target (color-coded by behavior) plus dashed pedigree links.

### Top bar (Challenge)

Replace or extend stats row:

`Day 12/30 · Pop 342 · Essence 67 · Health ■■■□ · ⏱ 2×`

---

## Future Modes (out of v1 scope, design now)

### Lineage Run

- Player selects one creature at start (`state.selected` → `state.lineageRoot`).
- Win: lineage reaches gen N with ≥ K living descendants.
- Lose: last descendant dies.
- Optional "Bless" action (1× per 10 days, small gene nudge on one offspring).

Uses existing pedigree fields (`parentIds`, `offspringIds`, `gen`) in creature objects.

### Roguelike Meta

- Between runs: pick one perk (e.g. +10 starting Essence, unlock boar spawn, migrants +10 s delay).
- Run modifiers rolled per attempt ("Long nights", "Aggressive predators").
- Persist `localStorage` meta save.

---

## What Stays Simulation-True

These sim behaviors are **not** overridden for game feel without explicit scenario flags:

- Creature AI priority (flee → thirst → graze/hunt → rest → mate → wander)
- Genetics (`breedGenome`, mutation rates)
- Climate stress from local temp vs genome
- Spatial hash perception
- Day/night movement penalties
- Carcass → veg feedback on death
- `MAX_POP = 900` cap

Game layer reads sim state; it does not fake population numbers.

---

## Implementation Roadmap

Phased delivery on top of the existing module layout.

### Phase 0 — Foundation (MVP "it's a game")

| Task | Primary files |
|------|----------------|
| Add `gameMode`, `essence`, `scenarioId`, `runState` to state | `js/state.js` |
| `SCENARIOS` with one tutorial scenario | `js/data.js` or `js/scenarios.js` |
| `GameRules` — essence gate, objective checks | `js/game-rules.js` (new) |
| Wire tool costs in `applyTool` | `js/tools.js` |
| Restore toolbar HTML + `initToolButtons()` | `wildlands-ecosim.html`, `js/tools.js` |
| Objectives panel + Essence in top bar | `wildlands-ecosim.html`, `js/ui.js` |
| Win/lose overlay | `js/ui.js` |
| Challenge start from `GameApp` boot path | `js/app.js` |
| Disable restock / migrants per scenario | `js/simulation.js`, `js/app.js` |

**MVP win condition:** Scenario `tutorial_grove` — survive 20 days, no species below 2, essence + rain only.

### Phase 1 — Content & polish

- 3–5 scenarios, event queue, star ratings, results recap with graph export.
- Scenario picker UI, difficulty selector.
- Cooldown UI on tools.

### Phase 2 — Lineage + Daily Seed

- Lineage mode variant in `GameRules`.
- Daily seed from date hash; score share string.

### Phase 3 — Meta (optional)

- Roguelike perks, `localStorage` persistence, unlock tree.

---

## Audio & Juice (later)

Not required for MVP. Placeholders:

- Soft chime on objective complete
- Low rumble on extinction fail
- Rain/meteor SFX tied to `js/fx.js` effect spawn
- Ambient biome loop by dominant biome under camera

---

## Open Questions

1. **Pause at speed 0** — Does Challenge allow indefinite pause, or auto-advance "thinking time" limit?
2. **Undo** — Single-step undo for tools would help learning but complicates determinism. Likely skip for v1.
3. **Multiplayer** — Async seed-only for v1; real-time shared world out of scope.
4. **Mobile** — Touch already partially wired in `js/input.js`; toolbar + panels need layout pass.
5. **Naming** — Product title "Wildlands" vs "Wildlands: Steward" for store/listing.

---

## Success Criteria (playtest)

- [ ] A new player completes `tutorial_grove` in under 15 minutes without docs.
- [ ] At least two viable strategies exist (e.g. rain-heavy vs spawn predator once).
- [ ] Failure causes feel fair — player identifies food web mistake in recap.
- [ ] Creative mode unchanged for existing users.
- [ ] One Daily Seed scenario is replayable for a week without feeling solved.

---

## Document Maintenance

Update this file when:

- Objectives, costs, or scenario schema change
- New game modes ship
- Migrants / difficulty rules change
- MVP scope shifts

Keep `AGENTS.md` focused on sim architecture; keep `GAMEPLAY.md` focused on player-facing rules and content.

*Preliminary draft — expect tuning numbers to move after first playtests.*
