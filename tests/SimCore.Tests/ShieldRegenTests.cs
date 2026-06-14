using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ShieldRegenTests
{
    private static FactionDef ShieldFaction(int max, int regen, int delay) => new("f", "F",
        units: System.Array.Empty<UnitDef>(),
        buildings: System.Array.Empty<BuildingDef>(),
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: new MechanicDef(MechanicKind.RegeneratingShields, max, regen, delay));

    [Fact]
    public void Shield_Regens_After_Delay_And_Caps_At_Max()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: ShieldFaction(max: 10, regen: 2, delay: 3));
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        w.GetUnit(id)!.ShieldHp = 0; // drained

        w.Step(System.Array.Empty<Command>()); // t=1, tsd 0->1, no regen (1 < 3)
        w.Step(System.Array.Empty<Command>()); // t=2, tsd 2
        Assert.Equal(0, w.GetUnit(id)!.ShieldHp);
        w.Step(System.Array.Empty<Command>()); // t=3, tsd 3 -> regen +2 (3 >= 3)
        Assert.Equal(2, w.GetUnit(id)!.ShieldHp);
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(10, w.GetUnit(id)!.ShieldHp); // capped at max
    }

    [Fact]
    public void Non_Shield_Faction_Has_No_Regen_State_Churn()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: TestFactions.Standard);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        for (int i = 0; i < 5; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(id)!.ShieldHp);
        Assert.Equal(0, w.GetUnit(id)!.TicksSinceDamaged);
    }
}
