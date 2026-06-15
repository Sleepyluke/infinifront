# Faction Forge — author an RTS faction as a JSON pack

**How to use:** Paste this entire document into a chat with an LLM (Claude, ChatGPT, Gemini, etc.), then describe the faction you want — its theme, playstyle, and any signature units. The LLM will reply with a single `faction.json` object. Save that output to a file named `faction.json` and import it into the game. The sections below give the LLM the exact format, the engine defaults, and the rules the in-game validator enforces, so the pack it produces will load and play.

---

## Your role (for the LLM)

You are a faction designer. Output **exactly one JSON object** — a faction pack — and **nothing else**. No commentary, no explanation, no markdown fences. The first character of your reply must be `{` and the last must be `}`.

---

## The format, by annotated example

This is the committed reference faction. It is a complete, valid pack — match this structure exactly.

```json
{
  "id": "reference",
  "name": "Reference",
  "units": [
    {
      "id": "fabber",
      "tier": 1,
      "producedBy": "depot",
      "requires": [],
      "maxHp": 40,
      "speed": 0.25,
      "mineralCost": 50,
      "supplyCost": 1,
      "buildTimeTicks": 100,
      "sightRange": 6,
      "harvester": {
        "carryCapacity": 5,
        "gatherTicks": 10
      }
    },
    {
      "id": "trooper",
      "tier": 1,
      "producedBy": "barracks",
      "requires": [],
      "maxHp": 45,
      "speed": 0.1999969482421875,
      "mineralCost": 50,
      "supplyCost": 1,
      "buildTimeTicks": 80,
      "sightRange": 7,
      "weapon": {
        "damage": 6,
        "range": 4,
        "cooldownTicks": 8
      }
    },
    {
      "id": "outrider",
      "tier": 1,
      "producedBy": "barracks",
      "requires": [],
      "maxHp": 60,
      "speed": 0.5,
      "mineralCost": 75,
      "supplyCost": 2,
      "buildTimeTicks": 120,
      "sightRange": 9,
      "weapon": {
        "damage": 4,
        "range": 3,
        "cooldownTicks": 5
      }
    },
    {
      "id": "tank",
      "tier": 2,
      "producedBy": "barracks",
      "requires": [
        "depot"
      ],
      "maxHp": 150,
      "speed": 0.125,
      "mineralCost": 150,
      "supplyCost": 3,
      "buildTimeTicks": 200,
      "sightRange": 7,
      "weapon": {
        "damage": 20,
        "range": 6,
        "cooldownTicks": 20
      }
    }
  ],
  "buildings": [
    {
      "id": "depot",
      "tier": 1,
      "requires": [],
      "maxHp": 400,
      "width": 2,
      "height": 2,
      "mineralCost": 100,
      "buildTimeTicks": 150,
      "supplyProvided": 8,
      "isDepot": true,
      "sightRange": 9
    },
    {
      "id": "barracks",
      "tier": 1,
      "requires": [],
      "maxHp": 350,
      "width": 2,
      "height": 2,
      "mineralCost": 150,
      "buildTimeTicks": 200,
      "canTrain": true,
      "sightRange": 8
    }
  ],
  "upgrades": []
}
```

### Field reference

**Top level**

| Field | Type | Notes |
|---|---|---|
| `id` | string | Faction id. Lowercase, no spaces. |
| `name` | string | Human-readable display name. |
| `units` | array | List of unit defs (see below). |
| `buildings` | array | List of building defs (see below). |
| `upgrades` | array | List of upgrade defs. Use `[]` if none. |
| `mechanic` | object | Optional faction-wide special mechanic (see below). Omit if none. |

**`units[]` — each entry**

