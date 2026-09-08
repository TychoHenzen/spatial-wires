using SpatialCircuits.Core;

namespace SpatialCircuits.Runner;

public static class ScheduledDriveFixtureExecutor
{
    public static IReadOnlyList<ScheduledDriveTraceRecord> Execute(RunnerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var plan = fixture.ScheduledDrive
            ?? throw new ArgumentException("Fixture does not contain a scheduled-drive plan.", nameof(fixture));
        var commands = BuildCommands(plan);

        var original = DeterministicScheduler.Replay(commands);
        var originalResults = Run(original, plan.Microticks, plan.SnapshotAfter, out var snapshot);
        var replay = DeterministicScheduler.Replay(commands);
        var replayResults = Run(replay, plan.Microticks, plan.SnapshotAfter, out _);
        var restored = DeterministicScheduler.Restore(snapshot);
        var restoredResults = Enumerable.Range(plan.SnapshotAfter, plan.Microticks - plan.SnapshotAfter)
            .Select(_ => restored.Step())
            .ToArray();

        var restoredByTick = restoredResults.ToDictionary(result => (int)result.Tick);
        return originalResults.Select((result, sequence) =>
        {
            var replayed = replayResults[sequence];
            var restoredHash = restoredByTick.TryGetValue((int)result.Tick, out var restoredResult)
                ? restoredResult.Hash
                : null;
            var observed = Observe(result, plan);
            var restoredObserved = restoredResult is null ? null : Observe(restoredResult, plan);
            var deliveredEvents = RenderEvents(result);
            var restoredDeliveredEvents = restoredResult is null ? null : RenderEvents(restoredResult);
            var diagnostics = RenderDiagnostics(result);
            var restoredDiagnostics = restoredResult is null ? null : RenderDiagnostics(restoredResult);
            var passed = Equivalent(result, replayed) &&
                (restoredResult is null || Equivalent(result, restoredResult));
            return new ScheduledDriveTraceRecord(
                fixture.TraceSchema,
                fixture.FixtureId,
                sequence,
                (int)result.Tick,
                observed,
                restoredObserved,
                deliveredEvents,
                restoredDeliveredEvents,
                diagnostics,
                restoredDiagnostics,
                result.Hash,
                replayed.Hash,
                restoredHash,
                passed);
        }).ToArray();
    }

    private static IReadOnlyList<SchedulerTickResult> Run(
        DeterministicScheduler scheduler,
        int microticks,
        int snapshotAfter,
        out SchedulerSnapshot snapshot)
    {
        var results = new List<SchedulerTickResult>(microticks);
        SchedulerSnapshot? captured = null;
        for (var index = 0; index < microticks; index++)
        {
            results.Add(scheduler.Step());
            if (index + 1 == snapshotAfter)
            {
                captured = scheduler.CaptureSnapshot();
            }
        }

        snapshot = captured
            ?? throw new InvalidOperationException("The practice fixture did not capture its snapshot.");
        return results;
    }

    private static bool Equivalent(SchedulerTickResult first, SchedulerTickResult second) =>
        first.Tick == second.Tick &&
        string.Equals(first.Hash, second.Hash, StringComparison.Ordinal) &&
        first.DeliveredEvents.SequenceEqual(second.DeliveredEvents) &&
        first.ResolvedInputs
            .OrderBy(pair => pair.Key.TargetStableId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.TargetIncarnation)
            .ThenBy(pair => pair.Key.PortOrLane, StringComparer.Ordinal)
            .SequenceEqual(second.ResolvedInputs
                .OrderBy(pair => pair.Key.TargetStableId, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.TargetIncarnation)
                .ThenBy(pair => pair.Key.PortOrLane, StringComparer.Ordinal)) &&
        first.Diagnostics.SequenceEqual(second.Diagnostics);

    private static string Observe(SchedulerTickResult result, ScheduledDrivePlan plan) =>
        result.ResolvedInputs.TryGetValue(
            new SchedulerPortAddress(plan.TargetId, 1, plan.TargetPort),
            out var value)
            ? value.ToString()
            : LogicValue.HighImpedance.ToString();

    private static string RenderEvents(SchedulerTickResult result) => string.Join(
        ";",
        result.DeliveredEvents.Select(item =>
            $"{item.Key}:{(byte)item.Value}:{item.Payload}:{item.TemporalRootId}:{item.SourceIncarnation}"));

    private static string RenderDiagnostics(SchedulerTickResult result) => string.Join(
        ";",
        result.Diagnostics.Select(item => $"{item.Code}:{item.Message}:{(byte)item.Phase}"));

    private static IReadOnlyList<SchedulerCommand> BuildCommands(ScheduledDrivePlan plan)
    {
        var drive = FixtureLogicValue.Parse(plan.Drive);
        return
        [
            SchedulerCommand.RegisterTarget(plan.SourceId),
            SchedulerCommand.RegisterTarget(plan.TargetId),
            SchedulerCommand.SetPersistentDrive(
                plan.SourceId,
                plan.SourcePort,
                plan.TargetId,
                0,
                plan.TargetPort,
                drive),
            SchedulerCommand.ReleaseSource(plan.SourceId, plan.ReleaseAt)
        ];
    }
}
