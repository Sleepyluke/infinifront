using SimCore.Math;
using SimCore.Sim;
using System.Linq;
using Xunit;

public class OccupancyTests
{
    [Fact]
    public void Spawned_Unit_Claims_Its_Cell()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        Assert.Equal(id, w.OccupantAt(3, 3));
        Assert.Equal(0, w.OccupantAt(4, 4)); // empty
    }

    [Fact]
    public void Dead_Unit_Releases_Its_Cell()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        w.GetUnit(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.OccupantAt(3, 3));
    }

    [Fact]
    public void Spawn_On_Occupied_Cell_Is_Rejected()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        var second = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        Assert.Equal(0, second); // 0 = rejected (no unit id is ever 0)
        Assert.Single(w.Units);
    }

    [Fact]
    public void Unit_Cannot_Enter_Occupied_Cell()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var blocker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50);
        var mover = w.SpawnUnit(0, w.Map.CellCenter(3, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { mover }, w.Map.CellCenter(5, 5)) });
        for (int i = 0; i < 50; i++) w.Step(System.Array.Empty<Command>());
        var (cx, cy) = w.Map.WorldToCell(w.GetUnit(mover)!.Position);
        Assert.NotEqual((5, 5), (cx, cy));               // never entered
        Assert.Equal(blocker, w.OccupantAt(5, 5));        // blocker still owns it
    }

    [Fact]
    public void Units_Queue_Through_A_Corridor()
    {
        // 3-wide map with a 1-wide corridor: two units ordered through must both arrive (one waits)
        var g = new MapGrid(7, 3);
        for (int x = 0; x < 7; x++) { g.SetPassable(x, 0, false); g.SetPassable(x, 2, false); }
        var w = new SimWorld(g, seed: 1);
        var a = w.SpawnUnit(0, w.Map.CellCenter(0, 1), Fix.FromFraction(1, 2), 50);
        var b = w.SpawnUnit(0, w.Map.CellCenter(1, 1), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { a, b }, w.Map.CellCenter(6, 1)) });
        for (int i = 0; i < 200; i++) w.Step(System.Array.Empty<Command>());
        var (ax, _) = w.Map.WorldToCell(w.GetUnit(a)!.Position);
        var (bx, _) = w.Map.WorldToCell(w.GetUnit(b)!.Position);
        Assert.True(ax >= 5, $"a stalled at x={ax}");
        Assert.True(bx >= 5, $"b stalled at x={bx}");
        Assert.NotEqual(w.Map.WorldToCell(w.GetUnit(a)!.Position), w.Map.WorldToCell(w.GetUnit(b)!.Position));
    }

    [Fact]
    public void Moving_Unit_Updates_Occupancy_As_It_Crosses_Cells()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(7, 5)) });
        for (int i = 0; i < 100; i++) w.Step(System.Array.Empty<Command>());
        var (cx, cy) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
        Assert.Equal(id, w.OccupantAt(cx, cy));   // owns where it stands
        Assert.Equal(0, w.OccupantAt(2, 5));      // released origin
    }

    [Fact]
    public void Occupancy_Stays_Coherent_Through_Battle()
    {
        // 6 units, 2 players, attack-move into each other; after 500 ticks every live
        // unit's current cell is its claim and every other live-unit cell is correct.
        var w = new SimWorld(new MapGrid(20, 20), seed: 42);
        var weapon = new Weapon { Damage = 8, Range = Fix.FromInt(2), CooldownTicks = 5 };
        // player 0 — left side
        for (int i = 0; i < 3; i++)
            w.SpawnUnit(0, w.Map.CellCenter(2 + i, 5), Fix.FromFraction(1, 2), 60,
                new Weapon { Damage = weapon.Damage, Range = weapon.Range, CooldownTicks = weapon.CooldownTicks });
        // player 1 — right side
        for (int i = 0; i < 3; i++)
            w.SpawnUnit(1, w.Map.CellCenter(15 + i, 5), Fix.FromFraction(1, 2), 60,
                new Weapon { Damage = weapon.Damage, Range = weapon.Range, CooldownTicks = weapon.CooldownTicks });

        var allIds = w.Units.Select(u => u.Id).ToArray();
        var p0Ids = w.Units.Where(u => u.OwnerId == 0).Select(u => u.Id).ToArray();
        var p1Ids = w.Units.Where(u => u.OwnerId == 1).Select(u => u.Id).ToArray();
        w.Step(new Command[]
        {
            new AttackMoveCommand(0, p0Ids, w.Map.CellCenter(17, 5)),
            new AttackMoveCommand(1, p1Ids, w.Map.CellCenter(2, 5)),
        });
        for (int i = 0; i < 499; i++) w.Step(System.Array.Empty<Command>());

        // every live unit must own exactly its current cell; no ghost claims
        foreach (var u in w.Units)
        {
            var (cx, cy) = w.Map.WorldToCell(u.Position);
            Assert.Equal(u.Id, w.OccupantAt(cx, cy));
            // bijective: the occupant at that cell is this unit, and this unit's position maps to that cell
            var (ux, uy) = w.Map.WorldToCell(w.GetUnit(w.OccupantAt(cx, cy))!.Position);
            Assert.Equal((cx, cy), (ux, uy));
        }
        // verify no cell is claimed by a dead unit
        for (int cy = 0; cy < w.Map.Height; cy++)
            for (int cx = 0; cx < w.Map.Width; cx++)
            {
                var occ = w.OccupantAt(cx, cy);
                if (occ == 0) continue;
                Assert.NotNull(w.GetUnit(occ)); // claimed cell must belong to a live unit
                // reverse: the unit's position must map back to this cell (ownership is bijective)
                var (ux, uy) = w.Map.WorldToCell(w.GetUnit(occ)!.Position);
                Assert.Equal((cx, cy), (ux, uy));
            }
    }

    [Fact]
    public void Group_Ordered_To_One_Point_Settles_On_Adjacent_Cells()
    {
        var w = new SimWorld(new MapGrid(12, 12), seed: 1);
        var ids = new int[4];
        for (int i = 0; i < 4; i++)
            ids[i] = w.SpawnUnit(0, w.Map.CellCenter(2 + i, 2), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, ids, w.Map.CellCenter(6, 6)) });
        for (int i = 0; i < 300; i++) w.Step(System.Array.Empty<Command>());
        foreach (var id in ids)
            Assert.False(w.GetUnit(id)!.HasMoveOrder, $"unit {id} never settled");
        // all on distinct cells, all within chebyshev 2 of the target
        var cells = ids.Select(id => w.Map.WorldToCell(w.GetUnit(id)!.Position)).ToHashSet();
        Assert.Equal(4, cells.Count);
        foreach (var (cx, cy) in cells)
            Assert.True(System.Math.Abs(cx - 6) <= 2 && System.Math.Abs(cy - 6) <= 2, $"({cx},{cy}) too far");
    }

    [Fact]
    public void HeadOn_Units_Swap_Instead_Of_Deadlocking()
    {
        var g = new MapGrid(8, 3);
        for (int x = 0; x < 8; x++) { g.SetPassable(x, 0, false); g.SetPassable(x, 2, false); }
        var w = new SimWorld(g, seed: 1);
        var a = w.SpawnUnit(0, w.Map.CellCenter(1, 1), Fix.FromFraction(1, 2), 50);
        var b = w.SpawnUnit(0, w.Map.CellCenter(5, 1), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] {
            new MoveCommand(0, new[] { a }, w.Map.CellCenter(6, 1)),
            new MoveCommand(0, new[] { b }, w.Map.CellCenter(1, 1)),
        });
        for (int i = 0; i < 200; i++) w.Step(System.Array.Empty<Command>());
        var (axc, _) = w.Map.WorldToCell(w.GetUnit(a)!.Position);
        var (bxc, _) = w.Map.WorldToCell(w.GetUnit(b)!.Position);
        Assert.True(axc >= 5, $"a deadlocked at x={axc}");
        Assert.True(bxc <= 2, $"b deadlocked at x={bxc}");
    }
}
