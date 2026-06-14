# Faction Pack Validation & Authoring — Design Spec (Plan 3d-2)

**Date:** 2026-06-14
**Status:** Approved (autonomous build authorized), pre-implementation
**Position:** Sub-project 4 of the faction-pack arc, part 2 of 2 (3d-1 serialization ✅ → 3d-2 validation & Forge). This finishes the arc.

## Why this exists

3d-1 made a faction round-trip to/from JSON and runs the existing structural
`FactionDef.Validate()` (referential integrity + cycle detection + mechanic
params) on load. That catches *broken* packs. 3d-2 adds the layer that makes
LLM-authored packs *good*: a richer validator (playability + tier sanity + a
point-budget balance check) that emits machine-readable findings a fix-it loop
can act on, plus the **Faction Forge** authoring prompt that an external LLM uses
to generate a valid, balanced pack in the first place. After this, the whole
"author a faction as data with an LLM and import it" vision is expressible.

## Scope (3d-2)

**In scope:**
- A `PackValidator` (new, in `SimCore.Packs`) that takes a loaded `FactionDef`
  and returns a list of structured `ValidationFinding`s (Error/Warning + stable
  machine code + target id + human message).
- Structural/ playability checks (Errors): structural minimums, producer-
  reachability, empty/blank ids.
- Tier-sanity check (Warning): tier monotonicity.
- Point-budget balance check (Warnings): per-unit efficiency outliers vs the
  faction's own mean, using a tunable weighted power formula with a tolerance
  band that **warns, never rejects**.
- The Faction Forge prompt doc (`docs/faction-forge-prompt.md`): the authoring
  guide an external LLM is given, using `packs/reference/faction.json` as the
  worked example.
- A convenience entry point so callers get load + structural errors + validator
  findings together.

**Out of scope:** Godot in-game import UI (later); embedded sprites/assets in the
pack (text-only stays); per-player factions and multiplayer pack-content hashing
(plan 5); auto-repair execution (the validator only *describes* fixes via codes;
the Forge loop / a human applies them). **Not changing** `SimCore` (determinism
core) — `FactionDef.Validate()` stays the structural seed; the new checks live in
`SimCore.Packs` and do not touch the sim, so the golden hash is unaffected.

## Architecture

The validator is a separate pass, deliberately NOT folded into
`FactionDef.Validate()`:
- `FactionDef.Validate()` (SimCore) = structural/referential seed, the *minimum*
  for a loadable def. Stays as is.
- `PackValidator` (SimCore.Packs) = the *richer* author-facing report. Lives with
  the JSON layer because it's about pack quality, not sim correctness, and it may
  use `double` math freely (it never feeds the deterministic sim — findings are
  advisory text for the author). This keeps all float/heuristic code out of
  `SimCore`.

```csharp
namespace SimCore.Packs;

public enum Severity { Error, Warning }

public sealed record ValidationFinding(
    Severity Severity, string Code, string? TargetId, string Message);

public static class PackValidator
{
    // Full author-facing report over an already-loaded def. Does NOT re-run
    // FactionDef.Validate() (the loader already did) — these are the ADDITIONAL
    // playability/tier/balance checks layered on top.
    public static IReadOnlyList<ValidationFinding> Validate(
        FactionDef faction, BudgetWeights? weights = null);
}
```

`BudgetWeights` is a record of tunable coefficients with sensible defaults (see
Budget section). Passing `null` uses `BudgetWeights.Default`.

### Convenience entry point

So an import flow / Forge loop gets everything in one call:
```csharp
// in FactionPackLoader
public sealed record PackReport(
    FactionDef? Faction,
    IReadOnlyList<string> LoadErrors,        // from 3d-1 (structural Validate + parse)
    IReadOnlyList<ValidationFinding> Findings); // from PackValidator (empty if Faction null)

public static PackReport LoadAndValidate(string json, BudgetWeights? weights = null);
```
`LoadAndValidate` calls `LoadFromJson`; if `Faction` is non-null it also runs
`PackValidator.Validate` and includes the findings. If the load hard-failed
(`Faction == null`), `Findings` is empty (nothing to balance-check). This leaves
the existing `LoadFromJson` untouched (some callers only want load).

## Checks

### 1. Structural minimums (Error)

A faction must be *playable*:
- `NO_BUILDINGS` — `BuildingList` empty.
- `NO_UNITS` — `UnitList` empty.
- `NO_SEED_UNIT` — no unit is buildable from game start (see reachability). A
  faction where nothing can be trained at t0 is unplayable.

