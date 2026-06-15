# Multiplayer M2 — ENet Transport + Game-Loop Integration — Design Spec

**Date:** 2026-06-15
**Status:** Approved (sub-project of the multiplayer umbrella design), pre-implementation.
**Position:** M2 of 4. Umbrella: `docs/superpowers/specs/2026-06-15-multiplayer-design.md`.
M1 (lockstep coordinator) ✅. **M2 = make a networked match actually run** over ENet, driving
the M1 coordinator. M3 (lobby) and M4 (robustness) follow.

## Goal

Two (or a few) machines on a LAN run a deterministic-lockstep match: exchange command frames +
state hashes over Godot ENet, drive the `LockstepCoordinator`, and stay in sync. Includes a
**minimal** Host/Join entry so it's playtestable before M3's full lobby exists.

## Testability split (be honest)

- **Headless-testable (TDD):** command (de)serialization — the `CommandCodec`. Pure C# in
  `SimCore.Net`; round-trip every command type.
- **Compile-checked + user-playtested:** the ENet socket layer (`NetSession`), the `SimRunner`
  networked loop, and the minimal Host/Join UI. There is no Godot/ENet runtime in CI, so these
  are verified by `dotnet build godot/LlmRts.Godot.csproj` (clean) + a **two-instance LAN
  playtest** (checklist provided). This mirrors 5e's split.

## Scope (M2)

**In scope:** binary `CommandCodec` for all `Command` types (+ `CommandFrame`); a `NetSession`
Godot node wrapping `ENetMultiplayerPeer` (host/join, peer connect/disconnect, host-relayed
frame + hash RPCs); a networked `SimRunner` path that drives the coordinator (submit→broadcast→
step→hash→broadcast, halt on desync); a minimal start handshake (host assigns player ids,
broadcasts seed); minimal Host / Join(IP) menu buttons starting a **fixed 2-human 1v1** (both
Reference faction, standard map).

**Out of scope (M3/M4):** full lobby + slot config + CPU/faction selection (M3); desync-halt
screen UI, disconnect-recovery, input-delay tuning UI, `_hashes` buffer bounding (M4); >2
players in the *minimal* M2 entry (the codec/coordinator already generalize; the M2 UI is
deliberately fixed-1v1). Single-player is untouched (keeps its existing direct path).

## CommandCodec (SimCore.Net; headless-tested)

```csharp
public static class CommandCodec
{
    public static void WriteFrame(System.IO.BinaryWriter w, CommandFrame frame);
    public static CommandFrame ReadFrame(System.IO.BinaryReader r);
    public static byte[] FrameToBytes(CommandFrame frame);
    public static CommandFrame FrameFromBytes(byte[] bytes);
}
```
- A frame serializes as: `Tick` (int), `PlayerId` (int), command count (int), then each command
  as a **1-byte type tag** + its fields.
- Per-command fields: ints as `Int32`; `int[]` as length + ints; `FixVec` as two `long` raws
  (`X.Raw`, `Y.Raw` → `new FixVec(new Fix(x), new Fix(y))`); `string` via `BinaryWriter.Write(string)`
  (length-prefixed UTF-8); enums (`Stance`) as `byte`. Covers all 11 command records
  (Move/Attack/AttackMove/Build/Train/Harvest/SetStance/Patrol/SetRally/Destroy/Research).
- Round-trip is exact and byte-identical across machines (no floats; `Fix.Raw` is a `long`).
- **Test:** a round-trip for every command type (and a multi-command frame, an empty frame)
  asserts the decoded frame equals the original field-by-field (compare via a small `Describe`/
  field check, since record `==` on `int[]` is by-reference).

## NetSession (Godot; compile-checked + playtested)

A `Node` wrapping Godot's high-level multiplayer:
- `void Host(int port)` → `ENetMultiplayerPeer.CreateServer(port)`, `Multiplayer.MultiplayerPeer = peer`.
- `void Join(string ip, int port)` → `CreateClient(ip, port)`.
- Tracks connected peers; exposes `event Action<int> PeerJoined / PeerLeft`, `IsHost`, and the
  assigned `LocalPlayerId`.
