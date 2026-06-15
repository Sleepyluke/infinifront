# Lockstep Coordinator (Multiplayer M1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the transport-agnostic, world-free, headless-testable lockstep coordinator: schedules delayed-lockstep stepping, buffers per-(tick,player) command frames, merges them deterministically, and detects desyncs via per-tick hash comparison.

**Architecture:** A new `SimCore.Net` class library (references `SimCore`, no Godot) holding `CommandFrame` + `LockstepCoordinator`. The coordinator is a pure state machine over `Command`/`ulong` — no `SimWorld`, no sockets — so several instances can be wired in-process to prove multi-peer determinism in unit tests. M2 (ENet transport) will drive it; M1 proves it.

**Tech Stack:** C# / .NET 8, xUnit. Determinism-adjacent (the merge order feeds `Step`), but adds no hashed sim state.

**Source spec:** `docs/superpowers/specs/2026-06-15-multiplayer-design.md` (the M1 section).

---

## Conventions for every task

- Run from repo root `C:\Users\lssha\llm-rts`. If `dotnet` missing: bash `export PATH="$PATH:/c/Program Files/dotnet"`.
- Run SimCore tests: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
- **Baseline:** 319 SimCore tests pass; golden = `1571756151672809223UL` (M1 adds no sim state → golden must stay unchanged).
- After each commit, confirm `git log --oneline -1`. End commit messages with `Co-Authored-By: RuFlo <ruv@ruv.net>`.

## Engine facts (verified against source)

- `Command` is `public abstract record Command(int PlayerId)` (`src/SimCore/Sim/Commands.cs`); concrete e.g. `MoveCommand(int PlayerId, int[] UnitIds, FixVec Target)`. `FixVec` is a struct (default is `(0,0)`), in `SimCore.Math`.
- `SimCore` is referenced by other libs via `<ProjectReference Include="..\SimCore\SimCore.csproj" />` (see `src/SimCore.Packs/SimCore.Packs.csproj`). `SimCore.Packs` is registered in `LlmRts.sln` (added via `dotnet sln add`). `tests/SimCore.Tests/SimCore.Tests.csproj` references both `SimCore` and `SimCore.Packs`.
- Record equality on `MoveCommand` is REFERENCE equality on its `int[] UnitIds` field, so tests compare merged command *sequences* via a `Describe(...)` string helper, never by `List<Command>` equality.

## File Structure

- `src/SimCore.Net/SimCore.Net.csproj` — NEW class library (refs SimCore).
- `src/SimCore.Net/CommandFrame.cs` — NEW: the `CommandFrame` record.
- `src/SimCore.Net/LockstepCoordinator.cs` — NEW: the coordinator (scheduling in Task 1, desync in Task 2).
- `tests/SimCore.Tests/Net/LockstepCoordinatorTests.cs` — NEW: across Tasks 1–2.
- Modified: `LlmRts.sln` (add project), `tests/SimCore.Tests/SimCore.Tests.csproj` (add ProjectReference).

---

## Task 1: Scaffold `SimCore.Net` + coordinator scheduling (frames, merge, stall, input delay)

**Files:**
- Create: `src/SimCore.Net/SimCore.Net.csproj`, `src/SimCore.Net/CommandFrame.cs`, `src/SimCore.Net/LockstepCoordinator.cs`
- Modify: `LlmRts.sln`, `tests/SimCore.Tests/SimCore.Tests.csproj`
- Test: `tests/SimCore.Tests/Net/LockstepCoordinatorTests.cs`

- [ ] **Step 1: Create the project + register it**

Create `src/SimCore.Net/SimCore.Net.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\SimCore\SimCore.csproj" />
  </ItemGroup>

</Project>
```

Run (from repo root): `dotnet sln add src/SimCore.Net/SimCore.Net.csproj`

Then add a ProjectReference to `tests/SimCore.Tests/SimCore.Tests.csproj` inside the existing ItemGroup that references SimCore/SimCore.Packs so it becomes:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\SimCore\SimCore.csproj" />
    <ProjectReference Include="..\..\src\SimCore.Packs\SimCore.Packs.csproj" />
    <ProjectReference Include="..\..\src\SimCore.Net\SimCore.Net.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