(Note: we do NOT require an `IsDepot` building or a `CanTrain` building
explicitly — those are conventions the engine/economy use, and a creative pack
might differ. `NO_SEED_UNIT` is the real playability gate. Whether the engine's
*start* placement needs a depot is an engine concern, flagged separately only if
it becomes load-bearing; for 3d-2 we keep the data-level playability check.)

### 2. Producer-reachability (Error)

Compute the set of **reachable buildings**: a building is reachable iff every id
in its `Requires` is reachable (base case: empty `Requires`). This is a fixpoint
over `BuildingList` (cycle-safe: cycles were already rejected by
`FactionDef.Validate()`, but compute defensively with a visited set so a cycle
can't infinite-loop here).
- A unit is **buildable** iff its `ProducedBy` building is reachable AND every id
  in the unit's `Requires` is reachable (a require may be a building or an
  upgrade; an upgrade is reachable iff its `ResearchedAt` building is reachable
  and its own `Requires` are reachable).
- `PRODUCER_UNREACHABLE` (Error, target = unit id) — a unit that can never be
  built because its producer or a prerequisite is itself unreachable. Dead
  content.
- `UPGRADE_UNREACHABLE` (Error, target = upgrade id) — an upgrade whose
  `ResearchedAt` or `Requires` chain is unreachable.

### 3. Tier monotonicity (Warning)

