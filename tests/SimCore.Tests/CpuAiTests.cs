using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class CpuAiTests
{
    private static readonly List<Command> Empty = new();

    [Fact]
    public void SetCpu_Sets_Controller_And_Difficulty()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        Assert.Equal(PlayerController.Human, w.Players[0].Controller); // default
        w.SetCpu(1, AiDifficulty.Easy);
        Assert.Equal(PlayerController.Cpu, w.Players[1].Controller);
        Assert.Equal(AiDifficulty.Easy, w.Players[1].Difficulty);
        Assert.Equal(PlayerController.Human, w.Players[0].Controller);
    }

    [Fact]
    public void Cpu_With_No_Assets_Does_Not_Crash_Or_Spawn()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.SetCpu(1, AiDifficulty.Easy);
        for (int t = 0; t < 30; t++) w.Step(Empty); // no buildings/workers → AI finds no producer; no throw
        Assert.Empty(w.Units);
    }
}
