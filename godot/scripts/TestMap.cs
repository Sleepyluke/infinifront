using SimCore.Math;
using SimCore.Sim;

namespace LlmRts.Godot;

public static class TestMap
{
    public const int Size = 40;

    /// <summary>Two bases in opposite corners, mineral lines near each,
    /// a rock ridge across the middle with two gaps.</summary>
    public static SimWorld Build()
    {
        var w = new SimWorld(new MapGrid(Size, Size), seed: 42, faction: ReferenceFaction.Def);

        foreach (var p in new[] { 0, 1 })
            w.Players[p].Minerals = 300;

        // rock ridge: vertical wall at x=20, gaps at y=8..11 and y=28..31
        for (int y = 0; y < Size; y++)
            if (y < 8 || (y > 11 && y < 28) || y > 31)
                w.Map.SetPassable(20, y, false);

        // player 0 base (top-left)
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 4, 4, "depot");
        w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 8, 4, "barracks");
        for (int i = 0; i < 4; i++)
            w.AddResourceNode(2, 8 + i, amount: 500);
        for (int i = 0; i < 3; i++)
            w.SpawnUnit(0, w.Map.CellCenter(6, 8 + i), ReferenceSpecs.Fabber);
        for (int i = 0; i < 4; i++)
            w.SpawnUnit(0, w.Map.CellCenter(10, 8 + i), ReferenceSpecs.Trooper);

        // player 1 base (bottom-right, mirrored)
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 34, 34, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 30, 34, "barracks");
        for (int i = 0; i < 4; i++)
            w.AddResourceNode(37, 28 + i, amount: 500);
        for (int i = 0; i < 3; i++)
            w.SpawnUnit(1, w.Map.CellCenter(33, 28 + i), ReferenceSpecs.Fabber);
        for (int i = 0; i < 4; i++)
            w.SpawnUnit(1, w.Map.CellCenter(29, 28 + i), ReferenceSpecs.Trooper);

        return w;
    }
}
