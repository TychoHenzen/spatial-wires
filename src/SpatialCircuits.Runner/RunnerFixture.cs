using System.Collections.Immutable;

namespace SpatialCircuits.Runner;

public sealed class RunnerFixture
{
    public RunnerFixture(
        FixtureVersion fixtureSchema,
        TraceVersion traceSchema,
        string fixtureId,
        FixtureAction action,
        IEnumerable<ResolutionCase> cases,
        ScheduledDrivePlan? scheduledDrive = null,
        PanelScenarioPlan? panelScenario = null,
        ChipNetworkScenarioPlan? chipNetworkScenario = null)
    {
        ArgumentNullException.ThrowIfNull(cases);

        FixtureSchema = fixtureSchema;
        TraceSchema = traceSchema;
        FixtureId = fixtureId ?? string.Empty;
        Action = action;
        Cases = cases.ToImmutableArray();
        ScheduledDrive = scheduledDrive;
        PanelScenario = panelScenario;
        ChipNetworkScenario = chipNetworkScenario;
    }

    public FixtureVersion FixtureSchema { get; }

    public TraceVersion TraceSchema { get; }

    public string FixtureId { get; }

    public FixtureAction Action { get; }

    public ImmutableArray<ResolutionCase> Cases { get; }

    public ScheduledDrivePlan? ScheduledDrive { get; }

    public PanelScenarioPlan? PanelScenario { get; }

    public ChipNetworkScenarioPlan? ChipNetworkScenario { get; }
}

public sealed record ScheduledDrivePlan(
    int Microticks,
    int SnapshotAfter,
    int ReleaseAt,
    string SourceId,
    string SourcePort,
    string TargetId,
    string TargetPort,
    string Drive);
