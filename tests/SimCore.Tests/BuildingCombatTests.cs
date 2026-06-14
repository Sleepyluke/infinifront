using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class BuildingCombatTests
{
    private static readonly BuildingSpec Hut = TestFactions.HutSpec;

    private static Weapon TestWeapon() =>
        new() { Damage = 10, Range = Fix.FromInt(3), CooldownTicks = 2 };

    private static (SimWorld w, int hutId) WorldWithEnemyHut()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[1].Minerals = 100;
        var enemyWorker = w.SpawnUnit(1, w.Map.CellCenter(10, 10), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(1, enemyWorker, "hut", 10, 11) });
        w.Step(System.Array.Empty<Command>()); // completes (BuildTimeTicks 1)
        w.GetUnit(enemyWorker)!.Hp = 0;
        w.Step(System.Array.Empty<Command>()); // remove worker
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Attack_Order_Destroys_Building_And_Restores_Passability()
    {
        var (w, hutId) = WorldWithEnemyHut();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(8, 11), Fix.FromFraction(1, 2), 50, TestWeapon());
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, hutId) });
        for (int i = 0; i < 60 && w.GetBuilding(hutId) is not null; i++)
            w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetBuilding(hutId));
        Assert.True(w.Map.IsPassable(10, 11));
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void AttackMove_Acquires_Building_When_No_Units_Near()
    {
        var (w, hutId) = WorldWithEnemyHut();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(9, 11), Fix.FromFraction(1, 2), 50, TestWeapon());
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { attacker }, w.Map.CellCenter(15, 11)) });
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(hutId, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Acquisition_Prefers_Units_Over_Buildings()
    {
        var (w, hutId) = WorldWithEnemyHut();
        var enemyUnit = w.SpawnUnit(1, w.Map.CellCenter(9, 10), Fix.FromFraction(1, 2), 100);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(9, 11), Fix.FromFraction(1, 2), 50, TestWeapon());
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { attacker }, w.Map.CellCenter(15, 11)) });
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(enemyUnit, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Cannot_Attack_Own_Building()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[0].Minerals = 100;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(10, 10), Fix.FromFraction(1, 2), 30, TestWeapon());
        w.Step(new Command[] { new BuildCommand(0, worker, "hut", 10, 11) });
        var bid = w.Buildings[0].Id;
        w.Step(new Command[] { new AttackCommand(0, new[] { worker }, bid) });
        Assert.Equal(0, w.GetUnit(worker)!.AttackTargetId);
    }
}
