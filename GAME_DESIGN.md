# Wildlands EcoSim — Game Design Document

> **Method:** This document was reconstructed **entirely from the source code** (`.cs`, `.json`, `.gdshader`, `project.godot`), not from any prose docs in the repo. Every number and rule below is traceable to a specific file; paths are cited inline.
> **Scope:** The Godot 4.7 C# project under [`godot/WildlandsEcoSim/`](godot/WildlandsEcoSim/), whose simulation lives in the standalone `EcoSim.Core` library.

---

## 1. What the game is

Wildlands EcoSim is a top-down 2D game with two intertwined halves that share one live simulation:

1. **Play as an animal** (possession). You take direct control of a single creature in a procedurally generated ecosystem, keep it alive against hunger/thirst/exhaustion/climate/predators, breed to earn **generation points**, spend those on a per-species **evolution tree**, and — when your body dies — jump into a new body (your killer, a sibling, or another of your kind). The run ends only when your species goes **extinct**.
2. **Observe/steward the ecosystem** (sandbox). With no creature possessed, you generate worlds, watch populations and bloodlines, inspect any creature, rewind a timeline, and apply world tools (restock, weather, cull, kill-all).

The application title, scene, and boot config come from [`project.godot`](godot/WildlandsEcoSim/project.godot): name **"Wildlands EcoSim"**, main scene `res://scenes/Main.tscn`, `1600×900` window, GL-compatibility renderer, and two autoloads — `EcoSimHost` (owns data + the `SimSession`) and `GameApp` (drives the tick loop).

---

## 2. Technical shape

| Concern | Implementation | Source |
|---------|----------------|--------|
| Engine | Godot 4.7 Mono / C# | `project.godot` |
| Simulation | Pure-C# `EcoSim.Core` (no Godot refs) | `EcoSim.Core/` |
| Session graph | `SimSession` wires state, species, behaviors, creatures, world, sim, player control, evolutions, life story, species stats | `EcoSim.Core/Sim/SimSession.cs` |
| Boot | `EcoSimHost` bootstraps `SpeciesCatalog` + `BehaviorLibrary`, resolves `data/` root, creates the session | `scripts/EcoSimHost.cs` |
| Tick driver | `GameApp._Process` advances the sim; `_PhysicsProcess` smooths display positions | `scripts/GameApp.cs` |
| Data | `data/species.json`, `data/behaviors/*.json`, `data/evolutions/*.json` | `godot/WildlandsEcoSim/data/` |
| Persistence | SQLite timeline (`TimelineDb`), snapshots (`SnapshotService`), panel layout | `EcoSim.Core/Sim/`, `scripts/ui/` |

`SimSession.Create` loads base data and constructs the whole object graph; `GenerateWorld` clears state, reseeds, runs `WorldGenerator.Generate`, then `CreatureSystem.StockLife` (`EcoSim.Core/Sim/SimSession.cs`).

---

## 3. The simulation clock & loop

From `GameApp._Process` and `Simulation.Tick`:

- **Time base** — `SimConstants.SimDaySeconds = 40`. `TimeOfDay` advances by `dt / 40` per tick and wraps at 1.0; `Day = floor(TGlobal / 40)` (`Simulation.Tick`).
- **Day/night** — `LightLevel` is derived from `TimeOfDay`; `IsNight` when `LightLevel < 0.28` (`Simulation.UpdateDayNight`). Night halves-ish movement (see §7).
- **Speed & substeps** — sim speed is `State.Speed` (0 = paused). Each frame runs `min(6, ceil(speed))` substeps of `dt = frameDelta * speed / steps` for stability at fast-forward (`GameApp._Process`).
- **Possession cap** — while a creature is possessed, speed is clamped to `1×` ("direct control at fast-forward is unplayable") (`GameApp._Process`).
- **Scrub freeze** — while the timeline scrub is active, speed is forced to 0 and only display smoothing runs.

**Per-tick order** (`Simulation.Tick`):

