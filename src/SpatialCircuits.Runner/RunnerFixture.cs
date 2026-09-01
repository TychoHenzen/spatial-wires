using System.Collections.Immutable;

namespace SpatialCircuits.Runner;

public sealed class RunnerFixture
{
    public RunnerFixture(
        FixtureVersion fixtureSchema,
        TraceVersion traceSchema,
        string fixtureId,
        FixtureAction action,
        IEnumerable<ResolutionCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);

        FixtureSchema = fixtureSchema;
        TraceSchema = traceSchema;
        FixtureId = fixtureId ?? string.Empty;
        Action = action;
        Cases = cases.ToImmutableArray();
    }

    public FixtureVersion FixtureSchema { get; }

    public TraceVersion TraceSchema { get; }

    public string FixtureId { get; }

    public FixtureAction Action { get; }

    public ImmutableArray<ResolutionCase> Cases { get; }
}
