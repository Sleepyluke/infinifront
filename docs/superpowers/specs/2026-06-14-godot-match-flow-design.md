# Godot Match Flow & Menu — Design Spec (Plan 5e)

**Date:** 2026-06-14
**Status:** Approved (autonomous build authorized), pre-implementation
**Position:** Sub-project 5 of 5 (final) in Plan 5. 5a match state ✅ → 5b per-player factions
✅ → 5c CPU framework + Easy ✅ → 5d Medium & Hard ✅ → **5e Godot match flow / menu**.

## Why this exists

Everything for a playable 1v1-vs-CPU now exists in the deterministic core (match outcome,
per-player factions, three CPU tiers). 5e is the Godot capstone that exposes it: a start menu
to choose your faction, the CPU's faction, and a difficulty; the match itself; and a
Victory/Defeat screen with Restart. After this, Plan 5 is done and the game is a complete
single-player experience: author a faction with the Forge, then fight a CPU with it.

## Testability reality (important)

The Godot UI (menu, overlay, scene wiring) **cannot be headless-tested** — there is no Godot
runtime in CI. So 5e is split deliberately:
- **Testable seams** live in `SimCore`/`SimCore.Packs` (no Godot types) and get real xUnit
  tests run headless: faction-catalog loading and match construction.
- **Godot-only UI** is kept thin and is **compile-checked** by the full-solution build
  (`dotnet build godot/LlmRts.Godot.csproj`, which CI also builds) and **verified by the user
  playtesting** via `scripts/play.ps1`. A playtest checklist is provided.

This maximizes the logic that's automatically verified and isolates the un-testable surface.

## Scope (5e)

**In scope:**
- `PackCatalog` (SimCore.Packs): load the in-code reference faction + every valid pack under a
  directory, via `FactionPackLoader`. Headless-tested.
- `MatchSetup` (SimCore): `BuildStandard1v1(human, cpu, difficulty, seed)` — a standard 1v1
  `SimWorld` (per-player factions, `SetCpu(1, difficulty)`, role-resolved starting bases +
  workers + mineral nodes for both players). Headless-tested. `TestMap` delegates to it.
- Godot: a `MenuScreen` (pick your faction, the CPU's faction, difficulty; Play), a
  `GameOverScreen` (Victory/Defeat/Draw + Restart + Menu), and `Main.cs` wiring (boot → menu
  paused → Play builds the match → overlay on game-over → Restart/Menu).

**Out of scope:** map selection (single standard map for v1); 3+ players / teams; in-game pack
import/authoring UI; netplay; any change to SimCore determinism (5e adds no hashed state; the
golden is untouched).

## Testable seam 1 — `PackCatalog` (SimCore.Packs)

```csharp
public sealed record FactionEntry(string Name, FactionDef Faction);
public static class PackCatalog
{
    /// <summary>The in-code reference faction plus every pack under packsDir that loads with no
    /// errors (each packsDir/<name>/faction.json via FactionPackLoader). Deterministic order:
    /// reference first, then packs by directory name (Ordinal). Invalid packs are skipped.</summary>
    public static IReadOnlyList<FactionEntry> Load(string packsDir);
}
```
- Uses `System.IO` (works under Godot's .NET) to enumerate `packsDir`'s subdirectories, read
  each `faction.json`, and `FactionPackLoader.LoadFromJson` it; include only entries with a
  non-null faction and no load errors. Always includes `ReferenceFaction.Def` first (named
  "Reference"). A missing/empty `packsDir` yields just the reference entry (never throws).
- Headless test: `PackCatalog.Load(<repo>/packs)` contains "reference" and loads clean.

## Testable seam 2 — `MatchSetup` (SimCore)

```csharp
public static class MatchSetup
{
    /// <summary>Standard 1v1: player 0 = human (humanFaction), player 1 = CPU (cpuFaction) at the
    /// given difficulty. Each side gets a role-resolved starting base (a depot/supply building +
    /// a train-capable producer), starting workers, and a nearby mineral node, on a fixed map.
    /// Deterministic; uses only SimCore types.</summary>
    public static SimWorld BuildStandard1v1(FactionDef humanFaction, FactionDef cpuFaction,
                                            AiDifficulty difficulty, ulong seed);
}
```
- Builds a fixed-size map, constructs `new SimWorld(map, seed, new FactionDef?[]{ humanFaction,
  cpuFaction })`, calls `SetCpu(1, difficulty)`, and for **each** player resolves roles from
  *that player's* faction (depot = first `IsDepot` building else first building; producer =
  first `CanTrain` building; worker = first unit with a `Harvester`) and places a base at the
  player's corner (`AddCompletedBuilding`), a few workers (`SpawnUnit`), and a mineral node
  (`AddResourceNode`) plus starting minerals. Null-safe: a faction missing a role just skips
  that piece (the 3d-2 validator guards structural minimums separately).
- `godot/scripts/TestMap.cs` becomes a thin wrapper: `MatchSetup.BuildStandard1v1(reference,
  reference, AiDifficulty.Easy, 42)` (so the existing sandbox boots with an Easy CPU opponent
  until the menu drives it).
- Headless tests: both players get a base + workers; `FactionFor(0)==human`, `FactionFor(1)==
  cpu`; `Players[1].Controller==Cpu` at the chosen difficulty; the match starts `InProgress`.

## Godot UI (compile-checked + playtested)

All three follow the existing in-code `CanvasLayer`/`Control` style of `Hud.cs` (no `.tscn`
UI). `SimRunner` already has `Paused`, `World`, `Init(world)`, and the `Ticked` event.

- **`MenuScreen` (CanvasLayer):** title; a "Your faction" selector and a "CPU faction" selector
  (buttons or an `OptionButton` listing `PackCatalog.Load(packsDir)` names); three difficulty
  buttons (Easy/Medium/Hard); a Play button. On Play, invokes a callback with (humanFaction,
  cpuFaction, difficulty). Shown at boot with the sim paused.
- **`GameOverScreen` (CanvasLayer):** subscribes to `SimRunner.Ticked`; when `World.Phase ==
  Over`, sets `Runner.Paused = true` and shows "Victory!" (`WinnerId == 0`), "Defeat"
  (`WinnerId == 1`), or "Draw" (`WinnerId == -1`), with Restart (same config) and Menu buttons.
- **`Main.cs`:** on `_Ready`, build the world (via `MatchSetup`, defaulting through `TestMap`)
  but start with the `MenuScreen` shown and `Runner.Paused = true`. Play → rebuild the world
  with the chosen config, `Runner.Init(world)`, hide the menu, `Paused = false`. Game-over →
  `GameOverScreen`. Restart → rebuild with the last config. Menu → show `MenuScreen` again.
  A `RebuildMatch(config)` helper centralizes world construction + `Runner.Init` + clearing
  selection/camera.
- **Input freeze:** when the match is `Over`, `Runner.Paused = true` stops the sim; the
  overlay's buttons still work (UI input isn't gated by pause).