```text
TimeOfDay/day update
→ CreatureSystem.SyncGrid()            (incremental spatial hash)
→ for each living creature: StepCreature(dt)
→ WorldGenerator.GrowVegetation(dt)    (one row per tick)
→ RunMigrantPulse(dt)                  (opt-in)
→ CreatureSystem.PruneDead()
```

---

## 4. World generation

`WorldGenerator.Generate` (`EcoSim.Core/Sim/WorldGenerator.cs`) is fully seeded (`GlobalRng.SetSeed(state.Seed)`), so a seed reproduces a world exactly.

### 4.1 Config & size

`WorldGenConfig` defaults (`EcoSim.Core/Sim/SimTypes.cs`): `Sea 0.46`, `Temp 0.5`, `Moist 0.5`, `Relief 0.6`, `Animals 0.45`, `Size "m"`.

`WorldSizePresets` (`SimConstants.cs`) — grid side = `round(SideKm × TilesPerKm)`:

| Key | Area (km²) | Side (km) | Tiles/km | Grid |
|-----|-----------|-----------|----------|------|
| s | 25 | 5 | 32 | 160² |
| m | 64 | 8 | 32 | 256² |
| l | 100 | 10 | 32 | 320² |
| xl | 400 | 20 | 32 | 640² |
| xxl | 900 | 30 | 24 | 720² |

### 4.2 Fields

For each tile (`WorldGenerator.Generate`):
- **Elevation** — 5-octave fbm, plus a radial continental falloff (`pow(clamp(radial,0,1), 2.2) * 0.34`) that pushes land toward the center, scaled by `Relief`, plus a fine detail octave; clamped 0–1.
- **Moisture** — fbm `× 0.7 + Moist × 0.4`.
- **Temperature** — latitude gradient `lat × 0.7`, plus `Temp × 0.5`, minus altitude cooling `max(0, e − Sea) × 0.55`, plus noise; clamped 0–1.

### 4.3 Biome classification

`WorldGenerator.BiomeAt(e, t, m)` thresholds:
- `e < Sea − 0.09` → Deep Ocean; `< Sea` → Ocean; `< Sea + 0.015` → Beach.
- `e > 0.86` → Peak (`> 0.93`) else Snow/Mountain by temperature.
- Otherwise a Whittaker-style temperature×moisture table → Tundra, Taiga, Grassland, Forest, Swamp, Desert, Savanna, Rainforest.
- **Lakes** — inland tiles (`e < 0.62`, non-water) with lake-noise `> 0.72` become Lake.

### 4.4 Biomes (`EcoSim.Core/Data/BiomeData.cs`)

16 biomes, each with color, water flag, passability, and **vegetation cap**:

| Biome | Water | Passable | VegCap |
|-------|-------|----------|--------|
| Deep Ocean / Ocean / Lake | yes | no | 0 |
| Beach | no | yes | 0.08 |
| Desert | no | yes | 0.06 |
| Savanna | no | yes | 0.5 |
| Grassland | no | yes | 0.75 |
| Shrubland | no | yes | 0.45 |
| Forest | no | yes | 0.85 |
| Rainforest | no | yes | 1.0 |
| Swamp | no | yes | 0.7 |
| Taiga | no | yes | 0.4 |
| Tundra | no | yes | 0.2 |
| Snow | no | yes | 0.04 |
| Mountains | no | yes | 0.1 |
| Peak | no | **no** | 0.02 |

`IsWater(b) = b <= Lake`. Water is impassable to non-swimmers; Peak is impassable to everyone.

### 4.5 Vegetation growth

Initial veg per tile = `cap × Rf(0.4, 1.0)`. `GrowVegetation` regrows **one row per tick** (`GrowRow` cycles) by `cap × 0.22 × dt × GrowStride × (0.6 + moist)`, where `GrowStride = clamp(H/40, 4, 12)` (`SimState`, `WorldGenerator.Generate`). Vegetation is the base of the food web; carcasses add `+0.15` veg to the death tile (`CreatureSystem.Die`).

---

## 5. Creatures

### 5.1 Data model (`EcoSim.Core/Sim/Creature.cs`)

