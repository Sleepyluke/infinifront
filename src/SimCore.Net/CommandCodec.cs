using System.Collections.Generic;
using System.IO;
using SimCore.Math;
using SimCore.Sim;

namespace SimCore.Net;

/// <summary>Compact, deterministic binary (de)serialization of CommandFrames so they can cross
/// an RPC. No floats: FixVec is two long raws. BinaryWriter/Reader are little-endian on every
/// platform, so the wire format is cross-OS stable. A 1-byte tag selects the command type.</summary>
public static class CommandCodec
{
    // Tag bytes — append-only; never renumber (the wire format depends on these).
    private const byte TagMove = 0, TagAttack = 1, TagAttackMove = 2, TagBuild = 3, TagTrain = 4,
        TagHarvest = 5, TagSetStance = 6, TagPatrol = 7, TagSetRally = 8, TagDestroy = 9, TagResearch = 10;

    public static byte[] FrameToBytes(CommandFrame frame)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) WriteFrame(w, frame);
        return ms.ToArray();
    }

    public static CommandFrame FrameFromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        return ReadFrame(r);
    }

    public static void WriteFrame(BinaryWriter w, CommandFrame frame)
    {
        w.Write(frame.Tick);
        w.Write(frame.PlayerId);
        w.Write(frame.Commands.Count);
        foreach (var c in frame.Commands) WriteCommand(w, c);
    }

    public static CommandFrame ReadFrame(BinaryReader r)
    {
        int tick = r.ReadInt32();
        int playerId = r.ReadInt32();
        int count = r.ReadInt32();
        var cmds = new Command[count];
        for (int i = 0; i < count; i++) cmds[i] = ReadCommand(r);
        return new CommandFrame(tick, playerId, cmds);
    }

    private static void WriteCommand(BinaryWriter w, Command c)
    {
        switch (c)
        {
            case MoveCommand m:
                w.Write(TagMove); w.Write(m.PlayerId); WriteInts(w, m.UnitIds); WriteVec(w, m.Target); break;
            case AttackCommand a:
                w.Write(TagAttack); w.Write(a.PlayerId); WriteInts(w, a.UnitIds); w.Write(a.TargetId); break;
            case AttackMoveCommand am:
                w.Write(TagAttackMove); w.Write(am.PlayerId); WriteInts(w, am.UnitIds); WriteVec(w, am.Target); break;
            case BuildCommand b:
                w.Write(TagBuild); w.Write(b.PlayerId); w.Write(b.WorkerUnitId); w.Write(b.BuildingDefId); w.Write(b.CellX); w.Write(b.CellY); break;
            case TrainCommand t:
                w.Write(TagTrain); w.Write(t.PlayerId); w.Write(t.BuildingId); w.Write(t.UnitDefId); break;
            case HarvestCommand h:
                w.Write(TagHarvest); w.Write(h.PlayerId); WriteInts(w, h.UnitIds); w.Write(h.NodeId); break;
            case SetStanceCommand s:
                w.Write(TagSetStance); w.Write(s.PlayerId); WriteInts(w, s.UnitIds); w.Write((byte)s.Stance); break;
            case PatrolCommand p:
                w.Write(TagPatrol); w.Write(p.PlayerId); WriteInts(w, p.UnitIds); WriteVec(w, p.Target); break;
            case SetRallyCommand sr:
                w.Write(TagSetRally); w.Write(sr.PlayerId); w.Write(sr.BuildingId); WriteVec(w, sr.Target); w.Write(sr.Clear); break;
            case DestroyCommand d:
                w.Write(TagDestroy); w.Write(d.PlayerId); WriteInts(w, d.Ids); break;
            case ResearchCommand rc:
                w.Write(TagResearch); w.Write(rc.PlayerId); w.Write(rc.BuildingId); w.Write(rc.UpgradeDefId); break;
            default:
                throw new System.NotSupportedException($"CommandCodec cannot serialize {c.GetType().Name}");
        }
    }

    private static Command ReadCommand(BinaryReader r)
    {
        byte tag = r.ReadByte();
        switch (tag)
        {
            case TagMove: { int p = r.ReadInt32(); var ids = ReadInts(r); return new MoveCommand(p, ids, ReadVec(r)); }
            case TagAttack: { int p = r.ReadInt32(); var ids = ReadInts(r); return new AttackCommand(p, ids, r.ReadInt32()); }
            case TagAttackMove: { int p = r.ReadInt32(); var ids = ReadInts(r); return new AttackMoveCommand(p, ids, ReadVec(r)); }
            case TagBuild: { int p = r.ReadInt32(); int wu = r.ReadInt32(); string bid = r.ReadString(); int cx = r.ReadInt32(); int cy = r.ReadInt32(); return new BuildCommand(p, wu, bid, cx, cy); }
            case TagTrain: { int p = r.ReadInt32(); int b = r.ReadInt32(); return new TrainCommand(p, b, r.ReadString()); }
            case TagHarvest: { int p = r.ReadInt32(); var ids = ReadInts(r); return new HarvestCommand(p, ids, r.ReadInt32()); }
            case TagSetStance: { int p = r.ReadInt32(); var ids = ReadInts(r); return new SetStanceCommand(p, ids, (Stance)r.ReadByte()); }
            case TagPatrol: { int p = r.ReadInt32(); var ids = ReadInts(r); return new PatrolCommand(p, ids, ReadVec(r)); }
            case TagSetRally: { int p = r.ReadInt32(); int b = r.ReadInt32(); var t = ReadVec(r); return new SetRallyCommand(p, b, t, r.ReadBoolean()); }
            case TagDestroy: { int p = r.ReadInt32(); return new DestroyCommand(p, ReadInts(r)); }
            case TagResearch: { int p = r.ReadInt32(); int b = r.ReadInt32(); return new ResearchCommand(p, b, r.ReadString()); }
            default: throw new System.NotSupportedException($"CommandCodec: unknown command tag {tag}");
        }
    }

    private static void WriteInts(BinaryWriter w, int[] xs)
    {
        w.Write(xs.Length);
        foreach (int x in xs) w.Write(x);
    }

    private static int[] ReadInts(BinaryReader r)
    {
        int n = r.ReadInt32();
        var xs = new int[n];
        for (int i = 0; i < n; i++) xs[i] = r.ReadInt32();
        return xs;
    }

    private static void WriteVec(BinaryWriter w, FixVec v) { w.Write(v.X.Raw); w.Write(v.Y.Raw); }
    private static FixVec ReadVec(BinaryReader r) => new(new Fix(r.ReadInt64()), new Fix(r.ReadInt64()));
}
