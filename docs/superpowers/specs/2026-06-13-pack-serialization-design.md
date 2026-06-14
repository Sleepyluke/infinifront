# Faction Pack Serialization & Loading — Design Spec (Plan 3d-1)

**Date:** 2026-06-13
**Status:** Approved (autonomous build authorized), pre-implementation
**Position:** Sub-project 4 of the faction-pack arc, part 1 of 2 (3d-1 serialization → 3d-2 validation & Forge prompt)

## Why this exists

The whole arc's payoff: a faction is **data** a player authors with an LLM and
imports. 3a–3c built the in-engine `FactionDef` vocabulary (tiers/prereqs,
upgrades, a mechanic). This plan makes `FactionDef` round-trip to/from JSON so a
pack on disk can become a playable faction. 3d-2 then adds the balance validator
and the authoring prompt.

## Scope (3d-1)

**In scope:**
- `JsonConverter<Fix>` — deterministic, human-authorable wire format.
- JSON ↔ `FactionDef` via DTO records + System.Text.Json (units, buildings,
  upgrades, mechanic, tiers, prereqs, all nested specs).
- A `FactionPack` loader: parse JSON text → `FactionDef` (+ structural
  `Validate()` from 3a/3c run on load), returning a result with errors.
- Export the in-code `ReferenceFaction` to a pack JSON file under
  `packs/reference/faction.json`, with a round-trip test (load it → deep-equals
  the in-code def).

**Out of scope (3d-2):** the point-budget balance formula, the richer validator
(tier monotonicity, producer-reachability, structural-minimums), the fix-it
machine output, the Faction Forge prompt. **Out of arc:** sprites/asset loading
in the pack (text-only packs for now; art stays the existing pipeline), in-game
import UI (a later Godot task), per-player factions (plan 5).

## The Fix wire format (the landmine)

`Fix` is a Q48.16 `readonly struct` over `long Raw`. **Do not** serialize via
`Fix.ToString()` (it formats a `double` — lossy, and the lone double in SimCore).

Wire format: a **JSON number written as a decimal** (e.g. `0.25`, `4`, `2.5`),
parsed deterministically into `Fix`:
- Serialize: write `Raw / 65536` as a decimal string with enough fractional
  digits to round-trip the values packs use (Q48.16 has 1/65536 resolution; 5
  decimal places suffice for the fractions in play, but to guarantee exactness we
  serialize the exact rational: emit `Raw`-derived decimal that `ParseFix`
  inverts exactly — see below).
- Parse: read the decimal string/number, split integer/fraction, compute
  `Raw = intPart * 65536 + round(fracPart * 65536)` using integer/decimal math
  (C# `decimal`, not `double`) so the result is deterministic and identical on
  every platform. Reject values whose precision exceeds Q48.16 resolution only if
  they don't round-trip (warn, not throw, in the loader; the converter clamps to
  nearest raw).

Determinism guarantee: parsing uses `decimal` (exact base-10) → integer `Raw`;
no `double` anywhere. A round-trip test asserts `ParseFix(Write(f)) == f` for the
reference faction's Fix values and a sweep of raws.

## DTO layer

Serialization uses plain DTO records mirroring the catalog (kept separate from
the runtime `FactionDef`/specs so the wire shape can evolve independently):
```
FactionPackDto(string Id, string Name, List<UnitDto> Units,
               List<BuildingDto> Buildings, List<UpgradeDto> Upgrades,
               MechanicDto? Mechanic)
UnitDto(string Id, int Tier, string ProducedBy, List<string> Requires,
        int MaxHp, Fix Speed, int MineralCost, int SupplyCost, int BuildTimeTicks,
        int SightRange, WeaponDto? Weapon, HarvesterDto? Harvester)
WeaponDto(int Damage, Fix Range, int CooldownTicks)
HarvesterDto(int CarryCapacity, int GatherTicks)
BuildingDto(string Id, int Tier, List<string> Requires,
            int MaxHp, int Width, int Height, int MineralCost, int BuildTimeTicks,
            int SupplyProvided, bool IsDepot, bool CanTrain, int SightRange)
UpgradeDto(string Id, int Tier, string ResearchedAt, List<string> Requires,
           List<string> TargetUnitDefIds, string Stat, Fix Delta,
           int MineralCost, int ResearchTicks)
MechanicDto(string Kind, int MaxShield, int RegenPerTick, int RegenDelayTicks)
```
- `Fix` fields use the `JsonConverter<Fix>`.
- Enums (`UpgradeStat`, `MechanicKind`) serialize **by name** (string) for
  forward-compat as values are added.