A `Creature` holds id, species key, sex, position/velocity/direction, `Genome`, generation, age, `Hp/Hunger/Thirst/Energy`, `State`, movement target + nav waypoint fields, `MateCd`, `Pregnant`, `LitterQ`, `MatePartner(+Id)`, `ParentIds`/`OffspringIds`, `Cause`/`KilledById`, behavior-tree debug fields, render smoothing (`Rx/Ry`, `Walk`), and an optional life-story log.

### 5.2 Genome (`Creature.cs`, ranges in `data/species.json`)

Ten genes with clamped ranges:

| Gene | Range | Used for |
|------|-------|----------|
| size | 0.35–2.2 | effective size, hunt reach/damage, need load |
| speed | 0.6–2.6 | movement |
| sense | 3–20 | perception radius, flee-safe distance |
| metab | 0.6–1.6 | need drain load |
| litter | 1–5 | offspring per birth |
| lifespan | 5–28 | max age (sim-years) |
| temp | 0–1 | preferred climate |
| tol | 0.15–0.7 | climate tolerance band |
| hue | 0–360 | color |
| agg | 0–1 | hunt strike chance |

### 5.3 Life stage & speed (`CreatureSystem`)

- **Adult** at `age ≥ lifespan × 0.25` (`IsAdult`).
- **Effective size** `ESize = size × (adult ? 1 : 0.55)`.
- **Effective speed** = `genome.speed`, `× 0.8` if juvenile, `× 0.6` at night (`EffectiveSpeed`). Actual movement applies `speed × 2.2 × dt` per step (`MoveTowardGoal`).
- **Aging** — `age += dt / 24` in `StepNeeds` (≈24 sim-seconds per game-year).

### 5.4 Genetics (`EcoSim.Core/Sim/Genetics.cs`)

- **`NewGenome`** — species base gene `× (1 + gauss × 0.12)` per gene (hue is `base + gauss × 20`), then clamped to range.
- **`BreedGenome`** — parent average `+ gauss × (range) × 0.05`; a **2%** chance adds a `gauss × (range) × 0.18` jump mutation; clamped. (Note: breeding reads only parents, never the species base — this is why evolution purchases must patch living genomes; see §9.)

---

## 6. Needs, damage & death

All in `CreatureSystem.StepNeeds` and `CreatureActions`. A global `SimConstants.NeedsDrainScale = 0.5` softens all decay. `load = size × metab`.

**Per second (× `dt`, × drain scale where noted):**

```text
hunger -= (0.9*load + (state=="hunt" ? 0.6 : 0)) * drain
thirst -= (1.0*load) * drain
energy -= (0.6*load + (moving ? 0.9 : 0)) * drain
age    += dt/24
```

**Climate stress:** `stress = max(0, |localTemp − genome.temp| − tol)`; if `stress > 0`, `hp -= stress × 14 × dt`.

**Starvation/dehydration:** at `hunger ≤ 0`, `hp -= 6×dt`; at `thirst ≤ 0`, `hp -= 7×dt`.

**Healing:** if `hunger > 55` **and** `thirst > 55` **and** no climate stress, `hp += 4×dt` (capped 100).

**Death:** `hp ≤ 0` → cause resolved to *starvation / dehydration / both / exhaustion*; or `age ≥ lifespan` → *old age* (`Die`). Death removes the creature from the grid, records species stats + life story, drops `+0.15` veg, and calls `PlayerControl.NotifyDeath` (crucial for possession succession).

**Action effects** (`CreatureActions`):
- **Drink** — `thirst += 60×dt` at water/shore.
- **Graze bite** — `bite = min(veg, 3.5×dt)`, `hunger += bite × 26`, veg decremented (edibility gated by `GrazeFood`).
- **Rest** — `energy += 9×dt`.

**Pregnancy:** `Pregnant` counts down in `StepNeeds`; hitting 0 calls `GiveBirth`.

---

## 7. Movement, perception & navigation

