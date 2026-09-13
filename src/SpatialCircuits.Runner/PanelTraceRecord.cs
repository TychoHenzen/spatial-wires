using System.Collections.Immutable;

namespace SpatialCircuits.Runner;

public sealed record PanelTraceRecord(
    TraceVersion TraceSchema,
    string FixtureId,
    int Sequence,
    int Microtick,
    string SchedulerHash,
    ImmutableSortedDictionary<string, string> Probes,
    ImmutableSortedDictionary<string, string> ExpectedProbes,
    ImmutableSortedDictionary<string, string> Outputs,
    bool Passed);
