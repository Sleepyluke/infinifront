# Multiplayer (Deterministic Lockstep) — Design Spec

**Date:** 2026-06-15
**Status:** Approved (design), pre-implementation. This is the umbrella design ("how
multiplayer works"); each sub-project (M1–M4) gets its own spec → plan → build.
**Decision context:** the deterministic core (fixed-point sim, command-only mutation,
`StateHasher` per-tick fingerprint, cross-OS golden-hash CI) was built specifically to
enable this.

## Goal

LAN / direct-IP multiplayer for matches mixing networked humans and (locally-computed) CPU
players, via **deterministic lockstep**: peers exchange only player commands, apply them on
the same tick on every machine, and the simulation stays bit-identical everywhere.

## Why lockstep (vs the alternatives)

- **Lockstep (chosen):** only commands cross the wire (~bytes/tick); every peer runs the
  identical `Step`. Tiny bandwidth, inherently cheat-resistant (no authoritative state to
  forge), and it's what the determinism work was for. The classic RTS model (StarCraft, AoE).
- **Rollback (rejected):** predict + re-simulate on correction. Built for 2-player
  low-latency fighting games; re-simulating hundreds of RTS units per rollback is expensive
  and the engineering is far harder. Overkill.
- **State-sync / host-authoritative-state (rejected):** host streams world state to clients.
  Throws away the determinism foundation, high bandwidth (hundreds of entities), and removes
  the cheat-resistance. Wrong for this project.

## Scope (v1)

**In scope:** LAN / direct-IP (Godot ENet — no servers); a match of N slots where each slot is
a local human, a remote human (one per connected peer), or a CPU (any difficulty); per-slot
faction; deterministic lockstep with input delay; `StateHasher`-based desync detection; a host
lobby to configure slots + start; basic disconnect/desync handling.

**Out of scope (later/never for v1):** internet matchmaking + relay/lobby servers + NAT
traversal; rollback/prediction; spectators; reconnect-into-running-match; >~4 players (the
sim supports N, but v1 targets small lobbies); replay-file save/load (the command stream makes
this trivial later, noted for the future).

## Architecture

```
  Local input ─► Commands ─► [tag execution tick T+delay] ─► LockstepCoordinator
                                                                 │  (buffers frames per (tick,player))
        peers' frames ──────────────────────────────────────────┤
                                                                 ▼
   when ALL human players' frames for tick X are present:  merged commands (deterministic order)
                                                                 ▼
                                       SimWorld.Step(merged)  ◄── CPU slots' commands generated
                                                                 in-sim by UpdateAi (identical on
                                                                 every peer — NO network traffic)
                                                                 ▼
                                   StateHasher.Hash  ─► broadcast ─► compare with peers ─► desync? halt
```

**Key invariant:** the only inputs that vary between machines are *human* commands; those are
exchanged. CPU commands are a pure function of the (identical) world + tick, computed locally
on every peer (5c/5d made the AI stateless + deterministic precisely so this holds). So CPU
players are free, network-wise.

## Components / decomposition (M1–M4)

### M1 — Lockstep coordinator (headless-testable core) — BUILD FIRST

A transport-agnostic state machine (new `SimCore.Net` library, references `SimCore`, no Godot)
that schedules lockstep stepping. **Fully unit-testable** by running several coordinators
in-process feeding each other frames — no sockets.

- `record CommandFrame(int Tick, int PlayerId, IReadOnlyList<Command> Commands)` — one player's
  commands for one execution tick (empty list = "I have nothing this tick", which is REQUIRED
  so peers can distinguish "no input" from "not yet arrived").
- `LockstepCoordinator(int localPlayerId, IReadOnlyList<int> humanPlayerIds, int inputDelay)`:
  - `CommandFrame SubmitLocal(IReadOnlyList<Command> cmds)` — once per local tick; tags the
    commands for execution tick `current + inputDelay`, buffers locally, returns the frame to
    broadcast (M2 sends it). An empty list still produces (and broadcasts) a frame.
  - `void Receive(CommandFrame frame)` — buffer a remote human's frame.
  - `bool TryDequeueStep(out IReadOnlyList<Command> merged)` — if every human player has a frame
    for the next execution tick, output the **deterministically merged** command list (frames
    sorted by `PlayerId`, each player's commands concatenated in submission order) and advance;
    else false (stall — the slowest peer paces everyone).
  - Desync hooks: `void RecordLocalHash(int tick, ulong hash)`, `void ReceiveHash(int tick,
    int playerId, ulong hash)`, and a `Desynced` flag/event raised when two peers report
    different hashes for the same tick.
- **Determinism responsibility:** the merge order (by `PlayerId`) is identical on all peers, so
  every peer feeds `Step` the same command sequence. This ordering is M1's correctness crux.
- Does NOT depend on `SimWorld` — it's pure scheduling over `Command`/`ulong`, so tests need no
  world. (The game loop in M2 owns the world and calls `Step`/`StateHasher`.)

**M1 tests (headless):** N coordinators wired to deliver each other's frames → all reach
ready-to-step tick X with an identical merged sequence over many ticks of varied (incl. empty)
input; frames arriving in different orders still merge identically; a missing frame stalls
`TryDequeueStep`; input-delay places a command at `current+delay`; an injected hash mismatch
trips `Desynced` on both peers.

### M2 — ENet transport + game-loop integration (Godot; playtested)

- Godot `ENetMultiplayerPeer`: host `CreateServer(port)`, client `CreateClient(ip, port)`; the
  host relays/forwards frames so every peer sees every human's frame.
- **Command (de)serialization:** a compact binary writer/reader for the ~11 `Command` record
  types (ints, `int[]`, `FixVec` as two `long` raws, strings, enums). Tested round-trip
  (headless — this part is testable even though the socket layer isn't).
- RPCs: `SendFrame(tick, playerId, bytes)`, `SendHash(tick, playerId, hash)`.
- `SimRunner` integration: instead of draining a local `_queue` each tick, drive the
  coordinator — each tick submit the local queue as a frame + broadcast it; only `Step` when
  `TryDequeueStep` is ready; after stepping, `RecordLocalHash` + broadcast the hash. The local
  AI-vs-human single-player path stays available (a coordinator with one human and input
  delay 0 ≈ today's behavior), so single-player is unaffected.

### M3 — Lobby + slot configuration (Godot; playtested)

- Host screen: N slots, each set to local-human / open (joinable by a remote) / CPU+difficulty,
  plus a per-slot faction (from `PackCatalog`). Host shares IP; clients connect and claim an
  open slot. Host "Start" broadcasts the agreed config (slot→{kind, faction, difficulty}, seed)
  so every peer builds an identical world via a generalized `MatchSetup` (N slots; reuse the 5e
  role-resolved base placement). Extends the 5e menu rather than replacing it.

### M4 — Robustness (Godot + coordinator polish; playtested)

- Desync → halt + a "Desync detected" screen (rather than silent drift), surfacing the tick.
- Disconnect handling: pause + notify; v1 default = the dropped human's slot is conceded
  (defeat) or the match ends — simplest safe behavior. (Reconnect/AI-takeover deferred.)
- Input-delay tuning (latency vs stall trade-off; the sim is 10 ticks/s, so delay 2–3 =
  ~200–300 ms input latency — the standard RTS feel).

## The honest trade-offs (standard for RTS lockstep)

- The match advances at the speed of the **slowest** peer; input has a fixed delay (the buffer).
- A dropped/stalled peer halts everyone until M4 handles it.
- **Any** nondeterminism becomes a hard desync — which is exactly why the per-tick hash check
  exists and why the determinism discipline (fixed-point, command-only mutation, golden CI)
  was worth it. The hash check turns "mystery drift" into "caught at tick N on this peer."

## Testing strategy

- **M1:** exhaustive headless unit tests (multi-peer in-process; merge determinism, stall,
  input delay, desync detection). This is the correctness-critical piece and it's fully testable.
- **M2 serialization:** headless round-trip tests for every `Command` type. The socket/ENet
  layer + the `SimRunner` loop change are verified by manual two-instance playtest.
- **M3/M4:** manual playtest (two game instances on a LAN / two local processes); a desync is
  *provable* via the hash check (and can be forced in a test by feeding divergent input).
- SimCore determinism golden is untouched by all of this (multiplayer adds no hashed sim state;
  it only schedules existing commands).

## Decisions Log

- Deterministic lockstep over Godot ENet; only commands on the wire; CPU computed in-sim on
  every peer (zero network cost).
- M1 (coordinator) is a transport-agnostic, world-free, headless-testable `SimCore.Net` library
  — built and proven first; M2 (ENet + serialization + loop), M3 (lobby), M4 (robustness) follow.
- Input-delay lockstep (slowest-peer-paced); `StateHasher` per-tick desync detection halts on
  mismatch.
- LAN/direct-IP only for v1; relay/matchmaking, rollback, spectators, reconnect, replay-files
  all deferred.