- **Spatial hash** — uniform grid, cell size `SimConstants.Cell = 6`; `SyncGrid` moves only creatures that changed cells; `Nearby(c, r)` scans the neighborhood with squared-distance culling (`CreatureSystem`).
- **Pathing** — `MoveTowardGoal` picks a waypoint via `Navigation.ResolveMovementTarget` (direct pursuit within `DirectPursuitRadius = 4` / line of sight, else windowed A*), replanning on a phase timer or when the goal changes. Water (non-swimmers) and Peak block steps.
- **Swim/fly** — `SpeciesCanSwim = CanSwim || shape=="bird"` (`SpeciesCatalog`); birds and the beaver (and `canSwim` evolutions) cross water; others route to shore drink tiles.
- **Foraging** — `FindFoodForGraze` expands its search radius up to 5× sense; water-stranded grazers seek nearest walkable land (`ResolveGrazeSearchGoal`).

---

## 8. AI behavior trees (un-possessed creatures)

Un-possessed creatures decide via a JSON behavior tree; possessed creatures bypass it (`StepCreature` routes to `PlayerControl.StepPlayer` when controlled, else `BehaviorTree.Tick`).

### 8.1 Templates & thresholds (`data/behaviors/library.json`)

Three diet templates — `herbivore_prey`, `carnivore`, `omnivore` — plus shared thresholds:

| Threshold | Value | Meaning |
|-----------|-------|---------|
| thirstUrgent / thirstExit | 30 / 55 | enter/exit drinking |
| hungerUrgent / hungerGraze / hungerExit / hungerHunt | 25 / 55 / 65 / 75 | hunger tiers |
| energyUrgent / restEnergy / energyExit | 12 / 18 / 30 | rest tiers |
| nightWanderRestEnergy | 75 | forced night rest |
| mateHungerMin / mateThirstMin / mateEnergyMin | 45 / 40 / 45 | mating gate |

Per-species files (`data/behaviors/{species}.json`) `extends` a template and override thresholds/actions (e.g. faster flee speed for prey).

### 8.2 Tree shape (4 priority tiers)

Each template is a top selector over four tier groups (`library.json` `trees`):

| Tier group | `interruptTier` | Branches |
|------------|-----------------|----------|
| `threat_response` | 0 (critical) | flee-and-drink-at-shore, flee |
| `survival_needs` | 1 | thirst → (hunt/graze) → rest |
| `discretionary` | 2 | mate, stalk (carnivore/omnivore) |
| `ambient` | 3 | night-rest, wander |

Actions carry `state`, `goal`, `speedMult`, `interruptTier`, and optional `minCommitSec` anti-flicker dwell. Conditions use hysteresis ops (`*BelowOrState`) so a creature that starts drinking/grazing/resting doesn't stop the instant it crosses the entry threshold. States: `flee · thirst · graze · hunt · huntSearch · rest · mate · wander`.

---

## 9. Predation & mating (shared by AI and player)

`CreatureActions` are used identically by the behavior executor and the player system.

**Hunt strike** (`TryHuntStrike`):
- **Reach** `HuntStrikeRange = ESize(hunter)×0.55 + ESize(prey)×0.45 + 0.65`.
- **Chance** `HuntStrikeChance = 0.35 + agg×0.35`.
- **Damage** `30 + hunter.size×15`. On kill, hunter gains `hunger +50`, `energy +12`; prey `Cause="predation"`, `KilledById=hunter.Id`.

**Mate** (`TryConsummateMate`): partners must be within `1.0` tile, opposite sex, not pregnant/on cooldown. The female becomes pregnant for `SampleGestation(species)`, stores the male's genome + id, sets `LitterQ = round(litter × Rf(0.7, 1.15))`, sets cooldowns (female full, male `× 0.6`), and both lose energy (female −20, male −12).

**Birth** (`CreatureSystem.GiveBirth`): produces `LitterQ` babies (capped at `MaxPop`), each `gen+1`, `age 0`, `hunger/thirst 70`, `energy 80`, bred from mother × stored partner genome; links parent/offspring ids; then calls `PlayerControl.NotifyBirth`.

