namespace SimCore.Sim;

public sealed partial class SimWorld
{
    /// <summary>The mechanic governing this unit. Today: the shared world Faction's mechanic.
    /// Structured as a per-unit accessor so it becomes per-player when packs let each player
    /// pick a faction (plan 5). Returns null when there is no mechanic.</summary>
    private MechanicDef? MechanicFor(Unit u) => Faction?.Mechanic;

    private bool HasShields(Unit u) => MechanicFor(u) is { Kind: MechanicKind.RegeneratingShields };

    /// <summary>Initial shield pool for a unit spawned under the current faction.</summary>
    private int InitialShield() =>
        Faction?.Mechanic is { Kind: MechanicKind.RegeneratingShields } m ? m.MaxShield : 0;
}
