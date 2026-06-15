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

    public static bool IsNetworked;
    public static bool IsHost;
    public static string Ip = "127.0.0.1";

    public static void Set(FactionDef human, FactionDef cpu, AiDifficulty difficulty)
    {
        Human = human; Cpu = cpu; Difficulty = difficulty; Configured = true;
    }

    /// <summary>Chosen from the menu: start a networked match (host or join). Takes precedence over
    /// the single-player config in Main. M2 minimal: a fixed 2-human 1v1 (Reference faction).</summary>
    public static void SetNetwork(bool isHost, string ip)
    {
        IsNetworked = true; IsHost = isHost; Ip = ip; Configured = false;
    }

    public static void Clear() { Configured = false; IsNetworked = false; }
}