| Field | Type | Notes |
|---|---|---|
| `id` | string | Unique across all units. Lowercase, no spaces. |
| `tier` | int | Integer ≥ 1. |
| `producedBy` | string | The id of a building that builds this unit. |
| `requires` | string[] | Building/upgrade ids that must exist first. `[]` for none. |
| `maxHp` | int | Hit points. |
| `speed` | decimal | Tiles per tick (e.g. `0.25`). |
| `mineralCost` | int | Mineral cost to build. |
| `supplyCost` | int | Supply consumed. |
| `buildTimeTicks` | int | Ticks to produce. |
| `sightRange` | int | Vision radius. Default `7` if omitted. |
| `weapon` | object | Optional. `{ "damage": int, "range": decimal, "cooldownTicks": int }`. Omit for unarmed units. |
| `harvester` | object | Optional. `{ "carryCapacity": int, "gatherTicks": int }`. Omit for non-gatherers. |

**`buildings[]` — each entry**

| Field | Type | Notes |
|---|---|---|
| `id` | string | Unique across all buildings. Lowercase, no spaces. |
| `tier` | int | Integer ≥ 1. |
| `requires` | string[] | Building ids that must exist first. `[]` for none. |
| `maxHp` | int | Hit points. |
| `width` | int | Footprint width in tiles. |
| `height` | int | Footprint height in tiles. |
| `mineralCost` | int | Mineral cost to build. |
| `buildTimeTicks` | int | Ticks to construct. |
| `supplyProvided` | int | Optional. Supply this building adds. Default `0`. |
| `isDepot` | bool | Optional. True if it's a resource drop-off / main base. Default `false`. |
| `canTrain` | bool | Optional. True if it produces units. Default `false`. |
| `sightRange` | int | Vision radius. Default `8` if omitted. |

**`upgrades[]` — each entry** (use `"upgrades": []` if your faction has none)

| Field | Type | Notes |
|---|---|---|
| `id` | string | Unique across all upgrades. Lowercase, no spaces. |
| `tier` | int | Integer ≥ 1. |
| `researchedAt` | string | Id of the building where it's researched. |
| `requires` | string[] | Building/upgrade ids that must exist first. `[]` for none. |
| `targetUnitDefIds` | string[] | Unit ids the upgrade affects, or `["*"]` for all units. |
| `stat` | enum | One of `"Damage"`, `"Range"`, `"CooldownTicks"`, `"Speed"`, `"Sight"`. |
| `delta` | decimal | Amount added to the stat (may be negative, e.g. to lower a cooldown). |
| `mineralCost` | int | Mineral cost to research. |
| `researchTicks` | int | Ticks to research. |

**`mechanic` — optional faction-wide mechanic**

| Field | Type | Notes |
|---|---|---|
| `kind` | enum | `"RegeneratingShields"` (the only special kind today). Omit the whole `mechanic` object for no mechanic. |
| `maxShield` | int | Shield capacity added on top of HP. |
| `regenPerTick` | int | Shield points restored per tick. |
| `regenDelayTicks` | int | Ticks after taking damage before regen resumes. |

---

## Conventions

