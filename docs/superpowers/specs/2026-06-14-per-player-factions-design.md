# Per-Player Factions — Design Spec (Plan 5b)

**Date:** 2026-06-14
**Status:** Approved (autonomous build authorized), pre-implementation
**Position:** Sub-project 2 of 5 in Plan 5 (CPU opponent + match flow). 5a match state ✅
→ **5b per-player factions** → 5c CPU framework + Easy → 5d Medium & Hard → 5e Godot
match flow / menu.

## Why this exists

Today `SimWorld` holds a single global `FactionDef? Faction`; every command that resolves
a def (Build/Train/Research) and the faction mechanic (shields) read from it. For a human
to play their chosen faction against a CPU running a *different* one (5e), the engine must
hold a faction **per player**. 5b is that refactor. It is behavior-preserving when all
players share one faction (so the determinism golden is unchanged), and only diverges when
factions actually differ — which is the new capability.

## Scope (5b)

**In scope (SimCore only):**
- Per-player faction storage + a `FactionFor(playerId)` accessor; a new constructor taking
  one faction per player; the existing single-faction constructor kept (applies that faction
  to every player) for backward compatibility.
- Route command resolution (`BuildCommand`/`TrainCommand`/`ResearchCommand`) and the
  mechanic accessors (`MechanicFor`/`InitialShield`/`UpdateShields`) against the **acting /
  owning player's** faction.
- Tests proving two different factions coexist; the shared-faction path stays byte-identical
  (determinism golden unchanged, no re-pin).

**Out of scope:** the menu/UI that lets a human *pick* a faction or scans `packs/` (5e); the
CPU (5c/5d); validating that each player's faction is internally consistent (that is
`FactionDef.Validate` / `PackValidator`, already built).

## API

On `SimWorld`:
```csharp
// storage (replaces the single `FactionDef? Faction` auto-property)
private readonly FactionDef?[] _factions;

/// <summary>The faction for a given player (null if that slot has none).</summary>
public FactionDef? FactionFor(int playerId) => _factions[playerId];

/// <summary>Player 0's faction. Back-compat alias for callers (e.g. the Godot HUD building
/// the human's catalog menus) that predate per-player factions; the human is player 0.</summary>
public FactionDef? Faction => _factions.Length > 0 ? _factions[0] : null;

// existing ctor — unchanged signature; now fills every player's slot with `faction`
public SimWorld(MapGrid map, ulong seed, int playerCount = 2, FactionDef? faction = null);

// new ctor — one faction per player; playerCount = factions.Length
public SimWorld(MapGrid map, ulong seed, FactionDef?[] factions);
```
The new ctor defensively copies the array. Both ctors initialize `_players` exactly as today.

## Resolution changes

In `SimWorld.Apply` (each command carries `PlayerId`):
- `BuildCommand`: `Faction?.GetBuilding(bc.BuildingDefId)` → `FactionFor(bc.PlayerId)?.GetBuilding(bc.BuildingDefId)`.
- `TrainCommand`: `Faction?.GetUnit(tc.UnitDefId)` → `FactionFor(tc.PlayerId)?.GetUnit(tc.UnitDefId)`.
- `ResearchCommand`: `Faction?.GetUpgrade(rc.UpgradeDefId)` → `FactionFor(rc.PlayerId)?.GetUpgrade(rc.UpgradeDefId)`.

In `SimWorld.Mechanics.cs` (a unit's mechanic is its **owner's** faction's mechanic):
- `MechanicFor(Unit u)` → `FactionFor(u.OwnerId)?.Mechanic`.
- `InitialShield()` → `InitialShield(int ownerId)` using `FactionFor(ownerId)?.Mechanic`; the
  `Spawn(...)` call site passes the unit's `ownerId`.
- `UpdateShields()` loses the single global guard/`m`; instead it iterates units and, per
  unit, reads `MechanicFor(u)` — `continue` when that unit's owner has no shield mechanic,
  else apply that owner's `MaxShield`/`RegenPerTick`/`RegenDelayTicks`. (Per-unit logic is
  otherwise identical to today.)

Prerequisite checks (`PrerequisitesMet`/`Has`) are already per-player (they inspect that
player's own buildings/upgrades) and do **not** change — only def *resolution* becomes
per-player.

## Determinism

Factions are immutable construction-time configuration, not mutated during a match — like the
map seed. They are therefore **not** hashed; their *effects* (spawned units, applied upgrades,
shield state) already are. When every player shares one faction (the determinism scenario and
`TestMap` both do), `FactionFor(p)` returns that same faction for all `p`, and the per-unit
`UpdateShields` rewrite processes the exact same units with the exact same mechanic as the
old global version — so the 500-tick trajectory is byte-identical. **No `StateHasher` change,
no golden re-pin.** The determinism gate confirms the golden is unchanged at
`9352778236967924871UL` (a green golden after the `UpdateShields` rewrite is the proof the
rewrite preserved behavior).

## Testing

A new `tests/SimCore.Tests/PerPlayerFactionTests.cs`:
- **`FactionFor` returns each player's faction**; `Faction` aliases `FactionFor(0)`.
- **Shared-faction ctor** still applies the one faction to every player slot.
- **Build resolution is per-acting-player:** in a 2-faction world, player 0 builds faction
  A's building and player 1 builds faction B's building (each from its own catalog), but
  player 0 building B's def id is rejected (A's catalog lacks it) — with the worker in range
  so the rejection is unambiguously the faction, not range.
- **Shields are per-owner:** faction A has `RegeneratingShields`, faction B has none; a unit
  owned by player 0 spawns with full shields and regenerates after being drained, while a
  player-1 unit spawns with 0 shields and never regenerates — proving `InitialShield`,
  `MechanicFor`, and `UpdateShields` all key off the owner.
- **Determinism:** full suite green in Debug + Release; the 3 determinism tests pass with the
  golden **unchanged** (no re-pin) — verifying the shared-faction path is byte-identical.

## Decisions Log

- Per-player faction storage (`FactionDef?[]`); `FactionFor(playerId)` accessor; `Faction`
  kept as a player-0 alias for back-compat (Godot HUD, human = player 0).
- Existing single-faction ctor retained (fills all slots) → all current callers/tests work
  unchanged; new array ctor added for per-player games.
- Resolution (Build/Train/Research) keys off `cmd.PlayerId`; mechanic accessors key off
  `unit.OwnerId`. Prereq checks unchanged.
- Factions are immutable config → not hashed; shared-faction path is behavior-preserving →
  **golden unchanged, no re-pin**. The `UpdateShields` rewrite's correctness is gated by the
  golden staying green.
