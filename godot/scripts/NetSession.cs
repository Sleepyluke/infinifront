using System;
using System.Collections.Generic;
using Godot;
using SimCore.Net;

namespace LlmRts.Godot;

/// <summary>ENet transport for deterministic lockstep. Host/Join, a host-authoritative lobby
/// protocol (sync slots / claim / faction / start-config broadcast), and host-relayed frame/hash
/// RPCs. Raises events for LobbyScreen + SimRunner. M3: up to 4 players in 2 teams.</summary>
public partial class NetSession : Node
{
    public const int Port = 7777;
    public const ulong MatchSeed = 42;   // M3: the host could later choose this; fixed for now.
    public const int InputDelay = 3;     // ~300 ms at 10 ticks/s.

    public bool IsHost { get; private set; }
    /// <summary>The local player's slot index. Set by Main from the final slot list (the slot whose
    /// OccupantPeerId == Multiplayer.GetUniqueId()) — no longer set in a handshake.</summary>
    public int LocalPlayerId { get; set; }

    // ---- Lobby protocol (M3) ----
    /// <summary>Host-authoritative current slot list changed (or first received). UI re-renders.</summary>
    public event Action<IReadOnlyList<LobbySlot>>? LobbyUpdated;
    /// <summary>The match is starting: final slots + seed. Build the world + enter the loop.</summary>
    public event Action<IReadOnlyList<LobbySlot>, ulong>? MatchStarting;
    /// <summary>(Host) Fired when a client requests its faction: (senderPeerId, factionId).</summary>
    public event Action<long, string>? FactionRequested;
    /// <summary>(Host) Fired when a client requests to claim a slot: (senderPeerId, slotIndex).</summary>
    public event Action<long, int>? ClaimRequested;
    /// <summary>(Host) A peer connected (assign it an open slot).</summary>
    public event Action<long>? PeerConnectedToLobby;

    /// <summary>A remote human's command frame arrived → coordinator.Receive.</summary>
    public event Action<CommandFrame>? FrameReceived;
    /// <summary>A remote peer's per-tick state hash arrived → coordinator.ReceiveHash.</summary>
    public event Action<int, int, ulong>? HashReceived;
    /// <summary>A peer dropped (M4 handles recovery; M3 just surfaces it).</summary>
    public event Action? PeerDropped;

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

    // ---- Lobby (host-authoritative) ----
    // Replaces M2's OnPeerConnected auto-start. The HOST owns the slot list and assigns joiners.
    // Host code calls SetLobby(slots) to push state; clients receive it via SyncLobbyRpc.
    private void OnPeerConnected(long peerId)
    {
        if (IsHost) PeerConnectedToLobby?.Invoke(peerId);
    }

    private void OnPeerDisconnected(long peerId) => PeerDropped?.Invoke();
    private void OnServerDisconnected() => PeerDropped?.Invoke();

    /// <summary>(Host) Broadcast the authoritative slot list to all clients and raise locally.</summary>
    public void SetLobby(IReadOnlyList<LobbySlot> slots)
    {
        if (!IsHost) return;
        var bytes = LobbyCodec.SlotsToBytes(slots);
        Rpc(MethodName.SyncLobbyRpc, bytes);
        LobbyUpdated?.Invoke(slots);
    }

    /// <summary>(Host) Broadcast the final config + seed; everyone (host local too) starts.</summary>
    public void StartMatch(IReadOnlyList<LobbySlot> slots, ulong seed)
    {
        if (!IsHost) return;
        var bytes = LobbyCodec.SlotsToBytes(slots);
        Rpc(MethodName.StartConfigRpc, bytes, unchecked((long)seed));
        MatchStarting?.Invoke(slots, seed);     // host starts locally (RPCs are CallLocal=false)
    }

    /// <summary>(Client) Ask the host to set MY slot's faction.</summary>
    public void RequestMyFaction(string factionId) => RpcId(1, MethodName.SetFactionRpc, factionId);
    /// <summary>(Client) Ask the host to claim an Open slot for me.</summary>
    public void RequestClaim(int slotIndex) => RpcId(1, MethodName.ClaimSlotRpc, slotIndex);

    // CORRECTNESS INVARIANT — all RPCs below intentionally share the default reliable channel (0).
    // The host emits StartConfigRpc (which makes the client subscribe to frames in InitNetworked)
    // BEFORE the primed frame RPCs; same-channel reliable delivery is ordered, so the client always
    // subscribes before any frame arrives. Do NOT add a per-method `channel:` to one of these without
    // the others — splitting channels can reorder StartConfigRpc after the frames → permanent startup stall.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncLobbyRpc(byte[] bytes) => LobbyUpdated?.Invoke(LobbyCodec.SlotsFromBytes(bytes));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartConfigRpc(byte[] bytes, long seedBits) =>
        MatchStarting?.Invoke(LobbyCodec.SlotsFromBytes(bytes), unchecked((ulong)seedBits));

    // Host-side requests from clients. The host raises an event so LobbyScreen mutates + re-broadcasts.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SetFactionRpc(string factionId) => FactionRequested?.Invoke(Multiplayer.GetRemoteSenderId(), factionId);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClaimSlotRpc(int slotIndex) => ClaimRequested?.Invoke(Multiplayer.GetRemoteSenderId(), slotIndex);

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
