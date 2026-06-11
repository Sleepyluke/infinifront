using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class VisionTests
{
    private static SimWorld World() => new(new MapGrid(30, 30), seed: 1);

    [Fact]
    public void Unit_Sees_Its_Surroundings_Not_The_Far_Map()
    {
        var w = World();
        w.SpawnUnit(0, w.Map.CellCenter(5, 5), ReferenceSpecs.Trooper); // sight 7
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(0, 5, 5));
        Assert.True(w.IsVisibleTo(0, 11, 5));   // distance 6 <= 7
        Assert.False(w.IsVisibleTo(0, 25, 25)); // far corner
        Assert.False(w.IsVisibleTo(1, 5, 5));   // other player sees nothing
    }

    [Fact]
    public void Vision_Moves_With_The_Unit_And_Leaves_Explored_Behind()
    {
        var w = World();
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), ReferenceSpecs.Outrider); // sight 9, fast
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(0, 5, 5));
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(25, 5)) });
        for (int i = 0; i < 120; i++) w.Step(System.Array.Empty<Command>());
        Assert.False(w.IsVisibleTo(0, 5, 5));   // moved away — no longer visible
        Assert.True(w.IsExploredBy(0, 5, 5));   // but remembered
        Assert.True(w.IsVisibleTo(0, 25, 5));
    }

    [Fact]
    public void Buildings_Grant_Vision()
    {
        var w = World();
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 10, 10); // sight 9
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(0, 15, 10));
        Assert.False(w.IsVisibleTo(0, 25, 10));
    }

    [Fact]
    public void FogDisabled_Sees_Everything()
    {
        var w = World();
        w.FogEnabled = false;
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(0, 25, 25));
        Assert.True(w.IsVisibleTo(1, 0, 0));
    }
}
