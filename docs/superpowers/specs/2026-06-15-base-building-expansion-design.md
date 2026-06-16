# Base-Building Expansion — Supply Building, Defense Tower, Textured Ground — Design Spec

**Date:** 2026-06-15
**Status:** Approved (design), pre-implementation.
**Context:** Reference faction currently has two buildings (Depot 2×2, Barracks 2×2). User wants
(1) a buildable **supply** structure, (2) a buildable **defense tower** that auto-fires, and
(3) **textured ground**. The art pipeline (nano-banana `gemini-3-pro-image` + `scripts/keysprite.py`)
is working; buildings render real sprites via `BuildingView` (loads `res://assets/buildings/<defId>.png`).

## Goal

Give players a more complete RTS base: a cheap supply structure to raise the cap, a static
defensive turret that shoots, and a real ground texture instead of flat tan.

## Decomposition & order (easy → hard)

1. **W1 — Supply building** (SimCore content + art). No new mechanic.
2. **W2 — Textured ground** (render-only + 1 art tile).
3. **W3 — Defense tower** (SimCore *building-combat* feature + content + art). The meaty one.

Each can ship independently. W3 is last because it adds a new sim mechanic.

## Determinism stance (the whole point)

**All three are designed to leave the golden trajectory hash `1571756151672809223UL` UNCHANGED —
no re-pin.**
- W1/W2 don't touch hashed sim behavior (the golden determinism scenario builds its own world
  from its own specs, not `ReferenceFaction`; ground is render-only).
- W3 adds building combat but makes it a **no-op for weaponless buildings** and hashes the new
  cooldown **only when a weapon is present** — so the towerless golden scenario is byte-identical
  (same keystone trick as team-play). The Debug+Release gate verifies; if the golden moves, the
  no-op wasn't clean — fix it, don't re-pin.

---

## W1 — Supply building ("Supply Silo")

- **`ReferenceSpecs.SupplySilo`**: `BuildingSpec(MaxHp: 200, Width: 2, Height: 2, MineralCost: 100,
  BuildTimeTicks: 120, SupplyProvided: 8, IsDepot: false, CanTrain: false, SightRange: 5)`.
- **`ReferenceFaction`**: add `new BuildingDef("supply", tier 1, None, ReferenceSpecs.SupplySilo)`.
- Workers build it with the existing `BuildCommand` (supply granted on construction-complete —
  existing mechanic). No sim change beyond the new def.
- **CPU AI:** the supply helper (`SupplyDef`) currently resolves the building with `SupplyProvided>0`;
  make it prefer a **cheaper, non-depot** supply building when one exists (so the CPU builds the
  Silo, not a second Depot, for supply). Behavior-preserving when no such building exists.
- **Art:** nano-banana sprite `godot/assets/buildings/supply.png` (industrial supply silo/generator,
  reads smaller/lesser than the Depot; depot as `--ref`). `BuildingView` loads it automatically by
  def-id; no Godot code change.
- **Packs:** regenerate `packs/reference/faction.json` (drift-guard test) + update building-count
  assertions in `ReferenceSpecsTests`/faction tests.
- **Determinism:** golden untouched (ReferenceFaction not in the golden scenario). Headless tests:
  building the silo grants +8 cap; CPU prefers it for supply.

## W2 — Textured ground

- Generate one **seamless, tileable** ground texture (nano-banana, sci-fi cracked-dirt/rockcrete to
  match the buildings; explicitly prompt "seamless tileable, edges wrap"). Opaque tile (NOT magenta-
  keyed) → `godot/assets/world/ground.png` (e.g. 256×256, power-of-two for tiling).
- **`MapView._Draw`:** replace the flat tan `DrawRect` fill with the ground texture tiled across the
  map. Use Godot texture repeat (set the texture's repeat + `DrawTextureRect(..., tile: true)` over
  the whole map rect, or per-cell). Keep the existing grid lines (lighter/optional), the terrain-rock
  squares, and the building/node cell-skip logic from the last fix.
- **Determinism:** none (render-only).
- **Risk:** seamless tiling can show seams if the generated tile doesn't wrap; iterate the prompt /
  use a mild edge blend if needed. Acceptance = no obvious grid seams at 1× zoom.

## W3 — Defense tower ("Sentry Turret") — building-combat feature

### Mechanic (SimCore, TDD, golden-safe)
- **`BuildingSpec.Weapon`**: add an optional `Weapon?` (default null). Weaponless buildings behave
  exactly as today.
- **`Building`**: when a building with a weapon is placed, clone the weapon (no aliasing, like units)
  and track `CooldownRemaining`.
- **`Step` pipeline:** add a building-combat pass (after unit combat). For each building that is
  **complete, alive, and has a weapon**: decrement cooldown; if ready, acquire the nearest enemy
  (unit preferred, then building) that is `!SameTeam`, within `Weapon.Range`, and visible to the
  building's team (reuse the unit `AcquireTarget` rules / extract a shared helper keyed on a world
  position + owner). If found, apply damage via the existing shield-aware `ApplyDamage` and reset
  cooldown. Buildings never move/chase — they only fire when a target is in range.
- **`StateHasher`:** fold each weaponed building's `CooldownRemaining` into the hash **only when the
  building has a weapon** (weaponless buildings contribute nothing new). Golden scenario has no
  weaponed buildings → hash unchanged → **no re-pin**.
- **Tower spec:** `ReferenceSpecs.SentryTurret = BuildingSpec(MaxHp: 250, Width: 2, Height: 2,
  MineralCost: 150, BuildTimeTicks: 180, SupplyProvided: 0, IsDepot: false, CanTrain: false,
  SightRange: 9, Weapon: <Damage ~12, Range ~6 cells, CooldownTicks ~8>)`. `ReferenceFaction`:
  `new BuildingDef("tower", tier 1, requires Depot, ReferenceSpecs.SentryTurret)`.
- **CPU:** does NOT build towers in v1 (its build order is unchanged). Noted as a future tweak.
- **Art:** nano-banana `godot/assets/buildings/tower.png` (armored turret with a barrel; depot as
  `--ref`). Loads via `BuildingView` by def-id.
- **Optional polish (not required for v1):** a brief muzzle-flash / line-to-target in `BuildingView`
  when the tower fires. Skip if it complicates the first version.

### Determinism (W3)
- Behavior-preserving for towerless worlds → golden `1571756151672809223UL` unchanged (gate-verified).
- New headless tests: a tower damages an enemy in range; ignores allies (team-aware) and out-of-range
  / fogged targets; a CPU-vs-CPU determinism replay of a world containing towers is reproducible.

## Testing

- **W1:** silo grants supply; CPU supply-pref; faction drift-guard; building counts.
- **W2:** Godot build clean (render-only, playtested for seams).
- **W3:** building weapon clone (no aliasing); building fires on cooldown at nearest enemy in range;
  ally-immune; fog-gated; shield-aware damage; golden unchanged Debug==Release; tower-world
  determinism replay.
- **Whole:** Release == Debug full suite; golden `1571756151672809223UL` unchanged across all three.

## Decisions Log

- Simple early turret (prereq Depot, ~150 min, 250 HP, ~6 range), cheap supply-only Silo (~100 min,
  +8, Depot keeps its supply), textured ground.
- Building combat is a new SimCore mechanic but **golden-safe** (no-op + unhashed for weaponless
  buildings) → no re-pin, like the team-play keystone.
- CPU does not build towers in v1; muzzle-flash visual optional.
- Order: supply → ground → tower.
