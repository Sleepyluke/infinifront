using SimCore.Sim;

namespace LlmRts.Godot;

public static class TestMap
{
    public const int Size = MatchSetup.MapSize;

    /// <summary>Default sandbox match: human (Reference) vs an Easy CPU (Reference).
    /// The menu (Main + MenuScreen) overrides this with the player's chosen config.</summary>
    public static SimWorld Build() =>
        MatchSetup.BuildStandard1v1(ReferenceFaction.Def, ReferenceFaction.Def, AiDifficulty.Easy, seed: 42);
}
