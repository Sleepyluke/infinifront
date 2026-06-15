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

    /// <summary>Recompute the latched outcome: Over when all players that still own a building
    /// share ONE team (or none remain = draw). WinnerId = the lowest-index surviving building-owner
    /// (a representative of the winning team); -1 on a draw. Solo teams reduce this to the old
    /// "≤1 player owns a building" rule, so the golden is unchanged. Reads only hashed building
    /// ownership + immutable team config — deterministic, integer-only.</summary>
    private void UpdateMatchState()
    {
        if (Phase == MatchPhase.Over) return;
        var hasBuilding = new bool[_players.Length];
        foreach (var b in _buildings) hasBuilding[b.OwnerId] = true;

        int firstOwner = -1;          // lowest-index player still owning a building (the representative)
        bool multipleTeams = false;
        for (int p = 0; p < hasBuilding.Length; p++)
        {
            if (!hasBuilding[p]) continue;
            if (firstOwner < 0) firstOwner = p;
            else if (!SameTeam(p, firstOwner)) { multipleTeams = true; break; }
        }
        if (!multipleTeams)
        {
            Phase = MatchPhase.Over;
            WinnerId = firstOwner;    // -1 if nobody owns a building (draw)
        }
    }
}
