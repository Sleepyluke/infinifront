using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ShieldDamageTests
{
    private static FactionDef ShieldFaction(int max) => new("f", "F",
        units: System.Array.Empty<UnitDef>(),
        buildings: System.Array.Empty<BuildingDef>(),
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: new MechanicDef(MechanicKind.RegeneratingShields, max, 1, 5));

    private static (SimWorld w, int atk, int vic) Setup(int shieldMax, int vicHp)
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: ShieldFaction(shieldMax));
        var weapon = new Weapon { Damage = 10, Range = Fix.FromInt(2), CooldownTicks = 1000 };
        var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, weapon);
        var vic = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), vicHp);
        return (w, atk, vic);
    }

    [Fact]
    public void Damage_Below_Shield_Only_Reduces_Shield()
    {
        var (w, atk, vic) = Setup(shieldMax: 25, vicHp: 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { atk }, vic) });
        var v = w.GetUnit(vic)!;
        Assert.Equal(15, v.ShieldHp);
        Assert.Equal(50, v.Hp);
        // After damage, TicksSinceDamaged is transitioned 0->1 by UpdateShields (reset happened in UpdateCombat,
        // then UpdateShields restarted the countdown). This is correct: the damage timer starts fresh.
        Assert.Equal(1, v.TicksSinceDamaged);
    }

    [Fact]
    public void Damage_Above_Shield_Spills_To_Hp()
    {
        var (w, atk, vic) = Setup(shieldMax: 4, vicHp: 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { atk }, vic) });
        var v = w.GetUnit(vic)!;
        Assert.Equal(0, v.ShieldHp);
        Assert.Equal(44, v.Hp);
    }

    [Fact]
    public void Non_Shield_Faction_Damage_Goes_Straight_To_Hp()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        var weapon = new Weapon { Damage = 10, Range = Fix.FromInt(2), CooldownTicks = 1000 };
        var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, weapon);
        var vic = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { atk }, vic) });
        Assert.Equal(40, w.GetUnit(vic)!.Hp);
        Assert.Equal(0, w.GetUnit(vic)!.ShieldHp);
    }
}
