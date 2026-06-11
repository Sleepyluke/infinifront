namespace SimCore.Sim;

/// <summary>Per-player economy state. All fields are mutable sim state — hashed (StateHasher v3 task).</summary>
public sealed class PlayerState
{
    public int Minerals { get; set; }
    public int SupplyUsed { get; set; }
    public int SupplyCap { get; set; }
}
