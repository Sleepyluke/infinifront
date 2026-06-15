using System.Collections.Generic;
using Godot;
using SimCore.Net;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Owns the SimWorld. Fixed 10 ticks/s via accumulator; queued
/// commands drain into each Step. Interpolation fraction exposed for views.</summary>
public partial class SimRunner : Node
{
    public const float TickSeconds = 0.1f;

    public SimWorld World { get; private set; } = null!;
    public bool Paused { get; set; }
    public float Alpha { get; private set; }          // 0..1 fraction into current tick
    public long TickCount { get; private set; }
    public event System.Action? Ticked;               // fired after every Step

    private readonly List<Command> _queue = new();
    private float _accum;

    // ---- Networked (lockstep) mode ----
    private NetSession? _net;
    private LockstepCoordinator? _coord;
    private int _localPlayerId;
    private bool _networked;
    private bool _desyncReported;
    /// <summary>Fired (with the desync tick) the first time the coordinator latches a desync.</summary>
    public event System.Action<int>? Desynced;

    // Diagnostics surfaced on the HUD (so a screenshot reveals lockstep health).
    public bool IsNetworked => _networked;
    public int NetLocalPlayer => _localPlayerId;
    /// <summary>Consecutive frames we wanted to step but couldn't (no peer frames). Grows if stalled.</summary>
    public int NetStallFrames { get; private set; }

    public void Init(SimWorld world) => World = world;

    public void Enqueue(Command c) => _queue.Add(c);

    /// <summary>Switch into deterministic-lockstep mode. Wires the transport to the coordinator and
    /// primes the input-delay pipeline. The local queue (filled by Enqueue) becomes this peer's
    /// per-tick input. Call once, after the start handshake, instead of running the single-player loop.</summary>
    public void InitNetworked(SimWorld world, LockstepCoordinator coord, NetSession net, int localPlayerId)
    {
        World = world;
        _coord = coord;
        _net = net;
        _localPlayerId = localPlayerId;
        _networked = true;
        net.FrameReceived += f => _coord.Receive(f);
        net.HashReceived += (tick, pid, hash) => _coord.ReceiveHash(tick, pid, hash);
        // Prime the pipeline: submit `InputDelay` empty frames so the first real exec ticks have input.
        for (int i = 0; i < NetSession.InputDelay; i++)
            _net.SendFrame(_coord.SubmitLocal(System.Array.Empty<Command>()));
    }

    public override void _Process(double delta)
    {
        if (Paused) return;
        if (_networked) { ProcessNetworked(delta); return; }

        _accum += (float)delta;
        // Cap catch-up to 5 ticks per frame; sim time slows under stalls rather than bursting.
        if (_accum > 5 * TickSeconds) _accum = 5 * TickSeconds;
        while (_accum >= TickSeconds)
        {
            _accum -= TickSeconds;
            World.Step(_queue.ToArray());
            _queue.Clear();
            TickCount++;
            Ticked?.Invoke();
        }
        Alpha = _accum / TickSeconds;
    }

    private void ProcessNetworked(double delta)
    {
        _accum += (float)delta;
        if (_accum > 5 * TickSeconds) _accum = 5 * TickSeconds;
        bool wantedToStep = _accum >= TickSeconds;
        int steps = 0;
        while (_accum >= TickSeconds && steps < 5)
        {
            if (_coord!.Desynced) { ReportDesync(); return; }
            // Stall (return false) until every human's frame for the next exec tick has arrived.
            if (!_coord.TryDequeueStep(out var merged)) break;
            _accum -= TickSeconds;
            World.Step(merged);
            int steppedTick = _coord.NextStepTick - 1;
            ulong h = StateHasher.Hash(World);
            _coord.RecordLocalHash(steppedTick, h);
            _net!.SendHash(steppedTick, _localPlayerId, h);
            // Submit this peer's input for a future exec tick (one frame per executed tick).
            var frame = _coord.SubmitLocal(_queue.ToArray());
            _queue.Clear();
            _net.SendFrame(frame);
            TickCount++;
            Ticked?.Invoke();
            steps++;
        }
        if (steps > 0) NetStallFrames = 0;
        else if (wantedToStep) NetStallFrames++;   // wanted to advance but had no peer frames
        Alpha = _accum / TickSeconds;
    }

    private void ReportDesync()
    {
        if (_desyncReported) return;
        _desyncReported = true;
        Paused = true;
        GD.PrintErr($"DESYNC detected at tick {_coord!.DesyncTick} — halting sim.");
        Desynced?.Invoke(_coord.DesyncTick);
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Space }) Paused = !Paused;
    }
}
