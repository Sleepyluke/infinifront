using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class AttackMoveTests
{
    private static Weapon TestWeapon() =>
        new() { Damage = 10, Range = Fix.FromInt(2), CooldownTicks = 2 };

    [Fact]
    public void AttackMove_Engages_Enemy_En_Route_Then_Arrives()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var bystander = w.SpawnUnit(1, w.Map.CellCenter(8, 5), Fix.FromFraction(1, 2), 20);
        var dest = w.Map.CellCenter(15, 5);

        w.Step(new Command[] { new AttackMoveCommand(0, new[] { soldier }, dest) });
        for (int i = 0; i < 300 && (w.GetUnit(soldier)!.IsAttackMoving || w.GetUnit(soldier)!.HasMoveOrder); i++)
            w.Step(System.Array.Empty<Command>());

        Assert.Null(w.GetUnit(bystander));                       // killed on the way
        Assert.Equal(dest, w.GetUnit(soldier)!.Position);        // then continued to destination
        Assert.False(w.GetUnit(soldier)!.IsAttackMoving);
    }

    [Fact]
    public void AttackMove_Without_Enemies_Just_Moves()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var dest = w.Map.CellCenter(10, 5);
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { soldier }, dest) });
        for (int i = 0; i < 200 && (w.GetUnit(soldier)!.IsAttackMoving || w.GetUnit(soldier)!.HasMoveOrder); i++)
            w.Step(System.Array.Empty<Command>());
        Assert.Equal(dest, w.GetUnit(soldier)!.Position);
        Assert.False(w.GetUnit(soldier)!.IsAttackMoving);
    }

    [Fact]
    public void Acquisition_Prefers_Nearest_Then_Earliest_Spawned()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(10, 10), Fix.FromFraction(1, 2), 50, TestWeapon());
        var east = w.SpawnUnit(1, w.Map.CellCenter(13, 10), Fix.FromFraction(1, 2), 100); // equidistant
        var west = w.SpawnUnit(1, w.Map.CellCenter(7, 10), Fix.FromFraction(1, 2), 100);  // equidistant

        w.Step(new Command[] { new AttackMoveCommand(0, new[] { soldier }, w.Map.CellCenter(10, 15)) });
        w.Step(System.Array.Empty<Command>());

        Assert.Equal(east, w.GetUnit(soldier)!.AttackTargetId);  // tie → earlier-spawned (lower id) wins
    }

    [Fact]
    public void Plain_Move_Cancels_Attack_State()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 500);
        w.Step(new Command[] { new AttackCommand(0, new[] { soldier }, enemy) });
        Assert.Equal(enemy, w.GetUnit(soldier)!.AttackTargetId);

        w.Step(new Command[] { new MoveCommand(0, new[] { soldier }, w.Map.CellCenter(1, 1)) });
        Assert.Equal(0, w.GetUnit(soldier)!.AttackTargetId);
        Assert.False(w.GetUnit(soldier)!.IsAttackMoving);
    }
}
