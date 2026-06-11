using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class NodeView : Node2D
{
    public void Init(ResourceNode n) =>
        Position = RenderMath.CellToPx(n.CellX, n.CellY) + new Vector2(32, 32);

    public override void _Draw()
    {
        var pts = new Vector2[] { new(0, -20), new(16, 0), new(0, 20), new(-16, 0) };
        DrawColoredPolygon(pts, new Color(0.4f, 0.8f, 1f));
    }
}
