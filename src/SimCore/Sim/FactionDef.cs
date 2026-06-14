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

    private readonly Dictionary<string, UnitDef> _units = new();
    private readonly Dictionary<string, BuildingDef> _buildings = new();

    public FactionDef(string id, string name, IEnumerable<UnitDef> units, IEnumerable<BuildingDef> buildings)
    {
        Id = id;
        Name = name;
        var ul = new List<UnitDef>();
        foreach (var u in units) { ul.Add(u); _units[u.Id] = u; }
        var bl = new List<BuildingDef>();
        foreach (var b in buildings) { bl.Add(b); _buildings[b.Id] = b; }
        UnitList = ul;
        BuildingList = bl;
    }

    public UnitDef? GetUnit(string id) => _units.TryGetValue(id, out var u) ? u : null;
    public BuildingDef? GetBuilding(string id) => _buildings.TryGetValue(id, out var b) ? b : null;

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
                if (GetBuilding(req) is null)
                    errors.Add($"unit '{u.Id}' requires unknown building '{req}'");
        }

        foreach (var b in BuildingList)
            foreach (var req in b.Requires)
                if (GetBuilding(req) is null)
                    errors.Add($"building '{b.Id}' requires unknown building '{req}'");

        // Cycle detection over the building-prerequisite graph (DFS, three-color).
        var state = new Dictionary<string, int>(); // 0=unvisited,1=in-stack,2=done
        bool HasCycle(string id)
        {
            if (state.TryGetValue(id, out var s))
            {
                if (s == 1) return true;
                if (s == 2) return false;
            }
            state[id] = 1;
            var b = GetBuilding(id);
            if (b is not null)
                foreach (var req in b.Requires)
                    if (GetBuilding(req) is not null && HasCycle(req))
                        return true;
            state[id] = 2;
            return false;
        }
        foreach (var b in BuildingList)
            if (state.GetValueOrDefault(b.Id) == 0 && HasCycle(b.Id))
            {
                errors.Add($"building prerequisite cycle detected involving '{b.Id}'");
                break; // one cycle report is enough
            }

        return errors;
    }
}

public sealed record UnitDef(
    string Id, int Tier, string ProducedBy, IReadOnlyList<string> Requires, UnitSpec Spec);

public sealed record BuildingDef(
    string Id, int Tier, IReadOnlyList<string> Requires, BuildingSpec Spec);
