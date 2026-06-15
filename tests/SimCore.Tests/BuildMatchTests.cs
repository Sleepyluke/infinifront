using System.Collections.Generic;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class BuildMatchTests
{
    [Fact]
    public void Builds_Four_Players_Two_Teams_Each_Based()
    {
        var slots = new List<MatchSlot>
        {
            new(ReferenceFaction.Def, PlayerController.Human, AiDifficulty.Easy, Team: 0),
            new(ReferenceFaction.Def, PlayerController.Cpu,   AiDifficulty.Hard, Team: 0),
            new(ReferenceFaction.Def, PlayerController.Cpu,   AiDifficulty.Easy, Team: 1),
            new(ReferenceFaction.Def, PlayerController.Cpu,   AiDifficulty.Medium, Team: 1),
        };
        var w = MatchSetup.BuildMatch(slots, seed: 42);

        Assert.Equal(4, w.Players.Count);
        Assert.Equal(PlayerController.Human, w.Players[0].Controller);
        Assert.Equal(PlayerController.Cpu, w.Players[1].Controller);
        Assert.Equal(AiDifficulty.Hard, w.Players[1].Difficulty);
        Assert.True(w.SameTeam(0, 1));
        Assert.True(w.SameTeam(2, 3));
        Assert.False(w.SameTeam(0, 2));
        for (int p = 0; p < 4; p++)
            Assert.Contains(w.Buildings, b => b.OwnerId == p);   // each player based at a distinct corner
        Assert.Equal(MatchPhase.InProgress, w.Phase);
    }

    [Fact]
    public void Two_Slot_Match_Equals_Old_Versus1v1()
    {
        // BuildVersus1v1 is now expressed on BuildMatch; both must produce the same building/unit layout.
        var viaVersus = MatchSetup.BuildVersus1v1(ReferenceFaction.Def, ReferenceFaction.Def, seed: 7);
        var viaMatch = MatchSetup.BuildMatch(new List<MatchSlot>
        {
            new(ReferenceFaction.Def, PlayerController.Human, AiDifficulty.Easy, Team: 0),
            new(ReferenceFaction.Def, PlayerController.Human, AiDifficulty.Easy, Team: 1),
        }, seed: 7);
        Assert.Equal(viaVersus.Buildings.Count, viaMatch.Buildings.Count);
        Assert.Equal(viaVersus.Units.Count, viaMatch.Units.Count);
        Assert.Equal(viaVersus.Players.Count, viaMatch.Players.Count);
    }

    [Theory]
    [InlineData(1)]   // too few
    [InlineData(5)]   // more than 4 corners
    public void Rejects_Out_Of_Range_Slot_Counts(int n)
    {
        var slots = new List<MatchSlot>();
        for (int i = 0; i < n; i++)
            slots.Add(new MatchSlot(ReferenceFaction.Def, PlayerController.Cpu, AiDifficulty.Easy, Team: i % 2));
        Assert.Throws<System.ArgumentException>(() => MatchSetup.BuildMatch(slots, seed: 1));
    }
}
