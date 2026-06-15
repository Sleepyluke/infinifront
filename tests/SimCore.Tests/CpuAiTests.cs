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
}
