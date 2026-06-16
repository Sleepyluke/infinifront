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
    /// <summary>Finds the packs directory by walking up from <paramref name="startDir"/>: returns the
    /// nearest ancestor (including startDir itself) that contains a "packs" subdirectory, or null if
    /// none. Lets the Godot runtime locate the repo's packs/ from its binary output dir
    /// (godot/.godot/mono/temp/bin/Debug/…) — or, in a shipped layout, packs/ sitting beside the exe.</summary>
    public static string? ResolvePacksDir(string startDir)
    {
        var dir = string.IsNullOrEmpty(startDir) ? null : new System.IO.DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "packs");
            if (System.IO.Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Convenience: resolve the packs dir from a runtime start directory (walk-up) and load.
    /// Falls back to the reference-only list when no packs/ directory is found anywhere above.</summary>
    public static IReadOnlyList<FactionEntry> LoadAuto(string startDir)
        => Load(ResolvePacksDir(startDir) ?? "");

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