- **Numbers are fixed-point decimals.** Write them plainly: `0.25`, `4`, `0.125`. Whole numbers and fractions both work. (You will see one value, `0.1999969482421875`, in the reference pack — that's just the fixed-point representation of ~0.2; you can write `0.2` and it will round to the nearest representable value.)
- **Omitted optional fields take engine defaults.** Leave a field out to accept its default rather than restating it: `sightRange` defaults to `7` for units and `8` for buildings; `supplyProvided` defaults to `0`; `isDepot` and `canTrain` default to `false`. `weapon`, `harvester`, and `mechanic` are simply omitted when not used.
- **Enums are written by NAME, as strings.** Use `"Damage"`, not a number; `"RegeneratingShields"`, not `1`.
- **Ids are lowercase, unique, and have no spaces.** Use short, readable ids (`hq`, `worker`, `marine`, `armory_armor`). An id must be unique within its list.

---

## The rules your pack must follow (the validator checks these)

The game runs every imported pack through a validator. **Errors** block the pack from loading; **warnings** are advisory and never reject the pack. Design to clear the errors; treat warnings as balance hints.

**Errors (must fix):**

- **Has buildings and units.** A faction needs at least one building and at least one unit. (`NO_BUILDINGS`, `NO_UNITS`.)
- **All ids non-blank.** No empty or whitespace-only ids. (`ID_BLANK`.)
- **Every unit's producer must be reachable from the start.** A unit's `producedBy` building (and its `requires` chain) must bottom out at a building with no requirements — i.e. you can actually build the producer starting from an empty base. (`PRODUCER_UNREACHABLE`.)
- **Every upgrade must be reachable.** An upgrade's `researchedAt` building and all its `requires` must be reachable. (`UPGRADE_UNREACHABLE`.)
- **At least one unit buildable at game start.** Some unit must be producible from a building you can construct immediately (a building with `requires: []`, ideally one with `canTrain: true` and/or `isDepot: true` so you have a starting producer). (`NO_SEED_UNIT`.)

**Warnings (advisory):**

- **Tiers should not go backward.** A prerequisite should be the *same tier or lower* than the thing requiring it. A tier-1 unit requiring a tier-2 building triggers a tier warning. (`TIER_NONMONOTONIC`.)
- **Keep units roughly balanced — power should track cost.** The validator computes a rough power and cost per unit and flags units whose power-per-cost efficiency strays more than ~±40% from the faction's own average. Free units (cost 0) are excluded. (`BUDGET_OVERPOWERED`, `BUDGET_UNDERPOWERED`.)

The power/cost heuristic the validator uses (for tuning intuition — you don't compute it, but design toward it):

```
power ≈ maxHp
      + 8 · (damage · 10 / cooldownTicks)   (if it has a weapon)
      + 4 · range                            (if it has a weapon)
      + 30 · speed
      + 2 · sightRange
      + 40   (if it's a harvester)
      + 1.5 · maxShield   (if the faction has RegeneratingShields)

cost  ≈ mineralCost + 25 · supplyCost
```

Aim to keep each cost-bearing unit's `power / cost` within about ±40% of the others. Outliers only warn — they don't block — but a balanced roster plays better.

- **If using `RegeneratingShields`,** pick sensible `maxShield`, `regenPerTick`, and `regenDelayTicks` (all ≥ 0). A small shield with a modest regen and a short delay is a good starting point.

---

## Identity guidance

**Asymmetry and gaps are good.** Factions are more interesting when they make hard choices. A faction with no air units, an all-melee army, or a single-unit rush is completely fine — the validator only *warns* on stylistic and balance choices, it never rejects them. Lean into a theme: pick a fantasy (swarm, heavy armor, fast raiders, defensive turtle) and let your unit kit express it, even if that means leaving whole categories empty. A focused, opinionated faction beats a bland, complete one.

---

## Output contract

**Output ONLY the JSON object** — no prose, no markdown fences — so it can be saved directly as `faction.json`.

For reference, here is a minimal but complete and valid faction: one base building that trains two units. It passes the validator with no errors and no warnings.

```json
{
  "id": "vanguard",
  "name": "Vanguard",
  "units": [
    {
      "id": "worker",
      "tier": 1,
      "producedBy": "hq",
      "requires": [],
      "maxHp": 50,
      "speed": 0.25,
      "mineralCost": 50,
      "supplyCost": 1,
      "buildTimeTicks": 100,
      "harvester": {
        "carryCapacity": 5,
        "gatherTicks": 10
      }
    },
    {
      "id": "soldier",
      "tier": 1,
      "producedBy": "hq",
      "requires": [],
      "maxHp": 50,
      "speed": 0.25,
      "mineralCost": 75,
      "supplyCost": 1,
      "buildTimeTicks": 120,
      "weapon": {
        "damage": 6,
        "range": 4,
        "cooldownTicks": 8
      }
    }
  ],
  "buildings": [
    {
      "id": "hq",
      "tier": 1,
      "requires": [],
      "maxHp": 400,
      "width": 2,
      "height": 2,
      "mineralCost": 100,
      "buildTimeTicks": 150,
      "supplyProvided": 10,
      "isDepot": true,
      "canTrain": true
    }
  ],
  "upgrades": []
}
```
