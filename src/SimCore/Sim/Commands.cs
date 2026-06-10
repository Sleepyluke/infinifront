using SimCore.Math;

namespace SimCore.Sim;

/// <summary>Commands are the ONLY way to mutate the sim from outside (spec rule).</summary>
public abstract record Command(int PlayerId);

public sealed record MoveCommand(int PlayerId, int[] UnitIds, FixVec Target) : Command(PlayerId);

public sealed record AttackCommand(int PlayerId, int[] UnitIds, int TargetId) : Command(PlayerId);
