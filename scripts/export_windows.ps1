# Build and export Wildlands EcoSim Windows desktop executable (Godot 4.7 Mono).
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "godot\WildlandsEcoSim"
$BuildDir = Join-Path $Root "build"
$GodotData = Join-Path $Project "data"
$RepoData = Join-Path $Root "data"
$DotnetDir = Join-Path $Root ".dotnet"
$Godot = if ($env:GODOT_BIN) { $env:GODOT_BIN } else { "C:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" }
$Preset = "Windows Desktop"

if (-not (Test-Path $Godot))
{
    Write-Error "Godot not found at $Godot. Set GODOT_BIN to the console executable."
}

$templatesDir = Join-Path $env:APPDATA "Godot\export_templates\4.7.stable.mono\windows_release_x86_64.exe"
if (-not (Test-Path $templatesDir))
{
    Write-Host "Export templates not found; installing..."
    & (Join-Path $Root "scripts\install_godot_export_templates.ps1")
}

if (-not (Test-Path $GodotData))
{
    cmd /c "mklink /J `"$GodotData`" `"$RepoData`""
    Write-Host "Created data junction for export."
}

$env:DOTNET_ROOT = $DotnetDir
$env:PATH = "$DotnetDir;" + $env:PATH
$dotnet = Join-Path $DotnetDir "dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null

Push-Location $Root
try
{
    & $dotnet build EcoSim.sln -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet test EcoSim.sln -c Release --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet build (Join-Path $Project "WildlandsEcoSim.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Exporting $Preset -> $BuildDir\WildlandsEcoSim.exe"
    & $Godot --headless --path $Project --export-release $Preset $BuildDir\WildlandsEcoSim.exe
    if ($LASTEXITCODE -ne 0)
    {
        Write-Host ""
        Write-Host "Export failed. Install Godot 4.7 export templates:"
        Write-Host "  Editor -> Manage Export Templates -> Download and Install"
        exit $LASTEXITCODE
    }

    Write-Host ""
    Write-Host "Export complete: $BuildDir\WildlandsEcoSim.exe"
}
finally
{
    Pop-Location
}
