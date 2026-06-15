using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class MatchSetupVersusTests
{
    [Fact]
    public void Versus1v1_Has_Two_Human_Players_Each_Based()
    {
        var w = MatchSetup.BuildVersus1v1(ReferenceFaction.Def, ReferenceFaction.Def, seed: 42);

        Assert.Equal(PlayerController.Human, w.Players[0].Controller);
        Assert.Equal(PlayerController.Human, w.Players[1].Controller);

        Assert.Contains(w.Buildings, b => b.OwnerId == 0);
        Assert.Contains(w.Buildings, b => b.OwnerId == 1);
        Assert.Contains(w.Units, u => u.OwnerId == 0);
        Assert.Contains(w.Units, u => u.OwnerId == 1);

        Assert.Same(ReferenceFaction.Def, w.FactionFor(0));
        Assert.Same(ReferenceFaction.Def, w.FactionFor(1));
        Assert.Equal(MatchPhase.InProgress, w.Phase);
    }

    [Fact]
    public void Versus1v1_Steps_Without_Throwing()
    {
        var w = MatchSetup.BuildVersus1v1(ReferenceFaction.Def, ReferenceFaction.Def, seed: 1);
        for (int i = 0; i < 30; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(MatchPhase.InProgress, w.Phase); // nobody loses in 30 idle ticks
    }
}
