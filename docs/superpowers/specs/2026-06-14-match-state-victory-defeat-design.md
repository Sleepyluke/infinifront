# Match State & Victory/Defeat — Design Spec (Plan 5a)

**Date:** 2026-06-14
**Status:** Approved (autonomous build authorized), pre-implementation
**Position:** Sub-project 1 of 5 in Plan 5 (CPU opponent + match flow). Decomposition:
5a match state & victory/defeat → 5b per-player factions → 5c CPU framework + Easy
tier → 5d Medium & Hard tiers → 5e Godot match flow / menu.

## Why this exists

The sim runs forever today — there is no notion of a player being beaten or a match
being won. Every later piece of Plan 5 needs this: the CPU must know when it has won
or lost, and the match-flow UI (5e) needs an outcome to show a Victory/Defeat screen.
5a adds deterministic match-outcome state to `SimWorld`. It is pure SimCore (no AI, no
Godot, no per-player factions) and immediately makes even the current sandbox detect a
winner.

## Scope (5a)

**In scope:**
- A deterministic per-player aliveness rule and latched match outcome on `SimWorld`.
- A `Step` phase that computes/latches the outcome each tick.
- Folding the outcome into `StateHasher` (v6) + golden re-pin.
- Read-only public surface so the Godot layer (later) can poll the result.
- Tests for elimination, win, draw, latch, the units-but-no-buildings case, and the
  determinism gate.

**Out of scope (later sub-projects):** the CPU agent (5c/5d), per-player factions
(5b), and ALL Godot UI / match-flow / victory screen / restart (5e). 5a exposes the
state; it does not render or react to it, and it does NOT halt the sim.

## Aliveness & outcome rules (decided)

- **Alive** = the player owns ≥ 1 building (any construction state — a half-built
  building still counts). **Units never count** (StarCraft-style: you are defeated when
  your last structure falls, even with an army on the field).
- **Defeated** = owns 0 buildings.
- **Match outcome:** the match is `Over` when ≤ 1 player remains alive. The winner is
  the sole survivor; if zero players are alive (mutual elimination on the same tick),
  it is a **draw**.

## State & API

Add to `SimWorld` (deterministic, hashed):

```csharp
public enum MatchPhase { InProgress, Over }

// On SimWorld:
public MatchPhase Phase { get; private set; } = MatchPhase.InProgress;
public int WinnerId { get; private set; } = -1; // -1 = undecided (InProgress) or draw (Over)

/// <summary>A player is defeated when they own no buildings (units do not count).</summary>
public bool IsDefeated(int playerId);
```

- `WinnerId` is `-1` while `InProgress` and stays `-1` on a draw; otherwise it is the
  surviving player's id once `Over`.
- `IsDefeated` recomputes from current buildings (owns 0 buildings owned by that player).
  Used by the UI to message/grey-out a defeated player even before the match is `Over`
  (not strictly needed for 2-player but cheap and forward-compatible for FFA).

## Where it runs

A new private `UpdateMatchState()` phase in `Step`, inserted **after**
`RemoveDeadBuildings()` (so destroyed buildings are already gone) and **before**
`Tick++`:

```
EnsureOccupancy → UpdateVision → Apply(commands) → UpdateCombat → MoveUnits
→ UpdateHarvest → UpdateConstruction → UpdateProduction → UpdateResearch
→ UpdateShields → RemoveDead → RemoveDeadBuildings → UpdateMatchState → Tick++
```

`UpdateMatchState()`:
1. If `Phase == Over`, return immediately — **the outcome latches** and never changes
   once decided (a post-decision tick cannot flip the winner or revert to InProgress).
2. Count alive players (own ≥ 1 building) and remember the last alive id.
3. If `aliveCount <= 1`: set `Phase = Over`; `WinnerId = aliveCount == 1 ? lastAlive : -1`.

Aliveness is an O(players × buildings) (or O(buildings) with a per-player tally) scan —
trivial at RTS scale. Determinism: integer-only, no floats, reads only hashed state.

## The sim does not halt on game-over

When `Phase == Over` the sim keeps stepping normally — leftover units can still fight,
move, and die; commands still apply. Only the outcome is latched. The decision to stop
feeding commands, freeze input, and show a Victory/Defeat screen belongs to the match-
flow layer (5e). Headless callers/tests just read `Phase`/`WinnerId`. This keeps `Step`
free of special-case control flow and keeps replays/lockstep identical whether or not a
front-end is attached.

## Setup invariant

Defeat is checked from the first tick, so **every participating player must start with
≥ 1 building**, else they are eliminated at tick 0 (which would latch `Over`
immediately — still deterministic, just an instant result). Normal match setup
(`TestMap`) already gives each player buildings. The determinism-test scenario must give
both players a building so it exercises the `InProgress` path across its run; if it
currently does not, 5a adjusts the scenario (and re-pins the golden accordingly).

## Determinism

- `Phase` (as its integer value) and `WinnerId` fold into `StateHasher` → **v6**. The
  golden trajectory hash is re-pinned in the same commit, **only after** the two replay
  tests pass and Debug == Release, and the re-pin is counterfactually verified (confirm
  the fold actually changed because of the new fields, not coincidence).
- No floats; outcome is a pure function of hashed building ownership + the latched
  prior phase. Two peers running the same inputs reach the same outcome on the same tick.

## Testing

- **Elimination/win:** a 2-player world; remove player 1's last building → after a Step,
  `IsDefeated(1)` true, `Phase == Over`, `WinnerId == 0`.
- **Units-but-no-buildings is defeated:** a player with units and zero buildings →
  `IsDefeated` true and they do not count as alive (locks in the chosen rule).
- **Draw:** both players reduced to zero buildings on the same tick → `Phase == Over`,
  `WinnerId == -1`.
- **Latch:** once `Over`, a later tick that (somehow) adds a building does not change
  `WinnerId` or revert `Phase`.
- **InProgress while both hold a building:** `Phase == InProgress`, `WinnerId == -1`.
- **Determinism:** `StateHasher` v6 folds the new fields; golden re-pinned; both replay
  tests (`Same_Script...`, `Replaying_After_Full_Run...`) and the trajectory-fold test
  pass; Debug == Release.

## Decisions Log

- Aliveness = owns ≥ 1 building; units never count (buildings-only, StarCraft-style).
- Outcome latches once decided; draw on mutual elimination (`WinnerId == -1`).
- The sim keeps stepping after `Over` (no halt); the front-end reacts (5e).
- Outcome computed in a new `UpdateMatchState()` phase after `RemoveDeadBuildings`,
  before `Tick++`; folded into `StateHasher` (v6) with a golden re-pin.
- Read-only `Phase`/`WinnerId`/`IsDefeated` surface; SimCore stays Godot-free.
