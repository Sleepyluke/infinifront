# Tech Tiers & Prerequisites — Design Spec (Plan 3a)

**Date:** 2026-06-13
**Status:** Approved design, pre-implementation
**Position:** Sub-project 1 of 4 in the faction-pack arc (tiers → upgrades → mechanics → pack format)

## Why this exists

The endgame is the faction-pack system: players author factions with an LLM and
import them as data. The user chose to **expand the engine to the full vision
before exposing the pack format**, so a pack can express a real tech tree,
upgrades, and a faction mechanic — not just a flat unit list.

This is sub-project 1: a tech tree (prerequisite gating) and the architectural
spine the whole arc builds on — `FactionDef`, the engine's single source of
truth for what a faction is. Later plans extend `FactionDef` (upgrades,
mechanics); the capstone (3d) is *just* JSON ↔ `FactionDef` + a validator, so all
parsing/validation concern stays isolated to the end.

## Scope

**In scope:**
- `FactionDef` — an in-memory named catalog of unit/building defs (tier,
  prerequisites, `produced_by`, reusing existing `UnitSpec`/`BuildingSpec`).
- Id-based `TrainCommand`/`BuildCommand` that resolve def ids against the
  `FactionDef` and enforce prerequisites + `produced_by`.
- `FactionDef.Validate()` — referential integrity + cycle detection (seed of the
  3d pack validator).
- Migrate `ReferenceSpecs.cs` → a `ReferenceFaction` `FactionDef` with a starter
  tech tree; update Godot wiring to id-based commands + catalog-driven build menu.

**Out of scope (later sub-projects):**
- Upgrades/research (3b), faction-mechanic system (3c).
- JSON (de)serialization, the `Fix` wire format, the Faction Forge prompt, the
  point-budget formula, and structural pack rules ("≥3 tier-1 units", etc.) —
  all 3d.
- Air units, casters, second resource — not on this arc.

## Architecture & determinism boundary

`FactionDef` lives in `SimCore.Sim` as immutable C# records (no JSON dependency).
`SimWorld` holds exactly one `FactionDef`, supplied at construction, and uses it
to resolve command def ids.

**No new hashed runtime state.** Prerequisites are validated at command time
against building completion state that `StateHasher` v3 already hashes. The tier
label is static spec metadata, identical on every client, never mutated during a
match — so it needs no hashing. Consequence: the golden trajectory hash moves
only if the reference *scenario's behavior* changes. Target is an unchanged
golden; the id-based rewrite of the determinism scenario must preserve sim
behavior, and the constant is re-pinned in-commit only if it provably cannot,
with the reason documented.

## FactionDef data shape

```csharp
public sealed record FactionDef(
    string Id, string Name,
    IReadOnlyDictionary<string, UnitDef> Units,
    IReadOnlyDictionary<string, BuildingDef> Buildings);

public sealed record UnitDef(
    string Id, int Tier, string ProducedBy,
    IReadOnlyList<string> Requires, UnitSpec Spec);

public sealed record BuildingDef(
    string Id, int Tier, IReadOnlyList<string> Requires, BuildingSpec Spec);
```

- `ProducedBy` — building def id that trains this unit.
- `Requires` — building def ids the player must own (complete) to train/build
  this entry.
- `Tier` — integer label (1/2/3). Not a gate; consumed by 3d's budget formula
  and structural validator.
- `Spec` — the existing stat record, reused unchanged.

Dictionaries are keyed by def id. They are used for lookup only and are **never
iterated in sim logic** (iteration order is undefined); any enumeration for UI or
validation happens outside the deterministic Step path.

## Prerequisite & tier semantics

**Train** unit `u` via building `b` succeeds iff:
1. `b` is complete and its spec `CanTrain` is true,
2. `b`'s def id equals `u.ProducedBy`,
3. the player owns ≥1 complete building of every id in `u.Requires`,
4. (existing checks unchanged: queue cap, minerals, supply headroom).

