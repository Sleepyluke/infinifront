# Multiplayer M2 — ENet Transport + Game-Loop Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a deterministic-lockstep match actually run over a LAN — two game instances exchange command frames + state hashes via Godot ENet, drive the M1 `LockstepCoordinator`, and stay bit-identical.

**Architecture:** A headless-tested binary `CommandCodec` (in `SimCore.Net`) serializes a `CommandFrame` to bytes. A Godot `NetSession` node wraps `ENetMultiplayerPeer` (host/join, a tiny start handshake, host-relayed frame/hash RPCs). `SimRunner` gains a networked loop that primes the input-delay pipeline, then steps only when the coordinator has every human's frame, hashing each step and broadcasting it. A minimal Host/Join menu entry starts a fixed 2-human 1v1 so it's playtestable; single-player keeps its existing direct path untouched.

**Tech Stack:** C# (.NET 8), Godot 4.6 .NET high-level multiplayer (`ENetMultiplayerPeer`, `[Rpc]`), `SimCore`/`SimCore.Net`, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-15-multiplayer-m2-transport-design.md`. **Umbrella:** `docs/superpowers/specs/2026-06-15-multiplayer-design.md`.

**Determinism contract (do not break):** only commands cross the wire; both peers feed `Step` the identical merged sequence (M1's ascending-PlayerId merge). SimCore is untouched by M2 → golden trajectory hash stays `1571756151672809223UL`. No floats anywhere in serialization (`Fix.Raw` is a `long`).

**Known landmine (carried from earlier sub-projects):** files under `src/SimCore*` use `using SimCore.Math;`, which shadows `System.Math`. If you need `System.Math.Round` etc., qualify it fully. (Not expected in M2, but noted.)

---

## File Structure

- **Create** `src/SimCore.Net/CommandCodec.cs` — static binary (de)serializer for `Command`/`CommandFrame`. Pure C#, no Godot, no floats. (Task 1)
- **Create** `tests/SimCore.Tests/Net/CommandCodecTests.cs` — round-trip tests for every command type. (Task 1)
- **Modify** `src/SimCore/Sim/MatchSetup.cs` — extract `BuildMap()`; add `BuildVersus1v1` (2-human 1v1, no CPU). (Task 2)
- **Create** `tests/SimCore.Tests/MatchSetupVersusTests.cs` — both players human + based. (Task 2)
- **Create** `godot/scripts/NetSession.cs` — ENet transport + handshake + frame/hash RPCs. (Task 3)
- **Modify** `godot/scripts/SimRunner.cs` — `InitNetworked` + networked `_Process` branch + `Desynced` event. (Task 4)
- **Modify** `godot/scripts/MatchConfig.cs` — networked fields + `SetNetwork`. (Task 4)
- **Modify** `godot/scripts/MenuScreen.cs` — Host / Join(IP) buttons. (Task 4)
- **Modify** `godot/scripts/Main.cs` — networked boot (create `NetSession`, gate start on `MatchReady`). (Task 4)

---

## Task 1: CommandCodec (binary serialization) — SimCore.Net, headless TDD

**Files:**
- Create: `src/SimCore.Net/CommandCodec.cs`
- Test: `tests/SimCore.Tests/Net/CommandCodecTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/SimCore.Tests/Net/CommandCodecTests.cs`:

```csharp
using System.Linq;
using SimCore.Math;
using SimCore.Net;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests.Net;

public class CommandCodecTests
{
    // Round-trip a one-command frame and return the decoded command.
    private static Command RoundTripOne(Command c)
    {
        var frame = new CommandFrame(Tick: 7, PlayerId: 1, Commands: new[] { c });
        var back = CommandCodec.FrameFromBytes(CommandCodec.FrameToBytes(frame));
        Assert.Equal(7, back.Tick);
        Assert.Equal(1, back.PlayerId);
        Assert.Single(back.Commands);
        return back.Commands[0];
    }

    [Fact]
    public void Move_RoundTrips()
    {
        var c = (MoveCommand)RoundTripOne(new MoveCommand(1, new[] { 3, 5, 8 }, new FixVec(Fix.FromInt(12), Fix.FromInt(-4))));
        Assert.Equal(1, c.PlayerId);
        Assert.Equal(new[] { 3, 5, 8 }, c.UnitIds);
        Assert.Equal(new FixVec(Fix.FromInt(12), Fix.FromInt(-4)), c.Target);
    }

    [Fact]
    public void Attack_RoundTrips()
    {
        var c = (AttackCommand)RoundTripOne(new AttackCommand(1, new[] { 9 }, TargetId: 42));
        Assert.Equal(new[] { 9 }, c.UnitIds);
        Assert.Equal(42, c.TargetId);
    }

    [Fact]
    public void AttackMove_RoundTrips()
    {
        var c = (AttackMoveCommand)RoundTripOne(new AttackMoveCommand(1, new[] { 2, 4 }, new FixVec(Fix.FromInt(7), Fix.FromInt(7))));
        Assert.Equal(new[] { 2, 4 }, c.UnitIds);
        Assert.Equal(new FixVec(Fix.FromInt(7), Fix.FromInt(7)), c.Target);
    }

    [Fact]
    public void Build_RoundTrips()
    {
        var c = (BuildCommand)RoundTripOne(new BuildCommand(1, WorkerUnitId: 11, BuildingDefId: "depot", CellX: 4, CellY: 9));
        Assert.Equal(11, c.WorkerUnitId);
        Assert.Equal("depot", c.BuildingDefId);
        Assert.Equal(4, c.CellX);
        Assert.Equal(9, c.CellY);
    }

    [Fact]
    public void Train_RoundTrips()
    {
        var c = (TrainCommand)RoundTripOne(new TrainCommand(1, BuildingId: 6, UnitDefId: "marine"));
        Assert.Equal(6, c.BuildingId);
        Assert.Equal("marine", c.UnitDefId);
    }

    [Fact]
    public void Harvest_RoundTrips()
    {
        var c = (HarvestCommand)RoundTripOne(new HarvestCommand(1, new[] { 1, 2 }, NodeId: 99));
        Assert.Equal(new[] { 1, 2 }, c.UnitIds);
        Assert.Equal(99, c.NodeId);
    }

    [Fact]
    public void SetStance_RoundTrips()
    {
        var c = (SetStanceCommand)RoundTripOne(new SetStanceCommand(1, new[] { 3 }, Stance.Passive));
        Assert.Equal(new[] { 3 }, c.UnitIds);
        Assert.Equal(Stance.Passive, c.Stance);
    }

    [Fact]
    public void Patrol_RoundTrips()
    {
        var c = (PatrolCommand)RoundTripOne(new PatrolCommand(1, new[] { 5 }, new FixVec(Fix.FromInt(1), Fix.FromInt(2))));
        Assert.Equal(new[] { 5 }, c.UnitIds);
        Assert.Equal(new FixVec(Fix.FromInt(1), Fix.FromInt(2)), c.Target);
    }

    [Fact]
    public void SetRally_RoundTrips()
    {
        var c = (SetRallyCommand)RoundTripOne(new SetRallyCommand(1, BuildingId: 8, new FixVec(Fix.FromInt(10), Fix.FromInt(11)), Clear: true));
        Assert.Equal(8, c.BuildingId);
        Assert.Equal(new FixVec(Fix.FromInt(10), Fix.FromInt(11)), c.Target);
        Assert.True(c.Clear);
    }

    [Fact]
    public void Destroy_RoundTrips()
    {
        var c = (DestroyCommand)RoundTripOne(new DestroyCommand(1, new[] { 12, 13, 14 }));
        Assert.Equal(new[] { 12, 13, 14 }, c.Ids);
    }

    [Fact]
    public void Research_RoundTrips()
    {
        var c = (ResearchCommand)RoundTripOne(new ResearchCommand(1, BuildingId: 3, UpgradeDefId: "weapons1"));
        Assert.Equal(3, c.BuildingId);
        Assert.Equal("weapons1", c.UpgradeDefId);
    }

    [Fact]
    public void EmptyFrame_RoundTrips()
    {
        var frame = new CommandFrame(Tick: 0, PlayerId: 0, Commands: System.Array.Empty<Command>());
        var back = CommandCodec.FrameFromBytes(CommandCodec.FrameToBytes(frame));
        Assert.Equal(0, back.Tick);
        Assert.Equal(0, back.PlayerId);
        Assert.Empty(back.Commands);
    }

    [Fact]
    public void MultiCommandFrame_RoundTrips_InOrder()
    {
        var frame = new CommandFrame(3, 1, new Command[]
        {
            new MoveCommand(1, new[] { 1 }, new FixVec(Fix.FromInt(5), Fix.FromInt(5))),
            new TrainCommand(1, 2, "marine"),
            new DestroyCommand(1, new[] { 9 }),
        });
        var back = CommandCodec.FrameFromBytes(CommandCodec.FrameToBytes(frame));
        Assert.Equal(3, back.Commands.Count);
        Assert.IsType<MoveCommand>(back.Commands[0]);
        Assert.IsType<TrainCommand>(back.Commands[1]);
        Assert.IsType<DestroyCommand>(back.Commands[2]);
    }

    [Fact]
    public void Reserialization_Is_Byte_Stable()
    {
        var frame = new CommandFrame(123, 1, new Command[]
        {
            new BuildCommand(1, 11, "depot", 4, 9),
            new SetRallyCommand(1, 8, new FixVec(Fix.FromInt(10), Fix.FromInt(11)), true),
        });
        var bytes1 = CommandCodec.FrameToBytes(frame);
        var bytes2 = CommandCodec.FrameToBytes(CommandCodec.FrameFromBytes(bytes1));
        Assert.Equal(bytes1, bytes2);
    }
}
```

- [ ] **Step 2: Run the tests, expect failure**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: build/compile FAIL — `CommandCodec` does not exist.

(If `dotnet` is not found in Bash on Windows, prepend the path: `export PATH="$PATH:/c/Program Files/dotnet"`.)

- [ ] **Step 3: Implement CommandCodec**

Create `src/SimCore.Net/CommandCodec.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using SimCore.Math;
using SimCore.Sim;

namespace SimCore.Net;

/// <summary>Compact, deterministic binary (de)serialization of CommandFrames so they can cross
/// an RPC. No floats: FixVec is two long raws. BinaryWriter/Reader are little-endian on every
/// platform, so the wire format is cross-OS stable. A 1-byte tag selects the command type.</summary>
public static class CommandCodec
{
    // Tag bytes — append-only; never renumber (the wire format depends on these).
    private const byte TagMove = 0, TagAttack = 1, TagAttackMove = 2, TagBuild = 3, TagTrain = 4,
        TagHarvest = 5, TagSetStance = 6, TagPatrol = 7, TagSetRally = 8, TagDestroy = 9, TagResearch = 10;

    public static byte[] FrameToBytes(CommandFrame frame)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) WriteFrame(w, frame);
        return ms.ToArray();
    }

    public static CommandFrame FrameFromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        return ReadFrame(r);
    }

    public static void WriteFrame(BinaryWriter w, CommandFrame frame)
    {
        w.Write(frame.Tick);
        w.Write(frame.PlayerId);
        w.Write(frame.Commands.Count);
        foreach (var c in frame.Commands) WriteCommand(w, c);
    }

    public static CommandFrame ReadFrame(BinaryReader r)
    {
        int tick = r.ReadInt32();
        int playerId = r.ReadInt32();
        int count = r.ReadInt32();
        var cmds = new Command[count];
        for (int i = 0; i < count; i++) cmds[i] = ReadCommand(r);
        return new CommandFrame(tick, playerId, cmds);
    }

    private static void WriteCommand(BinaryWriter w, Command c)
    {
        switch (c)
        {
            case MoveCommand m:
                w.Write(TagMove); w.Write(m.PlayerId); WriteInts(w, m.UnitIds); WriteVec(w, m.Target); break;
            case AttackCommand a:
                w.Write(TagAttack); w.Write(a.PlayerId); WriteInts(w, a.UnitIds); w.Write(a.TargetId); break;
            case AttackMoveCommand am:
                w.Write(TagAttackMove); w.Write(am.PlayerId); WriteInts(w, am.UnitIds); WriteVec(w, am.Target); break;
            case BuildCommand b:
                w.Write(TagBuild); w.Write(b.PlayerId); w.Write(b.WorkerUnitId); w.Write(b.BuildingDefId); w.Write(b.CellX); w.Write(b.CellY); break;
            case TrainCommand t:
                w.Write(TagTrain); w.Write(t.PlayerId); w.Write(t.BuildingId); w.Write(t.UnitDefId); break;
            case HarvestCommand h:
                w.Write(TagHarvest); w.Write(h.PlayerId); WriteInts(w, h.UnitIds); w.Write(h.NodeId); break;
            case SetStanceCommand s:
                w.Write(TagSetStance); w.Write(s.PlayerId); WriteInts(w, s.UnitIds); w.Write((byte)s.Stance); break;
            case PatrolCommand p:
                w.Write(TagPatrol); w.Write(p.PlayerId); WriteInts(w, p.UnitIds); WriteVec(w, p.Target); break;
            case SetRallyCommand sr:
                w.Write(TagSetRally); w.Write(sr.PlayerId); w.Write(sr.BuildingId); WriteVec(w, sr.Target); w.Write(sr.Clear); break;
            case DestroyCommand d:
                w.Write(TagDestroy); w.Write(d.PlayerId); WriteInts(w, d.Ids); break;
            case ResearchCommand rc:
                w.Write(TagResearch); w.Write(rc.PlayerId); w.Write(rc.BuildingId); w.Write(rc.UpgradeDefId); break;
            default:
                throw new System.NotSupportedException($"CommandCodec cannot serialize {c.GetType().Name}");
        }
    }

    private static Command ReadCommand(BinaryReader r)
    {
        byte tag = r.ReadByte();
        switch (tag)
        {
            case TagMove: { int p = r.ReadInt32(); var ids = ReadInts(r); return new MoveCommand(p, ids, ReadVec(r)); }
            case TagAttack: { int p = r.ReadInt32(); var ids = ReadInts(r); return new AttackCommand(p, ids, r.ReadInt32()); }
            case TagAttackMove: { int p = r.ReadInt32(); var ids = ReadInts(r); return new AttackMoveCommand(p, ids, ReadVec(r)); }
            case TagBuild: { int p = r.ReadInt32(); int wu = r.ReadInt32(); string bid = r.ReadString(); int cx = r.ReadInt32(); int cy = r.ReadInt32(); return new BuildCommand(p, wu, bid, cx, cy); }
            case TagTrain: { int p = r.ReadInt32(); int b = r.ReadInt32(); return new TrainCommand(p, b, r.ReadString()); }
            case TagHarvest: { int p = r.ReadInt32(); var ids = ReadInts(r); return new HarvestCommand(p, ids, r.ReadInt32()); }
            case TagSetStance: { int p = r.ReadInt32(); var ids = ReadInts(r); return new SetStanceCommand(p, ids, (Stance)r.ReadByte()); }
            case TagPatrol: { int p = r.ReadInt32(); var ids = ReadInts(r); return new PatrolCommand(p, ids, ReadVec(r)); }
            case TagSetRally: { int p = r.ReadInt32(); int b = r.ReadInt32(); var t = ReadVec(r); return new SetRallyCommand(p, b, t, r.ReadBoolean()); }
            case TagDestroy: { int p = r.ReadInt32(); return new DestroyCommand(p, ReadInts(r)); }
            case TagResearch: { int p = r.ReadInt32(); int b = r.ReadInt32(); return new ResearchCommand(p, b, r.ReadString()); }
            default: throw new System.NotSupportedException($"CommandCodec: unknown command tag {tag}");
        }
    }

    private static void WriteInts(BinaryWriter w, int[] xs)
    {
        w.Write(xs.Length);
        foreach (int x in xs) w.Write(x);
    }

    private static int[] ReadInts(BinaryReader r)
    {
        int n = r.ReadInt32();
        var xs = new int[n];
        for (int i = 0; i < n; i++) xs[i] = r.ReadInt32();
        return xs;
    }

    private static void WriteVec(BinaryWriter w, FixVec v) { w.Write(v.X.Raw); w.Write(v.Y.Raw); }
    private static FixVec ReadVec(BinaryReader r) => new(new Fix(r.ReadInt64()), new Fix(r.ReadInt64()));
}
```

- [ ] **Step 4: Run the tests, expect pass**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS — all CommandCodec tests green; total SimCore count rises by 14 (328 → 342).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore.Net/CommandCodec.cs tests/SimCore.Tests/Net/CommandCodecTests.cs
git commit -m "feat(net): CommandCodec — binary (de)serialization for command frames

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 2: MatchSetup.BuildVersus1v1 (2-human 1v1) — SimCore, headless TDD

**Files:**
- Modify: `src/SimCore/Sim/MatchSetup.cs`
- Test: `tests/SimCore.Tests/MatchSetupVersusTests.cs`

A networked 1v1 has **both** players human — no `SetCpu`. (If player 1 were CPU, both peers would run the AI for it *and* the remote human would send its commands → double-driven slot.) Extract the shared map + base placement so `BuildStandard1v1` and `BuildVersus1v1` stay DRY.

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/MatchSetupVersusTests.cs`:

```csharp
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class MatchSetupVersusTests
{
    [Fact]
    public void Versus1v1_Has_Two_Human_Players_Each_Based()
    {
        var w = MatchSetup.BuildVersus1v1(ReferenceFaction.Def, ReferenceFaction.Def, seed: 42);

        Assert.Equal(PlayerController.Human, w.Players[0].Controller);
        Assert.Equal(PlayerController.Human, w.Players[1].Controller);

        Assert.Contains(w.Buildings, b => b.Owner == 0);
        Assert.Contains(w.Buildings, b => b.Owner == 1);
        Assert.Contains(w.Units, u => u.Owner == 0);
        Assert.Contains(w.Units, u => u.Owner == 1);

        Assert.Same(ReferenceFaction.Def, w.FactionFor(0));
        Assert.Same(ReferenceFaction.Def, w.FactionFor(1));
        Assert.Equal(MatchPhase.InProgress, w.Phase);
    }

    [Fact]
    public void Versus1v1_Steps_Without_Throwing()
    {
        var w = MatchSetup.BuildVersus1v1(ReferenceFaction.Def, ReferenceFaction.Def, seed: 1);
        for (int i = 0; i < 30; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(MatchPhase.InProgress, w.Phase); // nobody loses in 30 idle ticks
    }
}
```

**Before running:** confirm the exact property names used above against the current source — `Building.Owner`, `Unit.Owner`, `SimWorld.Buildings`, `SimWorld.Units`, `SimWorld.FactionFor(int)`, `SimWorld.Phase`, and the phase enum value `MatchPhase.InProgress` (grep `enum MatchPhase` / `enum MatchState`, and `public MatchPhase Phase` in `src/SimCore/Sim/`). If any differ (e.g. the enum is `MatchState` or the property is `OwnerId`), adjust the test to match — do **not** change the sim to fit the test.

- [ ] **Step 2: Run the test, expect failure**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: compile FAIL — `MatchSetup.BuildVersus1v1` does not exist.

- [ ] **Step 3: Refactor MatchSetup + add BuildVersus1v1**

In `src/SimCore/Sim/MatchSetup.cs`, replace the `BuildStandard1v1` method (lines ~12–26) with this — it extracts `BuildMap()` and adds `BuildVersus1v1`. Leave `PlaceBase`, `FirstWorker`, `CheapestCombat` exactly as they are.

```csharp
    public static SimWorld BuildStandard1v1(FactionDef humanFaction, FactionDef cpuFaction,
                                            AiDifficulty difficulty, ulong seed)
    {
        var w = new SimWorld(BuildMap(), seed, new FactionDef?[] { humanFaction, cpuFaction });
        w.SetCpu(1, difficulty);
        PlaceBase(w, 0, humanFaction, depotX: 4, depotY: 4, raxX: 8, raxY: 4, nodeX: 2, nodeY: 8, workerX: 6, workerY: 8);
        PlaceBase(w, 1, cpuFaction, depotX: 34, depotY: 34, raxX: 30, raxY: 34, nodeX: 37, nodeY: 28, workerX: 33, workerY: 28);
        return w;
    }

    /// <summary>Networked 1v1: BOTH players are human (no CPU). Same map + base placement as the
    /// standard 1v1, minus SetCpu. Deterministic from the seed; identical on every peer.</summary>
    public static SimWorld BuildVersus1v1(FactionDef p0Faction, FactionDef p1Faction, ulong seed)
    {
        var w = new SimWorld(BuildMap(), seed, new FactionDef?[] { p0Faction, p1Faction });
        PlaceBase(w, 0, p0Faction, depotX: 4, depotY: 4, raxX: 8, raxY: 4, nodeX: 2, nodeY: 8, workerX: 6, workerY: 8);
        PlaceBase(w, 1, p1Faction, depotX: 34, depotY: 34, raxX: 30, raxY: 34, nodeX: 37, nodeY: 28, workerX: 33, workerY: 28);
        return w;
    }

    private static MapGrid BuildMap()
    {
        var map = new MapGrid(MapSize, MapSize);
        // Rock ridge at x=20 with gaps at y=8..11 and y=28..31 (matches the legacy sandbox).
        for (int y = 0; y < MapSize; y++)
            if (y < 8 || (y > 11 && y < 28) || y > 31) map.SetPassable(20, y, false);
        return map;
    }
```

- [ ] **Step 4: Run the tests, expect pass**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS — the two new tests green; SimCore count 342 → 344. The refactor is behavior-preserving (`BuildStandard1v1` produces the identical world), so all existing MatchSetup/sandbox tests stay green.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/MatchSetup.cs tests/SimCore.Tests/MatchSetupVersusTests.cs
git commit -m "feat(sim): MatchSetup.BuildVersus1v1 — 2-human 1v1 for networked play

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 3: NetSession — Godot ENet transport + handshake (compile-checked)

**Files:**
- Create: `godot/scripts/NetSession.cs`

`NetSession` is a `Node` wrapping Godot's high-level multiplayer. It hosts/joins, runs a tiny start handshake (host assigns player ids and broadcasts the seed), and relays frame/hash RPCs so every peer sees every human's frame. It is **transport only** — it raises events; `SimRunner` (Task 4) owns the coordinator and world. Not headless-testable (no ENet in CI); verified by compile + the Task 5 playtest.

> **Godot RPC notes:** `[Rpc]` methods must live on a node whose path is identical on all peers (`NetSession` is added as a child named `"Net"` of the same scene → same path). `ulong` is not a Godot Variant type, so hashes/seed travel as `long` bits via `unchecked`. `Rpc(MethodName.X, ...)` broadcasts to all directly-connected peers; `RpcId(id, MethodName.X, ...)` targets one. The server's peer id is always `1`.

- [ ] **Step 1: Create NetSession**

Create `godot/scripts/NetSession.cs`:

```csharp
using System;
using Godot;
using SimCore.Net;

namespace LlmRts.Godot;

/// <summary>ENet transport for deterministic lockstep. Host/Join, a start handshake (host assigns
/// player ids + seed), and host-relayed frame/hash RPCs. Raises events for SimRunner to drive the
/// LockstepCoordinator. M2 minimal: exactly 2 players (host=0, client=1).</summary>
public partial class NetSession : Node
{
    public const int Port = 7777;
    public const ulong MatchSeed = 42;   // M2: fixed; M3's lobby makes this host-chosen.
    public const int InputDelay = 3;     // ~300 ms at 10 ticks/s.

    public bool IsHost { get; private set; }
    public int LocalPlayerId { get; private set; }

    /// <summary>Fired once the handshake completes: (localPlayerId, seed). Build the world + start.</summary>
    public event Action<int, ulong>? MatchReady;
    /// <summary>A remote human's command frame arrived → coordinator.Receive.</summary>
    public event Action<CommandFrame>? FrameReceived;
    /// <summary>A remote peer's per-tick state hash arrived → coordinator.ReceiveHash.</summary>
    public event Action<int, int, ulong>? HashReceived;
    /// <summary>A peer dropped (M4 handles recovery; M2 just surfaces it).</summary>
    public event Action? PeerDropped;

    private bool _started;

    public void Host()
    {
        IsHost = true;
        LocalPlayerId = 0;
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(Port, maxClients: 8);
        if (err != Error.Ok) { GD.PrintErr($"NetSession host failed: {err}"); return; }
        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        GD.Print($"NetSession hosting on :{Port}");
    }

    public void Join(string ip)
    {
        IsHost = false;
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(ip, Port);
        if (err != Error.Ok) { GD.PrintErr($"NetSession join failed: {err}"); return; }
        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.ConnectedToServer += () => GD.Print("NetSession connected to host");
        Multiplayer.ConnectionFailed += () => GD.PrintErr("NetSession connection failed");
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        GD.Print($"NetSession joining {ip}:{Port}");
    }

    // ---- Handshake (host) ----
    private void OnPeerConnected(long peerId)
    {
        if (!IsHost || _started) return;       // M2: start on the first client.
        _started = true;
        // M2 minimal: the one client is player 1.
        RpcId(peerId, MethodName.StartMatchRpc, unchecked((long)MatchSeed), 1);
        // Host is player 0 — fire locally (RPCs are CallLocal=false).
        MatchReady?.Invoke(0, MatchSeed);
    }

    private void OnPeerDisconnected(long peerId) => PeerDropped?.Invoke();
    private void OnServerDisconnected() => PeerDropped?.Invoke();

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartMatchRpc(long seedBits, int assignedPlayerId)
    {
        LocalPlayerId = assignedPlayerId;
        MatchReady?.Invoke(assignedPlayerId, unchecked((ulong)seedBits));
    }

    // ---- Frames ----
    public void SendFrame(CommandFrame frame)
    {
        var bytes = CommandCodec.FrameToBytes(frame);
        if (IsHost) Rpc(MethodName.ReceiveFrameRpc, bytes);          // host → all clients
        else RpcId(1, MethodName.ReceiveFrameRpc, bytes);           // client → host (relays on)
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveFrameRpc(byte[] bytes)
    {
        FrameReceived?.Invoke(CommandCodec.FrameFromBytes(bytes));
        if (IsHost) RelayToOthers(MethodName.ReceiveFrameRpc, bytes);  // forward to other clients (no-op for 2P)
    }

    // ---- Hashes ----
    public void SendHash(int tick, int playerId, ulong hash)
    {
        long bits = unchecked((long)hash);
        if (IsHost) Rpc(MethodName.ReceiveHashRpc, tick, playerId, bits);
        else RpcId(1, MethodName.ReceiveHashRpc, tick, playerId, bits);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveHashRpc(int tick, int playerId, long hashBits)
    {
        HashReceived?.Invoke(tick, playerId, unchecked((ulong)hashBits));
        if (IsHost) RelayToOthers(MethodName.ReceiveHashRpc, tick, playerId, hashBits);
    }

    // Host-relay: forward a just-received RPC to every connected peer except the sender.
    private void RelayToOthers(StringName method, params Variant[] args)
    {
        int sender = Multiplayer.GetRemoteSenderId();
        foreach (int peer in Multiplayer.GetPeers())
            if (peer != sender) RpcId(peer, method, args);
    }
}
```

> **If the build complains** that `MethodName.ReceiveFrameRpc` (etc.) is undefined: Godot's source generator creates `MethodName` entries for the partial `Node` subclass — confirm the file `namespace`/`partial` are correct and rebuild. As a fallback, pass the method name as a string literal (e.g. `Rpc("ReceiveFrameRpc", bytes)`); `string` converts implicitly to `StringName`.

- [ ] **Step 2: Compile-check**

Run: `dotnet build godot/LlmRts.Godot.csproj --nologo`
Expected: build succeeds (warnings OK). This only confirms it compiles — behavior is verified in Task 5's playtest.

- [ ] **Step 3: Commit**

```bash
git add godot/scripts/NetSession.cs
git commit -m "feat(net): NetSession — Godot ENet transport + lockstep handshake/RPCs

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 4: SimRunner networked loop + Main/Menu/MatchConfig wiring (compile-checked + playtest)

**Files:**
- Modify: `godot/scripts/SimRunner.cs`
- Modify: `godot/scripts/MatchConfig.cs`
- Modify: `godot/scripts/MenuScreen.cs`
- Modify: `godot/scripts/Main.cs`

The networked loop **primes** the input-delay pipeline, then steps only when the coordinator has every human's frame, hashing each step and broadcasting it, and **submits one new local frame per step** (couples submission to execution → bounded input latency even if a peer freezes). Single-player keeps its existing direct path untouched.

- [ ] **Step 1: Add the networked loop to SimRunner**

In `godot/scripts/SimRunner.cs`: add `using SimCore.Net;` at the top (next to `using SimCore.Sim;`). Add these fields after `private float _accum;` (line ~20):

```csharp
    // ---- Networked (lockstep) mode ----
    private NetSession? _net;
    private LockstepCoordinator? _coord;
    private int _localPlayerId;
    private bool _networked;
    private bool _desyncReported;
    /// <summary>Fired (with the desync tick) the first time the coordinator latches a desync.</summary>
    public event System.Action<int>? Desynced;
```

Add this method after `Enqueue` (line ~24):

```csharp
    /// <summary>Switch into deterministic-lockstep mode. Wires the transport to the coordinator and
    /// primes the input-delay pipeline. The local queue (filled by Enqueue) becomes this peer's
    /// per-tick input. Call once, after the start handshake, instead of running the single-player loop.</summary>
    public void InitNetworked(SimWorld world, LockstepCoordinator coord, NetSession net, int localPlayerId)
    {
        World = world;
        _coord = coord;
        _net = net;
        _localPlayerId = localPlayerId;
        _networked = true;
        net.FrameReceived += f => _coord.Receive(f);
        net.HashReceived += (tick, pid, hash) => _coord.ReceiveHash(tick, pid, hash);
        // Prime the pipeline: submit `InputDelay` empty frames so the first real exec ticks have input.
        for (int i = 0; i < NetSession.InputDelay; i++)
            _net.SendFrame(_coord.SubmitLocal(System.Array.Empty<Command>()));
    }
```

Replace the body of `_Process` (lines ~26–41) with a branch:

```csharp
    public override void _Process(double delta)
    {
        if (Paused) return;
        if (_networked) { ProcessNetworked(delta); return; }

        _accum += (float)delta;
        // Cap catch-up to 5 ticks per frame; sim time slows under stalls rather than bursting.
        if (_accum > 5 * TickSeconds) _accum = 5 * TickSeconds;
        while (_accum >= TickSeconds)
        {
            _accum -= TickSeconds;
            World.Step(_queue.ToArray());
            _queue.Clear();
            TickCount++;
            Ticked?.Invoke();
        }
        Alpha = _accum / TickSeconds;
    }

    private void ProcessNetworked(double delta)
    {
        _accum += (float)delta;
        if (_accum > 5 * TickSeconds) _accum = 5 * TickSeconds;
        int steps = 0;
        while (_accum >= TickSeconds && steps < 5)
        {
            if (_coord!.Desynced) { ReportDesync(); return; }
            // Stall (return false) until every human's frame for the next exec tick has arrived.
            if (!_coord.TryDequeueStep(out var merged)) break;
            _accum -= TickSeconds;
            World.Step(merged);
            int steppedTick = _coord.NextStepTick - 1;
            ulong h = StateHasher.Hash(World);
            _coord.RecordLocalHash(steppedTick, h);
            _net!.SendHash(steppedTick, _localPlayerId, h);
            // Submit this peer's input for a future exec tick (one frame per executed tick).
            var frame = _coord.SubmitLocal(_queue.ToArray());
            _queue.Clear();
            _net.SendFrame(frame);
            TickCount++;
            Ticked?.Invoke();
            steps++;
        }
        Alpha = _accum / TickSeconds;
    }

    private void ReportDesync()
    {
        if (_desyncReported) return;
        _desyncReported = true;
        Paused = true;
        GD.PrintErr($"DESYNC detected at tick {_coord!.DesyncTick} — halting sim.");
        Desynced?.Invoke(_coord.DesyncTick);
    }
```

> Confirm `StateHasher.Hash(World)` is the correct call (it is `public static ulong Hash(SimWorld)` in `src/SimCore/Sim/StateHasher.cs`) and that `LockstepCoordinator` exposes `NextStepTick`, `Desynced`, `DesyncTick`, `SubmitLocal`, `Receive`, `TryDequeueStep`, `RecordLocalHash`, `ReceiveHash` (it does — M1).

- [ ] **Step 2: Add networked fields to MatchConfig**

In `godot/scripts/MatchConfig.cs`, add after the `Difficulty` field (line ~12):

```csharp
    public static bool IsNetworked;
    public static bool IsHost;
    public static string Ip = "127.0.0.1";

    /// <summary>Chosen from the menu: start a networked match (host or join). Takes precedence over
    /// the single-player config in Main. M2 minimal: a fixed 2-human 1v1 (Reference faction).</summary>
    public static void SetNetwork(bool isHost, string ip)
    {
        IsNetworked = true; IsHost = isHost; Ip = ip; Configured = false;
    }
```

And update `Clear()` to also reset the networked flags:

```csharp
    public static void Clear() { Configured = false; IsNetworked = false; }
```

- [ ] **Step 3: Add Host/Join to the menu**

In `godot/scripts/MenuScreen.cs`, add a multiplayer row inside `_Ready()` just before the `Play` button is created (after `box.AddChild(_diffLabel);`, line ~50):

```csharp
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Multiplayer (LAN — 2-human 1v1, Reference faction):" });

        var host = new Button { Text = "Host LAN game" };
        host.Pressed += () => { MatchConfig.SetNetwork(isHost: true, ip: ""); GetTree().ReloadCurrentScene(); };
        box.AddChild(host);

        var ipEdit = new LineEdit { Text = "127.0.0.1", CustomMinimumSize = new Vector2(160, 0) };
        box.AddChild(ipEdit);

        var join = new Button { Text = "Join" };
        join.Pressed += () => { MatchConfig.SetNetwork(isHost: false, ip: ipEdit.Text); GetTree().ReloadCurrentScene(); };
        box.AddChild(join);
```

(Add `using Godot;` is already present. `Vector2` is `Godot.Vector2`.)

- [ ] **Step 4: Wire networked boot into Main**

In `godot/scripts/Main.cs`, add `using SimCore.Net;` near the top. Replace the world-construction line in `_Ready` (line ~18–20) so a networked match builds the 2-human world:

```csharp
        Runner = new SimRunner { Name = "SimRunner" };
        Runner.Init(MatchConfig.IsNetworked
            ? MatchSetup.BuildVersus1v1(ReferenceFaction.Def, ReferenceFaction.Def, NetSession.MatchSeed)
            : MatchConfig.Configured
                ? MatchSetup.BuildStandard1v1(MatchConfig.Human, MatchConfig.Cpu, MatchConfig.Difficulty, seed: 42)
                : TestMap.Build());
        AddChild(Runner);
```

Then replace the boot-mode block at the end of `_Ready` (the `if (!MatchConfig.Configured) {...} else {...}`, lines ~69–79) with:

```csharp
        if (MatchConfig.IsNetworked)
        {
            StartNetworked();
        }
        else if (!MatchConfig.Configured)
        {
            Runner.Paused = true;                 // hold the sim behind the menu
            AddChild(new MenuScreen { Name = "Menu" });
        }
        else
        {
            var gameOver = new GameOverScreen { Name = "GameOver" };
            AddChild(gameOver);
            gameOver.Init(Runner);
        }
    }

    private void StartNetworked()
    {
        Runner.Paused = true;                     // hold until both peers are ready

        var status = new CanvasLayer { Name = "NetStatus", Layer = 100 };
        var statusLabel = new Label
        {
            Text = MatchConfig.IsHost ? "Hosting — waiting for opponent…" : $"Connecting to {MatchConfig.Ip}…",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        statusLabel.SetAnchorsPreset(Control.LayoutPreset.Center);
        status.AddChild(statusLabel);
        AddChild(status);

        var net = new NetSession { Name = "Net" };
        AddChild(net);

        net.MatchReady += (localPlayerId, seed) =>
        {
            var coord = new SimCore.Net.LockstepCoordinator(localPlayerId, new[] { 0, 1 }, NetSession.InputDelay);
            Runner.InitNetworked(Runner.World, coord, net, localPlayerId);
            Selection.ControlledPlayer = localPlayerId;   // command only your own slot
            statusLabel.Text = $"Connected — you are player {localPlayerId}";
            status.Visible = false;
            Runner.Paused = false;

            var gameOver = new GameOverScreen { Name = "GameOver" };
            AddChild(gameOver);
            gameOver.Init(Runner);
        };

        net.PeerDropped += () =>
        {
            Runner.Paused = true;
            status.Visible = true;
            statusLabel.Text = "Opponent disconnected — match halted.";
        };

        if (MatchConfig.IsHost) net.Host(); else net.Join(MatchConfig.Ip);
    }
```

> **`Selection.ControlledPlayer`** is read by `viewSync.ControlledPlayerProvider`. Confirm it has a public setter (grep `ControlledPlayer` in `godot/scripts/SelectionController.cs`). If it is read-only, add a public setter (or call the existing set-controlled-player method). Locking it to `localPlayerId` ensures this peer commands only its own units. (Full validation that every command's `PlayerId == frame owner` is an M4 anti-cheat item.)

- [ ] **Step 5: Compile-check the whole Godot project**

Run: `dotnet build godot/LlmRts.Godot.csproj --nologo`
Expected: build succeeds. Fix any signature mismatches surfaced (e.g. `ControlledPlayer` setter, `MatchPhase` name) before moving on.

- [ ] **Step 6: Commit**

```bash
git add godot/scripts/SimRunner.cs godot/scripts/MatchConfig.cs godot/scripts/MenuScreen.cs godot/scripts/Main.cs
git commit -m "feat(net): networked SimRunner loop + Host/Join menu + Main boot wiring

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 5: Full gate + playtest checklist + M3 inputs

**Files:**
- Modify: `docs/superpowers/plans/2026-06-15-multiplayer-m2-transport.md` (append the carry-forward)

- [ ] **Step 1: Headless test gate (Release + Debug)**

Run:
```bash
dotnet test --configuration Release --nologo -v q
dotnet test --configuration Debug --nologo -v q
```
Expected: both green. SimCore.Tests = **344** (328 + 14 codec + 2 versus), SpriteSlicer = 6, 0 failures.

- [ ] **Step 2: Determinism gate — SimCore + golden untouched**

Run:
```bash
git diff --stat master -- src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/DeterminismTests.cs
grep -n "GoldenTrajectoryHash =" tests/SimCore.Tests/DeterminismTests.cs
```
Expected: no diff to `StateHasher.cs`/`DeterminismTests.cs`; golden still `1571756151672809223UL`. (M2 only adds the `MatchSetup.BuildVersus1v1` composition + the `SimCore.Net` codec — no hashed sim state, no behavior change.)

- [ ] **Step 3: Godot build gate**

Run: `dotnet build godot/LlmRts.Godot.csproj --nologo`
Expected: succeeds.

- [ ] **Step 4: Append the M3 carry-forward + playtest checklist to this plan**

Append the "Plan-M3 Inputs (carry-forward)" + "Two-instance LAN playtest checklist" sections (below) to the end of this plan file.

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/plans/2026-06-15-multiplayer-m2-transport.md
git commit -m "docs: M2 carry-forward (M3 inputs) + LAN playtest checklist

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Self-Review (author checklist — completed)

- **Spec coverage:** `CommandCodec` (all 11 command types + frame, headless round-trip) = Task 1; the 2-human world (`BuildVersus1v1`) = Task 2; `NetSession` (ENet host/join, handshake, host-relay frame/hash RPCs) = Task 3; the networked `SimRunner` loop (prime → step-on-all-frames → hash → broadcast → submit, halt on desync) + minimal Host/Join menu + Main boot = Task 4; gate + determinism + playtest + M3 inputs = Task 5. Every spec "in scope" item maps to a task. Out-of-scope items (full lobby, desync UI, disconnect recovery, `_hashes` bound) are explicitly deferred to M3/M4.
- **Placeholder scan:** none — complete code in every code step. The two "confirm the property name" notes (Task 2 phase/owner names, Task 4 `ControlledPlayer` setter) are explicit verification steps with fallbacks, not deferred work.
- **Type consistency:** `CommandFrame(int Tick, int PlayerId, IReadOnlyList<Command> Commands)`, `LockstepCoordinator(localPlayerId, int[]{0,1}, InputDelay)` with `SubmitLocal`/`Receive`/`TryDequeueStep(out)`/`RecordLocalHash`/`ReceiveHash`/`Desynced`/`DesyncTick`/`NextStepTick` (all from M1), `CommandCodec.FrameToBytes`/`FrameFromBytes`, `NetSession.Host()`/`Join(ip)`/`SendFrame`/`SendHash`/`MatchReady`/`FrameReceived`/`HashReceived`/`PeerDropped`/`Port`/`MatchSeed`/`InputDelay`, `SimRunner.InitNetworked(world, coord, net, localPlayerId)`/`Desynced`, `MatchConfig.SetNetwork`/`IsNetworked`/`IsHost`/`Ip`, `MatchSetup.BuildVersus1v1(p0, p1, seed)` — all names used identically across tasks. Command record field names match `Commands.cs`; `Fix.Raw`/`new Fix(long)`/`FixVec(Fix,Fix)` match `Math/`.
- **Determinism:** SimCore's `StateHasher`/`DeterminismTests` are untouched (verified in Task 5); the codec is exact (longs/ints/strings, no floats); the coordinator's ascending-PlayerId merge feeds identical sequences to both peers.
