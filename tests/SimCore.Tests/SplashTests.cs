using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class SplashTests
{
    private static FactionDef SplashFaction() => new("sf", "SF",
        units: System.Array.Empty<UnitDef>(),
        buildings: System.Array.Empty<BuildingDef>(),
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: new MechanicDef(MechanicKind.Splash, MaxShield: 0, RegenPerTick: 0, RegenDelayTicks: 0));

    private static Weapon TestWeapon(int damage = 10, int cooldown = 1000) =>
        new() { Damage = damage, Range = Fix.FromInt(2), CooldownTicks = cooldown };

    // CellCenter cells are 1 world unit apart; SplashRadius is 2 world units.
    // Attacker (player 0) at (5,5) hits primary enemy (player 1) at (6,5), distance 1 (in Range 2).

    [Fact]
    public void Splash_Damages_Second_Enemy_Within_Radius_Half_Of_Primary()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: SplashFaction());
        int atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 100, TestWeapon(damage: 10));
        int primary = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 100);
        // Second enemy clustered next to the primary: distance 1 from primary center <= SplashRadius (2).
        int splashed = w.SpawnUnit(1, w.Map.CellCenter(7, 5), Fix.FromFraction(1, 2), 100);
        var a = w.GetUnit(atk)!;
        a.AttackTargetId = primary;
        a.Weapon!.CooldownRemaining = 0;

        w.Step(System.Array.Empty<Command>());

        Assert.Equal(90, w.GetUnit(primary)!.Hp);  // primary takes FULL dmg (10), not doubled
        Assert.Equal(95, w.GetUnit(splashed)!.Hp);  // second enemy takes floor(10/2) = 5
    }

    [Fact]
    public void Splash_Does_Not_Hit_Enemy_Outside_Radius()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: SplashFaction());
        int atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 100, TestWeapon(damage: 10));
        int primary = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 100);
        // Far enemy: distance 4 from primary center (6,5)->(6,9) > SplashRadius (2).
        int far = w.SpawnUnit(1, w.Map.CellCenter(6, 9), Fix.FromFraction(1, 2), 100);
        var a = w.GetUnit(atk)!;
        a.AttackTargetId = primary;
        a.Weapon!.CooldownRemaining = 0;

        w.Step(System.Array.Empty<Command>());

        Assert.Equal(90, w.GetUnit(primary)!.Hp);  // primary hit
        Assert.Equal(100, w.GetUnit(far)!.Hp);     // far enemy untouched by splash
    }

    [Fact]
    public void Splash_Is_Ally_Immune()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: SplashFaction());
        int atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 100, TestWeapon(damage: 10));
        int primary = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 100);
        // Ally of the attacker (player 0 = same team) standing next to the primary target.
        int ally = w.SpawnUnit(0, w.Map.CellCenter(7, 5), Fix.FromFraction(1, 2), 100);
        var a = w.GetUnit(atk)!;
        a.AttackTargetId = primary;
        a.Weapon!.CooldownRemaining = 0;

        w.Step(System.Array.Empty<Command>());

        Assert.Equal(90, w.GetUnit(primary)!.Hp);  // primary hit
        Assert.Equal(100, w.GetUnit(ally)!.Hp);    // ally near the target takes no splash
    }

    [Fact]
    public void Non_Splash_Faction_Deals_No_Splash()
    {
        // Player 0 has the no-mechanic reference faction: only the primary takes damage.
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        int atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 100, TestWeapon(damage: 10));
        int primary = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 100);
        int neighbor = w.SpawnUnit(1, w.Map.CellCenter(7, 5), Fix.FromFraction(1, 2), 100);
        var a = w.GetUnit(atk)!;
        a.AttackTargetId = primary;
        a.Weapon!.CooldownRemaining = 0;

        w.Step(System.Array.Empty<Command>());

        Assert.Equal(90, w.GetUnit(primary)!.Hp);    // primary takes full dmg
        Assert.Equal(100, w.GetUnit(neighbor)!.Hp);  // no splash mechanic => neighbor untouched
    }
}
