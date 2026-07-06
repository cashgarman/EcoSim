# Wildlands EcoSim — Godot Project

Godot 4.3+ port with C# simulation core (`EcoSim.Core`).

## Prerequisites

- [Godot 4.3 .NET](https://godotengine.org/download) (Windows x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Setup

```powershell
# From repo root — junction shared data into Godot project
cd I:\EcoSim\godot\WildlandsEcoSim
cmd /c mklink /J data ..\..\data

# Build core + tests (no Godot required)
cd I:\EcoSim
dotnet build EcoSim.sln
dotnet test EcoSim.sln
```

Open `godot/WildlandsEcoSim/project.godot` in Godot Editor and press F5.

## Structure

| Path | Role |
|------|------|
| `EcoSim.Core/` | Pure C# sim library (no Godot refs) |
| `scripts/` | Godot glue nodes |
| `scenes/` | UI and world scenes |
| `data/` | Junction → `../../data` |

## Migration branches

See root plan: `godot/phase-0-bootstrap` … `godot/phase-7-ship`, merged into `godot-migration`.
