using System.Collections.Generic;
using Godot;
using SimCore.Net;
using SimCore.Packs;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Multiplayer lobby overlay. Host configures slots (kind/team/faction/difficulty) and
/// starts; clients claim an open slot + pick their own faction. Drives NetSession; on Start the
/// host broadcasts the final config and everyone builds the match (Main wires MatchStarting).</summary>
public partial class LobbyScreen : CanvasLayer
{
    private NetSession _net = null!;
    private bool _isHost;
    private IReadOnlyList<FactionEntry> _factions = System.Array.Empty<FactionEntry>();
    private List<LobbySlot> _slots = new();          // host: authoritative; client: last received
    private VBoxContainer _rows = null!;
    private Button _start = null!;

    public void Init(NetSession net, bool isHost, IReadOnlyList<FactionEntry> factions)
    {
        _net = net; _isHost = isHost; _factions = factions;
    }

    public override void _Ready()
    {
        Layer = 100;
        var panel = new PanelContainer();
        var box = new VBoxContainer();
        panel.AddChild(box);
        AddChild(panel);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);

        box.AddChild(new Label { Text = _isHost ? "Lobby (Host)" : "Lobby — waiting for host", HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(new Label
        {
            Text = _isHost
                ? "Kind = Open seat (a joiner can Claim) or CPU.   Team A vs Team B (same team = allies).\nEvery seat must be filled — Claim it, or set it to CPU — before Start."
                : "Claim an Open seat and pick your faction, then wait for the host to start.",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        _rows = new VBoxContainer();
        box.AddChild(_rows);

        if (_isHost)
        {
            var addRow = new HBoxContainer();
            var add = new Button { Text = "+ Slot" };
            add.Pressed += () => { if (_slots.Count < 4) { _slots.Add(new LobbySlot(SlotKind.Cpu, _slots.Count % 2, _factions[0].Faction.Id, AiDifficulty.Easy, 0)); PushAndRender(); } };
            var rem = new Button { Text = "- Slot" };
            rem.Pressed += () => { if (_slots.Count > 2) { _slots.RemoveAt(_slots.Count - 1); PushAndRender(); } };
            addRow.AddChild(add); addRow.AddChild(rem);
            box.AddChild(addRow);

            _start = new Button { Text = "Start Match" };
            _start.Pressed += OnStart;
            box.AddChild(_start);

            // Default to a ready-to-go 2v2: you + an Open ally seat (Team A) vs two CPUs (Team B).
            // Flip the Open seat to CPU for a 1-human test, or let the other window Join + Claim it.
            string fid = _factions[0].Faction.Id;
            _slots = new List<LobbySlot>
            {
                new(SlotKind.Human, 0, fid, AiDifficulty.Easy, 1), // slot 0 = you (host), Team A
                new(SlotKind.Open,  0, fid, AiDifficulty.Easy, 0), // slot 1 = ally seat, Team A
                new(SlotKind.Cpu,   1, fid, AiDifficulty.Easy, 0), // slot 2 = enemy CPU, Team B
                new(SlotKind.Cpu,   1, fid, AiDifficulty.Easy, 0), // slot 3 = enemy CPU, Team B
            };

            _net.PeerConnectedToLobby += OnPeerJoined;
            _net.FactionRequested += OnFactionRequested;
            _net.ClaimRequested += OnClaimRequested;
            PushAndRender();
        }
        else
        {
            _net.LobbyUpdated += slots => { _slots = new List<LobbySlot>(slots); Render(); };
        }
    }

    // ---- Host slot mutation ----
    private void PushAndRender() { _net.SetLobby(_slots); Render(); }   // SetLobby raises LobbyUpdated locally too, but we render directly

    private void OnPeerJoined(long peerId)
    {
        // Assign the joiner to the first Open slot (becomes a remote Human).
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].Kind == SlotKind.Open)
            { _slots[i] = _slots[i] with { Kind = SlotKind.Human, OccupantPeerId = peerId }; PushAndRender(); return; }
    }

    private void OnFactionRequested(long peerId, string factionId)
    {
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].OccupantPeerId == peerId) { _slots[i] = _slots[i] with { FactionId = factionId }; PushAndRender(); return; }
    }

    private void OnClaimRequested(long peerId, int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < _slots.Count && _slots[slotIndex].Kind == SlotKind.Open)
        { _slots[slotIndex] = _slots[slotIndex] with { Kind = SlotKind.Human, OccupantPeerId = peerId }; PushAndRender(); }
    }

    private void OnStart()
    {
        // Validity: every slot filled (not Open), both teams non-empty.
        bool anyOpen = false; bool t0 = false, t1 = false;
        foreach (var s in _slots) { if (s.Kind == SlotKind.Open) anyOpen = true; if (s.Team == 0) t0 = true; else t1 = true; }
        if (anyOpen || !t0 || !t1) { GD.Print("Lobby not ready: fill all slots, both teams non-empty"); return; }
        _net.StartMatch(_slots, NetSession.MatchSeed);
    }

    // ---- Rendering ----
    private void Render()
    {
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        long myPeer = _net.Multiplayer.GetUniqueId();
        for (int i = 0; i < _slots.Count; i++)
        {
            var s = _slots[i];
            int idx = i;   // per-iteration copy: button handlers fire later, when the for-loop's `i` == Count
            var row = new HBoxContainer();
            bool mine = s.OccupantPeerId == myPeer && s.Kind == SlotKind.Human;
            string who = s.Kind == SlotKind.Open ? "Open seat"
                : s.Kind == SlotKind.Cpu ? "CPU"
                : s.OccupantPeerId == myPeer ? "You"
                : s.OccupantPeerId == 1 ? "Host"
                : $"Player {s.OccupantPeerId}";
            row.AddChild(new Label { Text = $"Slot {i}:  {who}   ·   Team {(s.Team == 0 ? "A" : "B")}" });

            if (_isHost)
            {
                // Buttons show the CURRENT value and toggle it on press. Slot 0 is always the host.
                var kind = new Button { Text = s.Kind == SlotKind.Cpu ? "Kind: CPU" : "Kind: Open" };
                kind.Pressed += () => { _slots[idx] = s with { Kind = s.Kind == SlotKind.Cpu ? SlotKind.Open : SlotKind.Cpu, OccupantPeerId = 0 }; PushAndRender(); };
                if (i != 0) row.AddChild(kind);
                var team = new Button { Text = $"Team {(s.Team == 0 ? "A" : "B")}" };
                team.Pressed += () => { _slots[idx] = s with { Team = 1 - s.Team }; PushAndRender(); };
                row.AddChild(team);
            }

            // Faction picker: host edits CPU + its own; a client edits only its own slot.
            bool canEditFaction = (_isHost && (s.Kind == SlotKind.Cpu || s.OccupantPeerId == 1)) || mine;
            if (canEditFaction)
            {
                var opt = new OptionButton { TooltipText = FactionInfo.BlurbFor(s.FactionId) };
                for (int f = 0; f < _factions.Count; f++) opt.AddItem(_factions[f].Name, f);
                int sel = FactionIndex(s.FactionId); opt.Selected = sel < 0 ? 0 : sel;
                int slotIdx = i;
                opt.ItemSelected += id =>
                {
                    string fid = _factions[(int)id].Faction.Id;
                    if (_isHost) { _slots[slotIdx] = _slots[slotIdx] with { FactionId = fid }; PushAndRender(); }
                    else _net.RequestMyFaction(fid);
                };
                row.AddChild(opt);
            }
            else row.AddChild(new Label { Text = FactionName(s.FactionId), TooltipText = FactionInfo.BlurbFor(s.FactionId) });

            if (_isHost && s.Kind == SlotKind.Cpu)
            {
                var diff = new Button { Text = $"Difficulty: {s.Difficulty}" };
                diff.Pressed += () => { var nd = (AiDifficulty)(((int)s.Difficulty + 1) % 3); _slots[idx] = s with { Difficulty = nd }; PushAndRender(); };
                row.AddChild(diff);
            }

            // Client: a button to claim this slot if it's Open.
            if (!_isHost && s.Kind == SlotKind.Open)
            {
                int slotIdx = i;
                var claim = new Button { Text = "Claim" };
                claim.Pressed += () => _net.RequestClaim(slotIdx);
                row.AddChild(claim);
            }
            _rows.AddChild(row);
        }
    }

    private int FactionIndex(string id)
    {
        for (int i = 0; i < _factions.Count; i++) if (_factions[i].Faction.Id == id) return i;
        return -1;
    }
    private string FactionName(string id) { int i = FactionIndex(id); return i < 0 ? id : _factions[i].Name; }
}
