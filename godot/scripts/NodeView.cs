using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class NodeView : Node2D
{
    private Texture2D? _sprite;

    public void Init(ResourceNode n)
    {
        Position = RenderMath.CellToPx(n.CellX, n.CellY) + new Vector2(32, 32);
        const string path = "res://assets/world/mineral.png";
        if (ResourceLoader.Exists(path)) _sprite = ResourceLoader.Load<Texture2D>(path);
    }

    public override void _Draw()
    {
        if (_sprite is not null)
        {
            // Crystals a touch larger than a cell, base resting on the node centre.
            float w = RenderMath.CellPx * 0.95f;
            float h = w * _sprite.GetHeight() / _sprite.GetWidth();
            DrawTextureRect(_sprite, new Rect2(-w / 2f, RenderMath.CellPx * 0.30f - h, w, h), false);
            return;
        }
        var pts = new Vector2[] { new(0, -20), new(16, 0), new(0, 20), new(-16, 0) };
        DrawColoredPolygon(pts, new Color(0.4f, 0.8f, 1f));
    }
}
