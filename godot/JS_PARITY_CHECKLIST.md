# Godot JS Parity Checklist

Track parity between Godot port and legacy `wildlands-ecosim.html` / `batch-test.html`.

## Phase 0 — Boot
- [x] Inspector panel structure (PanelHead + PanelBody + CollapseBtn)
- [x] Stable node binding (`FindChild` vs fragile `%` after drag)
- [x] Headless smoke gate (`tests/smoke_headless.ps1`)

## Phase 1 — Rendering
- [x] Creature LOD (marker / rect / sprite by zoom + quality tier)
- [x] Pixel-style sprites by shape
- [x] Species hover (blue) + lock (gold) highlight rings
- [x] Pedigree + behavior-target lines
- [x] Display smoothing (`Rx`/`Ry` lerp)
- [x] State emoji at high zoom
- [x] Night dimming (creatures brighter than terrain via per-creature brightness)

## Phase 2 — Input & tools
- [x] Per-species spawn tools
- [x] Rain/drought drag-paint
- [x] Tool brush ring overlay
- [x] Double-click select + follow + min zoom
- [x] Species row double-click → jump to nearest
- [x] Hover creature tooltip
- [x] Auto-migration toggle (gen panel)
- [x] Ecosystem maximize mode
- [ ] Balance tuning banner + reset

## Phase 3 — Timeline
- [x] Load `config/timeline-config.json` at boot
- [x] Perf policy helpers (`PerfPolicy.cs`)
- [x] Heartbeat rows in SQLite
- [x] Creature events table + API
- [x] Timeline DB panel (paginated browser)
- [ ] Timeline viewport zoom/pan + hover tooltip
- [ ] Scrub meta persistence (zoom/pan)

## Phase 4 — Story depth
- [x] `CreatureNotify` formatters + death refinement
- [x] Life story milestones (`born`, `gaveBirth`)
- [x] World events persisted to Timeline DB
- [ ] Full milestone set (mated, hunted, preyedOn, grazed, drank…)
- [ ] Clickable creature links in World/Life story
- [ ] Species death breakdown with killer emoji

## Phase 5 — Profiler
- [x] Profiler detail panel (CPU/GPU tabs)
- [x] Frame sparkline ring buffer
- [x] Scope timing dictionary (detail mode)
- [ ] Full hierarchical call tree
- [ ] Real per-bucket instrumentation

## Phase 6 — Batch UI
- [x] `BatchTest.tscn` + native runner UI
- [x] Test Runner button enabled
- [x] BatchCli `--fuzz` / `--fuzz-trials`
- [ ] Full balance designer + saved runs table
- [ ] Fuzz recommendations panel

## Phase 7 — GPU
- [x] GPU throttle presets (top bar)
- [x] `GpuSimulationBackend` spike stub (CPU fallback documented)
- [ ] RenderingDevice compute passes
- [ ] GPU creature instancing path
- [ ] CPU BT → GPU step bridge

## Verification gates

| Gate | Command |
|------|---------|
| Core tests | `dotnet test EcoSim.sln` |
| Godot smoke | `godot/WildlandsEcoSim/tests/smoke_headless.ps1` |
| Batch CLI | `dotnet run --project EcoSim.BatchCli -- --days 80 --size s` |
