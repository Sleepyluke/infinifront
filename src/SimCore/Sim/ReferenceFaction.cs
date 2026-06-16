namespace SimCore.Sim;

/// <summary>The hand-built reference faction as a FactionDef, wrapping ReferenceSpecs' stat
/// payloads with a starter tech tree. Plan 3d will make this the first data-driven pack.</summary>
public static class ReferenceFaction
{
    private static readonly string[] None = System.Array.Empty<string>();

    public static readonly FactionDef Def = new(
        id: "reference",
        name: "Reference",
        units: new[]
        {
            new UnitDef("fabber",   1, "depot",    None,                ReferenceSpecs.Fabber),
            new UnitDef("trooper",  1, "barracks", None,                ReferenceSpecs.Trooper),
            new UnitDef("outrider", 1, "barracks", None,                ReferenceSpecs.Outrider),
            new UnitDef("tank",     2, "barracks", new[] { "depot" },   ReferenceSpecs.Tank),
        },
        buildings: new[]
        {
            new BuildingDef("depot",    1, None, ReferenceSpecs.Depot),
            new BuildingDef("barracks", 1, None, ReferenceSpecs.Barracks),
            new BuildingDef("supply",   1, None, ReferenceSpecs.SupplySilo),
        });
}
