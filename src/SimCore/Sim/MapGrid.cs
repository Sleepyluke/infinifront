using SimCore.Math;

namespace SimCore.Sim;

public sealed class MapGrid
{
    public int Width { get; }
    public int Height { get; }
    private readonly bool[] _passable; // index = y * Width + x

    public MapGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _passable = new bool[width * height];
        System.Array.Fill(_passable, true);
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public bool IsPassable(int x, int y) => InBounds(x, y) && _passable[y * Width + x];

    public void SetPassable(int x, int y, bool value)
    {
        if (InBounds(x, y)) _passable[y * Width + x] = value;
    }

    public (int cx, int cy) WorldToCell(FixVec pos) => (pos.X.ToInt(), pos.Y.ToInt());

    public FixVec CellCenter(int cx, int cy) =>
        new(Fix.FromInt(cx) + Fix.FromFraction(1, 2), Fix.FromInt(cy) + Fix.FromFraction(1, 2));
}
