using SimCore.Math;

namespace SimCore.Sim;

public sealed class Unit
{
    public int Id { get; init; }
    public int OwnerId { get; init; }
    public FixVec Position { get; set; }
    public Fix SpeedPerTick { get; set; }
    public int Hp { get; set; }

    public bool HasMoveOrder { get; set; }
    public FixVec MoveTarget { get; set; }
    public FlowField? Path { get; set; } // null until Task 8 wires pathfinding in
}
