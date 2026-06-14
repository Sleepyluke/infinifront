# Faction Mechanics Framework + Regenerating Shields — Design Spec (Plan 3c)

**Date:** 2026-06-13
**Status:** Approved design, pre-implementation
**Position:** Sub-project 3 of 4 in the faction-pack arc (tiers ✅ → upgrades ✅ → **mechanics** → pack format)

## Why this exists

Faction packs (3d) must let a faction declare a signature mechanic — the thing that
gives a faction its identity (Protoss shields, Zerg regeneration, etc.). This plan
adds the mechanic vocabulary to the engine: a `FactionDef` mechanic selector plus
one concrete exemplar (regenerating shields) that establishes the integration
pattern for future mechanics.

## Scope decisions (approved)

- **Exemplar = regenerating shields** — exercises every real extension point: a
  per-tick Step hook, hashed per-unit state, and combat-damage integration.
- **Framework = selector + exemplar integration, NOT a general event-bus** (YAGNI).
  One mechanic per faction. Adding mechanic #2 later = new `MechanicKind` value +
  handling at the relevant integration points.
- **Units only** (shields don't apply to buildings this pass).
- **Shared faction for now**: `SimWorld` holds one `FactionDef`, so the mechanic
  applies to all units. Read via a `MechanicFor(unit)` accessor so it becomes
  per-player cleanly when packs let each player pick a faction (plan 5).

## Out of scope

- Per-player factions (plan 5), shields-on-buildings, multiple simultaneous
  mechanics, any mechanic other than shields, JSON/pack format (3d).

## Data shape

Extend `FactionDef` (additive ctor overload, mirroring the upgrades catalog):
```csharp
enum MechanicKind { None = 0, RegeneratingShields = 1 }

sealed record MechanicDef(
    MechanicKind Kind,
    int MaxShield, int RegenPerTick, int RegenDelayTicks);
// FactionDef.Mechanic : MechanicDef?  (null = no mechanic)
```
A faction with no mechanic has `Mechanic == null`. The existing 5-arg `FactionDef`
ctor (id, name, units, buildings, upgrades) keeps working via a new 6-arg overload
defaulting `mechanic: null`.

## Per-unit state

`Unit` gains:
```csharp
public int ShieldHp { get; set; }
public int TicksSinceDamaged { get; set; }
```
At spawn, if the unit's faction has `Kind == RegeneratingShields`, `ShieldHp` is
initialized to `MaxShield`; otherwise 0. Both fields are hashed (Task 5).

## Mechanic accessor

```csharp
private MechanicDef? MechanicFor(Unit u);  // today: Faction?.Mechanic (shared);
                                           // later: per-owner faction lookup
private bool HasShields(Unit u);           // MechanicFor(u)?.Kind == RegeneratingShields
```

## Combat integration

A damage helper routes through shields:
```csharp
private void ApplyDamage(Unit target, int amount)
// toShield = min(ShieldHp, amount); ShieldHp -= toShield;
// Hp -= (amount - toShield); TicksSinceDamaged = 0;
```
`UpdateCombat`'s unit-damage line (`targetUnit.Hp -= EffectiveDamage(u)`) becomes
`ApplyDamage(targetUnit, EffectiveDamage(u))`. Building damage stays direct (units
only). For a non-shield unit `ShieldHp == 0`, so `ApplyDamage` is byte-identical to
the old direct-HP subtraction — the determinism golden is unaffected until a
scenario uses shields.

## Regen hook (the framework's per-tick extension point)

`UpdateShields()` runs in `Step`, after `UpdateResearch` and before `RemoveDead`:
for each unit with shields, `TicksSinceDamaged++`; if `TicksSinceDamaged >=
RegenDelayTicks` and `ShieldHp < MaxShield`, `ShieldHp = min(MaxShield, ShieldHp +
RegenPerTick)`. Units without the mechanic are skipped → no-op for today's
factions, so the golden holds until a shield scenario exists.

This is where future per-tick mechanics hook in; the method dispatches on
`MechanicKind`.

## Hashing

`StateHasher` v5 folds per-unit `ShieldHp` + `TicksSinceDamaged` (after the
existing unit fields). The `MechanicDef` itself is static faction data (not hashed
per tick). Adding the two fields grows the hash function → re-pin once, combined
with the scenario change (Task 5), exactly as 3b-7 did.

## Validate()

`FactionDef.Validate()` checks mechanic params when `Mechanic != null`:
`MaxShield >= 0`, `RegenPerTick >= 0`, `RegenDelayTicks >= 0` (a `None`-kind
mechanic with nonzero shield params is flagged as inconsistent).

## Error handling

- A null `Mechanic` means no mechanic — every shield path is skipped.
- Negative/invalid params surface via `Validate()` at load/test time, never
  mid-Step.

## Testing

- **MechanicDef/selector:** FactionDef exposes `Mechanic`; additive ctor keeps old
  call sites; `Validate()` flags bad params and a None-kind-with-params.
- **Shield spawn init:** units of a shield faction spawn with `ShieldHp ==
  MaxShield`; units of a non-shield faction spawn with 0.
- **Damage routing:** damage below shield reduces only `ShieldHp`; damage above
  shield spills to `Hp`; a hit resets `TicksSinceDamaged`; non-shield units take
  damage straight to `Hp` (unchanged).
- **Regen:** shields don't regen before `RegenDelayTicks`; regen by `RegenPerTick`
  after the delay; caps at `MaxShield`; a unit damaged each tick never regens.
- **Hashing:** `ShieldHp` and `TicksSinceDamaged` changes move the hash; identical
  worlds hash equal.
- **Determinism:** scenario v7 gives the scenario faction the shield mechanic;
  shields absorb + regen mid-run; replay tests pass; golden re-pinned in-commit.
- **Godot:** shield bar renders above the HP bar when `ShieldHp > 0`; project
  builds.

## Decisions Log

- Selector + exemplar integration, not a speculative hook-bus (YAGNI).
- Regenerating shields chosen as the exemplar (exercises hook + state + combat).
- Units only; shared faction via `MechanicFor()` for forward per-player compat.
- Shield state hashed; `MechanicDef` static/unhashed; one combined re-pin in Task 5.
- Damage routes through a single `ApplyDamage` helper (units); buildings direct.
