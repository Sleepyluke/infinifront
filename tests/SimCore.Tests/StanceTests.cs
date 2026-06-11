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

    [Fact]
    public void Idle_AutoAttack_Unit_Engages_Visible_Enemy()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var guard = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var intruder = w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 50); // dist 4 <= range3+bonus2, sight 7
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(intruder, w.GetUnit(guard)!.AttackTargetId);
        Assert.True(w.GetUnit(guard)!.HasAnchor);
    }

    [Fact]
    public void Idle_Passive_Unit_Never_Engages()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var guard = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new SetStanceCommand(0, new[] { guard }, Stance.Passive) });
        w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 50);
        for (int i = 0; i < 5; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(guard)!.AttackTargetId);
    }

    [Fact]
    public void AutoAttack_Chase_Drops_At_Anchor_Leash()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var guard = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 4), 50, Gun());
        var bait = w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 500);
        w.Step(System.Array.Empty<Command>()); // guard acquires, anchor set
        Assert.Equal(bait, w.GetUnit(guard)!.AttackTargetId);
        w.Step(new Command[] { new MoveCommand(1, new[] { bait }, w.Map.CellCenter(35, 5)) });
        for (int i = 0; i < 200; i++) w.Step(System.Array.Empty<Command>());
        var g = w.GetUnit(guard)!;
        Assert.Equal(0, g.AttackTargetId);                      // dropped (leash or fog)
        var (gx, _) = w.Map.WorldToCell(g.Position);
        Assert.True(gx <= 5 + 3 + 4 + 1, $"guard chased to x={gx}"); // never beyond anchor + Range + LeashBonus (+1 slack)
        Assert.False(g.HasAnchor);                              // anchor cleared after disengage
    }

    [Fact]
    public void Explicit_Move_Order_Clears_Anchor_Engagement()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var guard = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 500);
        w.Step(System.Array.Empty<Command>()); // engaged
        w.Step(new Command[] { new MoveCommand(0, new[] { guard }, w.Map.CellCenter(2, 2)) });
        Assert.False(w.GetUnit(guard)!.HasAnchor);
        Assert.Equal(0, w.GetUnit(guard)!.AttackTargetId);
    }
}
