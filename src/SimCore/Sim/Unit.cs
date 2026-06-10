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
    public FlowField? Path { get; set; } // null when there is no active move order
    public int PathVersion { get; set; } // MapGrid.Version when Path was computed

    public Weapon? Weapon { get; set; }
    public int AttackTargetId { get; set; } // 0 = no target

    public bool IsAttackMoving { get; set; }
    public FixVec AttackMoveDest { get; set; }
}
