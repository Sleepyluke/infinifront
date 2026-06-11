using Godot;

namespace LlmRts.Godot;

public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print("LlmRts boot OK");
    }
}
