using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class StanceTests
{
    private static Weapon Gun() => new() { Damage = 5, Range = Fix.FromInt(3), CooldownTicks = 4 };

    [Fact]
    public void Units_Default_To_AutoAttack()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        Assert.Equal(Stance.AutoAttack, w.GetUnit(id)!.Stance);
    }

    [Fact]
    public void SetStance_Applies_To_Owned_Units_Only()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var mine = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var theirs = w.SpawnUnit(1, w.Map.CellCenter(8, 8), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new SetStanceCommand(0, new[] { mine, theirs }, Stance.Passive) });
        Assert.Equal(Stance.Passive, w.GetUnit(mine)!.Stance);
        Assert.Equal(Stance.AutoAttack, w.GetUnit(theirs)!.Stance);
    }

    [Fact]
    public void Stance_Changes_Hash()
    {
        SimWorld World()
        {
            var w = new SimWorld(new MapGrid(16, 16), seed: 5);
            w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromFraction(1, 2), 50);
            return w;
        }
        var a = World();
        var b = World();
        b.GetUnit(1)!.Stance = Stance.Defend;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
