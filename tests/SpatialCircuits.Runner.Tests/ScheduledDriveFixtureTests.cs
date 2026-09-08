using SpatialCircuits.Runner;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class ScheduledDriveFixtureTests
{
    [Fact]
    public void TenMicrotickPracticeReplaysAndRestoresTheSameHashes()
    {
        var fixture = new RunnerFixture(
            FixtureVersion.Current,
            TraceVersion.Current,
            "stage-03-scheduled-drive",
            FixtureAction.ScheduledDrive,
            [],
            new ScheduledDrivePlan(
                Microticks: 10,
                SnapshotAfter: 5,
                ReleaseAt: 6,
                SourceId: "scheduled-source",
                SourcePort: "out",
                TargetId: "scheduled-target",
                TargetPort: "input",
                Drive: "High"));

        var records = ScheduledDriveFixtureExecutor.Execute(fixture);

        Assert.Equal(10, records.Count);
        Assert.All(records, record => Assert.True(record.Passed));
        Assert.All(records, record => Assert.Equal(record.OriginalHash, record.ReplayedHash));
        Assert.All(records.Take(5), record => Assert.Null(record.RestoredHash));
        Assert.All(records.Skip(5), record => Assert.Equal(record.OriginalHash, record.RestoredHash));
        Assert.Equal("High", records[0].Observed);
        Assert.Equal("HighImpedance", records[6].Observed);
        Assert.All(records.Skip(5), record => Assert.Equal(record.Observed, record.RestoredObserved));
        Assert.All(records.Skip(5), record =>
            Assert.Equal(record.DeliveredEvents, record.RestoredDeliveredEvents));
        Assert.All(records.Skip(5), record =>
            Assert.Equal(record.Diagnostics, record.RestoredDiagnostics));
    }

    [Fact]
    public void ScheduledDriveMicroticksHaveAResourceBound()
    {
        var fixture = new RunnerFixture(
            FixtureVersion.Current,
            TraceVersion.Current,
            "bounded-scheduled-drive",
            FixtureAction.ScheduledDrive,
            [],
            new ScheduledDrivePlan(
                FixtureValidator.MaxScheduledDriveMicroticks + 1,
                1,
                1,
                "source",
                "out",
                "target",
                "input",
                "High"));

        var diagnostic = Assert.Single(FixtureValidator.Validate(fixture));

        Assert.Equal(FixtureDiagnosticCodes.ScheduledDriveInvalid, diagnostic.Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduledDriveFixtureExecutor.Execute(fixture));
    }
}
