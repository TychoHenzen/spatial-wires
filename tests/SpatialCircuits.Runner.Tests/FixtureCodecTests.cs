using System.Text;
using SpatialCircuits.Runner;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class FixtureCodecTests
{
    [Fact]
    public void ScheduledDrivePracticeParsesWithoutResolutionCases()
    {
        const string json = """
            {
              "fixtureSchema":{"major":1,"minor":0},
              "traceSchema":{"major":1,"minor":0},
              "fixtureId":"stage-03-scheduled-drive",
              "action":"scheduledDrive",
              "scheduledDrive":{
                "microticks":10,
                "snapshotAfter":5,
                "releaseAt":6,
                "sourceId":"scheduled-source",
                "sourcePort":"out",
                "targetId":"scheduled-target",
                "targetPort":"input",
                "drive":"High"
              }
            }
            """;

        var result = FixtureCodec.Read(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(result.Fixture);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(FixtureAction.ScheduledDrive, result.Fixture!.Action);
        Assert.Empty(result.Fixture.Cases);
        Assert.Equal(10, result.Fixture.ScheduledDrive!.Microticks);
    }

    [Fact]
    public void SupportedFixtureParsesWithoutDiagnostics()
    {
        const string json = """
            {
              "fixtureSchema":{"major":1,"minor":0},
              "traceSchema":{"major":1,"minor":0},
              "fixtureId":"unit-fixture",
              "action":"resolveDrives",
              "cases":[{"caseId":"floating","drives":[],"expected":"HighImpedance"}]
            }
            """;

        var result = FixtureCodec.Read(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(result.Fixture);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void MalformedJsonReturnsStableDiagnostic()
    {
        var result = FixtureCodec.Read("{"u8.ToArray());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Null(result.Fixture);
        Assert.Equal("FIXTURE_JSON_INVALID", diagnostic.Code);
        Assert.Equal("$", diagnostic.Path);
    }

    [Fact]
    public void NumericEnumTextIsRejectedAsAnInvalidDrive()
    {
        const string json = """
            {
              "fixtureSchema":{"major":1,"minor":0},
              "traceSchema":{"major":1,"minor":0},
              "fixtureId":"numeric-drive",
              "action":"resolveDrives",
              "cases":[{"caseId":"numeric","drives":["1"],"expected":"High"}]
            }
            """;
        var readResult = FixtureCodec.Read(Encoding.UTF8.GetBytes(json));

        var diagnostic = Assert.Single(FixtureValidator.Validate(readResult.Fixture!));

        Assert.Equal(FixtureDiagnosticCodes.DriveInvalid, diagnostic.Code);
        Assert.Equal("$.cases[0].drives[0]", diagnostic.Path);
    }
}
