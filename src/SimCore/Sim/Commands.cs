using SimCore.Math;

namespace SimCore.Sim;

/// <summary>Commands are the ONLY way to mutate the sim from outside (spec rule).</summary>
public abstract record Command(int PlayerId);

public sealed record MoveCommand(int PlayerId, int[] UnitIds, FixVec Target) : Command(PlayerId);

public sealed record AttackCommand(int PlayerId, int[] UnitIds, int TargetId) : Command(PlayerId);

public sealed record AttackMoveCommand(int PlayerId, int[] UnitIds, FixVec Target) : Command(PlayerId);

public sealed record BuildCommand(int PlayerId, int WorkerUnitId, BuildingSpec Spec, int CellX, int CellY) : Command(PlayerId);

public sealed record TrainCommand(int PlayerId, int BuildingId, UnitSpec Spec) : Command(PlayerId);

public sealed record HarvestCommand(int PlayerId, int[] UnitIds, int NodeId) : Command(PlayerId);
