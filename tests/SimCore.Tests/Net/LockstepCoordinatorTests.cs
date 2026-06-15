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
}