---

## 10. Populations & lifecycle management

- **Stocking** (`CreatureSystem.StockLife`): `budget = min(0.45×MaxPop, lerp(0, 260×√(area/64), Animals))`; each species gets `round(budget × stockWeight)` individuals placed on valid tiles (`FindSpawnTile`, respecting `SpawnNearWater`).
- **Cap** — `SimConstants.MaxPop = 6000`.
- **Pruning** (`PruneDead`): removes dead creatures (except the selected one) when over 115% capacity, when dead bloat exceeds `max(40, 8% of alive)`, or 5% of ticks randomly.
- **Migrants** (`Simulation.RunMigrantPulse`): **off by default** (`AutoMigrationEnabled`). When on, every >6 s: a near-extinct species (≤1 alive) with total pop < 70% cap reseeds — herbivores 60% chance for 2–3, predators 25% for 1 (only if a prey species has >2 members).

---

## 11. Species roster (`data/species.json`)

Eleven species; each has a flavor `blurb`, diet (`0` herbivore graze-only, `1` carnivore, `2` omnivore), shape, color, predator/prey lists, base genome, gestation/cooldown windows, and stock weight. Predator/prey lists are compiled into bitmasks (`HuntsMask`/`PreyMask`) used by both AI and player click-orders (`SpeciesCatalog.AttachSpeciesMasks`).

| Species | Diet | Shape | Speed | Sense | Litter | Lifespan | Agg | Hunts | Hunted by |
|---------|------|-------|-------|-------|--------|----------|-----|-------|-----------|
| 🐇 Rabbit | Herb | small | 1.6 | 7 | 3.2 | 9 | 0 | — | fox, wolf, hawk, owl, bear |
| 🐭 Mouse | Herb | small | 1.85 | 6 | 4.5 | 6 | 0 | — | fox, hawk, wolf, owl |
| 🦌 Deer | Herb | tall | 1.5 | 9 | 1.4 | 16 | 0 | — | wolf, bear |
| 🫎 Elk | Herb | tall | 1.35 | 10 | 1.2 | 18 | 0 | — | wolf, bear |
| 🦫 Beaver | Herb | stocky (canSwim, spawns near water) | 1.05 | 7 | 2.4 | 13 | 0.1 | — | wolf, bear |
| 🐗 Boar | Omni | stocky | 1.1 | 7 | 2.2 | 14 | 0.4 | (omnivore) | wolf, bear |
| 🦊 Fox | Carn | small | 1.7 | 10 | 2.4 | 11 | 0.7 | rabbit, mouse | — |
| 🐺 Wolf | Carn | tall | 1.55 | 12 | 2.0 | 13 | 0.85 | rabbit, deer, boar, elk, beaver, mouse | — |
| 🦅 Hawk | Carn | bird (flies) | 2.1 | 16 | 1.4 | 15 | 0.75 | rabbit, mouse | — |
| 🦉 Owl | Carn | bird (flies) | 1.85 | 18 | 1.6 | 14 | 0.72 | mouse, rabbit | — |
| 🐻 Bear | Omni | stocky | 1.0 | 9 | 1.8 | 20 | 0.55 | rabbit, deer, boar, mouse, beaver | — |

Stat design reads directly as ecological roles: mice/rabbits are fast, huge-litter, short-lived prey; deer/elk are slow-breeding, wary, long-lived; beaver escapes into water; birds fly and have elite senses; wolf is the widest-menu apex with the highest aggression and metabolism (starvation risk); bear is the slow, long-lived generalist.

---

## 12. Possession — the core game (`PlayerControlSystem`)

`SimSession.Player` (`EcoSim.Core/Sim/PlayerControlSystem.cs`) drives one creature; `scripts/PlayerModeController.cs` is the Godot glue (input, camera, modals).

### 12.1 Entering/leaving
- **P** toggles possession: possess the selected creature, or a random living creature if none selected (`PlayerModeController.TogglePossession`).
- On **world generation**, the **Species Select** modal opens (`HudController.OnGenerate` → `ShowSpeciesSelect`); choosing a species possesses a random living member, or "Just observe" stays in sandbox.
- `Possess` clears stale AI/nav state, selects the creature, and increments `BodiesInhabited`.

