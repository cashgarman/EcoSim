# EcoSim Godot dev setup (Windows)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$GodotData = Join-Path $Root "godot\WildlandsEcoSim\data"
$RepoData = Join-Path $Root "data"
$DotnetDir = Join-Path $Root ".dotnet"

if (-not (Test-Path $GodotData))
{
    cmd /c "mklink /J `"$GodotData`" `"$RepoData`""
    Write-Host "Created data junction: $GodotData -> $RepoData"
}

# Prefer repo-local SDK when C: is low on space
$env:TEMP = Join-Path $Root ".tmp"
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null

if (-not (Test-Path (Join-Path $DotnetDir "dotnet.exe")))
{
    Write-Host "Install .NET 8 SDK to $DotnetDir (requires ~300 MB on I: drive)..."
    $installScript = Join-Path $Root "scripts\dotnet-install.ps1"
    if (-not (Test-Path $installScript))
    {
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript
    }
    & $installScript -Channel 8.0 -InstallDir $DotnetDir
}

$env:DOTNET_ROOT = $DotnetDir
$env:PATH = "$DotnetDir;" + $env:PATH
$dotnet = Join-Path $DotnetDir "dotnet.exe"

Push-Location $Root
try
{
    & $dotnet build EcoSim.sln
    & $dotnet test EcoSim.sln --no-build
}
finally
{
    Pop-Location
}