Create `tests/SimCore.Tests/Net/LockstepCoordinatorTests.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Math;
using SimCore.Net;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests.Net;

public class LockstepCoordinatorTests
{
    // Stable string for a merged command sequence (record equality on int[] is by-reference,
    // so we compare descriptions, not the lists themselves).
    private static string Describe(IReadOnlyList<Command> cmds)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in cmds)
        {
            sb.Append(c switch
            {
                MoveCommand m => $"M{m.PlayerId}[{string.Join(",", m.UnitIds)}]",
                _ => $"?{c.PlayerId}",
            });
            sb.Append(';');
        }
        return sb.ToString();
    }

    private static Command[] Move(int player, int unit) =>
        new Command[] { new MoveCommand(player, new[] { unit }, default) };

    [Fact]
    public void Submit_Schedules_Commands_At_Input_Delay()
    {
        var c = new LockstepCoordinator(localPlayerId: 0, new[] { 0 }, inputDelay: 3);
        Assert.Equal(3, c.SubmitLocal(System.Array.Empty<Command>()).Tick); // input tick 0 -> exec 0+3
        Assert.Equal(4, c.SubmitLocal(System.Array.Empty<Command>()).Tick); // input tick 1 -> exec 1+3
    }

    [Fact]
    public void Single_Human_Never_Stalls()
    {
        var c = new LockstepCoordinator(0, new[] { 0 }, inputDelay: 0);
        for (int t = 0; t < 5; t++)
        {
            c.SubmitLocal(System.Array.Empty<Command>());
            Assert.True(c.TryDequeueStep(out _)); // only human is local -> always ready
        }
    }

    [Fact]
    public void Stalls_Until_All_Human_Frames_Arrive()
    {
        var c = new LockstepCoordinator(0, new[] { 0, 1 }, inputDelay: 0);
        c.SubmitLocal(System.Array.Empty<Command>());        // local frame for exec tick 0
        Assert.False(c.TryDequeueStep(out _));                // player 1's frame for tick 0 missing -> stall
        c.Receive(new CommandFrame(0, 1, System.Array.Empty<Command>()));
        Assert.True(c.TryDequeueStep(out var merged));        // now ready
        Assert.Empty(merged);
    }

    [Fact]
    public void Merge_Order_Is_By_PlayerId_Regardless_Of_Arrival()
    {
        static IReadOnlyList<Command> Run(bool zeroFirst)
        {
            var c = new LockstepCoordinator(localPlayerId: 1, new[] { 0, 1, 2 }, inputDelay: 0);
            c.SubmitLocal(Move(1, 11)); // local player 1, exec tick 0
            var f0 = new CommandFrame(0, 0, Move(0, 0));
            var f2 = new CommandFrame(0, 2, Move(2, 22));
            if (zeroFirst) { c.Receive(f0); c.Receive(f2); } else { c.Receive(f2); c.Receive(f0); }
            Assert.True(c.TryDequeueStep(out var merged));
            return merged;
        }
        var x = Run(true);
        var y = Run(false);
        Assert.Equal(Describe(x), Describe(y));               // identical sequence regardless of arrival order
        Assert.Equal(3, x.Count);
        Assert.Equal(0, x[0].PlayerId);
        Assert.Equal(1, x[1].PlayerId);
        Assert.Equal(2, x[2].PlayerId);                       // sorted by PlayerId
    }

    [Fact]
    public void Two_Peers_Step_Identically_Over_Many_Ticks()
    {
        var a = new LockstepCoordinator(localPlayerId: 0, new[] { 0, 1 }, inputDelay: 2);
        var b = new LockstepCoordinator(localPlayerId: 1, new[] { 0, 1 }, inputDelay: 2);
        var seqA = new List<string>();
        var seqB = new List<string>();
        for (int t = 0; t < 30; t++)
        {
            var fa = a.SubmitLocal(t % 3 == 0 ? Move(0, t) : System.Array.Empty<Command>());
            var fb = b.SubmitLocal(t % 4 == 0 ? Move(1, t) : System.Array.Empty<Command>());
            b.Receive(fa); // peers exchange frames
            a.Receive(fb);
            while (a.TryDequeueStep(out var ma)) seqA.Add(Describe(ma));
            while (b.TryDequeueStep(out var mb)) seqB.Add(Describe(mb));
        }
        Assert.Equal(seqA, seqB);  // identical per-tick merged command stream on both peers
        Assert.NotEmpty(seqA);
        Assert.Contains(seqA, s => s.Length > 0); // some ticks carried real commands
    }
}
```

