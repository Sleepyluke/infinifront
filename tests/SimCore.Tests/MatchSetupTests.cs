using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class MatchSetupTests
{
    private static int Workers(SimWorld w, int p)
    {
        int c = 0; foreach (var u in w.Units) if (u.OwnerId == p && u.Harvester is not null) c++; return c;
    }
    private static int Buildings(SimWorld w, int p)
    {
        int c = 0; foreach (var b in w.Buildings) if (b.OwnerId == p) c++; return c;
    }

    [Fact]
    public void Builds_A_Standard_1v1_With_Per_Player_Factions_And_Cpu()
    {
        var w = MatchSetup.BuildStandard1v1(ReferenceFaction.Def, ReferenceFaction.Def, AiDifficulty.Hard, seed: 1);
        Assert.Same(ReferenceFaction.Def, w.FactionFor(0));
        Assert.Same(ReferenceFaction.Def, w.FactionFor(1));
        Assert.Equal(PlayerController.Human, w.Players[0].Controller);
        Assert.Equal(PlayerController.Cpu, w.Players[1].Controller);
        Assert.Equal(AiDifficulty.Hard, w.Players[1].Difficulty);
        Assert.True(Buildings(w, 0) >= 1 && Buildings(w, 1) >= 1, "both players need a starting base");
        Assert.True(Workers(w, 0) >= 1 && Workers(w, 1) >= 1, "both players need starting workers");
        Assert.Equal(MatchPhase.InProgress, w.Phase);
    }

    [Fact]
    public void Steps_Without_Throwing()
    {
        var w = MatchSetup.BuildStandard1v1(ReferenceFaction.Def, ReferenceFaction.Def, AiDifficulty.Medium, seed: 2);
        var empty = new List<Command>();
        for (int t = 0; t < 50; t++) w.Step(empty); // CPU acts; no exception
    }

    [Fact]
    public void Uses_The_Cpu_Players_Own_Faction()
    {
        var cpu = new FactionDef("beta", "Beta",
            new[] { new UnitDef("bw", 1, "bhall", System.Array.Empty<string>(),
                new UnitSpec(10, Fix.One, 1, 1, 1, Harvester: new HarvesterSpec(1, 1))) },
            new[] { new BuildingDef("bhall", 1, System.Array.Empty<string>(),
                new BuildingSpec(10, 2, 2, 1, 1, IsDepot: true, CanTrain: true)) });
        var w = MatchSetup.BuildStandard1v1(ReferenceFaction.Def, cpu, AiDifficulty.Easy, seed: 3);
        Assert.Same(cpu, w.FactionFor(1));
        Assert.Contains(w.Buildings, b => b.OwnerId == 1 && b.DefId == "bhall"); // CPU base from its own faction
    }
}
