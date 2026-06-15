using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class CpuAiTests
{
    private static readonly List<Command> Empty = new();

    [Fact]
    public void SetCpu_Sets_Controller_And_Difficulty()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        Assert.Equal(PlayerController.Human, w.Players[0].Controller); // default
        w.SetCpu(1, AiDifficulty.Easy);
        Assert.Equal(PlayerController.Cpu, w.Players[1].Controller);
        Assert.Equal(AiDifficulty.Easy, w.Players[1].Difficulty);
        Assert.Equal(PlayerController.Human, w.Players[0].Controller);
    }

    [Fact]
    public void Cpu_With_No_Assets_Does_Not_Crash_Or_Spawn()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.SetCpu(1, AiDifficulty.Easy);
        for (int t = 0; t < 30; t++) w.Step(Empty); // no buildings/workers → AI finds no producer; no throw
        Assert.Empty(w.Units);
    }

    // A CPU player (id 1) with a depot+barracks+fabber, a node, and minerals.
    // Player 0 gets a lone depot too, so the match stays InProgress (otherwise it latches
    // Over at tick 0 and UpdateAi's Phase==Over guard would stop the CPU after one tick).
    private static SimWorld EasyEcoWorld()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 3, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot"); // keep player 0 alive → match InProgress
        w.Players[1].Minerals = 1000;
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 30, 30, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 33, 30, "barracks");
        w.SpawnUnit(1, w.Map.CellCenter(30, 28), ReferenceSpecs.Fabber, "fabber");
        w.AddResourceNode(28, 28, 1000);
        w.SetCpu(1, AiDifficulty.Easy);
        return w;
    }

    private static int Workers(SimWorld w, int p)
    {
        int c = 0; foreach (var u in w.Units) if (u.OwnerId == p && u.Harvester is not null) c++; return c;
    }

    // A CPU player (id 1) at the given difficulty with a full base + big node; player 0 has a lone
    // depot so the match stays InProgress.
    private static SimWorld OneCpuWorld(AiDifficulty diff)
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 9, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");
        w.Players[1].Minerals = 2000;
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 30, 30, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 33, 30, "barracks");
        w.SpawnUnit(1, w.Map.CellCenter(30, 28), ReferenceSpecs.Fabber, "fabber");
        w.AddResourceNode(28, 28, 100000);
        w.SetCpu(1, diff);
        return w;
    }

    [Fact]
    public void Medium_Trains_More_Workers_Than_Easy_Cap()
    {
        var w = OneCpuWorld(AiDifficulty.Medium);
        for (int t = 0; t < 3000; t++) w.Step(Empty);
        int workers = Workers(w, 1);
        Assert.True(workers > 8, $"Medium worker cap is 10 (> Easy's 8); got {workers}");
        Assert.True(workers <= 10, $"Medium worker cap is 10; got {workers}");
    }

    [Fact]
    public void Medium_Rebuilds_Lost_Production_Building()
    {
        var w = OneCpuWorld(AiDifficulty.Medium);
        for (int t = 0; t < 300; t++) w.Step(Empty); // establish economy
        // Destroy the CPU's only barracks (combat producer).
        foreach (var b in w.Buildings) if (b.OwnerId == 1 && b.DefId == "barracks") { b.Hp = 0; break; }
        w.Step(Empty); // RemoveDeadBuildings clears it
        Assert.DoesNotContain(w.Buildings, b => b.OwnerId == 1 && b.DefId == "barracks");
        for (int t = 0; t < 1000; t++) w.Step(Empty);
        Assert.Contains(w.Buildings, b => b.OwnerId == 1 && b.DefId == "barracks"); // rebuilt
    }

    [Fact]
    public void Easy_Trains_Workers_Up_To_Cap()
    {
        var w = EasyEcoWorld();
        int start = Workers(w, 1);
        for (int t = 0; t < 1200; t++) w.Step(Empty);
        int now = Workers(w, 1);
        Assert.True(now > start, $"expected CPU to train workers (start {start}, now {now})");
        Assert.True(now <= 8, $"worker cap is 8, got {now}");
    }

    [Fact]
    public void Easy_Harvests_So_Minerals_Are_Spent_And_Earned()
    {
        var w = EasyEcoWorld();
        // Run long enough to see harvest income (workers deliver minerals back).
        for (int t = 0; t < 600; t++) w.Step(Empty);
        // At least one worker is in a harvest phase (assigned to the node).
        bool anyHarvesting = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Harvester is not null && u.HarvestPhase != HarvestPhase.None) anyHarvesting = true;
        Assert.True(anyHarvesting, "expected CPU workers to be harvesting");
    }

    [Fact]
    public void Easy_Builds_Supply_When_Blocked()
    {
        var w = EasyEcoWorld();
        // Depot gives 8 supply; workers cost 1 each. Drive toward the cap so the AI builds supply.
        for (int t = 0; t < 2000; t++) w.Step(Empty);
        int depots = 0;
        foreach (var b in w.Buildings) if (b.OwnerId == 1 && b.Spec.SupplyProvided > 0) depots++;
        Assert.True(depots >= 2, $"expected CPU to build at least one extra supply building, total {depots}");
    }

    [Fact]
    public void Easy_Trains_Combat_Units()
    {
        var w = EasyEcoWorld();
        for (int t = 0; t < 1500; t++) w.Step(Empty);
        int combat = 0;
        foreach (var u in w.Units) if (u.OwnerId == 1 && u.Weapon is not null) combat++;
        Assert.True(combat > 0, "expected CPU to train combat units from the barracks");
    }

    [Fact]
    public void Easy_Attacks_Enemy_Base_Once_Army_Is_Built()
    {
        // Player 0 (human) has a building to be the attack target; player 1 is the CPU.
        var w = new SimWorld(new MapGrid(40, 40), seed: 5, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot"); // human base (attack target)
        w.Players[1].Minerals = 5000;
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 30, 30, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 33, 30, "barracks");
        w.SpawnUnit(1, w.Map.CellCenter(30, 28), ReferenceSpecs.Fabber, "fabber");
        w.AddResourceNode(28, 28, 5000);
        w.SetCpu(1, AiDifficulty.Easy);

        for (int t = 0; t < 2000; t++) w.Step(Empty);

        // Once the CPU has >= threshold combat units, an attack tick issues an attack-move toward
        // the human base — at least one CPU combat unit should be attack-moving (or moving west).
        bool attacking = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Weapon is not null && (u.IsAttackMoving || u.HasMoveOrder)) attacking = true;
        Assert.True(attacking, "expected CPU combat units to be attack-moving toward the enemy base");
    }

    private static SimWorld CpuVsCpuWorld()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 7, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.Players[0].Minerals = 800;
        w.Players[1].Minerals = 800;
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");
        w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 6, 3, "barracks");
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 34, 34, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 31, 34, "barracks");
        w.SpawnUnit(0, w.Map.CellCenter(5, 6), ReferenceSpecs.Fabber, "fabber");
        w.SpawnUnit(1, w.Map.CellCenter(33, 32), ReferenceSpecs.Fabber, "fabber");
        w.AddResourceNode(8, 8, 5000);
        w.AddResourceNode(30, 30, 5000);
        w.SetCpu(0, AiDifficulty.Easy);
        w.SetCpu(1, AiDifficulty.Easy);
        return w;
    }

    [Fact]
    public void Cpu_Vs_Cpu_Is_Deterministic_Across_Runs()
    {
        var a = CpuVsCpuWorld();
        var b = CpuVsCpuWorld();
        for (int t = 0; t < 400; t++)
        {
            a.Step(Empty);
            b.Step(Empty);
            Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        }
    }

    [Fact]
    public void Cpu_Vs_Cpu_Produces_Activity()
    {
        var w = CpuVsCpuWorld();
        for (int t = 0; t < 400; t++) w.Step(Empty);
        Assert.True(w.Units.Count > 2, $"expected CPUs to build up forces, got {w.Units.Count} units");
    }

    [Fact]
    public void Hard_Defends_A_Threatened_Base()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 11, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");        // far human base (commit target)
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 30, 30, "barracks"); // CPU base to defend
        for (int i = 0; i < 8; i++) w.SpawnUnit(1, w.Map.CellCenter(25 + i % 4, 35), ReferenceSpecs.Trooper, "trooper");
        var enemy = w.SpawnUnit(0, w.Map.CellCenter(31, 31), ReferenceSpecs.Trooper, "trooper"); // next to CPU base
        w.SetCpu(1, AiDifficulty.Hard);

        for (int t = 0; t <= 10; t++) w.Step(Empty); // reach a decision tick (Tick % 10 == 0 at t=10)

        bool defending = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Weapon is not null && u.IsAttackMoving && u.AttackMoveDest.X > Fix.FromInt(20))
                defending = true;
        Assert.True(defending, "expected Hard CPU to recall its army to the threatened base (east), not attack west");
    }

    [Fact]
    public void Hard_Holds_When_Outnumbered()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 12, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 30, 30, "barracks");
        for (int i = 0; i < 8; i++) w.SpawnUnit(1, w.Map.CellCenter(28, 30 + i % 6), ReferenceSpecs.Trooper, "trooper"); // CPU army 8
        for (int i = 0; i < 20; i++) w.SpawnUnit(0, w.Map.CellCenter(3, 5 + i % 20), ReferenceSpecs.Trooper, "trooper"); // human army 20 (far)
        w.SetCpu(1, AiDifficulty.Hard);

        for (int t = 0; t <= 120; t++) w.Step(Empty); // reach an attack tick (120 % 120 == 0)

        bool committed = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Weapon is not null && u.IsAttackMoving && u.AttackMoveDest.X < Fix.FromInt(20))
                committed = true;
        Assert.False(committed, "expected Hard CPU to hold (not march west) while outnumbered and unthreatened");
    }

    [Fact]
    public void Hard_Commits_When_Ahead()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 13, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 30, 30, "barracks");
        for (int i = 0; i < 10; i++) w.SpawnUnit(1, w.Map.CellCenter(28, 30 + i % 8), ReferenceSpecs.Trooper, "trooper");
        w.SetCpu(1, AiDifficulty.Hard);

        for (int t = 0; t <= 120; t++) w.Step(Empty);

        bool committed = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Weapon is not null && u.IsAttackMoving && u.AttackMoveDest.X < Fix.FromInt(20))
                committed = true;
        Assert.True(committed, "expected Hard CPU to commit (march toward the enemy base at x=3) when ahead");
    }
}
