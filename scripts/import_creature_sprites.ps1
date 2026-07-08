#Requires -Version 5.1
<#
.SYNOPSIS
  Curates open-source creature sprites into godot/WildlandsEcoSim/assets/creatures.

.DESCRIPTION
  Downloads Kenney Animal Pack Redux (CC0) and LPC fox walk frame (CC0),
  copies Round (outline) PNGs for all 11 sim species, builds manifest.json,
  and stages 20 extra Kenney animals for future species.

  Kenney Animal Pack Remastered can replace Redux when downloaded manually from
  https://kenney.nl/assets/animal-pack-remastered — re-run with -RemasteredRoot.

.PARAMETER SkipDownload
  Skip network downloads; use existing files under tmp/sprites.

.PARAMETER RemasteredRoot
  Optional path to extracted Kenney Animal Pack Remastered PNG folder. When set,
  uses "Round (outline)" from that tree instead of Redux.
#>
param(
    [switch]$SkipDownload,
    [string]$RemasteredRoot = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Tmp = Join-Path $RepoRoot "tmp\sprites"
$Out = Join-Path $RepoRoot "godot\WildlandsEcoSim\assets\creatures"
$ReduxZip = Join-Path $Tmp "animal-pack-redux.zip"
$ReduxUrl = "https://opengameart.org/sites/default/files/kenney_animalPackRedux.zip"
$LpcZip = Join-Path $Tmp "lpc-animals.zip"
$LpcUrl = "https://opengameart.org/sites/default/files/lpc_animals_2022_v1.1.zip"
$TinyZip = Join-Path $Tmp "tiny-creatures.zip"
$TinyUrl = "https://opengameart.org/sites/default/files/tiny-creatures.zip"

function Ensure-Dir([string]$Path)
{
    if (-not (Test-Path $Path))
    {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Download-IfMissing([string]$Url, [string]$Dest)
{
    if (Test-Path $Dest)
    {
        return
    }

    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Dest -UseBasicParsing
}

Ensure-Dir $Tmp
Ensure-Dir $Out

if (-not $SkipDownload)
{
    Download-IfMissing $ReduxUrl $ReduxZip
    Download-IfMissing $LpcZip $LpcZip
    Download-IfMissing $TinyUrl $TinyZip
}

$ReduxRoot = Join-Path $Tmp "animal-pack-redux"
if (-not (Test-Path $ReduxRoot))
{
    Expand-Archive -Path $ReduxZip -DestinationPath $ReduxRoot -Force
}

$LpcRoot = Join-Path $Tmp "lpc-animals\lpc animals 2022 v1.1"
if (-not (Test-Path $LpcRoot))
{
    Expand-Archive -Path $LpcZip -DestinationPath (Join-Path $Tmp "lpc-animals") -Force
}

if ($RemasteredRoot -and (Test-Path $RemasteredRoot))
{
    $StyleRoot = Get-ChildItem -Path $RemasteredRoot -Recurse -Directory |
        Where-Object { $_.Name -eq "Round (outline)" } |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $StyleRoot)
    {
        throw "Could not find 'Round (outline)' under RemasteredRoot: $RemasteredRoot"
    }
}
else
{
    $StyleRoot = Join-Path $ReduxRoot "PNG\Round (outline)"
}

if (-not (Test-Path $StyleRoot))
{
    throw "Kenney style folder not found: $StyleRoot"
}

$SpeciesSources = @{
    rabbit = "rabbit.png"
    mouse  = "chick.png"
    deer   = "goat.png"
    elk    = "moose.png"
    beaver = "duck.png"
    boar   = "pig.png"
    wolf   = "dog.png"
    hawk   = "parrot.png"
    owl    = "owl.png"
    bear   = "bear.png"
}

foreach ($entry in $SpeciesSources.GetEnumerator())
{
    $src = Join-Path $StyleRoot $entry.Value
    if (-not (Test-Path $src))
    {
        throw "Missing source sprite: $src"
    }

    Copy-Item -Path $src -Destination (Join-Path $Out "$($entry.Key).png") -Force
}

$LpcFox = Join-Path $LpcRoot "individual creature spritesheets\fox, woods.png"
if (-not (Test-Path $LpcFox))
{
    throw "Missing LPC fox sheet: $LpcFox"
}

$Py = @"
import json, os, shutil
from PIL import Image

out = r'$Out'
style_root = r'$StyleRoot'
lpc_fox = r'$LpcFox'

species_sources = {
    'rabbit': 'rabbit.png',
    'mouse': 'chick.png',
    'deer': 'goat.png',
    'elk': 'moose.png',
    'beaver': 'duck.png',
    'boar': 'pig.png',
    'wolf': 'dog.png',
    'hawk': 'parrot.png',
    'owl': 'owl.png',
    'bear': 'bear.png',
}

biology_scale = {
    'mouse': 0.88,
    'rabbit': 0.92,
    'hawk': 0.94,
    'owl': 0.94,
    'fox': 0.96,
    'wolf': 0.98,
    'beaver': 1.0,
    'deer': 1.0,
    'boar': 1.02,
    'elk': 1.05,
    'bear': 1.08,
}

def sprite_entry(file_name, species_key=None):
    path = os.path.join(out, file_name)
    im = Image.open(path).convert('RGBA')
    bbox = im.getbbox() or (0, 0, im.width, im.height)
    x, y, x2, y2 = bbox
    w, h = x2 - x, y2 - y
    scale = biology_scale.get(species_key, 1.0) if species_key else 1.0
    return {
        'file': file_name,
        'scale': round(scale, 2),
        'anchor': [0.5, 0.88],
        'content': [x, y, w, h],
    }

fox_img = Image.open(lpc_fox).convert('RGBA')
frame = fox_img.crop((0, 0, 64, 64))
frame_bbox = frame.getbbox()
if frame_bbox:
    frame = frame.crop(frame_bbox)
canvas = Image.new('RGBA', (max(64, frame.width + 8), max(64, frame.height + 8)), (0, 0, 0, 0))
canvas.paste(frame, ((canvas.width - frame.width) // 2, (canvas.height - frame.height) // 2 + 4), frame)
canvas.save(os.path.join(out, 'fox.png'))

used_stems = {os.path.splitext(v)[0] for v in species_sources.values()}
manifest = {
    'defaultStyle': 'kenney_round_outline',
    'species': {},
    'extras': {},
}

for key, src_name in species_sources.items():
    manifest['species'][key] = sprite_entry(f'{key}.png', key)

manifest['species']['fox'] = sprite_entry('fox.png', 'fox')

for name in sorted(os.listdir(style_root)):
    if not name.endswith('.png'):
        continue
    stem = os.path.splitext(name)[0]
    if stem in used_stems:
        continue
    dest = f'extra_{stem}.png'
    shutil.copy2(os.path.join(style_root, name), os.path.join(out, dest))
    manifest['extras'][stem] = sprite_entry(dest)

with open(os.path.join(out, 'manifest.json'), 'w', encoding='utf-8') as f:
    json.dump(manifest, f, indent=2)
    f.write('\n')

print('Wrote', len(manifest['species']), 'species,', len(manifest['extras']), 'extras')
"@

python -c $Py
Write-Host "Creature sprites ready in $Out"
