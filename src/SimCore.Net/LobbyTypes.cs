using SimCore.Sim;

namespace SimCore.Net;

/// <summary>Lobby slot kind. Human = a human player (local or remote); Cpu = an in-sim AI; Open = joinable.</summary>
public enum SlotKind : byte { Open = 0, Human = 1, Cpu = 2 }

/// <summary>One lobby slot. Faction is by ID (each peer resolves it via its own PackCatalog).
/// OccupantPeerId = the Godot peer occupying a Human slot (0 = none / not applicable).</summary>
public sealed record LobbySlot(SlotKind Kind, int Team, string FactionId, AiDifficulty Difficulty, long OccupantPeerId);
