using System.Collections.Generic;
using SimCore.Sim;

namespace SimCore.Net;

/// <summary>One player's commands for one EXECUTION tick. An empty Commands list is still a valid
/// frame ("I have nothing this tick") and MUST be sent, so peers can distinguish "no input" from
/// "input not yet arrived".</summary>
public sealed record CommandFrame(int Tick, int PlayerId, IReadOnlyList<Command> Commands);
