using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class DeterminismTests
{
    /// <summary>Builds the standard scenario: 20 units, walls, scripted move orders.</summary>
    private static (SimWorld world, Dictionary<int, List<Command>> script) Scenario()
    {
        var map = new MapGrid(40, 40);
        for (int y = 5; y < 35; y++) map.SetPassable(20, y, false);
        var w = new SimWorld(map, seed: 1234);

        var ids = new List<int>();
        for (int i = 0; i < 20; i++)
            ids.Add(w.SpawnUnit(i % 2, w.Map.CellCenter(2 + i % 5, 2 + i / 5), Fix.FromFraction(2, 5), 60));

        var script = new Dictionary<int, List<Command>>
        {
            [0] = new() { new MoveCommand(0, ids.FindAll(i => w.GetUnit(i)!.OwnerId == 0).ToArray(), w.Map.CellCenter(35, 35)) },
            [50] = new() { new MoveCommand(1, ids.FindAll(i => w.GetUnit(i)!.OwnerId == 1).ToArray(), w.Map.CellCenter(35, 2)) },
            [120] = new() { new MoveCommand(0, new[] { ids[0], ids[2] }, w.Map.CellCenter(2, 38)) },
        };
        return (w, script);
    }

    [Fact]
    public void Same_Script_Produces_Identical_Hash_Every_Tick()
    {
        var (a, scriptA) = Scenario();
        var (b, scriptB) = Scenario();
        var empty = new List<Command>();

        for (int t = 0; t < 500; t++)
        {
            a.Step(scriptA.TryGetValue(t, out var ca) ? ca : empty);
            b.Step(scriptB.TryGetValue(t, out var cb) ? cb : empty);
            Assert.True(StateHasher.Hash(a) == StateHasher.Hash(b),
                $"Desync at tick {t}: worlds diverged.");
        }
    }

    [Fact]
    public void Replaying_After_Full_Run_Matches_Recorded_Hashes()
    {
        var (a, script) = Scenario();
        var empty = new List<Command>();
        var recorded = new ulong[500];
        for (int t = 0; t < 500; t++)
        {
            a.Step(script.TryGetValue(t, out var c) ? c : empty);
            recorded[t] = StateHasher.Hash(a);
        }

        var (b, script2) = Scenario();
        for (int t = 0; t < 500; t++)
        {
            b.Step(script2.TryGetValue(t, out var c) ? c : empty);
            Assert.Equal(recorded[t], StateHasher.Hash(b));
        }
    }

    [Fact]
    public void Trajectory_Hash_Matches_Golden_Constant()
    {
        // Folds EVERY tick's state hash into one constant, so any mid-run behavior
        // change trips this test — not just end-state changes. If this fails: either
        // you changed sim behavior intentionally (update the constant in the same
        // commit, with a note in the commit message) or you introduced
        // nondeterminism (fix it).
        var (w, script) = Scenario();
        var empty = new List<Command>();
        ulong combined = 14695981039346656037UL;
        for (int t = 0; t < 500; t++)
        {
            w.Step(script.TryGetValue(t, out var c) ? c : empty);
            combined = unchecked((combined ^ StateHasher.Hash(w)) * 1099511628211UL);
        }
        Assert.Equal(GoldenTrajectoryHash, combined);
    }

    private const ulong GoldenTrajectoryHash = 12231289173812225436UL;
}
