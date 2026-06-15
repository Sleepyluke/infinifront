namespace SimCore.Sim;

/// <summary>Match outcome phase. Latches to Over once decided.</summary>
public enum MatchPhase { InProgress, Over }

public sealed partial class SimWorld
{
    /// <summary>Current match outcome. Latches to Over once a winner (or draw) is decided.</summary>
    public MatchPhase Phase { get; private set; } = MatchPhase.InProgress;

    /// <summary>Winner's player id once Over; -1 while InProgress or on a draw (mutual elimination).</summary>
    public int WinnerId { get; private set; } = -1;

    /// <summary>A player is defeated when they own no buildings (units never count, by design).</summary>
    public bool IsDefeated(int playerId)
    {
        foreach (var b in _buildings)
            if (b.OwnerId == playerId) return false;
        return true;
    }

    /// <summary>Recompute the latched outcome: Over when &lt;= 1 player still owns a building.
    /// Runs after RemoveDeadBuildings each tick; never reverts once Over. Reads only hashed
    /// building ownership — deterministic, integer-only.</summary>
    private void UpdateMatchState()
    {
        if (Phase == MatchPhase.Over) return;
        var hasBuilding = new bool[_players.Length];
        foreach (var b in _buildings) hasBuilding[b.OwnerId] = true;
        int aliveCount = 0, lastAlive = -1;
        for (int p = 0; p < hasBuilding.Length; p++)
            if (hasBuilding[p]) { aliveCount++; lastAlive = p; }
        if (aliveCount <= 1)
        {
            Phase = MatchPhase.Over;
            WinnerId = aliveCount == 1 ? lastAlive : -1;
        }
    }
}
