# Legacy Web Stack (Archived)

The original **vanilla JS / Canvas / WebGPU** implementation is **frozen**. New features and simulation work happen in the **Godot 4.7** desktop port.

## Archived entry points

| File | Former role |
|------|-------------|
| [`wildlands-ecosim.html`](wildlands-ecosim.html) | Sandbox game UI + render |
| [`batch-test.html`](batch-test.html) | Batch/balance test runner UI |
| [`serve.py`](serve.py) | Static HTTP server + batch report API |
| [`js/`](js/) | ES module simulation, render, and batch UI |

## Still shared with Godot

| Path | Role |
|------|------|
| [`data/species.json`](data/species.json) | Species stats, genes, behavior keys |
| [`data/behaviors/`](data/behaviors/) | Behavior tree library + per-species overrides |
| [`config/timeline-config.json`](config/timeline-config.json) | Timeline snapshot interval (web only today) |
| [`GAMEPLAY.md`](GAMEPLAY.md) | Player-facing design doc |

## Headless testing today

Use the C# stack instead of Playwright batch:

```powershell
dotnet test EcoSim.sln
python scripts/run_batch_godot.py --seed 42 --size s --days 100
```

## Agent docs

- **Primary:** [`GODOT.md`](GODOT.md)
- **Historical (web):** [`AGENTS.md`](AGENTS.md) — still useful for behavior reference during migration

Bugfixes to the web stack are discouraged unless they unblock data or shared JSON schema used by `EcoSim.Core`.
