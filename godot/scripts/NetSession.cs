using System;
using Godot;
using SimCore.Net;

namespace LlmRts.Godot;

/// <summary>ENet transport for deterministic lockstep. Host/Join, a start handshake (host assigns
/// player ids + seed), and host-relayed frame/hash RPCs. Raises events for SimRunner to drive the
/// LockstepCoordinator. M2 minimal: exactly 2 players (host=0, client=1).</summary>
public partial class NetSession : Node
{
    public const int Port = 7777;
    public const ulong MatchSeed = 42;   // M2: fixed; M3's lobby makes this host-chosen.
    public const int InputDelay = 3;     // ~300 ms at 10 ticks/s.

    public bool IsHost { get; private set; }
    public int LocalPlayerId { get; private set; }

    /// <summary>Fired once the handshake completes: (localPlayerId, seed). Build the world + start.</summary>
    public event Action<int, ulong>? MatchReady;
    /// <summary>A remote human's command frame arrived → coordinator.Receive.</summary>
    public event Action<CommandFrame>? FrameReceived;
    /// <summary>A remote peer's per-tick state hash arrived → coordinator.ReceiveHash.</summary>
    public event Action<int, int, ulong>? HashReceived;
    /// <summary>A peer dropped (M4 handles recovery; M2 just surfaces it).</summary>
    public event Action? PeerDropped;

    private bool _started;

    public void Host()
    {
        IsHost = true;
        LocalPlayerId = 0;
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(Port, maxClients: 8);
        if (err != Error.Ok) { GD.PrintErr($"NetSession host failed: {err}"); return; }
        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        GD.Print($"NetSession hosting on :{Port}");
    }

    public void Join(string ip)
    {
        IsHost = false;
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(ip, Port);
        if (err != Error.Ok) { GD.PrintErr($"NetSession join failed: {err}"); return; }
        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.ConnectedToServer += () => GD.Print("NetSession connected to host");
        Multiplayer.ConnectionFailed += () => GD.PrintErr("NetSession connection failed");
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        GD.Print($"NetSession joining {ip}:{Port}");
    }

    // ---- Handshake (host) ----
    private void OnPeerConnected(long peerId)
    {
        if (!IsHost || _started) return;       // M2: start on the first client.
        _started = true;
        // M2 minimal: the one client is player 1.
        RpcId(peerId, MethodName.StartMatchRpc, unchecked((long)MatchSeed), 1);
        // Host is player 0 — fire locally (RPCs are CallLocal=false).
        MatchReady?.Invoke(0, MatchSeed);
    }

    private void OnPeerDisconnected(long peerId) => PeerDropped?.Invoke();
    private void OnServerDisconnected() => PeerDropped?.Invoke();

    // CORRECTNESS INVARIANT — all RPCs below intentionally share the default reliable channel (0).
    // The host emits StartMatchRpc (which makes the client subscribe to frames in InitNetworked)
    // BEFORE the primed frame RPCs; same-channel reliable delivery is ordered, so the client always
    // subscribes before any frame arrives. Do NOT add a per-method `channel:` to one of these without
    // the others — splitting channels can reorder StartMatchRpc after the frames → permanent startup stall.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartMatchRpc(long seedBits, int assignedPlayerId)
    {
        LocalPlayerId = assignedPlayerId;
        MatchReady?.Invoke(assignedPlayerId, unchecked((ulong)seedBits));
    }

    // ---- Frames ----
    public void SendFrame(CommandFrame frame)
    {
        var bytes = CommandCodec.FrameToBytes(frame);
        if (IsHost) Rpc(MethodName.ReceiveFrameRpc, bytes);          // host → all clients
        else RpcId(1, MethodName.ReceiveFrameRpc, bytes);           // client → host (relays on)
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveFrameRpc(byte[] bytes)
    {
        FrameReceived?.Invoke(CommandCodec.FrameFromBytes(bytes));
        if (IsHost) RelayToOthers(MethodName.ReceiveFrameRpc, bytes);  // forward to other clients (no-op for 2P)
    }

    // ---- Hashes ----
    public void SendHash(int tick, int playerId, ulong hash)
    {
        long bits = unchecked((long)hash);
        if (IsHost) Rpc(MethodName.ReceiveHashRpc, tick, playerId, bits);
        else RpcId(1, MethodName.ReceiveHashRpc, tick, playerId, bits);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveHashRpc(int tick, int playerId, long hashBits)
    {
        HashReceived?.Invoke(tick, playerId, unchecked((ulong)hashBits));
        if (IsHost) RelayToOthers(MethodName.ReceiveHashRpc, tick, playerId, hashBits);
    }

    // Host-relay: forward a just-received RPC to every connected peer except the sender.
    private void RelayToOthers(StringName method, params Variant[] args)
    {
        int sender = Multiplayer.GetRemoteSenderId();
        foreach (int peer in Multiplayer.GetPeers())
            if (peer != sender) RpcId(peer, method, args);
    }
}
