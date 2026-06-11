using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class PatrolTests
{
    private static Weapon Gun() => new() { Damage = 5, Range = Fix.FromInt(3), CooldownTicks = 4 };

    [Fact]
    public void Patrol_Loops_Between_Both_Points()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        bool reachedB = false, returnedA = false;
        for (int i = 0; i < 400; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (cx, _) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
            if (cx == 15) reachedB = true;
            if (reachedB && cx == 5) returnedA = true;
        }
        Assert.True(reachedB, "never reached patrol point B");
        Assert.True(returnedA, "never returned to patrol point A");
        Assert.True(w.GetUnit(id)!.IsPatrolling); // still looping
    }

    [Fact]
    public void Patrolling_Unit_Engages_And_Resumes()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var victim = w.SpawnUnit(1, w.Map.CellCenter(10, 6), Fix.FromFraction(1, 2), 10);
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        for (int i = 0; i < 100; i++) w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetUnit(victim));            // engaged and killed en route
        Assert.True(w.GetUnit(id)!.IsPatrolling);  // resumed the loop
    }

    [Fact]
    public void Passive_Patrol_Walks_Without_Engaging()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var bystander = w.SpawnUnit(1, w.Map.CellCenter(10, 6), Fix.FromFraction(1, 2), 10);
        w.Step(new Command[] { new SetStanceCommand(0, new[] { id }, Stance.Passive) });
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        for (int i = 0; i < 100; i++) w.Step(System.Array.Empty<Command>());
        Assert.NotNull(w.GetUnit(bystander));      // untouched
    }

    [Fact]
    public void New_Order_Cancels_Patrol()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(20, 20)) });
        Assert.False(w.GetUnit(id)!.IsPatrolling);
    }

    [Fact]
    public void Blocked_Endpoint_Patrol_Still_Loops()
    {
        // Park an idle unit ON patrol point B. The patroller must still swap legs (one cell short is fine).
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        // Blocker sits at B — passive, no weapon, different owner so it's neutral to patroller
        // but blocks the cell. Actually same owner non-fighting is fine too.
        w.SpawnUnit(1, w.Map.CellCenter(15, 5), Fix.FromFraction(1, 2), 50);

        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        bool reachedNearB = false, returnedA = false;
        for (int i = 0; i < 400; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (cx, _) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
            if (cx >= 13) reachedNearB = true;  // got close to B (one cell short is fine)
            if (reachedNearB && cx <= 7) returnedA = true;
        }
        Assert.True(reachedNearB, "never got close to patrol point B");
        Assert.True(returnedA, "never returned toward patrol point A");
        Assert.True(w.GetUnit(id)!.IsPatrolling);
    }
}
