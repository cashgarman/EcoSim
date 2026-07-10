# TODO — contextual click orders (in progress)

Possessed animal clicks are becoming contextual orders instead of plain move commands.

- [x] Core `PlayerOrder` system in `PlayerControlSystem`: MoveTo / DrinkAt / GrazeAt / Hunt / FleeFrom / MateWith
- [x] `IssueClickOrder` interpretation: ground → move, water → drink, prey (carnivore) → hunt+feed, vegetation (herbivore) → graze, predator-of-us → flee, opposite-sex same species → court
- [x] WASD cancels the active order; orders auto-complete (arrival, full thirst/hunger, kill, target lost/escaped)
- [x] WorldRenderer click → `PickCreature` → `IssueClickOrder`
- [x] Tooltip + player HUD show the current order (e.g. "Hunting Rabbit #12") instead of the AI state label
- [ ] Update existing tests that used `ClickGoal` intents (`Wasd_MovesCreature_AndCancelsClickGoal`, `ClickGoal_PathfindsAndArrives`)
- [ ] New tests: order interpretation + execution per kind (drink, hunt, graze, flee, mate)
- [ ] Build + full test run
- [ ] In-game verification with screenshots