- [ ] **Step 3: Run, expect failure** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Compile FAILS (`SimCore.Net`/`CommandFrame`/`LockstepCoordinator` missing).

- [ ] **Step 4: Create `CommandFrame`**

Create `src/SimCore.Net/CommandFrame.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Sim;

namespace SimCore.Net;

/// <summary>One player's commands for one EXECUTION tick. An empty Commands list is still a valid
/// frame ("I have nothing this tick") and MUST be sent, so peers can distinguish "no input" from
/// "input not yet arrived".</summary>
public sealed record CommandFrame(int Tick, int PlayerId, IReadOnlyList<Command> Commands);
```

- [ ] **Step 5: Create `LockstepCoordinator` (scheduling)**

Create `src/SimCore.Net/LockstepCoordinator.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Sim;

namespace SimCore.Net;

/// <summary>Transport-agnostic delayed-lockstep scheduler. Buffers command frames per
/// (executionTick, player); dispatches execution tick X only once every human player has a frame
/// for X (or X is within the initial input-delay pad); merges frames in ascending PlayerId order
/// so every peer feeds the identical command sequence into Step. World-free + Godot-free.</summary>
public sealed class LockstepCoordinator
{
    private readonly int _localPlayerId;
    private readonly int[] _humanPlayerIds;   // sorted ascending — defines the merge order
    private readonly int _inputDelay;
    private readonly Dictionary<(int tick, int playerId), CommandFrame> _frames = new();
    private int _submitTick;  // next local input tick to submit
    private int _stepTick;    // next execution tick to dispatch

    public LockstepCoordinator(int localPlayerId, IReadOnlyList<int> humanPlayerIds, int inputDelay)
    {
        _localPlayerId = localPlayerId;
        var arr = new int[humanPlayerIds.Count];
        for (int i = 0; i < arr.Length; i++) arr[i] = humanPlayerIds[i];
        System.Array.Sort(arr);
        _humanPlayerIds = arr;
        _inputDelay = inputDelay;
    }

    /// <summary>The next execution tick that <see cref="TryDequeueStep"/> will dispatch.</summary>
    public int NextStepTick => _stepTick;

    /// <summary>Submit the local player's commands for the current input tick. They are scheduled
    /// to EXECUTE at inputTick + inputDelay, buffered locally, and returned as a frame to broadcast.</summary>
    public CommandFrame SubmitLocal(IReadOnlyList<Command> commands)
    {
        var frame = new CommandFrame(_submitTick + _inputDelay, _localPlayerId, commands);
        _frames[(frame.Tick, _localPlayerId)] = frame;
        _submitTick++;
        return frame;
    }

    /// <summary>Buffer a remote human's frame.</summary>
    public void Receive(CommandFrame frame) => _frames[(frame.Tick, frame.PlayerId)] = frame;

    /// <summary>If the next execution tick is ready, output its deterministically-merged commands
    /// and advance; else false (a stall — waiting on a peer's frame). Execution ticks within the
    /// initial input-delay pad (before any input could have been scheduled) dispatch empty.</summary>
    public bool TryDequeueStep(out IReadOnlyList<Command> merged)
    {
        if (_stepTick >= _inputDelay)
            foreach (var pid in _humanPlayerIds)
                if (!_frames.ContainsKey((_stepTick, pid)))
                {
                    merged = System.Array.Empty<Command>();
                    return false;
                }

        var list = new List<Command>();
        foreach (var pid in _humanPlayerIds) // ascending PlayerId -> identical order on every peer
            if (_frames.TryGetValue((_stepTick, pid), out var f))
            {
                list.AddRange(f.Commands);
                _frames.Remove((_stepTick, pid)); // release the buffered frame
            }
        merged = list;
        _stepTick++;
        return true;
    }
}
```

