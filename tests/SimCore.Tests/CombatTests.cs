using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class CombatTests
{
    private static Weapon TestWeapon() =>
        new() { Damage = 10, Range = Fix.FromInt(2), CooldownTicks = 4 };

    private static SimWorld NewWorld() => new(new MapGrid(20, 20), seed: 1);

    [Fact]
    public void In_Range_Attack_Deals_Damage_On_Cooldown_Cadence()
    {
        var w = NewWorld();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var victim = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 25);

        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, victim) });
        Assert.Equal(15, w.GetUnit(victim)!.Hp);          // hit on tick 1

        for (int i = 0; i < 3; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(15, w.GetUnit(victim)!.Hp);          // cooling down (ticks 2-4)

        w.Step(System.Array.Empty<Command>());
        Assert.Equal(5, w.GetUnit(victim)!.Hp);           // second hit on tick 5
    }

    [Fact]
    public void Target_Death_Removes_Unit_And_Clears_Attacker_Target()
    {
        var w = NewWorld();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var victim = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 10);

        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, victim) });
        Assert.Null(w.GetUnit(victim));                   // 10 dmg kills, removed same tick
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Out_Of_Range_Attacker_Chases_Target()
    {
        var w = NewWorld();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(2, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var victim = w.SpawnUnit(1, w.Map.CellCenter(15, 5), Fix.FromFraction(1, 2), 200);

        var startDist = (w.GetUnit(victim)!.Position - w.GetUnit(attacker)!.Position).Length();
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, victim) });
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        var endDist = (w.GetUnit(victim)!.Position - w.GetUnit(attacker)!.Position).Length();
        Assert.True(endDist < startDist, "attacker should close distance");

        for (int i = 0; i < 60; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.GetUnit(victim)!.Hp < 200, "chaser should eventually reach range and deal damage");
    }

    [Fact]
    public void Cannot_Attack_Own_Unit()
    {
        var w = NewWorld();
        var a = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var b = w.SpawnUnit(0, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { a }, b) });
        Assert.Equal(50, w.GetUnit(b)!.Hp);
        Assert.Equal(0, w.GetUnit(a)!.AttackTargetId);
    }

    [Fact]
    public void Unit_Without_Weapon_Ignores_Attack_Command()
    {
        var w = NewWorld();
        var pacifist = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50);
        var victim = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { pacifist }, victim) });
        Assert.Equal(50, w.GetUnit(victim)!.Hp);
        Assert.Equal(0, w.GetUnit(pacifist)!.AttackTargetId);
    }

    [Fact]
    public void Simultaneous_Lethal_Exchange_Kills_Both()
    {
        // Symmetric-exchange rule: the earlier-spawned unit's killing blow does not
        // prevent the later-spawned unit from firing back within the same tick.
        var w = NewWorld();
        var a = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 10, TestWeapon());
        var b = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 10, TestWeapon());
        w.Step(new Command[]
        {
            new AttackCommand(0, new[] { a }, b),
            new AttackCommand(1, new[] { b }, a),
        });
        Assert.Null(w.GetUnit(a));
        Assert.Null(w.GetUnit(b));
    }
}
