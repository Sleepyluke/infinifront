using SimCore.Math;

namespace SimCore.Sim;

/// <summary>Combat component. CooldownRemaining is mutable sim state — it is hashed (StateHasher v2 task).</summary>
public sealed class Weapon
{
    public int Damage { get; init; }
    public Fix Range { get; init; }
    public int CooldownTicks { get; init; }
    public int CooldownRemaining { get; set; }
}
