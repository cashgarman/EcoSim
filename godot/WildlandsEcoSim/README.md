# Wildlands EcoSim — Godot Project

Godot 4.7+ port with C# simulation core (`EcoSim.Core`).

**Important:** `WildlandsEcoSim.csproj` must use `Godot.NET.Sdk/4.7.0` to match the editor. After upgrading, delete `.godot/mono/` if the editor crashes on open, then let Godot rebuild.

## Prerequisites

- Godot 4.7 stable Mono (local install):
  - `C:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe`
  - `C:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe` (headless)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

Override Godot path: set `GODOT_BIN` to the console executable.

## Setup

```powershell
# From repo root — junction shared data into Godot project
cd I:\EcoSim\godot\WildlandsEcoSim
cmd /c mklink /J data ..\..\data

# Build core + tests (no Godot required)
cd I:\EcoSim
$env:DOTNET_ROOT = "I:\EcoSim\.dotnet"
$env:PATH = "I:\EcoSim\.dotnet;" + $env:PATH
dotnet build EcoSim.sln
dotnet test EcoSim.sln
```

Open `godot/WildlandsEcoSim/project.godot` in Godot Editor and press F5.

## Headless batch (C# CLI)

```powershell
python scripts/run_batch_godot.py --seed 42 --size s --days 10
```

## Structure

| Path | Role |
|------|------|
| `EcoSim.Core/` | Pure C# sim library (no Godot refs) |
| `scripts/` | Godot glue nodes |
| `scenes/` | UI and world scenes |
| `data/` | Junction → `../../data` |

## Migration branches

`godot/phase-0-bootstrap` … `godot/phase-7-ship`, merged into `godot-migration`.

### Phase 7 ship (current)

- Windows export preset + `scripts/export_windows.ps1` → `build/WildlandsEcoSim.exe`
- [`GODOT.md`](../../GODOT.md) — primary agent reference
- [`LEGACY.md`](../../LEGACY.md) — archived web stack
- GitHub Actions: `dotnet test` on push/PR

### Phase 4b JS UI parity

- Stone theme, full top bar (clock, gen, veg%, FPS, Follow, Profiler, Test Runner stub)
- Timeline strip with scrub + Present (SQLite snapshots)
- Labeled gen panel, pop graph, species stats, inspector tabs, world story feed
- Bottom toolbar + species GOD menu (Kill All)
- Terrain TX bake, water shimmer, dark page background

## Windows export

```powershell
# Install Godot 4.7 export templates in the editor first (Manage Export Templates)
powershell scripts/export_windows.ps1
# -> build/WildlandsEcoSim.exe
```
