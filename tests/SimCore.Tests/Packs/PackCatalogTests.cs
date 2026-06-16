using System.IO;
using System.Linq;
using SimCore.Packs;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests.Packs;

public class PackCatalogTests
{
    [Fact]
    public void Always_Includes_The_Reference_Faction_First()
    {
        var entries = PackCatalog.Load(RepoPaths.Pack("")); // the repo packs/ dir
        Assert.NotEmpty(entries);
        Assert.Equal("reference", entries[0].Faction.Id);
        // The on-disk packs/reference dedups against the in-code reference (no duplicate id).
        Assert.Single(entries, e => e.Faction.Id == "reference");
    }

    [Fact]
    public void Missing_Directory_Yields_Only_Reference()
    {
        var entries = PackCatalog.Load(Path.Combine(Path.GetTempPath(), "nope-" + System.Guid.NewGuid().ToString("N")));
        Assert.Single(entries);
        Assert.Equal("reference", entries[0].Faction.Id);
    }

    [Fact]
    public void Loads_A_Distinct_Valid_Pack_And_Skips_A_Malformed_One()
    {
        var dir = Path.Combine(Path.GetTempPath(), "packcat-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "alpha"));
        Directory.CreateDirectory(Path.Combine(dir, "broken"));
        try
        {
            // A valid distinct faction (id "alpha").
            var alpha = new FactionDef("alpha", "Alpha",
                new[] { new UnitDef("w", 1, "hq", System.Array.Empty<string>(),
                    new UnitSpec(10, SimCore.Math.Fix.One, 1, 1, 1, Harvester: new HarvesterSpec(1, 1))) },
                new[] { new BuildingDef("hq", 1, System.Array.Empty<string>(),
                    new BuildingSpec(10, 1, 1, 1, 1, IsDepot: true, CanTrain: true)) });
            File.WriteAllText(Path.Combine(dir, "alpha", "faction.json"), FactionPackLoader.ToJson(alpha));
            File.WriteAllText(Path.Combine(dir, "broken", "faction.json"), "{ not valid json ");

            var entries = PackCatalog.Load(dir);
            Assert.Contains(entries, e => e.Faction.Id == "alpha");
            Assert.DoesNotContain(entries, e => e.Faction.Id == "broken");
            Assert.Equal("reference", entries[0].Faction.Id); // reference still first
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ResolvePacksDir_Finds_Packs_In_An_Ancestor()
    {
        // Mimic the runtime: start deep inside a fake bin output dir; packs/ lives at the "repo root".
        var root = Path.Combine(Path.GetTempPath(), "resolve-" + System.Guid.NewGuid().ToString("N"));
        var deep = Path.Combine(root, "godot", ".godot", "mono", "temp", "bin", "Debug");
        Directory.CreateDirectory(deep);
        Directory.CreateDirectory(Path.Combine(root, "packs"));
        try
        {
            var found = PackCatalog.ResolvePacksDir(deep);
            Assert.Equal(Path.Combine(root, "packs"), found);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ResolvePacksDir_Finds_Packs_Directly_In_Start_Dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "resolve-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "packs"));
        try
        {
            Assert.Equal(Path.Combine(root, "packs"), PackCatalog.ResolvePacksDir(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ResolvePacksDir_Without_A_Packs_Dir_Does_Not_Find_The_Test_Tree()
    {
        // Walk-up eventually reaches the drive root, so we can't assert a hard null without
        // assuming the machine has no stray packs/ anywhere above temp. Instead assert the
        // resolver never points *into our isolated tree* when it contains no packs/ dir.
        var root = Path.Combine(Path.GetTempPath(), "resolve-" + System.Guid.NewGuid().ToString("N"));
        var deep = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(deep);
        try
        {
            var found = PackCatalog.ResolvePacksDir(deep);
            Assert.True(found is null || !found.StartsWith(root, System.StringComparison.Ordinal),
                $"resolver should not find a packs/ inside the packs-free test tree, got '{found}'");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
