# Godot Playable Slice — Design

**Date:** 2026-06-10
**Status:** Draft — pending user review
**Reorders the roadmap:** this is Plan 4 (presentation layer) pulled ahead of
Plan 2c (fog of war) and Plan 3 (faction packs), scoped down to a first
playable sandbox. Fog and packs remain on the roadmap unchanged.

## Goal

A playable sandbox where the user commands units on a real map with real
sprites: select, move, attack, build, train, harvest — to evaluate how the
game *feels* (speeds, attack rhythm, pathing, UI readability) and feed tuning
back into the sim's placeholder constants.

Explicit non-goals: fog of war, CPU opponent, sound, saving, multiplayer,
faction pack import, menus/settings. One hardcoded map, one hardcoded faction.

## Architecture: sim-authoritative view sync

The sim stays exactly as tested (headless, deterministic, Godot-free). Godot
is a *viewer and command source*:

```
input (mouse/keys) ──> CommandQueue ──> SimWorld.Step(commands)   ← fixed 10 ticks/s
                                              │
                            view-sync diff (by entity id)
                                              │
                       Godot nodes (sprites, HUD) + interpolation  ← 60 fps
```

- **Fixed tick loop:** accumulator in `_Process`; every 100 ms the queued
  commands are drained into `SimWorld.Step`. Pause key (Space) stops stepping;
  commands still queue.
- **Interpolation:** view keeps each unit's previous-tick and current-tick
  positions; render position = lerp by accumulator fraction. Fix→float
  conversion happens only here, at the render boundary — floats never enter
  the sim.
- **View sync:** after each tick, diff sim state against live nodes by id.
  New id → instantiate scene; missing id → death/destruction playback, then
  free. No sim-side events, no sim modifications for rendering.
- **Death playback:** the sim removes dead entities the same tick; the view
  detects disappearance and plays the death row (units) or swaps the wreck
  image (buildings) on a short-lived corpse node, then frees it.

## Project layout

```
llm-rts/
├── godot/                      # Godot 4.6 .NET project (presentation only)
│   ├── project.godot
│   ├── LlmRts.Godot.csproj     # references ../src/SimCore/SimCore.csproj
│   ├── assets/                 # sliced sheets, icons, tiles (slicer output)
│   ├── scenes/                 # Main, UnitView, BuildingView, NodeView, HUD
│   └── scripts/                # C# (see components)
├── tools/SpriteSlicer/         # console app: raw sheet + sidecar JSON → contract sheet
│   └── sidecars/*.json         # hand-authored rects per raw sheet
└── src/SimCore/                # UNTOUCHED except additions listed below
```

**Sim-side additions (all data/API, no behavior changes):**
- `ReferenceSpecs` static class: Fabber, Trooper, Outrider, Tank, Depot,
  Barracks with tuning constants in one reviewable file (precursor of the
  plan-3 faction pack).
- Tank = new `UnitSpec` (slow, high HP/damage). Specs are data; no sim code.
- If small read-only accessors are needed for the view (none anticipated),
  they are properties only — never mutations.

**Components (one purpose each):**

| Component | Responsibility |
|---|---|
| `SimRunner` | tick accumulator, command queue, pause |
| `ViewSync` | id-diff, node lifecycle, corpse playback |
| `UnitView` | sprite animation state machine, facing, health bar, selection ring |
| `BuildingView` | static image per state (construction %, complete, wreck), fallback rectangles |
| `SelectionController` | click/box select, control of current player, Tab switch |
| `CommandController` | context right-click, A-move, build-ghost placement → Command objects |
| `CameraRig` | WASD/edge pan, stepped zoom, pixel snap |
| `Hud` | minerals/supply, selection panel, build/train buttons, queue display |
| `TestMap` | hardcoded 40×40 map: terrain tiles, rock walls, mineral lines, two start bases |

## Controls & HUD

As approved in brainstorming:

- Left-click select, left-drag box-select, Shift adds.
- Right-click context order: ground = move, enemy = attack, node = harvest
  (workers; others move). A+click = attack-move. Esc cancels pending mode.
- **Tab switches controlled player** (0 ↔ 1); selection clears; HUD badge
  shows side; camera stays put. This substitutes for the missing CPU opponent
  and doubles as a manual-testing tool.
- Camera: WASD/arrows + edge scroll; wheel zoom in fixed steps, pixel-snapped.
- HUD: top-right minerals + supply (used/cap) for controlled player.
  Bottom panel by selection: worker → build buttons (Depot, Barracks) with
  green/red ghost placement via the sim's placement check; barracks → train
  buttons (Trooper, Outrider, Tank) + queue icons with progress bar; any unit →
  HP/carry readout.
- Health bars only on damaged units; green/yellow/red steps.
- Feedback: destination flag tick on move, brief flash on attack target,
  selection circles sized by unit class. No sound.

## Coordinates & rendering

- 1 sim cell = 64 px world space. `FixVec` → `Vector2` at the boundary:
  `(float)x.Raw / Fix.One.Raw * 64`.
- Facing snapped to S/W/N/E from move direction or target bearing; E renders
  the W row flipped. Idle keeps last facing.
- Animation rows from sim state: move order → walk; attack fired this tick →
  one-shot attack row; otherwise idle. Death row on despawn.
- Z-order: y-sort so southern entities draw in front.

## Sprite pipeline

`tools/SpriteSlicer` (plain .NET console + ImageSharp):

- Input: raw Gemini sheet + hand-authored JSON sidecar: per row {animation,
  facing, frameCount, rect}, plus baseline offset and icon rect.
- Process: crop frames, chroma-key `#FF00FF` → alpha (tolerance for fringe),
  nearest-neighbor downscale to contract cell size, re-center on baseline.
- Output: contract-compliant sheet (brief §2 layout) + 64×64 icon into
  `godot/assets/`. Same format the plan-3 importer will consume.
- Units sliced now: trooper-v2, best fabber, outrider-v1, tank-v1.
- Buildings/terrain/node art arrives from brief #2 when ready; until then
  `BuildingView` renders the silhouette fallback (colored footprint rectangle
  + icon glyph + construction shading) — which also proves the spec's
  fallback path. Terrain falls back to flat color; node to a blue diamond.

## Error handling

- Malformed/missing sheet at slicer time: tool fails loudly with the rect
  that didn't fit; nothing half-written.
- Missing asset at game load: log once, use fallback visual; never crash.
- Commands referencing dead ids: already safe — the sim ignores them (tested).

## Testing

- **Sim:** existing 109 tests must stay green and untouched. Determinism CI
  unchanged (sim never references Godot).
- **New sim data (`ReferenceSpecs`, Tank):** spec-sanity tests (counts,
  budget-ish invariants like cost > 0, depot provides supply).
- **SpriteSlicer:** unit tests on synthetic images — chroma key tolerance,
  frame slicing math, baseline centering.
- **Godot layer:** no automated UI tests in this slice. Manual test checklist
  in the plan (selection, each order type, build/train/harvest loop, Tab,
  death playback, camera). A CI-ish smoke check: `godot --headless --import`
  + .NET build of the Godot csproj must succeed.

## Risks

- **Godot .NET + Steam-version confusion:** the machine has both; docs and
  scripts must always invoke the winget .NET binary
  (`Godot_v4.6.3-stable_mono_win64.exe`), never the Steam exe.
- **Gemini sheet irregularity:** sidecar-rect authoring is eyeball work;
  budgeted as its own plan task, and any sheet can ship as fallback art
  without blocking.
- **Feel tuning churn is the point:** speeds/cooldowns will change after
  hands-on play; they live in `ReferenceSpecs` so tuning = data edits +
  golden-hash re-pin in the same commit per protocol.