### 12.2 Control (`StepPlayer`, `PlayerModeController`)
- **Movement** — WASD/arrows steer velocity directly (`SteerDirect`), with axis-separated sliding along shorelines; cancels any active order.
- **Sprint** — hold **Shift**: `SprintSpeedMult = 1.6`, `SprintEnergyPerSec = 2.2`, disabled below `SprintMinEnergy = 5` (`SimConstants`).
- **Idle auto-actions** (`StepAutoActions`) — auto-drink if thirst < 65 at water, auto-graze (non-carnivores) if hunger < 70 on veg, auto-rest if energy < 35.
- **Explicit** — **E** strikes nearest huntable prey; **R** mates with nearest eligible partner; **T** opens the evolution tree.
- Possessed needs, aging, breeding, and death run exactly like an AI creature (only the decision source changes).

### 12.3 Contextual click orders (`IssueClickOrder`)
A world click becomes an order based on what was clicked:

| Clicked | Order | Result |
|---------|-------|--------|
| opposite-sex same species | `MateWith` | approach, mate on contact |
| a species in our `PreyMask` (hunts us) | `FleeFrom` | flee until safe (`max(12, sense×2)`) |
| a species in our `HuntsMask` | `Hunt` | chase, strike, feed |
| water tile | `DrinkAt` | go to water/shore, drink to full |
| veg tile (non-carnivore) | `GrazeAt` | go graze until full/bare |
| anything else | `MoveTo` | pathfind there |

The active order is surfaced in the HUD ("Hunting Rabbit #12", "Heading to water", …) via `OrderDescription`.

### 12.4 Breeding → generation points (`NotifyBirth`)
When the possessed creature parents a litter, `PlayerProgress.AddPoint(species)` grants **+1 point** and a `BirthChoiceEvent` opens the **Birth Choice** modal (`scripts/ui/BirthChoicePanel.cs`): **Play as newborn** (possession jumps to an offspring) or **Continue as parent**.

### 12.5 Death → body transfer (`NotifyDeath`)
Succession is resolved synchronously before pruning:
1. Died by predation with a living killer → **possess the killer** (`TimesKilled++`).
2. Else (natural death) → **possess a sibling** (shares a parent).
3. Else → **possess a random same-species creature**.
4. Else → **Game Over** (`GameOverEvent`): the species is extinct.

Each transfer raises a `TransferEvent` (transient notice + camera refocus).

### 12.6 Game Over (`scripts/ui/GameOverPanel.cs`)
Full-screen **EXTINCTION** screen: run summary (`BodiesInhabited`, `TotalPointsEarned`, `TimesKilled`, `NaturalDeaths`) and a per-species world stats grid (alive/born/died/top death cause). Options: **Start Over** (new world; `EvolutionSystem.ResetAll`) or **Possess** another living species.

---

## 13. Progression & evolution

### 13.1 Generation points (`EcoSim.Core/Sim/PlayerProgress.cs`)
Points are earned per litter, tracked **per species** (`PointsBySpecies`), spent on evolution nodes, and are **per-run only** (`Reset`). Also tracks `TotalPointsEarned`, `BodiesInhabited`, `TimesKilled`, `NaturalDeaths`.

### 13.2 Evolution trees (`EcoSim.Core/Sim/EvolutionSystem.cs`, `data/evolutions/{species}.json`)
Each species has a hand-authored tree loaded/validated by `EvolutionCatalog` (unique ids, cost ≥ 1, known gene/ability keys, acyclic prerequisites). A node has `id`, `label`, `desc`, `cost`, `requires[]`, and `effects`:
- **Gene ops** (`GeneOp`): multiply and/or add on a base gene, clamped to range.
- **Abilities**: currently `canSwim`.

**Purchasing (`Purchase`)** spends points, then applies the effect to (a) the **live species base genome** so future spawns inherit it, and (b) **every currently-living member's genome** — required because `BreedGenome` averages parents and never re-reads the base. `ResetAll` restores pristine species defs and recompiles behaviors.

**Example — Rabbit** (`data/evolutions/rabbit.json`): Fleet Feet I/II (speed), Keen Ears (sense), Big Litters (+1 litter), Hardy Coat (+tol), Elder Bloodline (+25% lifespan), **Marsh Rabbit** (canSwim). **Example — Wolf** (`data/evolutions/wolf.json`): Pack Howl, Loping Gait, Savage Bite, Great Jaw, Den Litter, Lean Winter (−metab), **Dire Wolf** (+speed & lifespan). All 11 species have a tree; costs/requires create short branching paths capped by a signature capstone.

---

## 14. Controls (from `PlayerModeController`, `PlayerHudPanel`, `HudController`)

| Input | Action |
|-------|--------|
| **P** | Possess selected / random creature, or release |
| **WASD / arrows** | Steer possessed creature (cancels click order) |
| **Shift** | Sprint |
| **Left-click world** | Contextual order while possessing; select creature otherwise |
| **E** | Attack nearest prey (hunters) |
| **R** | Mate with nearest eligible partner |
| **T** | Open evolution tree (while possessing) |
| **Follow button / F** | Follow-camera on selection |
| speed slider | 0–10× (clamped to 1× while possessing) |
| Top-bar buttons | World Generator, Profiler, BT Observe, CPU/GPU, Test Runner, Follow |
| Right-click species row | Species GOD menu → Kill All |

The possession HUD (`scripts/ui/PlayerHudPanel.cs`, bottom-center) shows species/sex/#id + generation points, four need bars (HP/Food/Water/Rest), a live status line (order or state + age + pregnancy), and context key hints (attack hint only for hunters).

---

## 15. Sandbox tools & UI

**Panels** (wired in `scripts/ui/HudController.cs`, built in `scripts/ui/`):
World Generator (`GenPanel` — sliders, size, Generate, Restock), Ecosystem + pop graph (`EcosystemPanel`, `PopGraph`, `PopHistoryTracker`), Species Stats (`SpeciesStatsPanel`), Inspector (`InspectorPanel` — stats + life story), Behavior-Tree observe/editor (`BtEditorPanel`), World Story (`WorldStoryTracker`/`StoryPanel`), Timeline DB browser (`TimelineDbPanel`), Profiler + detail (`ProfilerPanel`, `ProfilerDetailPanel`), Toolbar (`ToolsController`), Species GOD menu (`SpeciesGodMenu`), Death notices (`DeathNoticePanel`), Pause menu (`PauseMenuPanel`), Timeline scrub strip (`TimelineStrip`). Top bar shows day icon/clock/day, population, max generation, veg %, sim FPS, and the speed control.

**Tools** (`ToolsController` / `WorldTools`): inspect, rain/drought (paint veg), meteor, cull, plus **Restock** and per-species **Kill All** (`CreatureSystem.KillAllBySpecies`).

**Selection/inspection:** clicking selects a creature; the selected creature is kept even when dead (excluded from pruning) so the inspector persists.

---

## 16. Timeline & persistence

- **Snapshots** are captured on a fixed sim-time cadence by `TimeScrubController.CaptureIfDue` (invoked from `GameApp._Process`) and stored via `SnapshotService` + `TimelineDb` (SQLite).
- **Scrub** — `TimelineStrip` seeks; the controller restores the nearest snapshot ≤ target and forks/truncates the future when a mutating tool/GOD action runs in the past (`OnMutatingAction`). While scrubbing, the sim is frozen (`GameApp` forces speed 0) and possession input is inert.
- **Heartbeats / events** — periodic metric rows and world/creature events feed the World Story and Timeline DB browser.

---

## 17. Rendering (from `scripts/render/`)

