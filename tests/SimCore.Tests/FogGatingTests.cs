using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FogGatingTests
{
    private static Weapon TestWeapon() =>
        new() { Damage = 5, Range = Fix.FromInt(4), CooldownTicks = 3 };

    [Fact]
    public void AttackMove_Ignores_Enemies_In_Fog()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        // enemy outside sight (legacy spawn default sight 7); within acquire range never matters — fog wins
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(30, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { attacker }, w.Map.CellCenter(10, 5)) });
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Explicit_Attack_On_Fogged_Target_Is_Rejected()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(30, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, enemy) });
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Chase_Drops_When_Target_Escapes_Into_Fog()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 8), 50, TestWeapon());
        var runner = w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 500);
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, runner) });
        Assert.Equal(runner, w.GetUnit(attacker)!.AttackTargetId);
        w.Step(new Command[] { new MoveCommand(1, new[] { runner }, w.Map.CellCenter(35, 5)) });
        for (int i = 0; i < 150; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId); // lost in the fog
    }

    [Fact]
    public void FogDisabled_Restores_Omniscient_Acquisition()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1) { FogEnabled = false };
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(30, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, enemy) });
        Assert.Equal(enemy, w.GetUnit(attacker)!.AttackTargetId);
    }
}
