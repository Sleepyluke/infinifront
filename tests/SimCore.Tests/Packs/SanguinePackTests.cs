using System.IO;
using SimCore.Packs;
using SimCore.Sim;
using Xunit;
using Xunit.Abstractions;

namespace SimCore.Tests.Packs;

public class SanguinePackTests
{
    private readonly ITestOutputHelper _out;
    public SanguinePackTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Sanguine_Pack_Loads_And_Validates_Without_Errors()
    {
        var json = File.ReadAllText(RepoPaths.Pack("sanguine/faction.json"));
        var report = FactionPackLoader.LoadAndValidate(json);

        foreach (var e in report.LoadErrors) _out.WriteLine("LOAD ERROR: " + e);
        foreach (var f in report.Findings) _out.WriteLine($"{f.Severity} {f.Code} [{f.TargetId}] {f.Message}");

        Assert.Empty(report.LoadErrors);
        Assert.NotNull(report.Faction);
        Assert.DoesNotContain(report.Findings, f => f.Severity == Severity.Error);

        // Sanguine-specific: the new Lifesteal mechanic round-trips through pack load.
        Assert.NotNull(report.Faction!.Mechanic);
        Assert.Equal(MechanicKind.Lifesteal, report.Faction!.Mechanic!.Kind);
    }
}
