using System.Linq;
using SimCore.Math;
using SimCore.Net;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests.Net;

public class CommandCodecTests
{
    // Round-trip a one-command frame and return the decoded command.
    private static Command RoundTripOne(Command c)
    {
        var frame = new CommandFrame(Tick: 7, PlayerId: 1, Commands: new[] { c });
        var back = CommandCodec.FrameFromBytes(CommandCodec.FrameToBytes(frame));
        Assert.Equal(7, back.Tick);
        Assert.Equal(1, back.PlayerId);
        Assert.Single(back.Commands);
        return back.Commands[0];
    }

    [Fact]
    public void Move_RoundTrips()
    {
        var c = (MoveCommand)RoundTripOne(new MoveCommand(1, new[] { 3, 5, 8 }, new FixVec(Fix.FromInt(12), Fix.FromInt(-4))));
        Assert.Equal(1, c.PlayerId);
        Assert.Equal(new[] { 3, 5, 8 }, c.UnitIds);
        Assert.Equal(new FixVec(Fix.FromInt(12), Fix.FromInt(-4)), c.Target);
    }

    [Fact]
    public void Attack_RoundTrips()
    {
        var c = (AttackCommand)RoundTripOne(new AttackCommand(1, new[] { 9 }, TargetId: 42));
        Assert.Equal(new[] { 9 }, c.UnitIds);
        Assert.Equal(42, c.TargetId);
    }

    [Fact]
    public void AttackMove_RoundTrips()
    {
        var c = (AttackMoveCommand)RoundTripOne(new AttackMoveCommand(1, new[] { 2, 4 }, new FixVec(Fix.FromInt(7), Fix.FromInt(7))));
        Assert.Equal(new[] { 2, 4 }, c.UnitIds);
        Assert.Equal(new FixVec(Fix.FromInt(7), Fix.FromInt(7)), c.Target);
    }

    [Fact]
    public void Build_RoundTrips()
    {
        var c = (BuildCommand)RoundTripOne(new BuildCommand(1, WorkerUnitId: 11, BuildingDefId: "depot", CellX: 4, CellY: 9));
        Assert.Equal(11, c.WorkerUnitId);
        Assert.Equal("depot", c.BuildingDefId);
        Assert.Equal(4, c.CellX);
        Assert.Equal(9, c.CellY);
    }

    [Fact]
    public void Train_RoundTrips()
    {
        var c = (TrainCommand)RoundTripOne(new TrainCommand(1, BuildingId: 6, UnitDefId: "marine"));
        Assert.Equal(6, c.BuildingId);
        Assert.Equal("marine", c.UnitDefId);
    }

    [Fact]
    public void Harvest_RoundTrips()
    {
        var c = (HarvestCommand)RoundTripOne(new HarvestCommand(1, new[] { 1, 2 }, NodeId: 99));
        Assert.Equal(new[] { 1, 2 }, c.UnitIds);
        Assert.Equal(99, c.NodeId);
    }

    [Fact]
    public void SetStance_RoundTrips()
    {
        var c = (SetStanceCommand)RoundTripOne(new SetStanceCommand(1, new[] { 3 }, Stance.Passive));
        Assert.Equal(new[] { 3 }, c.UnitIds);
        Assert.Equal(Stance.Passive, c.Stance);
    }

    [Fact]
    public void Patrol_RoundTrips()
    {
        var c = (PatrolCommand)RoundTripOne(new PatrolCommand(1, new[] { 5 }, new FixVec(Fix.FromInt(1), Fix.FromInt(2))));
        Assert.Equal(new[] { 5 }, c.UnitIds);
        Assert.Equal(new FixVec(Fix.FromInt(1), Fix.FromInt(2)), c.Target);
    }

    [Fact]
    public void SetRally_RoundTrips()
    {
        var c = (SetRallyCommand)RoundTripOne(new SetRallyCommand(1, BuildingId: 8, new FixVec(Fix.FromInt(10), Fix.FromInt(11)), Clear: true));
        Assert.Equal(8, c.BuildingId);
        Assert.Equal(new FixVec(Fix.FromInt(10), Fix.FromInt(11)), c.Target);
        Assert.True(c.Clear);
    }

    [Fact]
    public void Destroy_RoundTrips()
    {
        var c = (DestroyCommand)RoundTripOne(new DestroyCommand(1, new[] { 12, 13, 14 }));
        Assert.Equal(new[] { 12, 13, 14 }, c.Ids);
    }

    [Fact]
    public void Research_RoundTrips()
    {
        var c = (ResearchCommand)RoundTripOne(new ResearchCommand(1, BuildingId: 3, UpgradeDefId: "weapons1"));
        Assert.Equal(3, c.BuildingId);
        Assert.Equal("weapons1", c.UpgradeDefId);
    }

    [Fact]
    public void EmptyFrame_RoundTrips()
    {
        var frame = new CommandFrame(Tick: 0, PlayerId: 0, Commands: System.Array.Empty<Command>());
        var back = CommandCodec.FrameFromBytes(CommandCodec.FrameToBytes(frame));
        Assert.Equal(0, back.Tick);
        Assert.Equal(0, back.PlayerId);
        Assert.Empty(back.Commands);
    }

    [Fact]
    public void MultiCommandFrame_RoundTrips_InOrder()
    {
        var frame = new CommandFrame(3, 1, new Command[]
        {
            new MoveCommand(1, new[] { 1 }, new FixVec(Fix.FromInt(5), Fix.FromInt(5))),
            new TrainCommand(1, 2, "marine"),
            new DestroyCommand(1, new[] { 9 }),
        });
        var back = CommandCodec.FrameFromBytes(CommandCodec.FrameToBytes(frame));
        Assert.Equal(3, back.Commands.Count);
        Assert.IsType<MoveCommand>(back.Commands[0]);
        Assert.IsType<TrainCommand>(back.Commands[1]);
        Assert.IsType<DestroyCommand>(back.Commands[2]);
    }

    [Fact]
    public void Reserialization_Is_Byte_Stable()
    {
        var frame = new CommandFrame(123, 1, new Command[]
        {
            new BuildCommand(1, 11, "depot", 4, 9),
            new SetRallyCommand(1, 8, new FixVec(Fix.FromInt(10), Fix.FromInt(11)), true),
        });
        var bytes1 = CommandCodec.FrameToBytes(frame);
        var bytes2 = CommandCodec.FrameToBytes(CommandCodec.FrameFromBytes(bytes1));
        Assert.Equal(bytes1, bytes2);
    }
}