- [ ] **Step 6: Run, expect pass** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Expected: 319 + 5 = 324; golden unchanged `1571756151672809223UL`.

- [ ] **Step 7: Commit**

```bash
git add src/SimCore.Net/ tests/SimCore.Tests/Net/ tests/SimCore.Tests/SimCore.Tests.csproj LlmRts.sln
git commit -m "feat(net): SimCore.Net + LockstepCoordinator (frame buffering, deterministic merge, input delay)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 2: Desync detection (per-tick hash comparison)

**Files:**
- Modify: `src/SimCore.Net/LockstepCoordinator.cs`
- Test: `tests/SimCore.Tests/Net/LockstepCoordinatorTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `LockstepCoordinatorTests.cs` (inside the class):

```csharp
    [Fact]
    public void Detects_Desync_On_Hash_Mismatch()
    {
        var c = new LockstepCoordinator(0, new[] { 0, 1 }, inputDelay: 0);
        c.RecordLocalHash(5, 0xABCUL);
        Assert.False(c.Desynced);
        c.ReceiveHash(5, 1, 0xDEFUL); // a different hash for the same tick -> desync
        Assert.True(c.Desynced);
        Assert.Equal(5, c.DesyncTick);
    }

    [Fact]
    public void Matching_Hashes_Are_Not_A_Desync()
    {
        var c = new LockstepCoordinator(0, new[] { 0, 1 }, inputDelay: 0);
        c.RecordLocalHash(5, 0xABCUL);
        c.ReceiveHash(5, 1, 0xABCUL); // same hash -> fine
        Assert.False(c.Desynced);
        Assert.Equal(-1, c.DesyncTick);
    }

    [Fact]
    public void Hashes_For_Different_Ticks_Do_Not_Compare()
    {
        var c = new LockstepCoordinator(0, new[] { 0, 1 }, inputDelay: 0);
        c.RecordLocalHash(5, 0xABCUL);
        c.ReceiveHash(6, 1, 0xDEFUL); // different tick -> not compared
        Assert.False(c.Desynced);
    }
```

- [ ] **Step 2: Run, expect failure** — the desync tests fail (`RecordLocalHash`/`ReceiveHash`/`Desynced`/`DesyncTick` missing).

- [ ] **Step 3: Add desync detection**

In `src/SimCore.Net/LockstepCoordinator.cs`, add a hash buffer field next to `_frames`:

```csharp
    private readonly Dictionary<(int tick, int playerId), ulong> _hashes = new();
```

Add these members to the class:

```csharp
    /// <summary>True once two peers have reported different state hashes for the same tick.</summary>
    public bool Desynced { get; private set; }

    /// <summary>The tick at which a desync was first detected, or -1 if none.</summary>
    public int DesyncTick { get; private set; } = -1;

    /// <summary>Record this peer's own post-Step state hash for a tick (then broadcast it via M2).</summary>
    public void RecordLocalHash(int tick, ulong hash) => RegisterHash(tick, _localPlayerId, hash);

    /// <summary>Record a remote peer's reported state hash for a tick.</summary>
    public void ReceiveHash(int tick, int playerId, ulong hash) => RegisterHash(tick, playerId, hash);

    private void RegisterHash(int tick, int playerId, ulong hash)
    {
        if (Desynced) return;
        _hashes[(tick, playerId)] = hash;
        foreach (var pid in _humanPlayerIds) // any other peer's hash for this tick differing = desync
        {
            if (pid == playerId) continue;
            if (_hashes.TryGetValue((tick, pid), out var other) && other != hash)
            {
                Desynced = true;
                DesyncTick = tick;
                return;
            }
        }
    }
```

