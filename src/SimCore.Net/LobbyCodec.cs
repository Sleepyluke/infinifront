using System.Collections.Generic;
using System.IO;
using SimCore.Sim;

namespace SimCore.Net;

/// <summary>Binary (de)serialization of the lobby slot list for NetSession RPCs. Little-endian +
/// length-prefixed strings (cross-OS stable), same discipline as CommandCodec.</summary>
public static class LobbyCodec
{
    public static byte[] SlotsToBytes(IReadOnlyList<LobbySlot> slots)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(slots.Count);
            foreach (var s in slots)
            {
                w.Write((byte)s.Kind);
                w.Write(s.Team);
                w.Write(s.FactionId);
                w.Write((byte)s.Difficulty);
                w.Write(s.OccupantPeerId);
            }
        }
        return ms.ToArray();
    }

    public static IReadOnlyList<LobbySlot> SlotsFromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        int n = r.ReadInt32();
        var slots = new List<LobbySlot>(n);
        for (int i = 0; i < n; i++)
        {
            var kind = (SlotKind)r.ReadByte();
            int team = r.ReadInt32();
            string factionId = r.ReadString();
            var diff = (AiDifficulty)r.ReadByte();
            long peer = r.ReadInt64();
            slots.Add(new LobbySlot(kind, team, factionId, diff, peer));
        }
        return slots;
    }
}
