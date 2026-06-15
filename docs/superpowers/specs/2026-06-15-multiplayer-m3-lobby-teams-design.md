# Multiplayer M3 — Lobby + 2v2 Team Play — Design Spec

**Date:** 2026-06-15
**Status:** Approved (design), pre-implementation. Sub-project of the multiplayer umbrella design.
**Position:** M3 of 4. Umbrella: `docs/superpowers/specs/2026-06-15-multiplayer-design.md`.
M1 (lockstep coordinator) ✅, M2 (ENet transport, playtested) ✅. **M3 = a real lobby + team
play**, all in one sub-project (user's choice). M4 (robustness) follows.

## Goal

A host-driven lobby where up to 4 players (humans — local or remote — and CPUs at any
difficulty) are configured into 2 teams, each picks/has a faction, and the host starts a
synchronized 2v2 (or any team split) deterministic-lockstep match. Replaces M2's fixed
2-human `BuildVersus1v1` launcher with a configurable, team-aware match.

## Decisions settled in brainstorming

- **Mode:** teams (2v2), up to 4 players, built all-in-one (lobby + team sim changes together).
- **Shared team vision:** allies see the union of their team's vision.
- **Faction selection:** each human picks their own faction; the host picks factions for CPU
  slots (and its own).
- **Lobby architecture:** an in-scene overlay (NOT a separate scene), so the ENet connection
  survives — a `ReloadCurrentScene` would drop the peer.

## The determinism keystone (why team play is golden-safe)

Every team feature **defaults each player to a solo team** (`Team[i] = i`). Under solo teams,
"same team" ⟺ "same player", so team-aware combat, victory, and vision are **behavior-identical
to today**. The golden determinism scenario (2 solo players) is unaffected → the golden
trajectory hash `1571756151672809223UL` is **unchanged, no re-pin**. Teams only change behavior
once the lobby groups players. The Debug+Release determinism gate verifies this at the end.

This property is the spec's central constraint: **every SimCore change below must be a no-op for
solo teams.** If any change forces a golden re-pin, that's a signal the equivalence was broken —
stop and re-examine rather than re-pinning.

## Scope (M3)

**In scope:**
- SimCore (all headless-TDD, behavior-preserving for solo teams): a per-player `Team`;
  `SameTeam`/`SetTeam`; ally-immune target acquisition + `AttackCommand`; team-aware victory;
  shared (team-union) vision used for targeting + rendering; `MatchSetup.BuildMatch(slots, seed)`
  for 2–4 players on a 4-corner map.
- Godot (compile-checked + playtested): `LobbyScreen` (slot table: Open / local-human /
  CPU+difficulty, team A/B toggle, per-slot faction); networked authoritative lobby-state sync +
  start-config broadcast on `NetSession`; building the configured world in-scene on Start;
  `FogView` rendering team vision; coordinator `humanPlayerIds` = human slot indices.

**Out of scope (M4 / later):** desync-halt screen; anti-cheat command validation (the M2 Tab
cross-command vector); disconnect recovery beyond pause; bounding the `_hashes` buffer;
input-delay tuning UI; >4 players; >2 teams; team-colored unit tinting / ally UI polish;
reconnect; map selection (single 4-corner 40×40 map). No change to the single-player path.

## SimCore changes (headless-TDD; each a no-op for solo teams)

### Team model
- `PlayerState.Team` (int; **default = the player's own index**, set in `SimWorld` player
  construction so existing worlds are all-solo).
- `SimWorld.SameTeam(int a, int b)` → `Players[a].Team == Players[b].Team`.
- `SimWorld.SetTeam(int playerId, int team)` — mirrors `SetCpu`; called by `MatchSetup`/lobby.
- Determinism: **`Team` is NOT hashed.** It is immutable start-config agreed by all peers via the
  start-config broadcast (like map dimensions, which aren't hashed) — so omitting it from the
  fingerprint cannot mask a desync (every peer has identical teams). Not hashing it also keeps the
  golden hash untouched. (Controller/Difficulty are hashed, but adding `Team` would force a re-pin
  for zero desync-detection benefit, so we deliberately don't.) **Acceptance: golden unchanged.**

### Ally-immune combat
- Wherever combat **acquires** a target (auto-attack scan, attack-move acquisition) the
  enemy test changes from `owner != candidateOwner` to `!SameTeam(owner, candidateOwner)`.
- An explicit `AttackCommand` whose target is a same-team unit/building is ignored (no-op),
  consistent with acquisition.
- Buildings are valid targets only if on a different team (same rule).
- Solo-team equivalence: `!SameTeam` ⟺ different owner when teams are unique → identical
  targeting → golden unchanged. (The golden scenario never targets same-owner units, so no path
  changes.)

### Team-aware victory
- `UpdateMatchState` latches `Over` when **all players that still own ≥1 building share one
  team** (or zero remain). `WinnerId` = a deterministic representative of the winning team — the
  **lowest player index that still owns a building** on the winning team (or -1 if none remain =
  draw). `WinnerId` stays a player id, so **no new hashed field** and the solo case is
  byte-identical (one survivor ⇒ `WinnerId` = that player, exactly as today).
- Front-end "did I win?" = `SameTeam(localPlayerId, WinnerId)` (and `WinnerId >= 0`).
- Solo-team equivalence: "all building-owners share a team" with unique teams = "≤1 player owns
  a building" = today's rule; representative = the survivor = today's `WinnerId`. Golden
  unchanged.

### Shared team vision
- Per-player visibility is computed exactly as today. The **effective** visible set used for
  (a) fog-gated targeting and (b) rendering becomes the **union over the player's team**.
  Implementation: after per-player vision is computed in `UpdateVision`, derive a per-team
  visible set (union of member players' visible cells), and route fog-gated target checks +
  the Godot fog view through team visibility (`IsVisibleToTeam(team, cell)` or equivalent).
- Determinism: per-team vision = per-player vision when teams are solo → no behavior change →
  golden unchanged. Team vision is a deterministic function of (deterministic) per-player vision.
- Note: confirm whether visible/explored state is hashed today. Targeting reads *visible*; if
  the change only unions an already-recomputed-per-tick visible set, no persistent hashed state
  changes for solo. Verify with the gate.

### Generalized match builder
- New SimCore record `MatchSlot(FactionDef Faction, PlayerController Controller,
  AiDifficulty Difficulty, int Team)`.
- `MatchSetup.BuildMatch(IReadOnlyList<MatchSlot> slots, ulong seed)`: builds an N-player world
  (N = `slots.Count`, 2–4); `FactionDef?[]` from the slots; for each slot `SetCpu` if CPU +
  `SetTeam`; place a role-resolved base (reuse the existing `PlaceBase`) at corner *i*. Extend
  the map to **4 corners** — corner specs `(depot, rax, node, worker)` for indices 0..3
  (existing 2 corners + the two opposite corners), each with mineral nodes + workers.
- Re-express `BuildStandard1v1`/`BuildVersus1v1` on top of `BuildMatch` (behavior-preserving:
  same 2 corners, same controllers/teams) so there's one builder. Confirm the sandbox/menu
  worlds are byte-identical (they use corners 0 and 1).
- Headless tests: 4 slots / 2 teams → 4 players, correct `FactionFor`/`Controller`/`Difficulty`/
  `Team`, all based at distinct corners; team victory (destroy one team's buildings → other team
  wins, `SameTeam(survivor, WinnerId)`); ally-immune combat (a unit does not acquire a same-team
  unit); 2-slot `BuildMatch` matches the old `BuildVersus1v1` output.

## Lobby (Godot — compile-checked + playtested)

### Lobby state (authoritative on host)
A serializable slot list, host-owned:
```
SlotKind   = Open | LocalHuman | RemoteHuman | Cpu
LobbySlot  = { SlotKind Kind; int Team (0/1); string FactionId; AiDifficulty Difficulty;
               int OccupantPeerId (for RemoteHuman, the Godot peer that claimed it) }
```
- The host initializes 2–4 slots (default: slot 0 = LocalHuman team 0; the rest a sensible mix,
  editable). The host can set any slot to Open / CPU(+difficulty), its team (A/B), and the
  faction for CPU/own slots.
- A joining client is assigned the next Open slot (becomes RemoteHuman with its peerId) and may
  set **its own** faction; that choice is sent to the host.

### Networked sync (on `NetSession`)
- Host broadcasts the **full slot list** whenever it changes (`Rpc` `SyncLobbyRpc(bytes)` — a
  small binary/`Json` encoding of the slot list; reliable, channel 0 like the M2 RPCs).
- Client → host: `ClaimSlotRpc` / `SetMyFactionRpc(factionId)` (`RpcId(1, …)`); the host applies
  and rebroadcasts. **Host is the single writer** of authoritative state → no merge conflicts.
- Host "Start": broadcast `StartConfigRpc(bytes)` carrying `{ slots:[{kind,team,factionId,
  difficulty}], seed }`; every peer (host included, locally) builds the match from it.
- `NetSession` generalizes M2's fixed host=0/client=1: player id = slot index; the host maps
  each connected Godot peer → its claimed slot index. Frame/hash host-relay already generalizes
  to >2 peers (M2). `humanPlayerIds` = indices of LocalHuman+RemoteHuman slots; `localPlayerId`
  = this peer's slot.

### In-scene lobby + start (no reload — preserves ENet)
- `Main._Ready` (networked): create the persistent `NetSession`, build a **default world**
  (so the views initialize — the map is the same 40×40 for every config), `Runner.Paused = true`,
  and show the `LobbyScreen` overlay (instead of M2's immediate start handshake).
- On `StartConfigRpc`/host Start: build the configured world via `BuildMatch(slots, seed)`,
  `Runner.Init(world)` + a view re-sync (`ViewSync.ForceSync`, selection reset, minimap redraw —
  the map is unchanged so map-bound views need no rebuild), construct the
  `LockstepCoordinator(localSlot, humanSlotIds, InputDelay)`, `Runner.InitNetworked(...)`, hide
  the lobby, `Paused = false`, add the `GameOverScreen`. Lock `Selection.ControlledPlayer` to the
  local slot; `FogView` uses the local player's team vision.
- Rationale: `ReloadCurrentScene` frees `NetSession` and drops the ENet peer, so lobby→match must
  happen **without** a reload. The world swap is clean because the map is identical across configs.

### `LobbyScreen` (in-code CanvasLayer, existing UI style)
- A row per slot: a Kind control (Open/CPU+difficulty; the host's own row is LocalHuman), a Team
  A/B toggle, a faction `OptionButton` (from `PackCatalog`), and (for the host) add/remove-slot
  to vary 2–4. Read-only for clients except their own slot's faction. A Start button (host only,
  enabled when the config is valid — every slot filled, both teams non-empty). An IP/host info
  line. Reuses `MenuScreen`'s `PackCatalog` + difficulty patterns.
- Menu entry: `MenuScreen`'s existing "Host LAN game" / "Join" now lead into the lobby (set
  `MatchConfig.IsNetworked` + host/ip as in M2; Main shows the lobby overlay instead of starting).

## Determinism

- All team changes are no-ops for solo teams → golden `1571756151672809223UL` unchanged
  (verified Debug+Release at the end; if it changes, the solo-equivalence was violated — fix,
  don't re-pin).
- The start-config broadcast guarantees every peer calls `BuildMatch` with the **same** slot
  list + seed → identical worlds. CPU slots run in-sim (`UpdateAi`) identically on every peer →
  zero network cost, no desync.
- Commands remain the only wire input; merged ascending-PlayerId (M1). The per-tick `StateHasher`
  exchange (M2) catches any latent team-path nondeterminism.

## Testing

- **Headless (SimCore, TDD):** team model + `SameTeam`; ally-immune acquisition + `AttackCommand`
  no-op on allies; team victory (representative `WinnerId`, draw); team vision union; `BuildMatch`
  (4 players/2 teams based + configured; 2-slot ≡ old `BuildVersus1v1`). Plus a CPU-vs-CPU **2v2**
  determinism replay (two runs, identical hash) to prove team play is deterministic.
- **Determinism gate:** Debug == Release full suite; golden unchanged; `StateHasher` only touched
  if golden survives.
- **Compile:** `dotnet build godot/LlmRts.Godot.csproj` clean.
- **Two-instance LAN playtest (user):** host configures a 2v2 (e.g. you + CPU ally vs 2 CPUs, or
  two humans + CPUs), each human picks a faction, host starts; both peers see an identical match;
  allies don't attack each other and share vision; destroying a team's last buildings shows
  Victory for the winning team on its members and Defeat on the losers; no desync banner.

## Decisions Log

- All-in-one M3: lobby + 2v2 team play (combat ally-immunity, team victory, shared vision).
- Solo-team default makes every team change behavior-preserving → golden unchanged, no re-pin.
- `WinnerId` stays a representative player id (no new hashed field); front-end derives team win.
- One generalized `MatchSetup.BuildMatch(slots, seed)`; `BuildStandard1v1`/`BuildVersus1v1`
  re-expressed on it; map extended to 4 corners.
- Host-authoritative lobby state, broadcast on change + on Start; clients pick only their own
  faction/slot. In-scene lobby overlay (no reload) to preserve the ENet connection.
- Up to 4 players, exactly 2 teams; >4 players, >2 teams, team coloring, and anti-cheat
  validation deferred (M4/later).
