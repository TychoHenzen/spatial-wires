namespace SpatialCircuits.Runner;

public sealed record TraceRecord(
    TraceVersion TraceSchema,
    string FixtureId,
    int Sequence,
    string CaseId,
    string Observed,
    string Expected,
    bool Passed);