- Nullable nested DTOs (`Weapon`, `Harvester`, `Mechanic`) omit/`null` cleanly.
- `BuildingDto` defaults (`SupplyProvided` 0, `IsDepot`/`CanTrain` false,
  `SightRange` 8) are honored when the JSON omits them
  (`JsonSerializerOptions { DefaultIgnoreCondition = WhenWritingDefault }` on
  write; missing → default on read).

A `PackMapper` converts `FactionPackDto` ↔ `FactionDef` by **feeding the
`FactionDef` ctor ordered lists** (never rebuilding its lookup dicts directly) and
mapping enum strings to the engine enums.

## Loader

```csharp
public sealed record PackLoadResult(FactionDef? Faction, IReadOnlyList<string> Errors);
public static class FactionPackLoader
{
    public static PackLoadResult LoadFromJson(string json);   // parse + map + Validate()
    public static string ToJson(FactionDef faction);          // serialize
}
```
`LoadFromJson`: deserialize to DTO (catching malformed JSON / unknown enum → an
error in the result, never a throw escaping the loader), map to `FactionDef`, run
the existing `Validate()`; return the def (or null on hard failure) + errors.
This lives in a JSON-aware location. **Decision:** keep `SimCore` JSON-free —
the loader/DTOs/converter go in a new `SimCore.Packs` class library referencing
`SimCore`. (SimCore stays minimal and Godot-free; the determinism core never
depends on System.Text.Json.) Tests live in `SimCore.Packs.Tests` or the existing
test project referencing both.

## Reference faction as the first pack

Add `packs/reference/faction.json` = `FactionPackLoader.ToJson(ReferenceFaction.Def)`.
A test asserts `LoadFromJson(File.ReadAllText(...))` produces a `FactionDef`
deep-equal to `ReferenceFaction.Def` (compare via lists/fields — `FactionDef` is a
class, no structural `==`; a test helper does the comparison). This dogfoods the
format and guarantees the reference faction is expressible as data.

## Error handling

- Malformed JSON, unknown enum name, missing required field → captured as an error
  string in `PackLoadResult.Errors`; `Faction` is null. No exception escapes
  `LoadFromJson`.
- `Validate()` errors (referential integrity, mechanic params) are included in
  `Errors`; a structurally-broken-but-parseable pack returns the def + errors so
  3d-2's fix-it loop can surface them.
- The `Fix` converter parses with `decimal`; a value out of Q48.16 range → error.

## Determinism

Pack loading is **setup-time**, not in `Step`, so `double`-free isn't strictly
required there — but the `Fix` converter is `decimal`-based regardless so loaded
values are bit-identical across platforms (two players loading the same pack get
the same `FactionDef`). The pack content hash (for future multiplayer pack
verification) is out of scope here; noted for plan 5.

## Testing

- **Fix converter:** round-trip `ParseFix(Write(f)) == f` for a raw sweep
  (0, 1, 1/2, 1/4, 1/5, negative, large); decimal parse is deterministic; out of
  range → error.
- **DTO round-trip:** each DTO ↔ engine record maps all fields; enums by name;
  nullable Weapon/Harvester/Mechanic; BuildingDto defaults honored on omit.
- **Loader:** valid pack JSON → FactionDef with correct catalog; malformed JSON →
  error, no throw; unknown enum → error; a referentially-broken pack → def +
  Validate errors.
- **Reference round-trip:** `packs/reference/faction.json` loads deep-equal to
  `ReferenceFaction.Def`; the exported file matches `ToJson(ReferenceFaction.Def)`.
- No determinism golden change (this plan adds no sim state; SimCore untouched
  except possibly exposing what the mapper needs — prefer additive).

## Decisions Log

- `Fix` wire format = decimal string, `decimal`-based exact parse (never
  `Fix.ToString`/double). The recorded landmine, resolved.
- DTO layer separate from runtime records (wire shape evolves independently);
  enums serialized by name.
- JSON concerns isolated in a new `SimCore.Packs` library — `SimCore` stays
  JSON-free and minimal.
- Mapper feeds the `FactionDef` ctor (ordered lists), never rebuilds dicts.
- Reference faction exported as the first pack + round-trip test (dogfood).
- Text-only packs (no embedded art) this plan; budget/validator/Forge → 3d-2.
