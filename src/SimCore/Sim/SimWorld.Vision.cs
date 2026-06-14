namespace SimCore.Sim;

public sealed partial class SimWorld
{
    /// <summary>Debug-only toggle. Default true. EXCLUDED from StateHasher by design:
    /// never toggle mid-match in any context that must stay in lockstep.</summary>
    public bool FogEnabled { get; set; } = true;

    // visible recomputed each tick; explored is sticky. Derived state — EXCLUDED from hash.
    private bool[][] _visible = System.Array.Empty<bool[]>();   // [player][cell]
    private bool[][] _explored = System.Array.Empty<bool[]>();

    public bool IsVisibleTo(int player, int cx, int cy) =>
        !FogEnabled || (InBounds(cx, cy) && _visible.Length > player && _visible[player][cy * Map.Width + cx]);

    public bool IsExploredBy(int player, int cx, int cy) =>
        !FogEnabled || (InBounds(cx, cy) && _explored.Length > player && _explored[player][cy * Map.Width + cx]);

    private bool InBounds(int cx, int cy) => cx >= 0 && cy >= 0 && cx < Map.Width && cy < Map.Height;

    private void UpdateVision()
    {
        int players = _players.Length, cells = Map.Width * Map.Height;
        if (_visible.Length != players)
        {
            _visible = new bool[players][];
            _explored = new bool[players][];
            for (int p = 0; p < players; p++) { _visible[p] = new bool[cells]; _explored[p] = new bool[cells]; }
        }
        for (int p = 0; p < players; p++) System.Array.Clear(_visible[p]);

        foreach (var u in _units)
        {
            var (cx, cy) = Map.WorldToCell(u.Position);
            Stamp(u.OwnerId, cx, cy, EffectiveSight(u));
        }
        foreach (var b in _buildings)
            Stamp(b.OwnerId, b.CellX + b.Spec.Width / 2, b.CellY + b.Spec.Height / 2, b.Spec.SightRange);
    }

    private void Stamp(int player, int cx, int cy, int range)
    {
        int r2 = range * range;
        for (int y = System.Math.Max(0, cy - range); y <= System.Math.Min(Map.Height - 1, cy + range); y++)
            for (int x = System.Math.Max(0, cx - range); x <= System.Math.Min(Map.Width - 1, cx + range); x++)
            {
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > r2) continue;
                var i = y * Map.Width + x;
                _visible[player][i] = true;
                _explored[player][i] = true;
            }
    }
}