- `void SendFrame(CommandFrame frame)` / `void SendHash(int tick, int playerId, ulong hash)` —
  serialize and RPC. **Host-relay:** a client RPCs the host; the host re-broadcasts to all other
  peers (so every peer sees every human's frame). For host↔single-client (the M2 minimal case)
  this is a direct exchange. RPCs use Godot 4 `[Rpc(MultiplayerApi.RpcMode.AnyPeer, ...)]`
  methods + `Rpc(...)`/`RpcId(...)`; raises `event Action<CommandFrame> FrameReceived` and
  `event Action<int,int,ulong> HashReceived` for the runner.
- **Start handshake:** on the host, when the expected players are connected, assign player ids
  (host = 0, client = 1) and broadcast `Start(seed)`; clients build the match on receipt. (M2's
  minimal version: 2 fixed slots; M3 replaces with full config.)

## SimRunner networked loop (Godot; compile-checked + playtested)

Add a networked mode alongside the existing single-player `_Process`:
- When networked, each fixed-timestep iteration: `var frame = coordinator.SubmitLocal(_queue);
  _queue.Clear(); net.SendFrame(frame);` then drain: `while (!coordinator.Desynced &&
  coordinator.TryDequeueStep(out var cmds)) { World.Step(cmds); ulong h =
  StateHasher.Hash(World); coordinator.RecordLocalHash(NextStepTick-1, h); net.SendHash(...);
  Ticked?.Invoke(); }`. Incoming `FrameReceived`/`HashReceived` feed `coordinator.Receive` /
  `coordinator.ReceiveHash`. On `coordinator.Desynced`, stop stepping and log/raise (M4 adds the
  screen). **Single-player path is unchanged** (no `NetSession` → today's direct `_queue`→`Step`),
  so the golden and the existing sandbox/menu are unaffected.
- Input delay: a small constant (start at 3 ticks); exposed for M4 tuning.

## Minimal Host/Join entry (Godot)

`MenuScreen` gains "Host LAN game" and "Join" (with an IP text field, default `127.0.0.1`)
buttons. Host → `NetSession.Host`, wait for one client, start handshake → both build a fixed
1v1 (both human, Reference faction, standard map via `MatchSetup`) and enter the networked loop.
Join → `NetSession.Join(ip)`, receive `Start`, build the same match as player 1. This is the
throwaway-minimal launcher so M2 is playtestable; M3 replaces it with the real lobby.

## Determinism

The codec is exact (longs/ints/strings, no floats). The coordinator's ascending-PlayerId merge
(M1) means both peers feed `Step` the identical command sequence; CPU (none in the M2 minimal
match, but supported) would run in-sim identically. `StateHasher` per tick catches any drift.
SimCore + its golden are untouched (M2 is transport + Godot + the SimCore.Net codec only).

## Testing

- **Headless:** `CommandCodec` round-trips every command type + a multi-command frame + an empty
  frame (field-exact). Run in the SimCore.Tests suite.
- **Compile:** `dotnet build godot/LlmRts.Godot.csproj` clean.
- **Two-instance LAN playtest (user):** launch two instances; one Hosts, the other Joins
  `127.0.0.1` (or LAN IP); both enter a 1v1; issue commands on both — units/economy/combat stay
  identical on both screens; no desync banner; a winner shows on both. (A forced desync — e.g.
  feeding a divergent command on one side via a debug path — should trip the detector; optional.)

## Decisions Log

- `CommandCodec` (binary, in `SimCore.Net`, headless-tested) is the only fully-verifiable M2
  piece; `NetSession` + `SimRunner` loop + minimal menu are compile-checked + playtested.
- Host-relay frame/hash forwarding over Godot ENet; host=player0, client=player1 in the minimal
  M2 entry; input delay constant (3) for now.
- Single-player keeps its existing direct path (no coordinator) → zero single-player/golden risk.
- Minimal Host/Join 1v1 (both human, Reference) for playtest; M3 brings the real lobby
  (slots/factions/CPU/difficulty) and M4 the robustness (desync screen, disconnects, tuning,
  `_hashes` bound).
