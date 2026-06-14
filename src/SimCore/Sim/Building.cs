using SimCore.Math;

namespace SimCore.Sim;

/// <summary>Mutable building state. Shares the entity id space with units.</summary>
public sealed class Building
{
    public int Id { get; init; }
    public int OwnerId { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public string DefId { get; init; } = "";
    public BuildingSpec Spec { get; init; } = null!;
    public int Hp { get; set; }
    public bool IsComplete { get; set; }
    public int BuildProgress { get; set; }
    public System.Collections.Generic.List<TrainingItem> Queue { get; } = new(); // index 0 is in production

    public const int MaxQueueLength = 5;

    public bool HasRally { get; set; }
    public FixVec RallyPoint { get; set; }
}

public sealed class TrainingItem
{
    public UnitSpec Spec { get; init; } = null!;
    public int RemainingTicks { get; set; }
}
