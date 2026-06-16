using System.IO;
using SimCore.Packs;
using SimCore.Sim;
using Xunit;
using Xunit.Abstractions;

namespace SimCore.Tests.Packs;

public class KilnPackTests
{
    private readonly ITestOutputHelper _out;
    public KilnPackTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Kiln_Pack_Loads_And_Validates_Without_Errors()
    {
        var json = File.ReadAllText(RepoPaths.Pack("kiln/faction.json"));
        var report = FactionPackLoader.LoadAndValidate(json);

        foreach (var e in report.LoadErrors) _out.WriteLine("LOAD ERROR: " + e);
        foreach (var f in report.Findings) _out.WriteLine($"{f.Severity} {f.Code} [{f.TargetId}] {f.Message}");

        Assert.Empty(report.LoadErrors);
        Assert.NotNull(report.Faction);
        Assert.DoesNotContain(report.Findings, f => f.Severity == Severity.Error);

        // Kiln-specific: the new Splash mechanic round-trips through pack load.
        Assert.NotNull(report.Faction!.Mechanic);
        Assert.Equal(MechanicKind.Splash, report.Faction!.Mechanic!.Kind);
    }
}
