using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class MapView : Node2D
{
    private SimRunner _runner = null!;
    private MapGrid? _lastMap;
    private int _drawnVersion = -1;
    private Texture2D? _ground;

    public void Init(SimRunner runner)
    {
        _runner = runner;
        TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled;   // so DrawTextureRect(tile:true) repeats
        const string g = "res://assets/world/ground.png";
        if (ResourceLoader.Exists(g)) _ground = ResourceLoader.Load<Texture2D>(g);
    }

    public override void _Process(double delta)
    {
        var map = _runner.World.Map;
        if (!ReferenceEquals(map, _lastMap) || map.Version != _drawnVersion)
        {
            _lastMap = map;
            _drawnVersion = map.Version;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var world = _runner.World;
        var map = world.Map;
        const int px = RenderMath.CellPx;
        var full = new Rect2(0, 0, map.Width * px, map.Height * px);
        if (_ground is not null) DrawTextureRect(_ground, full, tile: true);
        else DrawRect(full, new Color(0.42f, 0.38f, 0.32f));

        // Cells covered by a building or resource node have their own sprite — don't paint rock under them
        // (building footprints + node cells are marked impassable for pathing, same as terrain).
        var covered = new System.Collections.Generic.HashSet<int>();
        foreach (var b in world.Buildings)
            for (int dy = 0; dy < b.Spec.Height; dy++)
                for (int dx = 0; dx < b.Spec.Width; dx++)
                    covered.Add((b.CellY + dy) * map.Width + (b.CellX + dx));
        foreach (var n in world.Nodes)
            covered.Add(n.CellY * map.Width + n.CellX);

        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (!map.IsPassable(x, y) && !covered.Contains(y * map.Width + x))
                    DrawRect(new Rect2(x * px, y * px, px, px), new Color(0.22f, 0.20f, 0.18f));

        for (int x = 0; x <= map.Width; x++)
            DrawLine(new Vector2(x * px, 0), new Vector2(x * px, map.Height * px), new Color(0, 0, 0, 0.06f));
        for (int y = 0; y <= map.Height; y++)
            DrawLine(new Vector2(0, y * px), new Vector2(map.Width * px, y * px), new Color(0, 0, 0, 0.06f));
    }
}
