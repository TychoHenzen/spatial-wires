namespace SpatialCircuits.Runner;

public sealed record FixtureReadResult(
    RunnerFixture? Fixture,
    IReadOnlyList<FixtureDiagnostic> Diagnostics);
