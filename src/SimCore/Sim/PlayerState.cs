namespace SimCore.Sim;

public enum PlayerController { Human, Cpu }
public enum AiDifficulty { Easy, Medium, Hard }

/// <summary>Per-player economy + tech state. AppliedUpgrades is kept SORTED so the hash
/// is independent of research-completion order. (Hashed in StateHasher v4 — Task 7.)</summary>
public sealed class PlayerState
{
    public int Minerals { get; set; }
    public int SupplyUsed { get; set; }
    public int SupplyCap { get; set; }
    public PlayerController Controller { get; set; } = PlayerController.Human;
    public AiDifficulty Difficulty { get; set; } = AiDifficulty.Easy; // only meaningful when Controller == Cpu
    public int Team { get; set; }   // default 0; SimWorld sets it to the player's index (solo) at construction

    private readonly System.Collections.Generic.List<string> _appliedUpgrades = new();
    public System.Collections.Generic.IReadOnlyList<string> AppliedUpgrades => _appliedUpgrades;

    public bool HasUpgrade(string id) => _appliedUpgrades.Contains(id);

    /// <summary>Idempotent; inserts in sorted position (Ordinal) for deterministic ordering.</summary>
    public void AddUpgrade(string id)
    {
        int i = _appliedUpgrades.BinarySearch(id, System.StringComparer.Ordinal);
        if (i < 0) _appliedUpgrades.Insert(~i, id);
    }
}
