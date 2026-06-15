using System.Collections.Generic;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

/// <summary>Proves a full 2v2 team match (4 CPUs, two teams, shared vision, ally-immune combat)
/// is deterministic: two independent runs from the same seed fold to the same StateHasher hash.
/// This is the team-play analogue of the CPU-vs-CPU determinism proof — multiplayer lockstep
/// relies on it (every peer runs Step identically, including the in-sim AI for CPU slots).</summary>
public class Team2v2DeterminismTests
{
    private static SimWorld Build() => MatchSetup.BuildMatch(new List<MatchSlot>
    {
        new(ReferenceFaction.Def, PlayerController.Cpu, AiDifficulty.Medium, Team: 0),
        new(ReferenceFaction.Def, PlayerController.Cpu, AiDifficulty.Easy,   Team: 0),
        new(ReferenceFaction.Def, PlayerController.Cpu, AiDifficulty.Hard,   Team: 1),
        new(ReferenceFaction.Def, PlayerController.Cpu, AiDifficulty.Medium, Team: 1),
    }, seed: 99);

    [Fact]
    public void Two_Runs_Of_A_2v2_Produce_Identical_Hashes()
    {
        var a = Build();
        var b = Build();
        for (int i = 0; i < 300; i++)
        {
            a.Step(System.Array.Empty<Command>());
            b.Step(System.Array.Empty<Command>());
        }
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
