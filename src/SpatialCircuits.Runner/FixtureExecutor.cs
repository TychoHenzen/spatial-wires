using SpatialCircuits.Core;

namespace SpatialCircuits.Runner;

public static class FixtureExecutor
{
    public static IReadOnlyList<TraceRecord> Execute(RunnerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        return fixture.Cases.Select((item, sequence) =>
        {
            var drives = item.Drives.Select(FixtureLogicValue.Parse);
            var observed = DriveResolver.Resolve(drives).ToString();
            return new TraceRecord(
                fixture.TraceSchema,
                fixture.FixtureId,
                sequence,
                item.CaseId,
                observed,
                item.Expected,
                string.Equals(observed, item.Expected, StringComparison.Ordinal));
        }).ToArray();
    }

}
