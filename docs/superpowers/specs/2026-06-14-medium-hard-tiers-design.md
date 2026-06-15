# CPU Medium & Hard Tiers — Design Spec (Plan 5d)

**Date:** 2026-06-14
**Status:** Approved (autonomous build authorized), pre-implementation
**Position:** Sub-project 4 of 5 in Plan 5. 5a match state ✅ → 5b per-player factions ✅
→ 5c CPU framework + Easy ✅ → **5d Medium & Hard tiers** → 5e Godot match flow/menu.

## Why this exists

5c shipped the deterministic CPU framework + the Easy tier. 5d fills in the other two
rungs of the difficulty ladder so a player can pick a real challenge: **Medium** (stronger,
earlier, sustained pressure + rebuilds) and **Hard** (reactive macro — commits when ahead,
defends when threatened). Both play **fair** (same rules, no resource bonuses — smarter, not
cheating) and remain **stateless** (decisions recomputed each tick), so there is **no new
hashed state and no golden re-pin**.

## Scope (5d)

**In scope (SimCore only):**
- Refactor `EasyDecide` into shared, parameterized helpers (behavior-preserving) so all three
  tiers compose them: economy macro, combat training, attack.
- `RebuildProduction` (rebuild a destroyed combat-producer building) shared by Medium/Hard.
- `MediumDecide` and `HardDecide`; `UpdateAi` dispatches on `Difficulty`.
- Hard's reactive layer: defend a threatened base, commit only when at-or-ahead of the enemy army.
- Per-tier tunable constants; CPU-vs-CPU determinism tests for each tier.

**Out of scope:** Godot UI / difficulty selection (5e); any cheating/bonus tier (all fair);
fog-limited AI vision (the AI reads full hashed state, as in 5c); new hashed state (tiers stay
stateless → golden unchanged).

## Refactor (behavior-preserving)

Extract `EasyDecide`'s steps into reusable helpers in `SimWorld.Ai.cs`, then rewrite
`EasyDecide` to call them — byte-identical behavior (verified by the existing 5c Easy tests +
the CPU-vs-CPU determinism test staying green):
- `MacroEconomy(p, workerCap, supplyBuffer)` — train workers up to `workerCap` (guard counts
  `CountUnits + QueuedUnits`), send idle workers to the nearest node, build supply when
  `SupplyUsed >= SupplyCap - supplyBuffer` (via `BuilderWorker` + `FreeFootprintNear`).
- `TrainCheapestCombat(p)` — train `CombatDef(p)` from its producer when minerals + supply allow.
- `MaybeAttack(p, threshold, interval)` — when `CountUnits(p, combat) >= threshold` and
  `Tick % interval == 0`, attack-move all combat units at `EnemyBaseCenter(p)`.

`EasyDecide(p)` = `MacroEconomy(p, EasyWorkerCap, EasySupplyBuffer)` → `TrainCheapestCombat(p)`
→ `MaybeAttack(p, EasyAttackThreshold, EasyAttackInterval)` (same order/params as today).

## New shared helper: RebuildProduction

```
RebuildProduction(p): if p owns no building (any construction state) whose DefId ==
CombatDef(p).ProducedBy, and p can afford it and its Requires are met and an idle/any worker
exists, BuildCommand that producer building at a placeable footprint near the worker.
```
(Destroyed *supply/worker* producer — the depot — is already rebuilt by `MacroEconomy`'s supply
step, since the depot has `SupplyProvided > 0` and is the worker producer. `RebuildProduction`
covers the combat producer, e.g. the barracks.)

## Medium tier

```
MediumDecide(p):
  MacroEconomy(p, MediumWorkerCap, MediumSupplyBuffer)   // bigger eco, supply kept further ahead
  RebuildProduction(p)                                    // replace a lost barracks
  TrainCheapestCombat(p)
  MaybeAttack(p, MediumAttackThreshold, MediumAttackInterval)  // lower threshold + shorter interval
```
Net: Medium out-economies Easy, never gets stuck without production, and applies earlier,
more frequent pressure. Defaults: `MediumWorkerCap=10`, `MediumSupplyBuffer=3`,
`MediumAttackThreshold=4`, `MediumAttackInterval=150`.

## Hard tier (reactive)

```
HardDecide(p):
  MacroEconomy(p, HardWorkerCap, HardSupplyBuffer)
  RebuildProduction(p)
  TrainCheapestCombat(p)
  ReactiveAttack(p)
```
`ReactiveAttack(p)`:
1. **Defend** — if `ThreatenedBuildingCenter(p)` is non-null (an enemy combat unit within
   `HardDefendRadius` cells of any of p's buildings), attack-move the whole army to that
   building's center and return.
2. **Commit when ahead** — else if `CountUnits(p, combat) >= HardMinArmy` AND
   `CountUnits(p, combat) >= EnemyCombatCount(p)` AND `Tick % HardAttackInterval == 0`,
   attack-move the army at `EnemyBaseCenter(p)`. (Holds/keeps massing when behind.)

New helpers: `ThreatenedBuildingCenter(p)` (first own building, stable order, with an enemy
combat unit within radius² — deterministic) and `EnemyCombatCount(p)` (count of all non-p
units with a `Weapon`). Defaults: `HardWorkerCap=14`, `HardSupplyBuffer=4`, `HardMinArmy=8`,
`HardAttackInterval=120`, `HardDefendRadius=10`.

## Dispatch

`UpdateAi`'s per-player switch becomes:
```
case AiDifficulty.Medium: MediumDecide(p); break;
case AiDifficulty.Hard:   HardDecide(p);   break;
default:                  EasyDecide(p);   break; // Easy
```

## Determinism

All three tiers are stateless (no AI memory), integer/`Fix`-only, no RNG, iterate the stable
`_units`/`_buildings`/`_nodes`/`_players` lists with first-wins tiebreaks, and mutate only via
`Apply`. **No new hashed state → golden trajectory hash unchanged at `1571756151672809223UL`
(no re-pin).** Each tier is proven reproducible by a CPU-vs-CPU two-run identical-hash test.

## Testing

- **Refactor:** all existing 5c Easy tests + the Easy CPU-vs-CPU determinism test pass unchanged
  after extraction (behavior preserved).
- **Medium attacks sooner than Easy:** in matched setups, the Medium CPU issues an attack-move
  with fewer combat units / earlier than Easy's threshold would allow.
- **Medium rebuilds:** destroy a Medium CPU's only barracks → it builds a new combat producer.
- **Hard defends:** with an enemy combat unit parked next to a Hard CPU's base, its army
  attack-moves toward the threatened building (not off across the map).
- **Hard commits only when ahead:** a Hard CPU with a large army and no enemy army attacks; with
  a larger enemy army present it holds (no attack-move issued that tick).
- **Determinism:** Medium-vs-Medium and Hard-vs-Hard two-run identical-hash tests; full golden
  unchanged; replay tests green; Debug == Release.

## Decisions Log

- Easy refactored into shared parameterized helpers (`MacroEconomy`/`TrainCheapestCombat`/
  `MaybeAttack`); behavior preserved (gated by existing Easy tests).
- `RebuildProduction` shared by Medium/Hard; depot/supply rebuild already handled by
  `MacroEconomy`'s supply step.
- Medium = bigger eco + rebuild + earlier/sustained attacks (lower threshold/shorter interval).
- Hard = reactive: defend threatened bases, commit only at-or-ahead of the enemy army, else mass.
- All tiers fair (no bonuses) and stateless → no hashed state, no golden re-pin; each proven by
  a CPU-vs-CPU determinism test.
