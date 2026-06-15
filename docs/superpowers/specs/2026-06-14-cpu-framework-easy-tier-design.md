# CPU Opponent Framework + Easy Tier — Design Spec (Plan 5c)

**Date:** 2026-06-14
**Status:** Approved (autonomous build authorized), pre-implementation
**Position:** Sub-project 3 of 5 in Plan 5. 5a match state ✅ → 5b per-player factions ✅
→ **5c CPU framework + Easy tier** → 5d Medium & Hard tiers → 5e Godot match flow/menu.

## Why this exists

5a/5b made the engine match-aware and per-player-faction-aware. 5c adds the actual
opponent: a **deterministic, in-sim CPU agent** that reads game state and issues the same
commands a human would, plus the first difficulty tier (Easy). It is the centerpiece of
Plan 5 — after it, you can play a real (if weak) 1v1 against the computer. It ships the
framework + one exemplar tier (mirroring 3c's mechanic-framework + shields), so 5d only
adds Medium/Hard behaviors.

## Design tenets (decided)

- **Deterministic & in SimCore.** The AI is a pure function of (tick, hashed sim state,
  difficulty). It runs inside `Step`, uses only `Fix`/int, and issues `Command` objects
  through the existing `Apply` path — so all command validation (prereqs, minerals, supply,
  placement) is reused and there is still exactly one mutation path. This keeps CPU play
  replayable, lockstep-safe, and headless-testable, and folds it under the golden hash.
- **Easy is stateless.** Easy's decisions are recomputed from the current world each
  decision tick (no build-order memory), so 5c adds **no per-player AI memory to hash** —
  only the constant `Controller`/`Difficulty` config. (5d may add `AiState` for Medium/Hard
  if their behavior needs memory.)
- **Role resolution from the faction catalog.** The AI does not hard-code unit/building
  ids; it derives *roles* from the acting player's `FactionDef`: a **worker** = a unit
  whose spec has a `Harvester`; a **combat unit** = a unit with a `Weapon` (Easy picks the
  cheapest); a **supply building** = a building whose spec has `SupplyProvided > 0`; a
  unit's **producer** = the building whose id equals the unit's `ProducedBy`. This makes the
  CPU work for any faction pack, not just the reference faction.
- **SC2-style Easy ladder.** Easy macros lightly and makes occasional weak attacks (a real
  but beatable opponent); 5d's Medium = stronger/earlier sustained pushes + rebuild; Hard =
  reactive macro + aggression.

## Player controller & difficulty

Add to `PlayerState` (hashed, constant config):
```csharp
public enum PlayerController { Human, Cpu }   // namespace SimCore.Sim
public enum AiDifficulty { Easy, Medium, Hard }
// on PlayerState:
public PlayerController Controller; // default Human
public AiDifficulty Difficulty;     // default Easy (only meaningful when Controller == Cpu)
```
`SimWorld` exposes a setup method `public void SetCpu(int playerId, AiDifficulty difficulty)`
(sets `Controller = Cpu`, `Difficulty = difficulty`). Default for all players is Human, so
existing setups/tests are unaffected in behavior. Both fields fold into `StateHasher` (→ v7)
with a golden re-pin (constant additive fields; the scenario stays human-only).

## The AI phase

A new `UpdateAi()` runs in `Step`, **after external commands are applied and before
`UpdateCombat`**, so the AI reacts to a consistent post-command snapshot:
```
EnsureOccupancy → UpdateVision → Apply(commands) → UpdateAi() → UpdateCombat → MoveUnits
→ UpdateHarvest → UpdateConstruction → UpdateProduction → UpdateResearch → UpdateShields
→ RemoveDead → RemoveDeadBuildings → UpdateMatchState → Tick++
```
`UpdateAi()`:
- For each player `p` with `Controller == Cpu` AND the match not `Over` (a defeated/decided
  CPU stops acting): if it is a **decision tick** (`Tick % DecisionInterval == 0`, a fixed
  cadence so the AI acts a few times/second, not every tick), dispatch on `Difficulty` —
  5c implements `EasyDecide(p)`; Medium/Hard throw-not-implemented placeholders filled in
  5d (or fall through to Easy — decided in the plan).
- `EasyDecide(p)` builds `Command` objects and applies each via the existing `Apply(cmd)`.
  Determinism: it iterates the stable `_units`/`_buildings` lists and resolves ties by id /
  fixed scan order — no RNG needed.

## Easy behavior (SC2-style), per decision tick for player p

Lives in a new `src/SimCore/Sim/SimWorld.Ai.cs`. Each decision tick, in order:
1. **Train workers** — if p's worker count < `EasyWorkerCap` and a worker producer exists
   and p can afford it: `TrainCommand` the worker from its producer building.
2. **Assign idle workers to harvest** — each worker with no harvest/move/attack order →
   `HarvestCommand` to the nearest resource node (deterministic: nearest by squared
   distance, id as tiebreak).
3. **Build supply when blocked** — if `SupplyUsed >= SupplyCap - EasySupplyBuffer`, p can
   afford the supply building, and there is an idle worker: `BuildCommand` it at the first
   placeable footprint found in a deterministic ring scan near that worker. (Local-to-worker
   placement avoids needing to path the worker first; skip and retry if none placeable.)
4. **Train army** — if supply headroom and minerals allow: `TrainCommand` the cheapest
   combat unit from its producer.
5. **Occasional weak attack** — if p's combat-unit count >= `EasyAttackThreshold` and
   `Tick % EasyAttackInterval == 0`: `AttackMoveCommand` all of p's combat units toward the
   enemy base (the nearest enemy building's center; lowest enemy building id as tiebreak).

All thresholds/intervals (`DecisionInterval`, `EasyWorkerCap`, `EasySupplyBuffer`,
`EasyAttackThreshold`, `EasyAttackInterval`) are named constants — first-pass values tuned
later. Easy never expands to new bases, never researches, never micros, and attacks
infrequently with whatever it has — a weak but genuine opponent.

## Determinism

- The AI is integer/Fix-only and reads only hashed state + `Tick`; commands it issues flow
  through `Apply` (already deterministic). Easy holds no memory, so nothing new beyond the
  constant `Controller`/`Difficulty` needs hashing.
- `StateHasher` v7 folds `Controller` + `Difficulty` per player; golden re-pinned (the
  determinism scenario stays human-only → both fields are constant 0, a clean additive
  re-pin; counterfactually verified).
- **CPU determinism is proven by a dedicated test**, not by perturbing the golden scenario:
  a CPU-vs-CPU world stepped twice produces identical per-tick hashes (and a single-run
  recorded-vs-replayed check), confirming the AI is reproducible from the seed alone.

## Testing

- **Framework:** `SetCpu` sets Controller/Difficulty; a Human player gets no AI commands; an
  out-of-the-box world is unaffected (golden re-pin only).
- **Easy economy:** a CPU-only world (one CPU player with the reference faction + a node) —
  after enough ticks the CPU has trained workers (up to the cap), they are harvesting, and
  minerals rise; when supply-blocked it builds a supply building.
- **Easy army + attack:** the CPU trains combat units and, once past the threshold, issues
  an attack-move toward the enemy base (assert its combat units acquire a move order toward
  the enemy on an attack tick).
- **Role resolution:** with a non-reference test faction (different ids), the CPU still
  finds worker/combat/supply roles from the catalog.
- **Determinism:** CPU-vs-CPU two-run identical-hash test; full golden re-pin verified;
  replay tests green; Debug == Release.
- **Match interaction:** a CPU whose match is `Over` (defeated) issues no commands.

## Scope boundaries

- **In scope:** the per-player controller/difficulty, the `UpdateAi` phase, the Easy tier
  (economy + supply + army + weak attack), role resolution, determinism (hash v7 + CPU
  determinism test).
- **Out of scope:** Medium/Hard tiers (5d); any Godot UI / difficulty selection / starting a
  vs-CPU match from a menu (5e — for now `SetCpu` is called from tests and can be wired into
  `TestMap` for manual play); expansion/teching/micro AI; multi-base.

## Decisions Log

- AI is deterministic, in SimCore, runs in an `UpdateAi()` Step phase after external
  commands, and issues commands through `Apply` (one mutation path, full validation reuse).
- Easy is **stateless** (recomputed each decision tick) → no AI memory to hash; only
  constant `Controller`/`Difficulty` added to `PlayerState` + `StateHasher` v7 (golden
  re-pin).
- Roles (worker/combat/supply/producer) are derived from the faction catalog, not
  hard-coded → works for any pack.
- SC2-style Easy: light macro + supply + cheapest-combat army + occasional weak attack-move;
  no expand/research/micro. Tunable constants.
- CPU determinism proven by a dedicated CPU-vs-CPU test; the golden scenario stays
  human-only (clean additive re-pin).
