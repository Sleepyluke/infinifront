using System.Collections.Generic;
using SimCore.Sim;

namespace SimCore.Packs;

/// <summary>An available faction for match setup: a display name + the loaded def.</summary>
public sealed record FactionEntry(string Name, FactionDef Faction);

/// <summary>Lists the factions available to play: the in-code reference faction first, then every
/// valid pack found under a directory (each <dir>/<name>/faction.json, via FactionPackLoader).
/// Deterministic order; invalid/duplicate-id packs are skipped; never throws.</summary>
public static class PackCatalog
{
    public static IReadOnlyList<FactionEntry> Load(string packsDir)
    {
        var list = new List<FactionEntry> { new("Reference", ReferenceFaction.Def) };
        var seenIds = new HashSet<string>(System.StringComparer.Ordinal) { ReferenceFaction.Def.Id };

        if (!System.IO.Directory.Exists(packsDir)) return list;

        var dirs = System.IO.Directory.GetDirectories(packsDir);
        System.Array.Sort(dirs, System.StringComparer.Ordinal); // deterministic order
        foreach (var dir in dirs)
        {
            var jsonPath = System.IO.Path.Combine(dir, "faction.json");
            if (!System.IO.File.Exists(jsonPath)) continue;
            string text;
            try { text = System.IO.File.ReadAllText(jsonPath); }
            catch { continue; }
            var result = FactionPackLoader.LoadFromJson(text);
            if (result.Faction is null || result.Errors.Count > 0) continue;
            if (!seenIds.Add(result.Faction.Id)) continue; // skip duplicate id (e.g. the on-disk reference)
            list.Add(new FactionEntry(result.Faction.Name, result.Faction));
        }
        return list;
    }
}
