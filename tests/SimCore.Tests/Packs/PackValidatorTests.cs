using System.Collections.Generic;
using System.Linq;
using SimCore.Math;
using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class PackValidatorTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    private static UnitDef Unit(string id, string producedBy, IReadOnlyList<string>? requires = null,
        int tier = 1, UnitSpec? spec = null) =>
        new(id, tier, producedBy, requires ?? None, spec ?? new UnitSpec(50, Fix.One, 50, 1, 50));

    private static BuildingDef Bld(string id, IReadOnlyList<string>? requires = null, int tier = 1) =>
        new(id, tier, requires ?? None, new BuildingSpec(100, 2, 2, 100, 100));

    private static bool Has(IReadOnlyList<ValidationFinding> fs, string code, string? target = null) =>
        fs.Any(f => f.Code == code && (target == null || f.TargetId == target));

    [Fact]
    public void Reference_faction_has_no_errors()
    {
        var findings = PackValidator.Validate(ReferenceFaction.Def);
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void No_buildings_is_an_error()
    {
        var f = new FactionDef("x", "X", new[] { Unit("u", "ghost") }, System.Array.Empty<BuildingDef>());
        Assert.True(Has(PackValidator.Validate(f), "NO_BUILDINGS"));
    }

    [Fact]
    public void No_units_is_an_error()
    {
        var f = new FactionDef("x", "X", System.Array.Empty<UnitDef>(), new[] { Bld("hq") });
        Assert.True(Has(PackValidator.Validate(f), "NO_UNITS"));
    }

    [Fact]
    public void Blank_unit_id_is_an_error()
    {
        var f = new FactionDef("x", "X", new[] { Unit("  ", "hq") }, new[] { Bld("hq") });
        Assert.True(Has(PackValidator.Validate(f), "ID_BLANK"));
    }

    [Fact]
    public void Unit_with_unreachable_producer_is_flagged()
    {
        // hq is reachable; locked requires missing-building so tank's producer chain is dead.
        var f = new FactionDef("x", "X",
            new[] { Unit("worker", "hq"), Unit("tank", "factory") },
            new[] { Bld("hq"), Bld("factory", new[] { "ghost" }) }); // factory needs a building that doesn't exist
        // NOTE: FactionDef.Validate would flag the dangling 'ghost' ref, but PackValidator
        // is about reachability: factory is unreachable, so tank is unbuildable.
        var findings = PackValidator.Validate(f);
        Assert.True(Has(findings, "PRODUCER_UNREACHABLE", "tank"));
        Assert.False(Has(findings, "PRODUCER_UNREACHABLE", "worker"));
    }

    [Fact]
    public void No_buildable_unit_is_an_error()
    {
        // The only unit is produced by an unreachable building => nothing can be built at start.
        var f = new FactionDef("x", "X",
            new[] { Unit("tank", "factory") },
            new[] { Bld("hq"), Bld("factory", new[] { "ghost" }) });
        Assert.True(Has(PackValidator.Validate(f), "NO_SEED_UNIT"));
    }

    [Fact]
    public void Unreachable_upgrade_is_flagged()
    {
        var up = new UpgradeDef("dmg", 1, "lab", None, new[] { "worker" },
            UpgradeStat.Damage, Fix.FromInt(1), 50, 50);
        var f = new FactionDef("x", "X",
            new[] { Unit("worker", "hq") },
            new[] { Bld("hq"), Bld("lab", new[] { "ghost" }) },
            new[] { up });
        Assert.True(Has(PackValidator.Validate(f), "UPGRADE_UNREACHABLE", "dmg"));
    }

    [Fact]
    public void Reachable_chain_is_not_flagged()
    {
        // hq -> factory(requires hq) -> tank(producedBy factory). All reachable.
        var f = new FactionDef("x", "X",
            new[] { Unit("worker", "hq"), Unit("tank", "factory", new[] { "factory" }) },
            new[] { Bld("hq"), Bld("factory", new[] { "hq" }) });
        var findings = PackValidator.Validate(f);
        Assert.False(Has(findings, "PRODUCER_UNREACHABLE"));
        Assert.False(Has(findings, "NO_SEED_UNIT"));
    }

    [Fact]
    public void Multi_level_upgrade_chain_is_reachable()
    {
        var baseUp = new UpgradeDef("base", 1, "lab", None, new[] { "worker" }, UpgradeStat.Damage, Fix.FromInt(1), 50, 50);
        var advUp  = new UpgradeDef("adv", 2, "lab", new[] { "base" }, new[] { "worker" }, UpgradeStat.Damage, Fix.FromInt(1), 50, 50);
        var f = new FactionDef("x", "X",
            new[] { Unit("worker", "hq") },
            new[] { Bld("hq"), Bld("lab", new[] { "hq" }) },
            new[] { baseUp, advUp });
        Assert.False(Has(PackValidator.Validate(f), "UPGRADE_UNREACHABLE")); // base and adv both reachable
    }

    [Fact]
    public void Upgrade_requiring_unreachable_upgrade_is_flagged()
    {
        var baseUp = new UpgradeDef("base", 1, "lab", None, new[] { "worker" }, UpgradeStat.Damage, Fix.FromInt(1), 50, 50);
        var advUp  = new UpgradeDef("adv", 2, "lab", new[] { "base" }, new[] { "worker" }, UpgradeStat.Damage, Fix.FromInt(1), 50, 50);
        var f = new FactionDef("x", "X",
            new[] { Unit("worker", "hq") },
            new[] { Bld("hq"), Bld("lab", new[] { "ghost" }) }, // lab unreachable -> base & adv unreachable (transitive)
            new[] { baseUp, advUp });
        var findings = PackValidator.Validate(f);
        Assert.True(Has(findings, "UPGRADE_UNREACHABLE", "base"));
        Assert.True(Has(findings, "UPGRADE_UNREACHABLE", "adv"));
    }

    [Fact]
    public void Unit_reachable_via_upgrade_requirement_is_not_flagged()
    {
        // tank's producer (hq) is reachable AND its required upgrade (armor, at reachable lab) is reachable.
        var armor = new UpgradeDef("armor", 1, "lab", None, new[] { "tank" }, UpgradeStat.Damage, Fix.FromInt(1), 50, 50);
        var f = new FactionDef("x", "X",
            new[] { Unit("worker", "hq"), Unit("tank", "hq", new[] { "armor" }) },
            new[] { Bld("hq"), Bld("lab", new[] { "hq" }) },
            new[] { armor });
        Assert.False(Has(PackValidator.Validate(f), "PRODUCER_UNREACHABLE", "tank")); // reachable via reachable upgrade
    }

    [Fact]
    public void Tier1_unit_requiring_tier3_building_warns()
    {
        var f = new FactionDef("x", "X",
            new[] { Unit("grunt", "hq", new[] { "citadel" }, tier: 1) },
            new[] { Bld("hq"), Bld("citadel", new[] { "hq" }, tier: 3) });
        var findings = PackValidator.Validate(f);
        Assert.True(Has(findings, "TIER_NONMONOTONIC", "grunt"));
        Assert.Equal(Severity.Warning, findings.First(x => x.Code == "TIER_NONMONOTONIC").Severity);
    }

    [Fact]
    public void Reference_faction_has_no_tier_warnings()
    {
        var findings = PackValidator.Validate(ReferenceFaction.Def);
        Assert.False(Has(findings, "TIER_NONMONOTONIC"));
    }
}
