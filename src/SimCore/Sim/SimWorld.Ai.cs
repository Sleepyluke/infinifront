using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    // Easy-tier tunables (first-pass; tuned later).
    private const int AiDecisionInterval = 10;   // act roughly every 10 ticks
    private const int EasyWorkerCap = 8;
    private const int EasySupplyBuffer = 2;       // build supply when SupplyUsed >= SupplyCap - this
    private const int EasyAttackThreshold = 6;    // min combat units before attacking
    private const int EasyAttackInterval = 300;   // attack-move cadence (ticks)

    /// <summary>Mark a player as CPU-controlled at the given difficulty (setup-time).</summary>
    public void SetCpu(int playerId, AiDifficulty difficulty)
    {
        _players[playerId].Controller = PlayerController.Cpu;
        _players[playerId].Difficulty = difficulty;
    }

    /// <summary>Deterministic AI phase: each CPU player decides on a fixed cadence and issues
    /// commands through Apply. Integer/Fix only, no RNG (stable scans). Skips once the match is
    /// decided.</summary>
    private void UpdateAi()
    {
        if (Phase == MatchPhase.Over) return;
        if (Tick % AiDecisionInterval != 0) return;
        for (int p = 0; p < _players.Length; p++)
        {
            if (_players[p].Controller != PlayerController.Cpu) continue;
            switch (_players[p].Difficulty)
            {
                default: EasyDecide(p); break; // Medium/Hard fall back to Easy until 5d
            }
        }
    }

    /// <summary>Easy tier: stub in Task 1 (does nothing). Filled in Tasks 2-3.</summary>
    private void EasyDecide(int playerId)
    {
    }
}