- **Terrain/veg** baked by `TerrainBaker`; **infinite ocean** backdrop by `InfiniteOceanOverlay`; GPU water shimmer via `water_overlay.gdshader`.
- **Creatures** drawn by `CreatureRenderer` (`CreatureDrawUtil`, `CreatureSpriteCatalog`) with LOD by zoom/quality; overlays for highlights (`CreatureHighlightOverlay`), pedigree lines (`CreaturePedigreeOverlay`), and tool FX (`ToolFxOverlay`).
- **Camera** — `WorldCamera` pans/zooms and follows the selection (auto-enabled on possession/transfer).
- **Display smoothing** — `CreatureSystem.AdvanceDisplayPositions` lerps render positions toward sim positions (`Rx/Ry`), faster while scrubbing.
- **Quality/throttle** — `QualityConfig` (nav radius/replan) and `scripts/gpu/GpuThrottle.cs`.

---

## 18. Tooling (non-gameplay)

- **Batch/headless** — `EcoSim.Core/Batch/` (`BatchHarness`, `BatchMetrics`, `BalanceConfigLoader`) fast-forwards the sim for balance evaluation; `EcoSim.BatchCli` is the CLI entry.
- **Species overrides** — `SpeciesCatalog.ApplyOverrides` deep-merges balance patches over pristine defs (used by batch/balance).

---

## 19. Key tuning constants (single source: code)

| Constant | Value | File |
|----------|-------|------|
| `MaxPop` | 6000 | `SimConstants.cs` |
| `Cell` | 6 | `SimConstants.cs` |
| `SimDaySeconds` | 40 | `SimConstants.cs` |
| `NeedsDrainScale` | 0.5 | `SimConstants.cs` |
| `DirectPursuitRadius` | 4 | `SimConstants.cs` |
| `SprintSpeedMult / SprintEnergyPerSec / SprintMinEnergy` | 1.6 / 2.2 / 5 | `SimConstants.cs` |
| Substep cap | 6 | `GameApp.cs` |
| Possession speed cap | 1× | `GameApp.cs` |
| Night threshold | LightLevel < 0.28 | `Simulation.cs` |
| Hunt strike chance / damage | `0.35 + agg×0.35` / `30 + size×15` | `CreatureActions.cs` |
| Drink / graze / rest rates | `+60 / bite×26 / +9` per unit dt | `CreatureActions.cs` |
| Need decays | hunger `0.9×load(+0.6 hunt)`, thirst `1.0×load`, energy `0.6×load(+0.9 moving)` | `CreatureSystem.cs` |
| Breeding mutation | avg + 5% range gauss; 2% chance +18% jump | `Genetics.cs` |
| Spawn jitter | ±12% gauss per gene | `Genetics.cs` |
| Migrant interval / chances | >6 s; herb 60%×(2–3), pred 25%×1 | `Simulation.cs` |
| Stock budget | `min(0.45×MaxPop, lerp(0, 260√(area/64), animals))` | `CreatureSystem.cs` |

---

## 20. Observations & gaps (evident in code, not doc-sourced)

- **No scenario/objective/win layer** — runs are open-ended; the only failure state is species extinction (`GameOverEvent`). There is no timer, quota, or score-to-beat in code.
- **No cross-run persistence** — `PlayerProgress` resets each run; evolutions are not saved between worlds.
- **Contextual orders are the current player-interaction model** — `PlayerControlSystem` and `TODO`-style `// #region agent log` debug hooks show active iteration around orders and starvation/thirst edge cases (`CreatureSystem` logging).
- **GPU simulation is not the authoritative path** — the CPU tick is; `scripts/gpu/` is limited to throttle config here (no compute sim in `EcoSim.Core`).
- **Abilities are minimal** — the only non-gene evolution effect is `canSwim` (`EvolutionSystem.ApplyToSpeciesDef`); richer abilities (new behaviors, diet shifts) would be a natural extension of the existing `EvolutionEffects` schema.
- **Balance tooling is CLI/harness-based** (`EcoSim.Core/Batch/`), with species-override merging already supported for tuning experiments.

---

*Reconstructed from source only. When possession, evolution, needs, worldgen, or the tick loop change in code, this document should be re-derived from the same files cited above.*
