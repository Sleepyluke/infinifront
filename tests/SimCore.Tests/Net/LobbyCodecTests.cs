using System.Collections.Generic;
using SimCore.Net;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests.Net;

public class LobbyCodecTests
{
    private static List<LobbySlot> Sample() => new()
    {
        new LobbySlot(SlotKind.Human, Team: 0, FactionId: "reference", Difficulty: AiDifficulty.Easy, OccupantPeerId: 1),
        new LobbySlot(SlotKind.Cpu,   Team: 0, FactionId: "swarm",     Difficulty: AiDifficulty.Hard, OccupantPeerId: 0),
        new LobbySlot(SlotKind.Open,  Team: 1, FactionId: "",          Difficulty: AiDifficulty.Easy, OccupantPeerId: 0),
        new LobbySlot(SlotKind.Human, Team: 1, FactionId: "reference", Difficulty: AiDifficulty.Medium, OccupantPeerId: 77),
    };

    [Fact]
    public void Slots_RoundTrip_Field_Exact()
    {
        var slots = Sample();
        var back = LobbyCodec.SlotsFromBytes(LobbyCodec.SlotsToBytes(slots));
        Assert.Equal(slots.Count, back.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            Assert.Equal(slots[i].Kind, back[i].Kind);
            Assert.Equal(slots[i].Team, back[i].Team);
            Assert.Equal(slots[i].FactionId, back[i].FactionId);
            Assert.Equal(slots[i].Difficulty, back[i].Difficulty);
            Assert.Equal(slots[i].OccupantPeerId, back[i].OccupantPeerId);
        }
    }

    [Fact]
    public void Empty_Slot_List_RoundTrips()
    {
        var back = LobbyCodec.SlotsFromBytes(LobbyCodec.SlotsToBytes(new List<LobbySlot>()));
        Assert.Empty(back);
    }
}