## Determinism

5e adds **no** hashed sim state and changes no sim behavior — `MatchSetup` only composes
existing construction calls; the Godot UI only reads `Phase`/`WinnerId` and calls existing
setup/`Enqueue`. The golden trajectory hash is untouched (`1571756151672809223UL`). The full
SimCore determinism suite stays green; the new SimCore tests are ordinary unit tests.

## Testing

- **`PackCatalog`** (headless): `Load(<repo>/packs)` includes a "reference" entry whose faction
  has the reference units/buildings and no load errors; a non-existent dir yields just the
  reference entry; a dir with a malformed `faction.json` skips it (still returns reference).
- **`MatchSetup`** (headless): `BuildStandard1v1(ReferenceFaction.Def, ReferenceFaction.Def,
  Hard, seed)` → both players own ≥1 building and ≥1 worker; `FactionFor`/`Controller`/
  `Difficulty` correct; `Phase == InProgress` at start; stepping a few ticks doesn't throw.
  A different-faction case (a tiny second test faction for the CPU) → `FactionFor(1)` is that
  faction and its role-resolved base is placed.
- **Godot build:** `dotnet build godot/LlmRts.Godot.csproj` succeeds (compile-check of the UI).
- **Manual playtest (user, via `scripts/play.ps1`):** menu appears; pick factions + difficulty;
  Play starts a match vs the CPU; the CPU plays (workers, army, attacks per tier); destroying
  the CPU's last building shows Victory; losing yours shows Defeat; Restart and Menu work.

## Decisions Log

- Split into headless-testable SimCore seams (`PackCatalog`, `MatchSetup`) + thin Godot UI
  (compile-checked + playtested). Honest about the un-testable UI surface.
- `MatchSetup.BuildStandard1v1` resolves starting roles per player's own faction (works for any
  pack); `TestMap` delegates to it (sandbox now boots with an Easy CPU).
- Menu: pick BOTH factions (yours + CPU's) from the catalog + a difficulty; single standard map.
- Game-over: poll `Phase`/`WinnerId`, pause the sim, overlay Victory/Defeat/Draw + Restart/Menu.
- No determinism impact; golden untouched.
