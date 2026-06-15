using System;
using System.Collections.Generic;
using System.Text.Json;
using SimCore.Sim;

namespace SimCore.Packs;

/// <summary>Result of loading a pack: the faction (null only on a hard parse/map failure)
/// plus any errors. A structurally-parseable-but-invalid pack returns the faction AND its
/// Validate() errors, so a fix-it loop (3d-2) can surface them.</summary>
public sealed record PackLoadResult(FactionDef? Faction, IReadOnlyList<string> Errors);

/// <summary>Load + full author-facing validation in one call. LoadErrors are the
/// structural/parse errors from LoadFromJson; Findings are the PackValidator report
/// (empty when the load hard-failed, i.e. Faction is null).</summary>
public sealed record PackReport(
    FactionDef? Faction,
    IReadOnlyList<string> LoadErrors,
    IReadOnlyList<ValidationFinding> Findings);

/// <summary>Loads/saves faction packs as JSON. The only entry points the engine/UI should use.</summary>
public static class FactionPackLoader
{
    public static PackLoadResult LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new PackLoadResult(null, new[] { "pack JSON was null or empty" });

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

        IReadOnlyList<string> errors;
        try
        {
            errors = def.Validate();
        }
        catch (Exception ex)
        {
            // A pack can parse + map yet still contain nulls inside inner lists that make
            // Validate()'s dictionary lookups throw. Never let that escape the loader.
            return new PackLoadResult(null, new[] { $"pack validation error: {ex.Message}" });
        }

        return new PackLoadResult(def, errors);
    }

    public static string ToJson(FactionDef faction) =>
        JsonSerializer.Serialize(PackMapper.ToDto(faction), PackJson.Options);

    public static PackReport LoadAndValidate(string json, BudgetWeights? weights = null)
    {
        var result = LoadFromJson(json);
        var findings = result.Faction is null
            ? (IReadOnlyList<ValidationFinding>)System.Array.Empty<ValidationFinding>()
            : PackValidator.Validate(result.Faction, weights);
        return new PackReport(result.Faction, result.Errors, findings);
    }
}
