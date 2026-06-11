using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class DeterminismTests
{
    // Deterministic entity id of the barracks: node=1, units 2-21, worker=22, depot=23, rax=24.
    // If spawn order in Scenario() changes, update this and re-pin the golden constant.
    private const int RaxId = 24;

    /// <summary>Scenario v3: pitched battle (kiters → leash; explicit attack) PLUS a parallel
    /// economy — build depot + barracks, harvest a node to depletion, train marines mid-run.
    /// Every economy system (placement, construction, supply, production, harvest, node
    /// removal/passability restore) executes inside the hashed trajectory.</summary>
    private static (SimWorld world, Dictionary<int, List<Command>> script) Scenario()
    {
        var map = new MapGrid(40, 40);
        for (int y = 5; y < 35; y++) map.SetPassable(20, y, false);
        var w = new SimWorld(map, seed: 1234);
        w.Players[0].Minerals = 400;
        w.Players[1].Minerals = 400;

        var nodeId = w.AddResourceNode(10, 12, amount: 10);

        var ids = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            var weapon = new Weapon { Damage = 5, Range = Fix.FromInt(2), CooldownTicks = 8 };
            var speed = i % 5 == 0 ? Fix.FromFraction(4, 5) : Fix.FromFraction(2, 5); // some fast kiters
            ids.Add(w.SpawnUnit(i % 2, w.Map.CellCenter(2 + i % 5, 2 + i / 5), speed, 60, weapon));
        }

        var workerSpec = new UnitSpec(30, Fix.FromFraction(1, 2), 50, 1, 10,
            Harvester: new HarvesterSpec(CarryCapacity: 5, GatherTicks: 4));
        var worker = w.SpawnUnit(0, w.Map.CellCenter(8, 10), workerSpec);
        w.Players[0].SupplyCap = 4; // headroom for worker + first marine before depot completes

        var depotSpec = new BuildingSpec(100, 2, 2, 100, BuildTimeTicks: 30, SupplyProvided: 8, IsDepot: true);
        var raxSpec = new BuildingSpec(150, 2, 2, 150, BuildTimeTicks: 40, CanTrain: true);
        var marineSpec = new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 25,
            Weapon: new WeaponSpec(6, Fix.FromInt(2), 5));

        int[] Owned(int owner) => ids.FindAll(i => w.GetUnit(i)!.OwnerId == owner).ToArray();

        var script = new Dictionary<int, List<Command>>
        {
            [0] = new()
            {
                new MoveCommand(0, Owned(0), w.Map.CellCenter(35, 35)),
                new BuildCommand(0, worker, depotSpec, 7, 11),       // worker at (8,10) is in range
                new HarvestCommand(0, new[] { worker }, nodeId),     // harvest while depot builds
            },
            [50] = new() { new MoveCommand(1, Owned(1), w.Map.CellCenter(35, 2)) },
            [80] = new() { new BuildCommand(0, worker, raxSpec, 11, 9) }, // ignored if worker out of range — deterministic either way
            [120] = new() { new MoveCommand(0, new[] { ids[0], ids[2] }, w.Map.CellCenter(2, 38)) },
            [200] = new() { new AttackMoveCommand(0, Owned(0), w.Map.CellCenter(35, 2)) },
            [220] = new() { new AttackMoveCommand(1, Owned(1), w.Map.CellCenter(35, 35)) },
            [230] = new() { new MoveCommand(1, new[] { ids[5], ids[15] }, w.Map.CellCenter(38, 20)) },
            [250] = new() { new TrainCommand(0, RaxId, marineSpec), new TrainCommand(0, RaxId, marineSpec) },
            [350] = new() { new AttackCommand(0, Owned(0), ids[11]) },
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

    private const ulong GoldenTrajectoryHash = 11976348282665656445UL;
}
