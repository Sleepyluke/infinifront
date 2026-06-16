using System.Collections.Generic;

namespace SimCore.Sim;

/// <summary>Engine source of truth for a faction: a named catalog of unit/building defs.
/// Immutable. Plan 3d deserializes faction-pack JSON into this; today ReferenceFaction is
/// the only instance. Dictionaries are lookup-only; ordered enumeration uses the *List
/// properties (deterministic). Never iterate the dictionaries in sim logic.</summary>
public sealed class FactionDef
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<UnitDef> UnitList { get; }
    public IReadOnlyList<BuildingDef> BuildingList { get; }
    public IReadOnlyList<UpgradeDef> UpgradeList { get; }
    public MechanicDef? Mechanic { get; }

    private readonly Dictionary<string, UnitDef> _units = new();
    private readonly Dictionary<string, BuildingDef> _buildings = new();
    private readonly Dictionary<string, UpgradeDef> _upgrades = new();

    public FactionDef(string id, string name, IEnumerable<UnitDef> units, IEnumerable<BuildingDef> buildings)
        : this(id, name, units, buildings, System.Array.Empty<UpgradeDef>()) { }

    public FactionDef(string id, string name, IEnumerable<UnitDef> units,
        IEnumerable<BuildingDef> buildings, IEnumerable<UpgradeDef> upgrades, MechanicDef? mechanic = null)
    {
        Id = id;
        Name = name;
        var ul = new List<UnitDef>();
        foreach (var u in units) { ul.Add(u); _units[u.Id] = u; }
        var bl = new List<BuildingDef>();
        foreach (var b in buildings) { bl.Add(b); _buildings[b.Id] = b; }
        var gl = new List<UpgradeDef>();
        foreach (var g in upgrades) { gl.Add(g); _upgrades[g.Id] = g; }
        UnitList = ul;
        BuildingList = bl;
        UpgradeList = gl;
        Mechanic = mechanic;
    }

    public UnitDef? GetUnit(string id) => _units.TryGetValue(id, out var u) ? u : null;
    public BuildingDef? GetBuilding(string id) => _buildings.TryGetValue(id, out var b) ? b : null;
    public UpgradeDef? GetUpgrade(string id) => _upgrades.TryGetValue(id, out var g) ? g : null;

    /// <summary>Referential integrity + cycle detection. Returns human-readable errors
    /// (empty = valid). Seed of the plan-3d pack validator; budget/structural rules are NOT here.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        foreach (var u in UnitList)
        {
            if (string.IsNullOrEmpty(u.ProducedBy))
                errors.Add($"unit '{u.Id}' has no producer (ProducedBy is empty)");
            else if (GetBuilding(u.ProducedBy) is null)
                errors.Add($"unit '{u.Id}' ProducedBy references unknown building '{u.ProducedBy}'");
            foreach (var req in u.Requires)
                if (GetBuilding(req) is null && GetUpgrade(req) is null)
                    errors.Add($"unit '{u.Id}' requires unknown building/upgrade '{req}'");
        }

        foreach (var b in BuildingList)
            foreach (var req in b.Requires)
                if (GetBuilding(req) is null && GetUpgrade(req) is null)
                    errors.Add($"building '{b.Id}' requires unknown building/upgrade '{req}'");

        foreach (var up in UpgradeList)
        {
            if (GetBuilding(up.ResearchedAt) is null)
                errors.Add($"upgrade '{up.Id}' ResearchedAt references unknown building '{up.ResearchedAt}'");
            foreach (var req in up.Requires)
                if (GetBuilding(req) is null && GetUpgrade(req) is null)
                    errors.Add($"upgrade '{up.Id}' requires unknown building/upgrade '{req}'");
            foreach (var t in up.TargetUnitDefIds)
                if (t != "*" && GetUnit(t) is null)
                    errors.Add($"upgrade '{up.Id}' targets unknown unit '{t}'");
        }

        // Combined prerequisite cycle detection over buildings + upgrades.
        var state = new Dictionary<string, int>(); // 0=unvisited,1=in-stack,2=done
        IReadOnlyList<string> RequiresOf(string id)
        {
            var b = GetBuilding(id);
            if (b is not null) return b.Requires;
            var up = GetUpgrade(id);
            if (up is not null) return up.Requires;
            return System.Array.Empty<string>();
        }
        bool Resolves(string id) => GetBuilding(id) is not null || GetUpgrade(id) is not null;
        bool HasCycle(string id)
        {
            if (state.TryGetValue(id, out var s)) { if (s == 1) return true; if (s == 2) return false; }
            state[id] = 1;
            foreach (var req in RequiresOf(id))
                if (Resolves(req) && HasCycle(req)) return true;
            state[id] = 2;
            return false;
        }
        bool cycleFound = false;
        foreach (var b in BuildingList)
            if (state.GetValueOrDefault(b.Id) == 0 && HasCycle(b.Id)) { cycleFound = true; break; }
        if (!cycleFound)
            foreach (var up in UpgradeList)
                if (state.GetValueOrDefault(up.Id) == 0 && HasCycle(up.Id)) { cycleFound = true; break; }
        if (cycleFound)
            errors.Add("prerequisite cycle detected");

        if (Mechanic is { } m)
        {
            if (m.Kind == MechanicKind.None && (m.MaxShield != 0 || m.RegenPerTick != 0 || m.RegenDelayTicks != 0))
                errors.Add("mechanic kind is None but has nonzero params");
            if (m.MaxShield < 0 || m.RegenPerTick < 0 || m.RegenDelayTicks < 0)
                errors.Add($"mechanic has negative params (shield {m.MaxShield}, regen {m.RegenPerTick}, delay {m.RegenDelayTicks})");
        }

        return errors;
    }
}

public sealed record UnitDef(
    string Id, int Tier, string ProducedBy, IReadOnlyList<string> Requires, UnitSpec Spec);

public sealed record BuildingDef(
    string Id, int Tier, IReadOnlyList<string> Requires, BuildingSpec Spec);

public enum UpgradeStat { Damage, Range, CooldownTicks, Speed, Sight }

public sealed record UpgradeDef(
    string Id, int Tier, string ResearchedAt, IReadOnlyList<string> Requires,
    IReadOnlyList<string> TargetUnitDefIds, UpgradeStat Stat, SimCore.Math.Fix Delta,
    int MineralCost, int ResearchTicks);

public enum MechanicKind { None = 0, RegeneratingShields = 1, Regeneration = 2 }

public sealed record MechanicDef(MechanicKind Kind, int MaxShield, int RegenPerTick, int RegenDelayTicks);