- [ ] **Step 4: Run, expect pass** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Expected: 324 + 3 = 327; golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore.Net/LockstepCoordinator.cs tests/SimCore.Tests/Net/LockstepCoordinatorTests.cs
git commit -m "feat(net): lockstep desync detection (per-tick state-hash comparison)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 3: Full gate + M2 inputs

**Files:** Modify `docs/superpowers/plans/2026-06-15-lockstep-coordinator.md` (append note).

- [ ] **Step 1: Full solution gate (Release + Debug)**

Run: `dotnet test --configuration Release --nologo -v q` then `dotnet test --configuration Debug --nologo -v q`
Expected: PASS — SimCore.Tests 327, SpriteSlicer.Tests 6, 0 failures; the 3 determinism tests green with golden UNCHANGED `1571756151672809223UL` (SimCore untouched — `git diff --stat master -- src/SimCore/` is empty); Debug == Release.

- [ ] **Step 2: Append the M2 carry-forward note**

Append to the end of this plan file:

```markdown
---

## Plan-M2 Inputs (carry-forward)

M1 shipped the headless lockstep coordinator. M2 (ENet transport + game-loop integration):
- **Command serialization:** a compact binary writer/reader for every concrete `Command`
  (ints, `int[]`, `FixVec`=two `long` raws, strings, enums) so a `CommandFrame` can cross an
  RPC. Round-trip is headless-testable (do it TDD); the socket layer is playtested.
- **Transport:** Godot `ENetMultiplayerPeer` host/client; host forwards every human's frame so
  all peers see all frames; RPCs `SendFrame(tick, playerId, bytes)` + `SendHash(tick, playerId, hash)`.
- **Loop:** drive `LockstepCoordinator` from `SimRunner` — each tick `SubmitLocal(localQueue)` +
  broadcast the frame; `while (TryDequeueStep(out var cmds)) { World.Step(cmds); var h =
  StateHasher.Hash(World); coordinator.RecordLocalHash(tick, h); broadcast hash; }`. Single-player
  routes through a 1-human, inputDelay-0 coordinator (≈ today's immediate apply) — verify the
  golden/sandbox are unaffected.
- **Determinism:** the coordinator's merge order (ascending PlayerId) is the contract M2 relies
  on; never reorder. CPU slots are NOT in the frame exchange — `UpdateAi` runs in `Step` on every
  peer. Input delay 2–3 ticks (~200–300 ms) is the starting default; expose it for tuning (M4).
- **M3/M4** then add the lobby (slot config + start-config broadcast) and robustness (desync-halt
  screen via `Desynced`/`DesyncTick`, disconnect handling).
```

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/plans/2026-06-15-lockstep-coordinator.md
git commit -m "docs: record M2 inputs from M1 lockstep coordinator

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Self-Review (author checklist — completed)

- **Spec coverage (M1 section):** `CommandFrame` (empty-frame semantics) + `LockstepCoordinator` with `SubmitLocal`/`Receive`/`TryDequeueStep` (input-delay scheduling, all-humans gating with the initial pad, ascending-PlayerId deterministic merge, stall) in Task 1; `RecordLocalHash`/`ReceiveHash`/`Desynced`/`DesyncTick` in Task 2; world-free + Godot-free `SimCore.Net` library; multi-peer headless tests proving identical stepping + desync detection; golden untouched (Task 3). Every M1 "in scope" item maps to a task.
- **Placeholder scan:** none — complete code in every step.
- **Type consistency:** `CommandFrame(int Tick, int PlayerId, IReadOnlyList<Command> Commands)`, `LockstepCoordinator(int localPlayerId, IReadOnlyList<int> humanPlayerIds, int inputDelay)`, `SubmitLocal`/`Receive`/`TryDequeueStep(out IReadOnlyList<Command>)`/`RecordLocalHash`/`ReceiveHash`/`Desynced`/`DesyncTick`/`NextStepTick` used identically across tasks; `Command`/`MoveCommand` shapes match `Commands.cs`; the `Describe` helper sidesteps int[]-by-reference record equality in the merge tests.
