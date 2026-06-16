# Builds the current code, imports assets, then launches the game.
# The Godot runtime loads .godot/mono/temp/bin/Debug at launch WITHOUT rebuilding,
# so launching the exe directly can silently run stale code (caught in playtest 2026-06-11).
# It also won't pick up NEW/changed art unless the project is (re)imported first.
#
#   ./play.ps1            build + import + launch ONE instance (single-player / 1v1 vs CPU)
#   ./play.ps1 -Two       build + import ONCE + launch TWO instances side by side (LAN MP playtest:
#                         Host on one window, Join 127.0.0.1 on the other)
#   ./play.ps1 -NoBuild   launch one instance WITHOUT building or importing — use to add another
#                         peer by hand; a running instance locks LlmRts.Godot.dll so a rebuild would FAIL.
param([switch]$Two, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$env:Path += ';C:\Program Files\dotnet'

$godot = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_*" -Recurse -Filter 'Godot_v*_mono_win64.exe' | Select-Object -First 1

if (-not $NoBuild) {
    Write-Host 'Building latest code...'
    dotnet build "$repo\godot\LlmRts.Godot.csproj" --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'BUILD FAILED - not launching (fix the build first).' -ForegroundColor Red
        pause
        exit 1
    }
    # Import any new/changed assets (sprites, etc.) so the running game sees them, not stale art.
    Write-Host 'Importing assets...'
    & $godot.FullName --headless --import --path "$repo\godot" *> $null
}

# First (or only) window.
Start-Process -FilePath $godot.FullName -ArgumentList '--path', "$repo\godot", '--position', '80,80'

if ($Two) {
    # Build/import already ran once above; just launch a second window. No second build => no DLL lock.
    Start-Sleep -Seconds 2
    Start-Process -FilePath $godot.FullName -ArgumentList '--path', "$repo\godot", '--position', '760,80'
    Write-Host 'Launched TWO instances. In one: "Host LAN game". In the other: "Join" (127.0.0.1).' -ForegroundColor Green
}
