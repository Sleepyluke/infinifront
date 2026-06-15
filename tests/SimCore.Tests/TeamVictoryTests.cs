using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class TeamVictoryTests
{
    // Place a building for the given owners, then step once to update match state.
    // NOTE (verify-against-source): BuildingSpec's positional ctor is
    // (MaxHp, Width, Height, MineralCost, BuildTimeTicks, ...) — MineralCost and BuildTimeTicks
    // are required, so we supply them (0, 0) rather than the plan's partial named-arg form.
    // AddCompletedBuilding(owner, spec, x, y, defId="") takes an OPTIONAL defId, so we omit it.
    // Test-only adjustment; the sim is unchanged.
    private static SimWorld WithBuildingOwners(int players, params int[] owners)
    {
        var w = new SimWorld(new MapGrid(24, 24), seed: 1, playerCount: players, faction: null);
        var spec = new BuildingSpec(MaxHp: 100, Width: 2, Height: 2, MineralCost: 0, BuildTimeTicks: 0, SightRange: 4);
        int x = 0;
        foreach (var o in owners) { w.AddCompletedBuilding(o, spec, x, 0); x += 3; }
        return w;
    }

    [Fact]
    public void Two_Teams_Both_Alive_Is_Not_Over()
    {
        var w = WithBuildingOwners(4, 0, 1, 2, 3);
        w.SetTeam(0, 0); w.SetTeam(1, 0); w.SetTeam(2, 1); w.SetTeam(3, 1);
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(MatchPhase.InProgress, w.Phase);
    }

    [Fact]
    public void Team_Wins_When_Only_Its_Members_Have_Buildings()
    {
        var w = WithBuildingOwners(4, 0, 2); // only team-0 player 0 and team-1 player 2 own buildings... set teams:
        w.SetTeam(0, 0); w.SetTeam(1, 0); w.SetTeam(2, 0); w.SetTeam(3, 1);
        // owners {0,2} are both team 0 -> team 0 wins, representative = 0
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);
        Assert.True(w.SameTeam(0, w.WinnerId));
        Assert.True(w.SameTeam(2, w.WinnerId));   // ally also "wins"
        Assert.False(w.SameTeam(3, w.WinnerId));  // the other team lost
    }

    [Fact]
    public void Mutual_Elimination_Is_A_Draw()
    {
        var w = new SimWorld(new MapGrid(24, 24), seed: 1, playerCount: 2, faction: null);
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(-1, w.WinnerId);
    }
}
