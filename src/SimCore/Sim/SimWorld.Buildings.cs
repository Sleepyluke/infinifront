using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    private readonly System.Collections.Generic.List<Building> _buildings = new(); // stable order — determinism
    private readonly System.Collections.Generic.Dictionary<int, Building> _buildingsById = new(); // lookup only

    public System.Collections.Generic.IReadOnlyList<Building> Buildings => _buildings;
    public Building? GetBuilding(int id) => _buildingsById.TryGetValue(id, out var b) ? b : null;

    internal static FixVec FootprintCenter(int cellX, int cellY, int width, int height) =>
        new(Fix.FromInt(cellX) + Fix.FromFraction(width, 2), Fix.FromInt(cellY) + Fix.FromFraction(height, 2));

    internal FixVec CenterOf(Building b) => FootprintCenter(b.CellX, b.CellY, b.Spec.Width, b.Spec.Height);

    private bool FootprintPlaceable(int cellX, int cellY, int width, int height)
    {
        for (int y = cellY; y < cellY + height; y++)
            for (int x = cellX; x < cellX + width; x++)
                if (!Map.IsPassable(x, y)) return false;
        foreach (var u in _units)
        {
            var (ux, uy) = Map.WorldToCell(u.Position);
            if (ux >= cellX && ux < cellX + width && uy >= cellY && uy < cellY + height) return false;
        }
        return true;
    }

    internal int PlaceBuilding(int ownerId, BuildingSpec spec, int cellX, int cellY)
    {
        var b = new Building { Id = _nextId++, OwnerId = ownerId, CellX = cellX, CellY = cellY, Spec = spec, Hp = spec.MaxHp };
        _buildings.Add(b);
        _buildingsById[b.Id] = b;
        for (int y = cellY; y < cellY + spec.Height; y++)
            for (int x = cellX; x < cellX + spec.Width; x++)
                Map.SetPassable(x, y, false);
        return b.Id;
    }

    private void UpdateConstruction()
    {
        foreach (var b in _buildings)
        {
            if (b.IsComplete) continue;
            b.BuildProgress++;
            if (b.BuildProgress >= b.Spec.BuildTimeTicks)
            {
                b.IsComplete = true;
                _players[b.OwnerId].SupplyCap += b.Spec.SupplyProvided;
            }
        }
    }

    /// <summary>Reverse-index removal preserves order; restores footprint passability.</summary>
    private void RemoveDeadBuildings()
    {
        for (int i = _buildings.Count - 1; i >= 0; i--)
        {
            var b = _buildings[i];
            if (b.Hp > 0) continue;
            if (b.IsComplete) _players[b.OwnerId].SupplyCap -= b.Spec.SupplyProvided;
            for (int y = b.CellY; y < b.CellY + b.Spec.Height; y++)
                for (int x = b.CellX; x < b.CellX + b.Spec.Width; x++)
                    Map.SetPassable(x, y, true);
            _buildingsById.Remove(b.Id);
            _buildings.RemoveAt(i);
        }
    }
}