**Build** building `b` succeeds iff the player owns ≥1 complete building of every
id in `b.Requires` (plus all existing checks: worker proximity, minerals,
footprint placeable).

Tier never gates. Missing prerequisites → command silently rejected, matching
every other command-validation path in the sim.

## Command changes

```csharp
TrainCommand(int PlayerId, int BuildingId, string UnitDefId)
BuildCommand(int PlayerId, int WorkerUnitId, string BuildingDefId, int CellX, int CellY)
```

`SimWorld.Apply` resolves the def id through its `FactionDef` (dictionary
lookup — deterministic result), runs the prerequisite/`produced_by` checks, then
executes exactly as today (deduct minerals, reserve supply, enqueue / place
footprint). Specs are no longer carried in commands; they come from the resolved
def. This touches `Commands.cs`, the train/build cases in `SimWorld`, the Godot
`CommandController`, and the building/production test fixtures (which register
their test specs in a small local `FactionDef`).

## FactionDef integrity validation

```csharp
IReadOnlyList<string> FactionDef.Validate();
```

A pure function returning human-readable errors:
- every `ProducedBy` resolves to an existing building def,
- every `Requires` id (unit and building) resolves to an existing building def,
- the building-prerequisite graph has no cycles,
- every unit def has a non-empty `ProducedBy`.

Returns empty for a valid def. Used here to guard the reference faction (a test
asserts `ReferenceFaction.Validate()` is empty); reused and extended by the 3d
pack validator. Budget/structural rules are **not** part of this method.

## Reference faction migration

`ReferenceSpecs.cs` becomes `ReferenceFaction` (a `FactionDef`). Starter tech
tree (concrete wiring finalized in the plan):
- **Buildings:** `depot` (tier 1, no requires), `barracks` (tier 1, no requires).
- **Units:** `fabber` (tier 1, produced_by `depot`), `trooper` (tier 1,
  produced_by `barracks`), `outrider` (tier 1, produced_by `barracks`),
  `tank` (tier 2, produced_by `barracks`, requires `depot`).

This is a deliberately shallow tree (the engine has only two building types
today); it exists to exercise prereq gating end-to-end. Richer trees become
expressible as later plans add building variety and as packs are authored. The
Godot `CommandController` issues id-based commands and reads
`FactionDef.Buildings`/`Units` to populate its build/train menus.

## Error handling

- Unknown def id in a command → resolve fails → command silently rejected
  (no throw in the sim path; deterministic).
- An invalid `FactionDef` is a developer/authoring error, surfaced by
  `Validate()` at load/test time, never mid-Step.

## Testing

- **Prerequisite gating:** train/build rejected when a required building is
  absent or incomplete; allowed once present. `produced_by` mismatch rejected.
- **FactionDef.Validate():** flags dangling `produced_by`, dangling `requires`,
  a prerequisite cycle, a producer-less unit; passes a good def.
- **Reference faction:** `ReferenceFaction.Validate()` returns empty.
- **Determinism:** scenario rewritten to id-based commands; replay tests
  unchanged-and-passing; golden trajectory unchanged (or re-pinned in-commit with
  documented reason).
- All existing tests migrated to id-based fixtures continue to pass.

## Decisions Log

- Prereq graph (`requires` + `produced_by`) is the gate; tier is a label for
  balance/validation only — chosen over explicit-tier gating for expressiveness.
- Commands become id-based (necessary consequence of prereq/`produced_by` needing
  entity identity; also what the pack format and build-menu UI require).
- `FactionDef` introduced now (not deferred to 3d) as the architectural spine the
  whole arc extends; `ReferenceSpecs` migrates to the first `FactionDef`.
- String def ids (human-readable, pack-friendly; command resolution is a
  deterministic dictionary lookup).
- No new hashed state this plan; tiers/prereqs are validation + static metadata.
