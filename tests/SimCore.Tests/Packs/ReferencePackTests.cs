using System;
using System.IO;
using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class ReferencePackTests
{
    private static string PackPath => RepoPaths.Pack("reference/faction.json");

    [Fact]
    public void Reference_pack_file_is_in_sync_with_serialization()
    {
        string expected = FactionPackLoader.ToJson(ReferenceFaction.Def);

        if (Environment.GetEnvironmentVariable("UPDATE_PACKS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PackPath)!);
            File.WriteAllText(PackPath, expected);
        }

        Assert.True(File.Exists(PackPath),
            $"missing {PackPath}; regenerate by running this test with UPDATE_PACKS=1");
        Assert.Equal(RepoPaths.Normalize(expected), RepoPaths.Normalize(File.ReadAllText(PackPath)));
    }

    [Fact]
    public void Reference_pack_file_loads_deep_equal_to_in_code_def()
    {
        Assert.True(File.Exists(PackPath),
            $"missing {PackPath}; regenerate by running ReferencePackTests with UPDATE_PACKS=1");
        var result = FactionPackLoader.LoadFromJson(File.ReadAllText(PackPath));
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Faction);
        FactionDefAssert.DeepEqual(ReferenceFaction.Def, result.Faction!);
    }
}
