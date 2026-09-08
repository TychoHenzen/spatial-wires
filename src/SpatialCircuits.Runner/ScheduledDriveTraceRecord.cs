namespace SpatialCircuits.Runner;

public sealed record ScheduledDriveTraceRecord(
    TraceVersion TraceSchema,
    string FixtureId,
    int Sequence,
    int Microtick,
    string Observed,
    string? RestoredObserved,
    string DeliveredEvents,
    string? RestoredDeliveredEvents,
    string Diagnostics,
    string? RestoredDiagnostics,
    string OriginalHash,
    string ReplayedHash,
    string? RestoredHash,
    bool Passed);
