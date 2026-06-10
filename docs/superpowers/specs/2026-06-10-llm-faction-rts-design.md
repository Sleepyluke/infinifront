# LLM-Faction RTS — Design Spec

**Date:** 2026-06-10
**Status:** Approved design, pre-implementation
**Engine:** Godot 4.x (C# / .NET for simulation, GDScript for presentation)

## Concept

A StarCraft-style RTS where the factions are designed by players using an LLM.
The game ships a faction schema, a prompt template ("Faction Forge"), and a
strict validator. Players generate a faction in an external LLM chat
(Claude/ChatGPT), save the output as a faction pack, and import it. The engine
implements a fixed vocabulary of mechanics; the LLM composes that vocabulary
into novel units, tech trees, and strategic identities.

**Core principle: the LLM generates data, never code.** A faction pack is
JSON + sprite sheets validated against a versioned schema. This keeps factions
safe (no arbitrary code), balanceable (point budgets), and multiplayer-ready
(content-hashable, deterministic).

## V1 Scope

- Player vs CPU, one hand-built reference faction, full faction import pipeline.
- Faction creation via external LLM + pack import (no API integration in v1).
- AI-generated sprites supplied by the player inside the pack; the prompt
  template emits image-generation prompts per unit. Missing/invalid art falls
  back to colored silhouettes + icons so packs are playable text-only.
- 2D. Long-term feature target is the full classic feature set (two resources,
  air, casters, terrain levels, transports), built in phases (see Roadmap).

## Architecture

Four layers, strictly separated:

1. **Presentation (GDScript):** rendering, animation, UI, camera, sound,
   input. Reads sim state snapshots, interpolates, draws. Sends player
   commands down.
2. **Simulation Core (C#):** fixed timestep (~20 ticks/s), fixed-point/integer
   math only, ECS-style units (entity = ID + component bag). Pathfinding
   (flow fields), combat, economy, fog of war, victory. **Deterministic** and
   Godot-free — runs headless.
3. **Faction Pack System (C#):** schema definition, multi-stage validator,
   point-budget calculator, pack loader, sprite-sheet importer.
4. **Faction Packs (data):** folder of `faction.json`, `sprites/`, `README.md`.

Rules:

- The sim never touches Godot node state and never uses floats for game logic.
- **Commands are the only input to the sim.** The CPU opponent issues the same
  commands a human would; no state mutation, no fog cheating.
- The ability vocabulary lives in the sim as composable components; faction
  JSON only declares component lists + parameters.
- `schema_version` gates vocabulary. Old packs keep working on new engines.

This sim/render split + determinism is what makes replays, headless balance
testing, and future lockstep multiplayer additive rather than rewrites.

## Faction Pack Schema (v1)

### Pack layout

```
my_faction/
├── faction.json
├── sprites/
│   ├── unit_<id>.png        # fixed-grid sprite sheet per unit
│   ├── building_<id>.png
│   └── icons/<id>.png       # 64x64 UI portraits
└── README.md                # LLM-written lore & strategy guide
```

### faction.json top level

```json
{
  "schema_version": 1,
  "identity": { "name": "...", "tagline": "...", "lore": "...",
                "palette": ["#...", "#...", "#..."] },
  "faction_mechanic": { "type": "swarm_discount", "params": {} },
  "buildings": [],
  "units": [],
  "upgrades": [],
  "ai_hints": { "opening": [], "compositions": [] }
}
```

### Unit definition

```json
{
  "id": "spore_lurker",
  "name": "Spore Lurker",
  "tier": 2,
  "role": "siege",
  "cost": { "minerals": 125, "gas": 50, "supply": 2, "build_time": 28 },
  "stats": { "hp": 90, "armor": 1, "speed": 2.2, "sight": 9 },
  "components": [
    { "type": "ranged_attack", "damage": 14, "range": 6, "cooldown": 1.8,
      "damage_type": "explosive", "projectile": "acid_glob" },
    { "type": "burrow", "can_attack_burrowed": true },
    { "type": "armor_aura", "radius": 3, "bonus": 1 }
  ],
  "produced_by": "spore_den",
  "sprite": "sprites/unit_spore_lurker.png",
  "flavor": "..."
}
```

Roles: `worker | scout | core | siege | tank | support | harass`.

### Component vocabulary (v1 — grows with schema versions)

melee_attack, ranged_attack (projectile), aoe_attack, heal, repair, cloak,
burrow, detector, armor_aura / speed_aura / damage_aura, summon, transform,
builder, harvester, regeneration. (v2 adds: flying, transport/cargo,
anti-air weapon flags. v3 adds: caster + spell vocabulary, terrain
interactions.)

### Faction mechanic

Exactly one, from a curated engine-implemented menu (~8–10 at launch):
regenerating shields, swarm cost-discount, units-from-corpses, mobile
buildings, sacrifice-to-empower, etc. Parameterized by the pack. This menu is
the primary lever for deepening faction variety over time.

### Structural requirements (validator-enforced)

- Exactly 1 worker unit, 1 main base building, a supply mechanism.
- 8–14 combat units: ≥3 tier-1, ≥3 tier-2, ≥2 tier-3.
- At least one ranged option and one melee-or-tank option.
- Complete production chain: every `produced_by` exists; tech tree reachable;
  tier gates enforced by buildings. 4–8 upgrades.
- **No mandatory unit types beyond the skeleton.** Air units, casters,
  detection, anti-air, cloak, etc. are all optional vocabulary. Faction gaps
  are a feature — they create strategic identity.
- **Counterplay soft warnings:** the validator allows hard gaps but flags them
  loudly at import (e.g. "this faction cannot damage air units — it will lose
  to any air faction"; same for absent detection). The player decides.

### Point-budget balance

- Every stat point, component, and faction mechanic has a power cost from a
  published formula (shipped inside the prompt template, with worked examples,
  so the LLM can self-balance).
- Per-unit: `power(unit) ≤ budget(tier)`, and resource cost must scale with
  power within tolerance bands.
- Per-faction: aggregate budget prevents maxing everything.
- Expensive mobility/utility (flight, spells) prices itself: an air-heavy
  faction pays with thinner ground forces.
- Violations reject/flag at import with exact numbers.

### Sprite sheet contract

Fixed grid: rows = animations (idle, walk, attack, death) with fixed frame
counts; 8 facings (or 4 + mirroring initially); cell sizes by size-class
(small 64px / medium 96px / large 128px); transparent background. Importer
validates dimensions and slices automatically. Invalid/missing art → fallback
silhouette + icon; never blocks import.

## LLM Pipeline

**Faction Forge prompt template** (shipped document, copy-paste into any LLM):

1. Role + creative brief.
2. Full schema contract: structure, component vocabulary with parameters and
   power costs, faction-mechanic menu, structural requirements, budget formula
   with worked examples.
3. Output instructions: one fenced JSON block (faction), one image-generation
   prompt per sprite sheet (grid layout/size/background baked in), README lore.
4. One abbreviated gold-example faction.

**Import flow:**

```
Pick folder
  → 1. JSON parse + schema validation       (structure)
  → 2. Referential integrity                (ids resolve, tech tree complete)
  → 3. Point-budget audit                   (per-unit + aggregate)
  → 4. Asset validation                     (sheets exist, dims, slicing)
  → PASS → faction select   |   FAIL/WARN → error report + "Copy fix-it prompt"
```

**Fix-it prompt:** every failure renders as machine-readable text the player
pastes back into their LLM chat to get corrected JSON. The loop
(generate → import → paste errors → regenerate) converges in 2–3 rounds with
no hand-editing.

Validated packs get a content hash (future multiplayer pack verification).

## CPU Opponent

Utility-based AI, not scripted. Reads the faction pack itself — costs, roles,
tech tree, and the `ai_hints` the LLM wrote (build orders, unit compositions) —
and runs: economy targets → opening from hints → army composition by role
counters → scout/attack/defend decisions. Because it consumes only pack data,
it plays any generated faction automatically. Difficulty = APM caps, reaction
delays, resource handicaps. Never cheats (no fog vision, no state mutation).

## Roadmap

| Phase | Engine adds | Schema |
|-------|------------|--------|
| 1 — Core | C# fixed-timestep sim, flow-field pathfinding, 1 resource + supply, workers/bases/production, fog, melee/ranged/AoE combat, control groups, attack-move, victory, reference faction, full import pipeline | v1 |
| 2 — Depth | Gas (2nd resource), air units, transports, detection/cloak interplay | v2: flying, transport, anti-air flags |
| 3 — Mastery | Casters (energy + targeted spells), high ground/terrain levels, building addons | v3: caster/spell vocabulary, terrain |
| 4 — Multiplayer | Lockstep netcode over the deterministic sim, pack-hash verification, replays | — |

Each phase ends with a playable game.

## Testing

- **Determinism tests (CI):** replay a command log twice; assert identical
  state hashes every tick. Protects multiplayer.
- **Validator corpus:** deliberately broken packs (missing worker, budget
  overflow, circular tech tree, bad sprite dims) each asserting the right
  error/warning.
- **Headless balance harness:** AI-vs-AI matches at max speed; new pack vs
  reference faction over N matches → win-rate report. (Post-v1 nice-to-have;
  falls out of headless sim + pack-driven AI.)
- Unit tests: budget formula, pathfinding, combat math.

## Error Handling

- All pack problems caught at import; runtime trusts validated factions
  completely.
- Sim errors fail loudly in dev builds.
- Future multiplayer desync → end match + save diagnostic replay.

## Key Decisions Log

- Godot 4 with C# sim core (perf for SC-scale battles) — Rust/GDExtension kept
  open as a later optimization behind the same sim/render boundary.
- LLM access: external-tool + pack import for v1 (no backend, packs are
  shareable files); in-game generation UI is a possible later layer.
- Art: AI-generated sprites, player-supplied in pack, silhouette fallback.
- Balance: point-budget formula at import; sim-based balance reports later.
- Air/casters/detection optional, soft warnings only — gaps are identity.
