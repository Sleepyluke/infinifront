using System.Collections.Generic;
using Godot;
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

    public void Init(SimWorld world) => World = world;

    public void Enqueue(Command c) => _queue.Add(c);

    public override void _Process(double delta)
    {
        if (Paused) return;
        _accum += (float)delta;
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

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Space }) Paused = !Paused;
    }
}
