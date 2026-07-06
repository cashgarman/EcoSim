# Headless boot smoke — must exit 0 within ~5 sim seconds.
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = $projectRoot
$csproj = Join-Path $projectRoot "WildlandsEcoSim.csproj"

dotnet build $csproj -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0)
{
    Write-Error "dotnet build failed"
}

$godotCandidates = @(
    $env:GODOT_EXE,
    "C:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe",
    "godot"
) | Where-Object { $_ -and (($_ -eq "godot") -or (Test-Path $_)) }

$godot = $godotCandidates | Select-Object -First 1
if (-not $godot)
{
    Write-Error "Godot executable not found. Set GODOT_EXE or install Godot 4.7 Mono."
}

Write-Host "Running headless smoke: $godot"
& $godot --headless --path $project --quit-after 5
if ($LASTEXITCODE -ne 0)
{
    Write-Error "Headless smoke failed with exit code $LASTEXITCODE"
}

Write-Host "Headless smoke passed."
