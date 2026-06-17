using System.Collections.Generic;
using System.Linq;
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
    public void Ctor_Rejects_Local_Player_Not_Among_Humans()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new LockstepCoordinator(localPlayerId: 5, new[] { 0, 1 }, inputDelay: 0));
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

    // ----- Turret-in-lockstep integration -----
    // The Sentry Turret is the only recent change that touched HASHED sim state (StateHasher folds
    // Building.Weapon when present). The golden trajectory scenario is towerless, so it never proves
    // a weaponed building survives the real multiplayer path. This drives two identical turret worlds
    // through the coordinator's merged command stream (real frames crossing both ways) and asserts the
    // per-step hash sequences stay byte-identical — the turret's cooldown folded into every hash. A
    // turret-induced desync (e.g. weapon state hashed under non-deterministic iteration) would split them.
    [Fact]
    public void Turret_World_Stays_In_Sync_Through_Lockstep()
    {
        var worldA = TurretMatchWorld();
        var worldB = TurretMatchWorld();
        var a = new LockstepCoordinator(localPlayerId: 0, new[] { 0, 1 }, inputDelay: 2);
        var b = new LockstepCoordinator(localPlayerId: 1, new[] { 0, 1 }, inputDelay: 2);
        var hashesA = new List<ulong>();
        var hashesB = new List<ulong>();
        for (int t = 0; t < 90; t++)
        {
            // Each peer issues real move frames for its OWN units; they cross the wire and merge.
            var fa = a.SubmitLocal(t % 5 == 0 ? MoveFor(worldA, 0, 12, 12) : System.Array.Empty<Command>());
            var fb = b.SubmitLocal(t % 7 == 0 ? MoveFor(worldB, 1, 4, 5) : System.Array.Empty<Command>());
            b.Receive(fa);
            a.Receive(fb);
            while (a.TryDequeueStep(out var ma)) { worldA.Step(ma.ToArray()); hashesA.Add(StateHasher.Hash(worldA)); }
            while (b.TryDequeueStep(out var mb)) { worldB.Step(mb.ToArray()); hashesB.Add(StateHasher.Hash(worldB)); }
        }
        Assert.Equal(hashesA, hashesB);   // identical per-step hash sequence on both peers — turret cooldown included
        Assert.NotEmpty(hashesA);
    }

    private static SimWorld TurretMatchWorld()
    {
        var w = new SimWorld(new MapGrid(24, 24), seed: 7, playerCount: 4, faction: null);
        w.FogEnabled = false;
        w.AddCompletedBuilding(0, ReferenceSpecs.SentryTurret, 4, 4, "tower");
        w.SpawnUnit(0, w.Map.CellCenter(2, 18), Fix.FromInt(1), hp: 50);   // owner's units, away from combat
        w.SpawnUnit(0, w.Map.CellCenter(3, 18), Fix.FromInt(1), hp: 50);
        w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), hp: 80);    // in turret range → it fires (cooldown ticks)
        w.SpawnUnit(1, w.Map.CellCenter(7, 6), Fix.FromInt(1), hp: 80);
        return w;
    }

    private static Command[] MoveFor(SimWorld w, int player, int cx, int cy)
    {
        var ids = w.Units.Where(u => u.OwnerId == player).Select(u => u.Id).ToArray();
        return ids.Length == 0
            ? System.Array.Empty<Command>()
            : new Command[] { new MoveCommand(player, ids, w.Map.CellCenter(cx, cy)) };
    }
}
