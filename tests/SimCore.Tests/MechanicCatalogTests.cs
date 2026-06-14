using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class MechanicCatalogTests
{
    private static UnitSpec U() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec B() => new(100, 2, 2, 100, 10, CanTrain: true);
    private static FactionDef Make(MechanicDef? mech) => new("f", "F",
        units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
        buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) },
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: mech);

    [Fact]
    public void Mechanic_Defaults_Null_And_Old_Ctors_Work()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Null(f.Mechanic);
    }

    [Fact]
    public void Shield_Mechanic_Is_Stored()
    {
        var f = Make(new MechanicDef(MechanicKind.RegeneratingShields, 20, 1, 30));
        Assert.Equal(MechanicKind.RegeneratingShields, f.Mechanic!.Kind);
        Assert.Equal(20, f.Mechanic.MaxShield);
        Assert.Equal(1, f.Mechanic.RegenPerTick);
        Assert.Equal(30, f.Mechanic.RegenDelayTicks);
        Assert.Empty(f.Validate());
    }

    [Fact]
    public void Negative_Shield_Params_Flagged()
    {
        var f = Make(new MechanicDef(MechanicKind.RegeneratingShields, -5, 1, 30));
        Assert.Contains(f.Validate(), e => e.Contains("mechanic"));
    }

    [Fact]
    public void None_Kind_With_Shield_Params_Flagged()
    {
        var f = Make(new MechanicDef(MechanicKind.None, 20, 1, 30));
        Assert.Contains(f.Validate(), e => e.Contains("mechanic"));
    }
}
