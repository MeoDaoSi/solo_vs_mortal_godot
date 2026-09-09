$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $projectRoot 'build'
$publishDir = Join-Path $projectRoot '.godot\mono\temp\bin\ExportRelease\win-x64'
$dataDir = Join-Path $buildDir 'data_solo_vs_mortal_godot_windows_x86_64'
$configDir = Join-Path $buildDir 'data\configs'

New-Item -ItemType Directory -Force $buildDir | Out-Null
dotnet restore (Join-Path $projectRoot 'solo_vs_mortal_godot.csproj') -r win-x64 --ignore-failed-sources
dotnet publish (Join-Path $projectRoot 'solo_vs_mortal_godot.csproj') -c ExportRelease -r win-x64 --self-contained true --no-restore -o $publishDir

$godot = (Get-Command godot.exe -ErrorAction Stop).Source
& $godot --headless --path $projectRoot --export-release 'Windows Desktop' (Join-Path $buildDir 'solo_vs_mortal_godot.exe')
if (-not (Test-Path (Join-Path $buildDir 'solo_vs_mortal_godot.exe'))) { throw 'Godot export did not produce an executable.' }

# Headless Godot versions may report a publish warning and omit the external data directory.
# Keep the verified self-contained publish output beside the executable as the runtime expects.
if (Test-Path $dataDir) { Remove-Item -LiteralPath $dataDir -Recurse -Force }
Copy-Item -LiteralPath $publishDir -Destination $dataDir -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $buildDir 'data') | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'data\*') -Destination (Join-Path $buildDir 'data') -Recurse -Force

$required = @(
    (Join-Path $dataDir 'solo_vs_mortal_godot.dll'),
    (Join-Path $dataDir 'GodotSharp.dll'),
    (Join-Path $buildDir 'data\configs\player.json'),
    (Join-Path $buildDir 'data\asset-manifest.json'),
    (Join-Path $buildDir 'data\v2.5\spec-lock.json'),
    (Join-Path $buildDir 'data\v2.5\content.v2.5.json'),
    (Join-Path $buildDir 'data\v2.5\balance.v2.5.json'),
    (Join-Path $buildDir 'data\v2.5\asset-requirements.v2.5.json'),
    (Join-Path $buildDir 'data\v2.5\style-lock.json')
)
foreach ($path in $required) { if (-not (Test-Path $path)) { throw "Missing export runtime file: $path" } }
Write-Output "WINDOWS_EXPORT_MANAGED_ASSEMBLIES_PASS $dataDir"
