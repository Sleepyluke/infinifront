# Builds the current code, then launches the game.
# The Godot runtime loads .godot/mono/temp/bin/Debug at launch WITHOUT rebuilding,
# so launching the exe directly can silently run stale code (caught in playtest 2026-06-11).
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$env:Path += ';C:\Program Files\dotnet'

Write-Host 'Building latest code...'
dotnet build "$repo\godot\LlmRts.Godot.csproj" --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host 'BUILD FAILED - not launching (fix the build first).' -ForegroundColor Red
    pause
    exit 1
}

$godot = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_*" -Recurse -Filter 'Godot_v*_mono_win64.exe' | Select-Object -First 1
Start-Process -FilePath $godot.FullName -ArgumentList '--path', "$repo\godot"
