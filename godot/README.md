# LlmRts — Godot presentation layer

Playable sandbox over the deterministic C# sim core (`src/SimCore`).

## Run

Requires Godot 4.6 .NET (the winget install, NOT the Steam build — Steam's has no C# support):

    $env:GODOT = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
    & $env:GODOT --path godot

## Controls

| Input | Action |
|---|---|
| Left click / drag | select / box-select (Shift adds) |
| Right click | context: move / attack / harvest |
| A + left click | attack-move |
| Tab | switch controlled player |
| Esc | cancel attack-move or build ghost |
| WASD / arrows / screen edge | pan camera |
| Mouse wheel | zoom steps |
| Space | pause sim (orders still queue) |
| F3 | toggle fog of war (debug) |
| Minimap click/drag | jump camera |

## Architecture

Sim-authoritative: `SimRunner` steps the sim at 10 ticks/s; `ViewSync` diffs
state by id; views interpolate at 60 fps. Floats exist only in `RenderMath`.
Spec: `docs/superpowers/specs/2026-06-10-godot-playable-slice-design.md`.

## Manual playtest checklist

1. Boot: 14 units, 4 buildings, 8 nodes, ridge. No errors.
2. Selection: click, box, shift-add, click-empty clears, Tab switches.
3. Move: group pathing around the ridge through gaps.
4. Attack: explicit attack chases; attack-move engages at gaps; death anims play.
5. Economy: Fabbers harvest (move-gather-return-deposit); node depletes and unblocks.
6. Build: ghost red on ridge/units/nodes; construction fill; depot raises supply.
7. Train: queue 5 max, progress bar, spawn adjacency, supply enforcement.
8. Buildings die: attack enemy barracks, fade-out, cells passable again.
9. Tank: slow, beefy, hard-hitting.
10. Pause (Space): freezes world, orders queue, unpause executes.
11. Minimap: dots track fights, camera rect, click-to-jump.
12. Performance: train ~30 units, big fight, hold 60fps.

## Tuning notes (fill during playtesting)

- [ ] unit speeds:
- [ ] attack rhythm:
- [ ] costs/build times:
