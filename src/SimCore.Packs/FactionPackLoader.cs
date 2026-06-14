using System;
using System.Collections.Generic;
using System.Text.Json;
using SimCore.Sim;

namespace SimCore.Packs;

/// <summary>Result of loading a pack: the faction (null only on a hard parse/map failure)
/// plus any errors. A structurally-parseable-but-invalid pack returns the faction AND its
/// Validate() errors, so a fix-it loop (3d-2) can surface them.</summary>
public sealed record PackLoadResult(FactionDef? Faction, IReadOnlyList<string> Errors);

/// <summary>Loads/saves faction packs as JSON. The only entry points the engine/UI should use.</summary>
public static class FactionPackLoader
{
    public static PackLoadResult LoadFromJson(string json)
    {
        FactionPackDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FactionPackDto>(json, PackJson.Options);
        }
        catch (JsonException ex)
        {
            return new PackLoadResult(null, new[] { $"JSON parse error: {ex.Message}" });
        }

        if (dto is null)
            return new PackLoadResult(null, new[] { "pack JSON deserialized to null" });

        FactionDef def;
        try
        {
            def = PackMapper.ToFactionDef(dto);
        }
        catch (Exception ex)
        {
            return new PackLoadResult(null, new[] { $"pack mapping error: {ex.Message}" });
        }

        var errors = def.Validate();
        return new PackLoadResult(def, errors);
    }

    public static string ToJson(FactionDef faction) =>
        JsonSerializer.Serialize(PackMapper.ToDto(faction), PackJson.Options);
}
