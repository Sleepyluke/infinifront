using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class AllyImmuneCombatTests
{
    // A short-range weapon; two adjacent units. Helper builds a world with both units placed close.
    // NOTE (verify-against-source): Weapon is a sealed class with init-only props (Damage is int,
    // not Fix), not a positional record — so we construct it with an object initializer rather than
    // the plan's `new Weapon(Damage: Fix..., ...)`. Test-only adjustment; the sim is unchanged.
    private static (SimWorld w, int a, int b) TwoUnits(int ownerA, int ownerB)
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 4, faction: null);
        var weapon = new Weapon { Damage = 5, Range = Fix.FromInt(3), CooldownTicks = 10 };
        int a = w.SpawnUnit(ownerA, w.Map.CellCenter(5, 5), Fix.FromInt(1), hp: 50, weapon: weapon);
        int b = w.SpawnUnit(ownerB, w.Map.CellCenter(6, 5), Fix.FromInt(1), hp: 50, weapon: weapon);
        w.FogEnabled = false; // isolate the ally test from vision
        return (w, a, b);
    }

    [Fact]
    public void Enemy_Is_Attacked()
    {
        var (w, a, b) = TwoUnits(ownerA: 0, ownerB: 1); // different solo teams
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.GetUnit(b)!.Hp < 50, "an enemy in range should take damage");
    }

    [Fact]
    public void Teammate_Is_Not_Attacked()
    {
        var (w, a, b) = TwoUnits(ownerA: 0, ownerB: 1);
        w.SetTeam(0, 7); w.SetTeam(1, 7); // same team
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(b)!.Hp); // allies never auto-acquire each other
    }

    [Fact]
    public void Explicit_Attack_On_Teammate_Is_Ignored()
    {
        var (w, a, b) = TwoUnits(ownerA: 0, ownerB: 1);
        w.SetTeam(0, 7); w.SetTeam(1, 7);
        w.Step(new Command[] { new AttackCommand(0, new[] { a }, b) });
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(b)!.Hp);
    }
}
