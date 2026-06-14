# Upgrades & Research — Design Spec (Plan 3b)

**Date:** 2026-06-13
**Status:** Approved design, pre-implementation
**Position:** Sub-project 2 of 4 in the faction-pack arc (tiers ✅ → **upgrades** → mechanics → pack format)

## Why this exists

Faction packs (the endgame, plan 3d) must express upgrades/research. This plan
adds that vocabulary to the engine on top of the 3a `FactionDef`, so a pack can
later declare an upgrades catalog as data. It also delivers a real RTS depth
layer (tech investment that boosts an army).

## Approved decisions (from brainstorm)

- **Retroactive effects, implemented via compute-on-read.** A completed research
  instantly benefits every existing AND future unit of the targeted type. No
  `Unit`/`Weapon` field is ever mutated by an upgrade; instead the sim reads
  **effective stats** = base + Σ(applicable per-player upgrade deltas). The only
  new hashed state is the per-player applied-upgrade set — minimal determinism
  surface, immutable bases, no apply-once bookkeeping.
- **Generic stat-delta system** over the static-per-unit stats: **Damage, Range,
  CooldownTicks, Speed, Sight**. **HP/MaxHp is deferred** (no MaxHp cap / heal
  mechanic exists yet; can't be cleanly retroactive without extra scope).

## Scope

**In scope:**
- Extend `FactionDef` with an upgrades catalog (`UpgradeDef`, `UpgradeList`,
  `GetUpgrade`), additively.
- `Unit.DefId` (set at spawn from the unit def; not hashed — derived static label,
  mirroring `Building.DefId`) so effective-stat lookups match upgrades to units.
- `ResearchCommand` + a single research slot per building.
- Per-player `AppliedUpgrades` (deterministically ordered; hashed).
- Effective-stat accessors and the read-site refactor in combat/movement/vision.
- Extend `FactionDef.Validate()` for upgrades.
- Godot research-button row; determinism scenario v6 (research mid-run, re-pin).

**Out of scope:**
- HP/MaxHp upgrades (deferred). Building-stat upgrades (units only this pass).
- Faction mechanics (3c), JSON/pack format + Fix-JSON converter (3d).
- Upgrade removal/downgrade (upgrades are permanent once researched).

## Data shape

Extend `FactionDef`:
```csharp
FactionDef(string Id, string Name,
           IEnumerable<UnitDef> units,
           IEnumerable<BuildingDef> buildings,
           IEnumerable<UpgradeDef> upgrades);   // new, additive (default empty)
// + UpgradeList, GetUpgrade(id)

enum UpgradeStat { Damage, Range, CooldownTicks, Speed, Sight }

UpgradeDef(
    string Id, int Tier, string ResearchedAt,        // building def id that researches it
    IReadOnlyList<string> Requires,                  // building AND/OR upgrade def ids
    IReadOnlyList<string> TargetUnitDefIds,          // unit def ids affected; ["*"] = all units
    UpgradeStat Stat, int Delta,
    int MineralCost, int ResearchTicks);
```
The `units`/`buildings`-only `FactionDef` constructor stays valid via a defaulted
empty upgrades list (preserves all existing call sites incl. ReferenceFaction and
TestFactions until they opt in).

## Effective-stat model

Five accessors on `SimWorld` (they need the unit's owner + `DefId` to find
applicable upgrades):
```csharp
int  EffectiveDamage(Unit u);
Fix  EffectiveRange(Unit u);
int  EffectiveCooldownTicks(Unit u);
Fix  EffectiveSpeed(Unit u);
int  EffectiveSight(Unit u);
```
Each = the unit's base stat + Σ of `Delta` over the owner's applied upgrades whose
`Stat` matches and whose `TargetUnitDefIds` contains `u.DefId` (or `"*"`). Deltas
are clamped so an effective stat never goes below a floor (Damage/Sight ≥ 0;
CooldownTicks ≥ 1; Speed/Range ≥ 0) — generic deltas could be negative.

**Read-site refactor:** `UpdateCombat` uses `EffectiveDamage/Range/CooldownTicks`
instead of reading `u.Weapon.*` directly; `MoveUnits` uses `EffectiveSpeed`
instead of `u.SpeedPerTick`; vision uses `EffectiveSight`. Units with no weapon or
no applicable upgrades return their base values unchanged (zero-delta fast path
when the owner has no upgrades).

Base values remain the source: `Weapon.Damage/Range/CooldownTicks`,
`Unit.SpeedPerTick`, `Unit.SightRange` are never written by upgrades.

## Research mechanic

`ResearchCommand(int PlayerId, int BuildingId, string UpgradeDefId)`.

A building gains a single research slot (both fields hashed — mutable building
state):
```csharp
string ResearchingId;        // "" = idle
int    ResearchTicksRemaining;
```
Validation (in `Apply`, mirroring TrainCommand): upgrade def resolves; building
owned, complete; `building.DefId == upgradeDef.ResearchedAt`; prerequisites met;
the upgrade is not already applied for the player and not already researching in
this building; player has minerals. On success: deduct minerals, set
`ResearchingId`/`ResearchTicksRemaining`. Rejected silently otherwise (consistent
with all command validation). Research has no supply cost.

A new `UpdateResearch` system (in `SimWorld.Buildings.cs`, sequenced in `Step`
next to `UpdateProduction`) decrements `ResearchTicksRemaining` on the in-progress
slot; at zero it adds `ResearchingId` to the player's `AppliedUpgrades` and clears
the slot. If the researching building is destroyed mid-research, the slot is lost
(no refund) — consistent with destroyed production-queue handling being explicit;
minerals already spent are not refunded for research (one-shot investment).

## Applied-upgrade state & prerequisites

`PlayerState` gains `AppliedUpgrades` — a `List<string>` kept **sorted** on insert
(deterministic order independent of research-completion order across players).
Helpers: `HasUpgrade(id)`.

`PrerequisitesMet` generalizes to resolve each required id against **either** an
owned complete building (`b.DefId == reqId`) **or** an applied upgrade
(`player.HasUpgrade(reqId)`) — a single `Has(playerId, reqId)` predicate used by
build, train, and research validation.

## Hashing

`StateHasher` v4 additions:
- Per building: `ResearchingId` (fold its chars/length) + `ResearchTicksRemaining`.
- Per player: `AppliedUpgrades.Count` + each id in sorted order.
`Unit.DefId` is NOT hashed (derived static label, 1:1 with the unit's already-
hashed spec, set once at spawn — documented exclusion, like `Building.DefId`).
Effective stats are NOT hashed (derived from hashed base + hashed applied set).
The determinism scenario re-pins `GoldenTrajectoryHash` in the task that adds the
research to the scenario.

## FactionDef.Validate() additions

- Every `UpgradeDef.ResearchedAt` resolves to a building def.
- Every `UpgradeDef.Requires` id resolves to a building **or** upgrade def.
- Every `TargetUnitDefIds` entry is `"*"` or resolves to a unit def.
- Cycle detection extended to include upgrade→upgrade `Requires` edges (combined
  prerequisite graph over buildings + upgrades).

## Error handling

- Unknown ids in commands → silent reject (no throw in sim path).
- Invalid faction surfaced by `Validate()` at load/test time, never mid-Step.
- Negative effective stats clamped to the documented floors.

## Testing

- **Research gating:** ResearchedAt mismatch, building incomplete, prereq missing
  vs present (building-prereq AND upgrade-prereq), double-research rejected,
  already-applied rejected, unknown id rejected, insufficient minerals rejected.
- **Effect application:** a unit trained BEFORE research and one trained AFTER
  both read the boosted stat (proves retroactive); targeting filter excludes
  non-targeted unit def ids; `"*"` hits all; two upgrades on the same stat sum;
  negative delta clamps at the floor.
- **Effective accessors:** base returned when owner has no upgrades; combat uses
  effective damage/range/cooldown (a damage upgrade kills faster); movement uses
  effective speed; vision uses effective sight.
- **Validate():** flags dangling ResearchedAt, dangling/where-resolved Requires,
  bad TargetUnitDefIds, an upgrade prerequisite cycle.
- **Hashing:** AppliedUpgrades change and research-slot change each move the hash;
  identical worlds with identical applied sets hash equal.
- **Determinism:** scenario v6 researches an upgrade mid-run; replay tests pass;
  golden re-pinned in-commit with documented reason.

## Decisions Log

- Retroactive via compute-on-read effective stats (not field mutation): same
  gameplay, minimal hashed-state surface, immutable bases.
- Generic stat-delta over Damage/Range/CooldownTicks/Speed/Sight; HP/MaxHp
  deferred until a MaxHp/heal mechanic exists.
- `Unit.DefId` added (unhashed, like `Building.DefId`) to match upgrades to units.
- Single research slot per building (not the train queue); research one-shot, no
  supply, no refund on building loss.
- `AppliedUpgrades` sorted for deterministic hashing; `Has(playerId, reqId)`
  unifies building + upgrade prerequisite resolution.
- Negative effective stats clamped to documented floors.
