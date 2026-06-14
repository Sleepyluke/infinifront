using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class DeterminismTests
{
    // Deterministic entity ids:
    //   node=1, base units 2-21, worker=22,
    //   sniper=23, sniper_target=24, slow_attacker=25, fast_runner=26  [v4b fog units]
    //   depot=27, rax=28  [placed at runtime via BuildCommand t=0 and t=80]
    // If spawn order in Scenario() changes, update this and re-pin the golden constant.
    private const int RaxId = 28;

    /// <summary>Scenario v6: pitched battle (kiters → leash; explicit attack) PLUS a parallel
    /// economy — build depot + barracks, harvest a node to depletion, train marines mid-run —
    /// PLUS fog verification units: a sniper whose explicit attack on a distant target is
    /// fog-gated at command-application time, and a slow attacker whose chase-drop fires as
    /// the fast runner escapes beyond sight. Both fog branches diverge vs FogEnabled=false,
    /// proving fog code executes under the golden hash net.
    ///
    /// Earlier additions:
    ///   t=2   Defend stance on sniper (id 23) + slow_attacker (id 25) — fog combat units
    ///   t=60  Patrol on slow_attacker (id 25) from (2,25)→(2,32) — south of fog corridor, legs swap
    ///   t=245 SetRallyCommand on rax (id 28) → (25,15) — marines trained at t=250 rally there
    ///   t=400 DestroyCommand on sniper (id 23) — fighting sniperTarget (200hp at range 6), alive at t=399
    /// New in v6:
    ///   t=300 ResearchCommand on rax → "dmg1" (+3 damage, target *) — applies ~t=320,
    ///         retroactively boosting all player-0 units inside the golden trajectory.</summary>
    private static (SimWorld world, Dictionary<int, List<Command>> script) Scenario()
    {
        var map = new MapGrid(40, 40);
        for (int y = 5; y < 35; y++) map.SetPassable(20, y, false);

        var depotSpec = new BuildingSpec(100, 2, 2, 100, BuildTimeTicks: 30, SupplyProvided: 8, IsDepot: true);
        var raxSpec = new BuildingSpec(150, 2, 2, 150, BuildTimeTicks: 40, CanTrain: true);
        var marineSpec = new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 25,
            Weapon: new WeaponSpec(6, Fix.FromInt(2), 5));

        var scenarioFaction = new FactionDef("scenario", "Scenario",
            units: new[] { new UnitDef("marine", 1, "rax", System.Array.Empty<string>(), marineSpec) },
            buildings: new[]
            {
                new BuildingDef("depot", 1, System.Array.Empty<string>(), depotSpec),
                new BuildingDef("rax", 1, System.Array.Empty<string>(), raxSpec),
            },
            upgrades: new[]
            {
                new UpgradeDef("dmg1", 1, "rax", System.Array.Empty<string>(), new[] { "*" },
                    UpgradeStat.Damage, Fix.FromInt(3), 50, 20),
            },
            mechanic: new MechanicDef(MechanicKind.RegeneratingShields, MaxShield: 15, RegenPerTick: 1, RegenDelayTicks: 10));

        var w = new SimWorld(map, seed: 1234, faction: scenarioFaction);
        w.Players[0].Minerals = 500;
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

        // ── v4b fog units (IDs 23-26, appended after base units) ─────────────────────────────
        // Sniper (p0, id=23): long-range weapon (Range=6). Placed left of wall at (2,20).
        // AcquireBonus=2 → acquire range=8 > sight=7. An explicit AttackCommand at t=5 on
        // sniper_target (id=24, cell 10,20, distance=8) is fog-gated: cell (10,20) lies
        // outside sight-7 from (2,20) (dx=8, 64>49=7²) → rejected when FogEnabled=true,
        // accepted when false — immediate trajectory divergence.
        var sniperWeapon = new Weapon { Damage = 5, Range = Fix.FromInt(6), CooldownTicks = 10 };
        var sniperId = w.SpawnUnit(0, w.Map.CellCenter(2, 20), Fix.FromFraction(1, 5), 200, sniperWeapon);
        var sniperTargetId = w.SpawnUnit(1, w.Map.CellCenter(10, 20), Fix.FromFraction(1, 5), 200);

        // Slow attacker (p0, id=25): Range=2, sight=7, very slow. Placed at (2,25).
        // Fast runner (p1, id=26): no weapon, very fast, high HP. Placed at (6,25) — distance
        // 4 from attacker (within sight and chase-range). At t=15 runner gets MoveCommand to
        // (38,25): it escapes past sight boundary of the attacker. Chase-drop fires when
        // FogEnabled=true; attacker keeps chasing when false — sustained trajectory divergence.
        var slowWeapon = new Weapon { Damage = 3, Range = Fix.FromInt(2), CooldownTicks = 12 };
        var slowAttackerId = w.SpawnUnit(0, w.Map.CellCenter(2, 25), Fix.FromFraction(1, 8), 200, slowWeapon);
        var fastRunnerId = w.SpawnUnit(1, w.Map.CellCenter(6, 25), Fix.FromFraction(3, 4), 500);
        // ──────────────────────────────────────────────────────────────────────────────────────

        int[] Owned(int owner) => ids.FindAll(i => w.GetUnit(i)!.OwnerId == owner).ToArray();

        // Collision-phase unit subsets: funnel two opposing mini-squads through the top gap
        // (wall at x=20 spans y=5..34; gap at y<5 is the only passage from left to right at y≈2).
        // At tick 105, id 8 is the left→right gap crosser for p0; ids 10/16/18 may already be
        // east of the wall by then. Both p0 and p1 subsets funnel through cells (20,0)..(20,4)
        // from opposite directions — exercises queueing, blocking, and head-on swaps.
        var p0FunnelIds = new[] { ids[6], ids[8], ids[14], ids[16] }; // player-0: ids 8,10,16,18 — row 3..4 left side
        var p1FunnelIds = new[] { ids[7], ids[9], ids[13], ids[19] }; // player-1: ids 9,11,15,21

        var script = new Dictionary<int, List<Command>>
        {
            [0] = new()
            {
                new MoveCommand(0, Owned(0), w.Map.CellCenter(35, 35)),
                new BuildCommand(0, worker, "depot", 7, 11),       // worker at (8,10) is in range
                new HarvestCommand(0, new[] { worker }, nodeId),     // harvest while depot builds
            },
            // v5 new: Defend stance on sniper (23) + slow_attacker (25).
            // sniper stays isolated at (2,20) — fog-gates any acquisition.
            // slow_attacker at (2,25) gets an explicit AttackCommand at t=5 (clears anchor),
            // then its explicit target flees at t=15 (fog chase-drop at distance>7).
            // After disengage, slow_attacker is idle-Defend and stays near (2,25).
            [2] = new()
            {
                new SetStanceCommand(0, new[] { sniperId, slowAttackerId }, Stance.Defend),
            },
            // v4b fog branch 1: explicit AttackCommand on a target outside sight (distance 8 > sight 7).
            // Fog-ON: Apply(AttackCommand) rejects it (IsVisibleTo returns false) → sniperId idle.
            // Fog-OFF: command accepted → sniperId chases sniperTargetId → trajectory diverges.
            [5] = new()
            {
                new AttackCommand(0, new[] { sniperId }, sniperTargetId),
                // slow_attacker explicit attack on fast_runner: distance 4 < sight 7 → accepted with fog ON.
                // Runner will escape at t=15; chase-drop fires when FogEnabled=true.
                new AttackCommand(0, new[] { slowAttackerId }, fastRunnerId),
            },
            // v4b fog branch 2: fast_runner bolts to (38,25); escapes sight of slow_attacker.
            // Fog-ON: chase-drop (Combat.cs IsVisibleTo check) sets AttackTargetId=0 once runner
            // crosses x=9 (distance > 7 from attacker at x=2). Fog-OFF: chase persists.
            [15] = new() { new MoveCommand(1, new[] { fastRunnerId }, w.Map.CellCenter(38, 25)) },
            [50] = new() { new MoveCommand(1, Owned(1), w.Map.CellCenter(35, 2)) },
            // v5 new: Patrol slow_attacker (id 25) from (2,25)→(2,32).
            // By t=60 the slow_attacker's explicit target (fastRunnerId=26) has fled (fog chase-drop
            // at t≈20-25); slow_attacker is idle near (2,25) in Defend stance.
            // South corridor (y=25-32, x=2) is enemy-free: sniperTarget at (10,20) is >7 cells
            // from any patrol cell (min dist ≈9.4), fastRunner is at (38,25). No acquisition fires.
            // Speed=1/8 → ~56 ticks/leg. By t=400, ~3 leg swaps confirmed via temp instrumentation.
            [60] = new()
            {
                new PatrolCommand(0, new[] { slowAttackerId }, w.Map.CellCenter(2, 32)),
            },
            [80] = new() { new BuildCommand(0, worker, "rax", 11, 9) }, // ignored if worker out of range — deterministic either way
            // v4a: collision phase — opposing squads through the top gap (x=20, y=0..4)
            // Player-0 subset crosses left→right; player-1 subset crosses right→left.
            // Head-on traffic through the 5-cell gap exercises queueing + head-on swaps.
            [105] = new()
            {
                new MoveCommand(0, p0FunnelIds, w.Map.CellCenter(38, 2)),  // p0 → right
                new MoveCommand(1, p1FunnelIds, w.Map.CellCenter(2, 2)),   // p1 → left
            },
            [120] = new() { new MoveCommand(0, new[] { ids[0], ids[2] }, w.Map.CellCenter(2, 38)) },
            [200] = new() { new AttackMoveCommand(0, Owned(0), w.Map.CellCenter(35, 2)) },
            [220] = new() { new AttackMoveCommand(1, Owned(1), w.Map.CellCenter(35, 35)) },
            [230] = new() { new MoveCommand(1, new[] { ids[5], ids[15] }, w.Map.CellCenter(38, 20)) },
            // v5 new: SetRallyCommand on rax before the t=250 trains so spawned marines move
            // toward (25,15). Rally evidence: marines have HasMoveOrder set after spawn.
            [245] = new()
            {
                new SetRallyCommand(0, RaxId, w.Map.CellCenter(25, 15)),
            },
            [250] = new() { new TrainCommand(0, RaxId, "marine"), new TrainCommand(0, RaxId, "marine") },
            // v6 new: research dmg1 upgrade at rax — completes ~t=320, boosting all p0 unit damage by +3.
            [300] = new() { new ResearchCommand(0, RaxId, "dmg1") },
            [350] = new() { new AttackCommand(0, Owned(0), ids[11]) },
            // v5 new: DestroyCommand on sniper (id=23). By t=400 the sniper has been fighting
            // sniperTarget (200hp, 5dmg/10ticks — target nearly dead but sniper fully alive at 200hp).
            // The sniper idle-acquired sniperTarget after the fastRunner fled (fog chase-drop).
            // Deterministically alive at t=399, confirmed via temp instrumentation.
            [400] = new()
            {
                new DestroyCommand(0, new[] { sniperId }),
            },
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

    private const ulong GoldenTrajectoryHash = 5141900307592480923UL;

}
