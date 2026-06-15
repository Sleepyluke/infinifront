using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Process-static chosen match config, set by the menu and read by Main on (re)load.
/// Survives GetTree().ReloadCurrentScene() so Restart/Play rebuild a fresh scene deterministically.</summary>
public static class MatchConfig
{
    public static bool Configured;
    public static FactionDef Human = ReferenceFaction.Def;
    public static FactionDef Cpu = ReferenceFaction.Def;
    public static AiDifficulty Difficulty = AiDifficulty.Easy;

    public static void Set(FactionDef human, FactionDef cpu, AiDifficulty difficulty)
    {
        Human = human; Cpu = cpu; Difficulty = difficulty; Configured = true;
    }

    public static void Clear() => Configured = false;
}
