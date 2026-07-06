# Download and install Godot 4.7 Mono export templates (Windows).
$ErrorActionPreference = "Stop"
$Version = "4.7.stable.mono"
$Url = "https://github.com/godotengine/godot-builds/releases/download/4.7-stable/Godot_v4.7-stable_mono_export_templates.tpz"
$DestRoot = Join-Path $env:APPDATA "Godot\export_templates"
$DestDir = Join-Path $DestRoot $Version
$TmpZip = Join-Path $env:TEMP "Godot_v4.7-stable_mono_export_templates.zip"
$TmpExtract = Join-Path $env:TEMP "Godot_v4.7_export_templates_extract"

if (Test-Path (Join-Path $DestDir "windows_release_x86_64.exe"))
{
    Write-Host "Export templates already installed at $DestDir"
    exit 0
}

Write-Host "Downloading Godot 4.7 Mono export templates (~1 GB)..."
if (Test-Path $TmpZip) { Remove-Item $TmpZip -Force }
if (Test-Path $TmpExtract) { Remove-Item $TmpExtract -Recurse -Force }

# curl is more reliable for large GitHub release assets on Windows
curl.exe -L --ssl-no-revoke --fail --retry 3 --retry-delay 5 -o $TmpZip $Url
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $TmpZip))
{
    Write-Host "curl failed; trying Invoke-WebRequest..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $Url -OutFile $TmpZip -UseBasicParsing
}

if (-not (Test-Path $TmpZip) -or (Get-Item $TmpZip).Length -lt 1MB)
{
    Write-Error "Download failed or file too small. Check network and retry."
}

New-Item -ItemType Directory -Force -Path $TmpExtract | Out-Null
Expand-Archive -Path $TmpZip -DestinationPath $TmpExtract -Force

# .tpz may extract flat or into a version subfolder
$src = $TmpExtract
$nested = Get-ChildItem $TmpExtract -Directory | Where-Object { Test-Path (Join-Path $_.FullName "windows_release_x86_64.exe") } | Select-Object -First 1
if ($nested)
{
    $src = $nested.FullName
}

if (-not (Test-Path (Join-Path $src "windows_release_x86_64.exe")))
{
    Write-Error "Extracted archive missing windows_release_x86_64.exe. Contents: $(Get-ChildItem $TmpExtract -Recurse | Select-Object -First 20 | ForEach-Object { $_.FullName })"
}

if (Test-Path $DestDir) { Remove-Item $DestDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
Copy-Item -Path (Join-Path $src "*") -Destination $DestDir -Recurse -Force

Remove-Item $TmpZip -Force -ErrorAction SilentlyContinue
Remove-Item $TmpExtract -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Installed export templates to $DestDir"
