using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class RegenerationTests
{
    private static FactionDef RegenFaction(int regen, int delay) => new("rf", "RF",
        units: System.Array.Empty<UnitDef>(),
        buildings: System.Array.Empty<BuildingDef>(),
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: new MechanicDef(MechanicKind.Regeneration, MaxShield: 0, RegenPerTick: regen, RegenDelayTicks: delay));

    [Fact]
    public void Hp_Regens_After_Delay_And_Caps_At_MaxHp()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: RegenFaction(regen: 2, delay: 3));
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        var u = w.GetUnit(id)!;
        Assert.Equal(50, u.MaxHp);     // SpawnUnit sets MaxHp = spawn hp
        u.Hp = 40;                     // damaged below full
        u.TicksSinceDamaged = 0;       // just took damage

        w.Step(System.Array.Empty<Command>()); // t=1, tsd 0->1, no regen (1 < 3)
        w.Step(System.Array.Empty<Command>()); // t=2, tsd 2, no regen (2 < 3)
        Assert.Equal(40, w.GetUnit(id)!.Hp);
        w.Step(System.Array.Empty<Command>()); // t=3, tsd 3 -> regen +2 (3 >= 3)
        Assert.Equal(42, w.GetUnit(id)!.Hp);
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(id)!.Hp); // capped at MaxHp, never exceeds
    }

    [Fact]
    public void Hp_Does_Not_Regen_Before_Delay()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: RegenFaction(regen: 5, delay: 30));
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 100);
        var u = w.GetUnit(id)!;
        u.Hp = 60;
        u.TicksSinceDamaged = 0;

        for (int i = 0; i < 29; i++) w.Step(System.Array.Empty<Command>()); // tsd reaches 29 < 30
        Assert.Equal(60, w.GetUnit(id)!.Hp); // no healing before the delay elapses
        w.Step(System.Array.Empty<Command>()); // tsd 30 -> first regen
        Assert.Equal(65, w.GetUnit(id)!.Hp);
    }

    [Fact]
    public void Dead_Unit_Is_Not_Revived()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: RegenFaction(regen: 2, delay: 1));
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        var u = w.GetUnit(id)!;
        u.Hp = 0;                  // dead
        u.TicksSinceDamaged = 0;

        // Step once: RemoveDead runs after UpdateShields, so the unit is gone — but verify
        // the regen branch itself never lifted Hp above 0 (no revival) by checking before removal.
        // Set delay 1 so the branch would fire if Hp>0 were not gated.
        w.Step(System.Array.Empty<Command>());
        // Unit is removed once dead; if it had been revived it would still be present at Hp 2.
        Assert.Null(w.GetUnit(id));
    }

    [Fact]
    public void Dead_Unit_Regen_Branch_Does_Not_Heal()
    {
        // Directly exercise the Hp>0 guard without RemoveDead interfering: regen never lifts a
        // 0-Hp unit. Use a large MaxHp gap and delay 0 so the branch is maximally eager.
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: RegenFaction(regen: 10, delay: 0));
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 100);
        var u = w.GetUnit(id)!;
        u.Hp = 0;
        u.TicksSinceDamaged = 5; // already past any delay

        // Inspect the unit reference we hold (RemoveDead removes it from the world, but our
        // captured reference reflects whether UpdateShields mutated Hp this tick).
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, u.Hp); // the dead unit was never healed by the regen branch
    }

    [Fact]
    public void No_Mechanic_Faction_Does_Not_Heal()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: TestFactions.Standard);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        var u = w.GetUnit(id)!;
        Assert.Equal(50, u.MaxHp);
        u.Hp = 30;
        u.TicksSinceDamaged = 0;

        for (int i = 0; i < 50; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(30, w.GetUnit(id)!.Hp);              // unchanged: no mechanic, no healing
        Assert.Equal(0, w.GetUnit(id)!.TicksSinceDamaged); // no per-tick churn either
    }

    [Fact]
    public void Null_Faction_Player_Does_Not_Heal()
    {
        // Player 0 has a regen faction; player 1 has none (null). A player-1 unit must not heal.
        var regen = RegenFaction(regen: 5, delay: 1);
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, new FactionDef?[] { regen, null });
        var id = w.SpawnUnit(1, w.Map.CellCenter(4, 4), Fix.FromInt(1), 80);
        var u = w.GetUnit(id)!;
        Assert.Equal(80, u.MaxHp);
        u.Hp = 50;
        u.TicksSinceDamaged = 0;

        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(id)!.Hp);              // null faction → no heal
        Assert.Equal(0, w.GetUnit(id)!.TicksSinceDamaged); // no churn
    }

    [Fact]
    public void Regen_Faction_Does_Not_Regen_Shields()
    {
        // Regeneration reuses RegenPerTick/RegenDelayTicks for Hp; ShieldHp stays 0 (MaxShield 0).
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: RegenFaction(regen: 2, delay: 1));
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        var u = w.GetUnit(id)!;
        Assert.Equal(0, u.ShieldHp); // no shield pool under a regen faction
        u.Hp = 40;
        u.TicksSinceDamaged = 0;

        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(id)!.ShieldHp); // shields never grow
    }
}
