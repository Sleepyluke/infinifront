using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class MatchStateTests
{
    private static BuildingSpec Bld() => new(100, 2, 2, 100, 10);
    private static readonly List<Command> Empty = new();

    private static (SimWorld w, int b0, int b1) TwoBaseWorld()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        int b0 = w.AddCompletedBuilding(0, Bld(), 2, 2);
        int b1 = w.AddCompletedBuilding(1, Bld(), 14, 14);
        return (w, b0, b1);
    }

    [Fact]
    public void Both_Players_With_Buildings_Is_InProgress()
    {
        var (w, _, _) = TwoBaseWorld();
        w.Step(Empty);
        Assert.Equal(MatchPhase.InProgress, w.Phase);
        Assert.Equal(-1, w.WinnerId);
        Assert.False(w.IsDefeated(0));
        Assert.False(w.IsDefeated(1));
    }

    [Fact]
    public void Destroying_Last_Building_Eliminates_Player_And_Decides_Winner()
    {
        var (w, _, b1) = TwoBaseWorld();
        w.Step(Empty);
        Assert.Equal(MatchPhase.InProgress, w.Phase);

        w.GetBuilding(b1)!.Hp = 0;
        w.Step(Empty); // RemoveDeadBuildings clears b1, then UpdateMatchState runs

        Assert.True(w.IsDefeated(1));
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);
    }

    [Fact]
    public void Player_With_Units_But_No_Buildings_Is_Defeated()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.AddCompletedBuilding(0, Bld(), 2, 2);
        w.SpawnUnit(1, w.Map.CellCenter(14, 14), Fix.One, 10); // p1: a unit, zero buildings
        w.Step(Empty);
        Assert.True(w.IsDefeated(1));
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);
    }

    [Fact]
    public void Mutual_Elimination_Same_Tick_Is_A_Draw()
    {
        var (w, b0, b1) = TwoBaseWorld();
        w.Step(Empty);
        w.GetBuilding(b0)!.Hp = 0;
        w.GetBuilding(b1)!.Hp = 0;
        w.Step(Empty);
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(-1, w.WinnerId);
    }

    [Fact]
    public void Outcome_Latches_Once_Decided()
    {
        var (w, _, b1) = TwoBaseWorld();
        w.Step(Empty);
        w.GetBuilding(b1)!.Hp = 0;
        w.Step(Empty);
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);

        // p1 somehow regains a building — the decided outcome must NOT change.
        w.AddCompletedBuilding(1, Bld(), 14, 14);
        w.Step(Empty);
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);
    }
}
