# Unit Behaviors — Stances, Patrol, Rally, Self-Destruct — Design

**Date:** 2026-06-11
**Status:** Approved in discussion (auto-attack default chosen by user; Del-to-destroy added by user)

## Goal

Players can set how units behave without orders (auto-attack / defend / passive),
order patrols, set building rally points, and delete their own units/buildings
with Del. All behavior lives in the deterministic sim as commands + hashed
state, ready for the plan-5 CPU opponent to use the same vocabulary.

Non-goals: formation movement, waypoint queues (>2 patrol points), rally-to-harvest
auto-command (rally is a plain move in v1), stance hotkeys (buttons only in v1).

## Sim model

**Stance** — `enum Stance : byte { AutoAttack = 0, Defend = 1, Passive = 2 }` on
`Unit`, default `AutoAttack`. Weaponless units are effectively Passive regardless
(combat system already skips them).

Engagement rules (combat system, all gated by fog visibility as today):

| Stance | Idle acquisition | Chase behavior |
|---|---|---|
| AutoAttack | acquires visible enemies within `Weapon.Range + AcquireBonus` | chases with the existing attack-move leash semantics (drops at `Range + LeashBonus` from the point of acquisition — the unit's **anchor**, captured when it acquires while idle) |
| Defend | same acquisition | same leash but measured from the anchor; when the target dies/escapes/becomes fogged, the unit issues itself a move back to the anchor |
| Passive | never self-acquires | n/a (still fights when explicitly ordered) |

Notes:
- "Idle" = no move order, no attack target, not attack-moving, not harvesting.
- Anchor is per-unit `FixVec` state (hashed), set at the moment of idle acquisition;
  cleared when the engagement ends. Explicit player orders override/clear anchors.
- Existing attack-move acquisition is unchanged; stances only govern IDLE units.

**Patrol** — an order, not a stance: `PatrolCommand(playerId, unitIds, FixVec target)`.
The unit stores `PatrolA` (its position at command time) and `PatrolB` (target),
plus `IsPatrolling`. It attack-moves A→B→A→… forever. Engagement during patrol
follows attack-move rules (acquire + leash, resume patrol leg after). Any new
explicit order (move/attack/attack-move/harvest/patrol) or Esc-issued stop clears
patrol. Stance interacts only in that Passive patrol units don't engage (they
just walk the loop) — patrol uses attack-move acquisition for AutoAttack/Defend.

**Rally** — `SetRallyCommand(playerId, buildingId, FixVec target)` sets
`Building.RallyPoint` (`FixVec`, plus `HasRally` bool; both hashed). When
production spawns a unit from a building with a rally, the sim issues that unit
a move to the rally point immediately (same tick, via the normal move logic —
no special pathing). Clearing: right-click the building itself (or its footprint)
= clear rally.

**Self-destruct** — `DestroyCommand(playerId, ids)`. Each id that resolves to a
unit or building OWNED by the issuing player gets `Hp = 0`; the normal
`RemoveDead`/`RemoveDeadBuildings` sweeps handle everything (supply release,
mineral refunds for queued units, occupancy/passability restore, corpse/wreck
playback in the view). Non-owned/missing ids are ignored.

**Hashing** — new hashed state: `u.Stance`, `u.AnchorX/Y + HasAnchor`,
`u.IsPatrolling + PatrolA/B`, `b.HasRally + RallyPoint`. Golden re-pinned with
the behavior change (idle auto-acquisition WILL change existing trajectories —
that is the point; determinism scenario v5 must exercise stance/patrol/rally/destroy).

## UI (Godot layer)

- **Stance buttons** in the bottom panel when ≥1 owned combat unit selected:
  `Auto / Defend / Passive`, current stance of the selection highlighted (mixed
  selection shows none highlighted); clicking issues `SetStanceCommand` for the
  selected ids (one command, `SetStanceCommand(playerId, unitIds, Stance)`).
- **Patrol**: `P` arms patrol-target mode (like A for attack-move); left-click
  issues `PatrolCommand`. Esc cancels the armed mode. Patrolling units show a
  small looping-arrows glyph near their selection ring.
- **Rally**: with an owned production building selected, right-click on ground
  sets rally (replaces the current "nothing happens" case); right-click on the
  building clears it. A small flag + dashed line drawn while the building is
  selected.
- **Del**: with owned units and/or building selected, Del issues `DestroyCommand`
  for the whole selection. No confirmation (RTS standard; cheap units, and the
  sim ignores non-owned ids). HUD shows nothing special; corpses/wrecks play.
- Pause-friendly: all of these queue as commands while paused, like everything else.

## Testing

- Sim TDD per feature: stance acquisition matrix (auto acquires / passive doesn't /
  defend returns to anchor), patrol loop reaches both endpoints repeatedly and
  re-engages per stance, rally moves trained units, destroy kills own-only and
  refunds supply, fog still gates idle acquisition.
- Determinism scenario v5 adds: an idle auto-attack defender meeting a raid, a
  patrol loop crossing the gap, a rally point, and a scripted DestroyCommand —
  re-pin golden (both configs).
- Godot layer: manual checklist (stance buttons reflect+set, P-patrol, rally flag,
  Del). Headless smoke stays green.

## Risks / decisions

- Idle auto-acquisition changes the feel of every existing fight — intended
  (user chose auto-attack default). Tuning lever: stance acquisition uses the
  existing AcquireBonus; no new constants in v1.
- Defend's "return to anchor" reuses the normal move path (flow fields), so a
  defender can be body-blocked returning home — acceptable, consistent with
  collision rules.
- Del with a mixed selection deletes everything selected and owned — standard,
  but worth seeing in playtest (no confirmation dialog by design).
