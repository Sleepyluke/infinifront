using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class MapView : Node2D
{
    private MapGrid _map = null!;
    private int _drawnVersion = -1;

    public void Init(MapGrid map) => _map = map;

    public override void _Process(double delta)
    {
        if (_map.Version != _drawnVersion) { _drawnVersion = _map.Version; QueueRedraw(); }
    }

    public override void _Draw()
    {
        const int px = RenderMath.CellPx;
        DrawRect(new Rect2(0, 0, _map.Width * px, _map.Height * px), new Color(0.42f, 0.38f, 0.32f));
        for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
                if (!_map.IsPassable(x, y))
                    DrawRect(new Rect2(x * px, y * px, px, px), new Color(0.22f, 0.20f, 0.18f));
        for (int x = 0; x <= _map.Width; x++)
            DrawLine(new Vector2(x * px, 0), new Vector2(x * px, _map.Height * px), new Color(0, 0, 0, 0.06f));
        for (int y = 0; y <= _map.Height; y++)
            DrawLine(new Vector2(0, y * px), new Vector2(_map.Width * px, y * px), new Color(0, 0, 0, 0.06f));
    }
}
