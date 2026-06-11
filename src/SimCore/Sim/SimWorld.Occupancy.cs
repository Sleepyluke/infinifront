namespace SimCore.Sim;

public sealed partial class SimWorld
{
    // unit id per cell, 0 = empty. Derived from unit positions — EXCLUDED from StateHasher.
    private int[] _occupancy = System.Array.Empty<int>();

    private void EnsureOccupancy()
    {
        if (_occupancy.Length != Map.Width * Map.Height)
            _occupancy = new int[Map.Width * Map.Height];
    }

    public int OccupantAt(int cx, int cy) =>
        cx < 0 || cy < 0 || cx >= Map.Width || cy >= Map.Height ? 0
        : _occupancy[cy * Map.Width + cx];

    private void ClaimCell(int cx, int cy, int unitId) => _occupancy[cy * Map.Width + cx] = unitId;
    private void ReleaseCell(int cx, int cy, int unitId)
    {
        var i = cy * Map.Width + cx;
        if (_occupancy[i] == unitId) _occupancy[i] = 0; // only the claimant releases
    }
}
