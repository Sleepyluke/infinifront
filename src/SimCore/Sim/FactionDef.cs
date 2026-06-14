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
}

public sealed record UnitDef(
    string Id, int Tier, string ProducedBy, IReadOnlyList<string> Requires, UnitSpec Spec);

public sealed record BuildingDef(
    string Id, int Tier, IReadOnlyList<string> Requires, BuildingSpec Spec);
