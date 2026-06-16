using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class LifestealTests
{
    private static FactionDef LifestealFaction(int healPerHit) => new("lf", "LF",
        units: System.Array.Empty<UnitDef>(),
        buildings: System.Array.Empty<BuildingDef>(),
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: new MechanicDef(MechanicKind.Lifesteal, MaxShield: 0, RegenPerTick: healPerHit, RegenDelayTicks: 0));

    private static Weapon TestWeapon(int damage = 10, int cooldown = 1000) =>
        new() { Damage = damage, Range = Fix.FromInt(2), CooldownTicks = cooldown };

    // Spawn a Lifesteal attacker (player 0) adjacent to an enemy unit (player 1), cooldown ready.
    private static (SimWorld w, int atk, int enemy) Setup(int healPerHit, int atkHp, int enemyHp)
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: LifestealFaction(healPerHit));
        var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), atkHp, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), enemyHp);
        var a = w.GetUnit(atk)!;
        a.AttackTargetId = enemy;       // target the adjacent enemy directly
        a.Weapon!.CooldownRemaining = 0; // ready to fire this tick
        return (w, atk, enemy);
    }

    [Fact]
    public void Lifesteal_Heals_By_RegenPerTick_On_Landed_Hit()
    {
        var (w, atk, enemy) = Setup(healPerHit: 3, atkHp: 50, enemyHp: 100);
        var a = w.GetUnit(atk)!;
        a.Hp = a.MaxHp - 5; // 45, damaged below full

        int enemyBefore = w.GetUnit(enemy)!.Hp;
        w.Step(System.Array.Empty<Command>()); // attacker fires this tick

        Assert.True(w.GetUnit(enemy)!.Hp < enemyBefore, "attacker must have landed a hit");
        Assert.Equal(a.MaxHp - 5 + 3, w.GetUnit(atk)!.Hp); // healed by RegenPerTick (45 -> 48)
    }

    [Fact]
    public void Lifesteal_Does_Not_Overheal_Past_MaxHp()
    {
        var (w, atk, enemy) = Setup(healPerHit: 3, atkHp: 50, enemyHp: 100);
        var a = w.GetUnit(atk)!;
        a.Hp = a.MaxHp - 1; // 49, one below cap

        w.Step(System.Array.Empty<Command>()); // fires, heal would push to 52 but caps at 50

        Assert.True(w.GetUnit(enemy)!.Hp < 100, "attacker must have landed a hit");
        Assert.Equal(a.MaxHp, w.GetUnit(atk)!.Hp); // capped exactly at MaxHp
    }

    [Fact]
    public void Lifesteal_Does_Not_Proc_Without_A_Hit()
    {
        // No cooldown reset / no target => no fire => no heal.
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: LifestealFaction(healPerHit: 3));
        var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var a = w.GetUnit(atk)!;
        a.Hp = a.MaxHp - 5; // 45, damaged but no enemy to hit

        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(a.MaxHp - 5, w.GetUnit(atk)!.Hp); // no hit landed => no heal
    }

    [Fact]
    public void Non_Lifesteal_Unit_Does_Not_Heal_When_Attacking()
    {
        // Reference faction with no mechanic: attacking deals damage but never heals the attacker.
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 100);
        var a = w.GetUnit(atk)!;
        a.Hp = a.MaxHp - 5; // 45
        a.AttackTargetId = enemy;
        a.Weapon!.CooldownRemaining = 0;

        int enemyBefore = w.GetUnit(enemy)!.Hp;
        w.Step(System.Array.Empty<Command>());

        Assert.True(w.GetUnit(enemy)!.Hp < enemyBefore, "attacker must have landed a hit");
        Assert.Equal(a.MaxHp - 5, w.GetUnit(atk)!.Hp); // no mechanic => Hp unchanged by attacking
    }

    [Fact]
    public void Lifesteal_Procs_When_Attacking_A_Building()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: LifestealFaction(healPerHit: 3));
        // Enemy building (player 1) adjacent to the attacker; full Hp so it survives the hit.
        int hutId = w.AddCompletedBuilding(1, TestFactions.HutSpec, cellX: 6, cellY: 5, defId: "hut");
        var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon(damage: 5));
        var a = w.GetUnit(atk)!;
        a.Hp = a.MaxHp - 5; // 45
        a.AttackTargetId = hutId;
        a.Weapon!.CooldownRemaining = 0;

        int hutBefore = w.GetBuilding(hutId)!.Hp;
        w.Step(System.Array.Empty<Command>());

        Assert.True(w.GetBuilding(hutId)!.Hp < hutBefore, "attacker must have hit the building");
        Assert.Equal(a.MaxHp - 5 + 3, w.GetUnit(atk)!.Hp); // lifesteal procs on building hits too
    }
}
