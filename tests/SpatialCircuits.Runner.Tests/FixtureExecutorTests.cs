using SpatialCircuits.Runner;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class FixtureExecutorTests
{
    [Fact]
    public void ResolutionCasesKeepFixtureOrder()
    {
        var fixture = new RunnerFixture(
            FixtureVersion.Current,
            TraceVersion.Current,
            "ordered",
            FixtureAction.ResolveDrives,
            [
                new ResolutionCase("first", ["HighImpedance"], "HighImpedance"),
                new ResolutionCase("second", ["Low"], "Low"),
                new ResolutionCase("third", ["Low", "High"], "Unknown")
            ]);

        var records = FixtureExecutor.Execute(fixture);

        Assert.Equal(["first", "second", "third"], records.Select(record => record.CaseId));
        Assert.All(records, record => Assert.True(record.Passed));
    }
}
