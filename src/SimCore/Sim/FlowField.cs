using System.Collections.Generic;

namespace SimCore.Sim;

/// <summary>Per-cell direction field toward a target. Deterministic: fixed neighbor order.</summary>
public sealed class FlowField
{
    public int TargetX { get; }
    public int TargetY { get; }
    private readonly int _width;
    private readonly int _height;
    private readonly int[] _cost;        // BFS integration cost; int.MaxValue = unreachable
    private readonly sbyte[] _dirX;
    private readonly sbyte[] _dirY;

    // Fixed neighbor order — never reorder, determinism depends on it.
    private static readonly (int dx, int dy)[] Neighbors =
        { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    private FlowField(int width, int height, int targetX, int targetY)
    {
        _width = width; _height = height;
        TargetX = targetX; TargetY = targetY;
        _cost = new int[width * height];
        _dirX = new sbyte[width * height];
        _dirY = new sbyte[width * height];
        System.Array.Fill(_cost, int.MaxValue);
    }

    public (int dx, int dy) DirectionAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height) return (0, 0);
        var i = y * _width + x;
        return (_dirX[i], _dirY[i]);
    }

    public static FlowField Compute(MapGrid map, int targetX, int targetY)
    {
        var f = new FlowField(map.Width, map.Height, targetX, targetY);
        var queue = new Queue<(int x, int y)>();

        if (map.IsPassable(targetX, targetY))
        {
            f._cost[targetY * map.Width + targetX] = 0;
            queue.Enqueue((targetX, targetY));
        }
        else
        {
            // Approach semantics: impassable target (building/resource) — seed its passable
            // neighbors at cost 0 so units walk up adjacent and stop there. Fixed neighbor
            // order; no corner-cut constraint for seeds (they are start points, not steps).
            foreach (var (dx, dy) in Neighbors)
            {
                int nx = targetX + dx, ny = targetY + dy;
                if (!map.IsPassable(nx, ny)) continue;
                f._cost[ny * map.Width + nx] = 0;
                queue.Enqueue((nx, ny));
            }
        }
        if (queue.Count == 0) return f; // fully enclosed — empty field, give-up semantics

        // BFS integration field
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            var c = f._cost[cy * map.Width + cx];
            foreach (var (dx, dy) in Neighbors)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!map.IsPassable(nx, ny)) continue;
                // no corner cutting: diagonal requires both orthogonal cells passable
                if (dx != 0 && dy != 0 && (!map.IsPassable(cx + dx, cy) || !map.IsPassable(cx, cy + dy))) continue;
                var ni = ny * map.Width + nx;
                if (f._cost[ni] != int.MaxValue) continue;
                f._cost[ni] = c + 1;
                queue.Enqueue((nx, ny));
            }
        }

        // Direction field: each cell points at its cheapest passable neighbor
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var i = y * map.Width + x;
            if (f._cost[i] == int.MaxValue || f._cost[i] == 0) continue;
            int best = f._cost[i];
            (int bx, int by) = (0, 0);
            foreach (var (dx, dy) in Neighbors)
            {
                int nx = x + dx, ny = y + dy;
                if (!map.IsPassable(nx, ny)) continue;
                if (dx != 0 && dy != 0 && (!map.IsPassable(x + dx, y) || !map.IsPassable(x, y + dy))) continue;
                var nc = f._cost[ny * map.Width + nx];
                if (nc < best) { best = nc; (bx, by) = (dx, dy); }
            }
            f._dirX[i] = (sbyte)bx;
            f._dirY[i] = (sbyte)by;
        }
        return f;
    }
}