Tiers are author-facing labels for "how deep in the tech tree." A prerequisite at
a *higher* tier than the thing that needs it is almost always an authoring slip
(you'd unlock the advanced thing before its requirement). Warn, don't reject
(gaps/odd trees can be deliberate identity):
- `TIER_NONMONOTONIC` (Warning, target = the dependent's id) — for any
  unit/building/upgrade, if any of its prerequisites (`Requires`, plus a unit's
  `ProducedBy`, plus an upgrade's `ResearchedAt`) resolves to an entity with a
  strictly greater `Tier`. Message names the offending prerequisite.

### 4. Empty/blank ids (Error)

- `ID_BLANK` (Error) — any unit/building/upgrade whose `Id` is null/empty/
  whitespace. (Referential checks in `FactionDef.Validate` assume ids are usable
  keys; a blank id is malformed even if it parsed. This also surfaces the
  null-element class from 3d-1's loader as a precise, author-actionable finding
  when a def is hand-constructed rather than loaded.)

### 5. Point-budget balance (Warning)

The "is this faction roughly fair?" heuristic. Self-relative (no coupling to the
reference faction): compare each unit to the faction's own mean, so a single
wildly over/under-tuned unit stands out. Warn-only.

**Unit power** (a `double`; uses `double` freely — validator is not in the sim):
```
power(u) =
    wHp      * MaxHp
  + wDps     * (Weapon == null ? 0 : Damage * (RefCooldown / max(1, CooldownTicks)))
  + wRange   * (Weapon == null ? 0 : RangeAsDouble)
  + wSpeed   * SpeedAsDouble
  + wSight   * SightRange
  + (Harvester == null ? 0 : wHarvester)
  + (factionMechanic is RegeneratingShields m ? wShield * m.MaxShield : 0)
```
- `RangeAsDouble`/`SpeedAsDouble` = `fix.Raw / 65536.0`.
- The shield term is added to every unit's power when the faction has the
  shields mechanic (shields are a faction-wide unit buff in 3c) — so a shielded
  faction's units read as costing more power, which is correct.

**Unit cost:** `cost(u) = MineralCost + wSupply * SupplyCost`.

**Efficiency:** `eff(u) = power(u) / max(1.0, cost(u))`.

**Outlier test:** let `mean` = average `eff` over all units. For each unit, if
`eff(u) > mean * (1 + tol)` → `BUDGET_OVERPOWERED` (Warning, target = unit id);
if `eff(u) < mean * (1 - tol)` → `BUDGET_UNDERPOWERED` (Warning). Default
`tol = 0.40` (±40%). Skip the whole check if `UnitList.Count < 2` (no meaningful
mean). Messages include the unit's efficiency and the faction mean so the author
can see the gap.

**`BudgetWeights` defaults** (first-pass, tunable by playtesting — documented as
such):
```
wHp = 1.0, wDps = 8.0, wRange = 4.0, wSpeed = 30.0, wSight = 2.0,
wHarvester = 40.0, wShield = 1.5, wSupply = 25.0, RefCooldown = 10.0, tol = 0.40
```
These are calibrated so the reference faction's four units land within the band
(verified by a test — see Testing). They are NOT claimed to be true balance;
they're a sane default the author can override and we'll tune later. The point of
the check is to catch gross outliers, not to enforce a precise economy.

## Severity semantics

- **Error** = the pack is broken or unplayable; an importer should refuse to start
  a match with it (but the loader still returns the def so the fix-it loop can
  inspect it).
- **Warning** = unusual, possibly a mistake, but legal; surfaced to the author,
  never blocks. Faction identity (asymmetry, gaps) lives here.

The arc's design tenet "gaps are faction identity; soft warnings only" is honored:
everything subjective (tiers, balance) is a Warning; only objective breakage
(empty catalog, dead/unbuildable content, blank ids) is an Error.

## Faction Forge prompt (`docs/faction-forge-prompt.md`)

A self-contained prompt a player pastes into an external LLM chat to author a
faction. Contents:
- **Role/goal:** "You design a faction for an RTS as a single JSON pack."
- **The JSON schema, by example:** the full annotated `packs/reference/faction.json`
  with each field explained (types, units in tiles, ticks at the sim rate, the
  `Fix` decimal convention, enum names `UpgradeStat`/`MechanicKind`, which fields
  are optional and their defaults, that omitted booleans/zeros take engine
  defaults).
- **The rules the validator enforces** (so the LLM self-checks before emitting):
  every unit needs a reachable producer; prereqs should be same-or-lower tier;
  ≥1 unit buildable at start; ids unique and non-blank; keep per-unit
  power/cost efficiency within ~±40% of each other (with the rough weighting so
  the LLM can eyeball it); mechanic params if using RegeneratingShields.
- **Identity guidance:** asymmetry/gaps are encouraged (no air? all-melee? fine) —
  the validator only warns. Cost/power should track stats.
- **Output contract:** emit ONLY the JSON pack (so it can be saved straight to a
  `.json` and imported), matching the schema. A short worked mini-example
  (a 2-unit faction) shows the shape end to end.

This doc is the user-facing capstone of the arc. It is text only (no code).

## Testing

- **PackValidator** (`tests/SimCore.Tests/Packs/PackValidatorTests.cs`):
  - Reference faction → zero `Error` findings (and lands within the budget band:
    zero `BUDGET_*` warnings — this both tests the validator and calibrates the
    default weights).
  - `NO_BUILDINGS` / `NO_UNITS` / `NO_SEED_UNIT` each triggered by a minimal
    faction missing that piece.
  - `PRODUCER_UNREACHABLE`: a unit whose producer requires an unbuildable
    building.
  - `UPGRADE_UNREACHABLE`: an upgrade whose `ResearchedAt` is unreachable.
  - `TIER_NONMONOTONIC`: a tier-1 unit requiring a tier-3 building → Warning.
  - `ID_BLANK`: a unit with an empty id → Error.
  - `BUDGET_OVERPOWERED`: a faction with one unit far more efficient than the
    rest → Warning on that unit; the others not flagged.
  - Budget check skipped for a 1-unit faction (no false outlier).
  - Findings carry the right `Code`, `Severity`, and `TargetId`.
- **LoadAndValidate** (`FactionPackLoaderTests` additions): valid reference pack
  JSON → non-null faction, no load errors, no Error findings; a hard-broken JSON
  → null faction, empty findings; a parseable-but-unbalanced pack → faction +
  budget warnings.
- No determinism golden change (SimCore untouched; assert `git diff master --
  src/SimCore/` stays empty conceptually — verified in the gate task).

## Decisions Log

- Validator is a SEPARATE pass in `SimCore.Packs` (not in `FactionDef.Validate`);
  may use `double` since it's advisory and never in the sim.
- Findings are structured (`Severity` + stable `Code` + `TargetId` + `Message`)
  for machine fix-it; codes are the stable contract.
- Errors = objective breakage / unplayable; Warnings = subjective (tiers,
  balance). Nothing balance-related ever rejects (warn-only), per the arc tenet.
- Budget formula = self-relative per-unit efficiency outliers vs faction mean,
  tunable weighted power sum, ±40% default band. Not coupled to the reference
  faction; default weights calibrated so the reference faction is clean.
- `LoadAndValidate` convenience added; `LoadFromJson` unchanged.
- Faction Forge prompt is a text doc using the reference pack as the worked
  example; output contract = JSON-only so it imports directly.
- Text-only packs; no Godot import UI, no asset embedding, no multiplayer hash
  (all deferred). SimCore untouched → golden hash safe.
