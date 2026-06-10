using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class StateHasherTests
{
    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 5);
        w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromFraction(1, 2), 50);
        w.SpawnUnit(1, FixVec.FromInts(5, 5), Fix.FromFraction(1, 2), 80);
        return w;
    }

    [Fact]
    public void Identical_Worlds_Hash_Equal() =>
        Assert.Equal(StateHasher.Hash(World()), StateHasher.Hash(World()));

    [Fact]
    public void Position_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.GetUnit(1)!.Position = FixVec.FromInts(2, 1);
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Hp_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.GetUnit(2)!.Hp = 79;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Tick_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.Step(System.Array.Empty<Command>());
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void MoveTarget_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.GetUnit(1)!.MoveTarget = FixVec.FromInts(9, 9);
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Rng_Draw_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.Rng.NextUInt();
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Weapon_Cooldown_Changes_Hash()
    {
        var a = World();
        var b = World();
        var weaponA = new Weapon { Damage = 5, Range = Fix.FromInt(2), CooldownTicks = 4 };
        var weaponB = new Weapon { Damage = 5, Range = Fix.FromInt(2), CooldownTicks = 4 };
        a.GetUnit(1)!.Weapon = weaponA;
        b.GetUnit(1)!.Weapon = weaponB;
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));   // identical weapons → equal
        weaponB.CooldownRemaining = 3;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b)); // cooldown drift → detected
    }

    [Fact]
    public void Zero_Stat_Weapon_Differs_From_No_Weapon()
    {
        var a = World();
        var b = World();
        b.GetUnit(1)!.Weapon = new Weapon { Damage = 0, Range = Fix.Zero, CooldownTicks = 0 };
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
