using System.Collections.Generic;
using SimCore.Sim;

namespace SimCore.Net;

/// <summary>Transport-agnostic delayed-lockstep scheduler. Buffers command frames per
/// (executionTick, player); dispatches execution tick X only once every human player has a frame
/// for X (or X is within the initial input-delay pad); merges frames in ascending PlayerId order
/// so every peer feeds the identical command sequence into Step. World-free + Godot-free.</summary>
public sealed class LockstepCoordinator
{
    private readonly int _localPlayerId;
    private readonly int[] _humanPlayerIds;   // sorted ascending — defines the merge order
    private readonly int _inputDelay;
    private readonly Dictionary<(int tick, int playerId), CommandFrame> _frames = new();
    private int _submitTick;  // next local input tick to submit
    private int _stepTick;    // next execution tick to dispatch

    public LockstepCoordinator(int localPlayerId, IReadOnlyList<int> humanPlayerIds, int inputDelay)
    {
        _localPlayerId = localPlayerId;
        var arr = new int[humanPlayerIds.Count];
        for (int i = 0; i < arr.Length; i++) arr[i] = humanPlayerIds[i];
        System.Array.Sort(arr);
        _humanPlayerIds = arr;
        _inputDelay = inputDelay;
    }

    /// <summary>The next execution tick that <see cref="TryDequeueStep"/> will dispatch.</summary>
    public int NextStepTick => _stepTick;

    /// <summary>Submit the local player's commands for the current input tick. They are scheduled
    /// to EXECUTE at inputTick + inputDelay, buffered locally, and returned as a frame to broadcast.</summary>
    public CommandFrame SubmitLocal(IReadOnlyList<Command> commands)
    {
        var frame = new CommandFrame(_submitTick + _inputDelay, _localPlayerId, commands);
        _frames[(frame.Tick, _localPlayerId)] = frame;
        _submitTick++;
        return frame;
    }

    /// <summary>Buffer a remote human's frame.</summary>
    public void Receive(CommandFrame frame) => _frames[(frame.Tick, frame.PlayerId)] = frame;

    /// <summary>If the next execution tick is ready, output its deterministically-merged commands
    /// and advance; else false (a stall — waiting on a peer's frame). Execution ticks within the
    /// initial input-delay pad (before any input could have been scheduled) dispatch empty.</summary>
    public bool TryDequeueStep(out IReadOnlyList<Command> merged)
    {
        if (_stepTick >= _inputDelay)
            foreach (var pid in _humanPlayerIds)
                if (!_frames.ContainsKey((_stepTick, pid)))
                {
                    merged = System.Array.Empty<Command>();
                    return false;
                }

        var list = new List<Command>();
        foreach (var pid in _humanPlayerIds) // ascending PlayerId -> identical order on every peer
            if (_frames.TryGetValue((_stepTick, pid), out var f))
            {
                list.AddRange(f.Commands);
                _frames.Remove((_stepTick, pid)); // release the buffered frame
            }
        merged = list;
        _stepTick++;
        return true;
    }
}
