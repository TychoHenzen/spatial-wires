using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Core.Tests;

public sealed class DeterministicSchedulerTests
{
    [Fact]
    public void ScheduledEventKeyUsesEveryCanonicalIdentityField()
    {
        var first = Key(targetPort: "port-a", source: "source-a", ordinal: 1);
        var second = Key(targetPort: "port-b", source: "source-a", ordinal: 1);
        var third = Key(targetPort: "port-b", source: "source-b", ordinal: 1);
        var fourth = Key(targetPort: "port-b", source: "source-b", ordinal: 2);

        Assert.True(first.CompareTo(second) < 0);
        Assert.True(second.CompareTo(third) < 0);
        Assert.True(third.CompareTo(fourth) < 0);
    }

    [Fact]
    public void ScheduledEventKeySerializationDoesNotCollideOnDelimitedStrings()
    {
        var first = new ScheduledEventKey(
            0,
            SchedulerPhase.Deliver,
            "sink",
            1,
            "port|a",
            "source",
            "out",
            "drive",
            1);
        var second = first with
        {
            TargetPortOrLane = "port",
            SourceStableId = "a|source"
        };

        Assert.NotEqual(first.ToString(), second.ToString());
    }

    [Fact]
    public void ShuffledInsertionProducesTheSameTraceAndHash()
    {
        var events = new[]
        {
            new ScheduledEvent(Key("port-c", "source-c", 3), LogicValue.High),
            new ScheduledEvent(Key("port-a", "source-a", 1), LogicValue.Low),
            new ScheduledEvent(Key("port-b", "source-b", 2), LogicValue.Unknown)
        };
        var first = CreateTargetedScheduler(events);
        var second = CreateTargetedScheduler(events.Reverse());

        var firstResult = first.Step();
        var secondResult = second.Step();

        Assert.Equal(firstResult.Hash, secondResult.Hash);
        Assert.Equal(
            firstResult.DeliveredEvents.Select(item => item.Key),
            secondResult.DeliveredEvents.Select(item => item.Key));
    }

    [Fact]
    public void DistinctTargetPortsRemainDistinct()
    {
        var scheduler = new DeterministicScheduler();
        scheduler.RegisterTarget("sink");
        scheduler.SeedEvent(new ScheduledEvent(Key("port-b", "source", 2), LogicValue.High));
        scheduler.SeedEvent(new ScheduledEvent(Key("port-a", "source", 1), LogicValue.Low));

        var result = scheduler.Step();

        Assert.Equal(["port-a", "port-b"], result.DeliveredEvents.Select(item => item.TargetPortOrLane));
    }

    [Fact]
    public void TraceHashSeparatesDelimitedEventMetadata()
    {
        var first = new DeterministicScheduler();
        first.RegisterTarget("sink");
        first.SeedEvent(new ScheduledEvent(
            Key("input", "source", 1),
            LogicValue.High,
            "payload|root",
            "id"));

        var second = new DeterministicScheduler();
        second.RegisterTarget("sink");
        second.SeedEvent(new ScheduledEvent(
            Key("input", "source", 1),
            LogicValue.High,
            "payload",
            "root|id"));

        Assert.NotEqual(first.Step().Hash, second.Step().Hash);
    }

    [Fact]
    public void UnsupportedEventPhaseIsRejectedAtTheBoundary()
    {
        var scheduler = new DeterministicScheduler();
        scheduler.RegisterTarget("sink");
        var invalidPhaseKey = new ScheduledEventKey(
            0,
            SchedulerPhase.Record,
            "sink",
            1,
            "input",
            "source",
            "out",
            "drive",
            1);

        var exception = Assert.Throws<SchedulerException>(() =>
            scheduler.SeedEvent(new ScheduledEvent(invalidPhaseKey, LogicValue.High)));

        Assert.Equal(SchedulerDiagnosticCodes.InvalidPhase, exception.Diagnostic.Code);
        Assert.Empty(scheduler.PendingEvents);
    }

    [Fact]
    public void UnsupportedCommandEventPhaseIsRejectedWhenAccepted()
    {
        var scheduler = new DeterministicScheduler();
        var invalidEvent = new ScheduledEvent(
            new ScheduledEventKey(
                0,
                SchedulerPhase.Record,
                "sink",
                1,
                "input",
                "source",
                "out",
                "drive",
                1),
            LogicValue.High);

        var scheduleException = Assert.Throws<SchedulerException>(() =>
            scheduler.Accept(SchedulerCommand.ScheduleEvent(invalidEvent)));
        var rootException = Assert.Throws<SchedulerException>(() =>
            scheduler.Accept(SchedulerCommand.AddTemporalRoot("root", invalidEvent)));

        Assert.Equal(SchedulerDiagnosticCodes.InvalidPhase, scheduleException.Diagnostic.Code);
        Assert.Equal(SchedulerDiagnosticCodes.InvalidPhase, rootException.Diagnostic.Code);
        Assert.Empty(scheduler.AcceptedCommands);
    }

    [Fact]
    public void InvalidAcceptedCommandsAreRejectedBeforeTheyCanPoisonRollback()
    {
        var scheduler = new DeterministicScheduler();
        var invalidIdentifier = Assert.Throws<SchedulerException>(() =>
            scheduler.Accept(SchedulerCommand.RegisterTarget("")));
        var invalidKind = Assert.Throws<SchedulerException>(() =>
            scheduler.Accept(new SchedulerCommand((SchedulerCommandKind)255, 0)));
        var invalidDueTick = Assert.Throws<SchedulerException>(() =>
            scheduler.Accept(SchedulerCommand.ScheduleEvent(
                new ScheduledEvent(Key("input", "source", 1), LogicValue.High),
                applyAtTick: 1)));
        var invalidValue = Assert.Throws<SchedulerException>(() =>
            scheduler.Accept(SchedulerCommand.SetPersistentDrive(
                "source",
                "out",
                "sink",
                0,
                "input",
                (LogicValue)255)));

        Assert.Equal(SchedulerDiagnosticCodes.InvalidCommand, invalidIdentifier.Diagnostic.Code);
        Assert.Equal(SchedulerDiagnosticCodes.InvalidCommand, invalidKind.Diagnostic.Code);
        Assert.Equal(SchedulerDiagnosticCodes.InvalidCommand, invalidDueTick.Diagnostic.Code);
        Assert.Equal(SchedulerDiagnosticCodes.InvalidCommand, invalidValue.Diagnostic.Code);
        Assert.Empty(scheduler.AcceptedCommands);
        Assert.Equal(1, scheduler.NextAcceptedOrdinal);
    }

    [Fact]
    public void SameTargetCommandsFollowAcceptanceOrder()
    {
        var scheduler = new DeterministicScheduler();
        var target = scheduler.RegisterTarget("sink");
        scheduler.Accept(SchedulerCommand.SetPersistentDrive(
            "source",
            "out",
            target.StableId,
            target.Incarnation,
            "input",
            LogicValue.Low));
        scheduler.Accept(SchedulerCommand.SetPersistentDrive(
            "source",
            "out",
            target.StableId,
            target.Incarnation,
            "input",
            LogicValue.High));

        scheduler.Step();

        Assert.Equal(LogicValue.High, scheduler.CurrentInputs[new SchedulerPortAddress("sink", 1, "input")]);
    }

    [Fact]
    public void AcceptedCommandOrdinalSeedsItsScheduledEventCausalOrdinal()
    {
        var scheduler = new DeterministicScheduler();
        scheduler.RegisterTarget("sink");
        var acceptedOrdinal = scheduler.Accept(SchedulerCommand.ScheduleEvent(
            new ScheduledEvent(Key("input", "source", 99), LogicValue.High)));

        var result = scheduler.Step();

        Assert.Equal(acceptedOrdinal, result.DeliveredEvents.Single().Key.CausalOrdinal);
    }

    [Fact]
    public void InternalProposalsAreReducedIndependentlyOfEnumerationOrder()
    {
        var first = CreateProposalScheduler(reverse: false);
        var second = CreateProposalScheduler(reverse: true);

        first.Step();
        second.Step();
        var firstResult = first.Step();
        var secondResult = second.Step();

        Assert.Equal(firstResult.Hash, secondResult.Hash);
        Assert.Equal(first.PendingEvents, second.PendingEvents);
    }

    [Fact]
    public void ProposalReductionOrdersDistinctSourceIncarnations()
    {
        var first = CreateSourceIncarnationProposalScheduler(reverse: false);
        var second = CreateSourceIncarnationProposalScheduler(reverse: true);

        var firstResult = first.Step();
        var secondResult = second.Step();

        Assert.Equal(firstResult.Hash, secondResult.Hash);
        Assert.Equal(
            first.PendingEvents.Select(item => item.SourceIncarnation),
            second.PendingEvents.Select(item => item.SourceIncarnation));
        Assert.Equal([1L, 2L], first.PendingEvents.Select(item => item.SourceIncarnation));
    }

    [Fact]
    public void EvaluationReadsOneCommittedSnapshotAndEffectsWaitForNextTick()
    {
        var observed = LogicValue.HighImpedance;
        var scheduler = new DeterministicScheduler(context =>
        {
            observed = context.Inputs[new SchedulerPortAddress("sink", 1, "input")];
            return [new SchedulerProposal(
                context.Tick + 1,
                "sink",
                1,
                "future",
                "source",
                "out",
                "drive",
                LogicValue.High)];
        });
        scheduler.RegisterTarget("sink");
        scheduler.SeedEvent(new ScheduledEvent(
            Key("input", "source-a", 1),
            LogicValue.Low));
        scheduler.SeedEvent(new ScheduledEvent(
            Key("input", "source-b", 2),
            LogicValue.High));

        scheduler.Step();

        Assert.Equal(LogicValue.Unknown, observed);
        Assert.DoesNotContain(scheduler.CurrentInputs.Keys, key => key.PortOrLane == "future");

        scheduler.Step();

        Assert.Equal(
            LogicValue.High,
            scheduler.CurrentInputs[new SchedulerPortAddress("sink", 1, "future")]);
    }

    [Fact]
    public void ZeroDelayProposalFailsWithoutPartialCommit()
    {
        var scheduler = new DeterministicScheduler(_ =>
            [new SchedulerProposal(
                0,
                "sink",
                1,
                "input",
                "source",
                "out",
                "drive",
                LogicValue.High)]);
        scheduler.RegisterTarget("sink");

        var exception = Assert.Throws<SchedulerCausalException>(() => scheduler.Step());

        Assert.Equal(SchedulerDiagnosticCodes.ZeroDelayProposal, exception.Diagnostic.Code);
        Assert.Equal(0, scheduler.CurrentTick);
        Assert.Empty(scheduler.PendingEvents);
        Assert.Empty(scheduler.Trace);
    }

    [Fact]
    public void CallbackCannotReenterTheScheduler()
    {
        DeterministicScheduler? scheduler = null;
        scheduler = new DeterministicScheduler(context =>
        {
            context.Step();
            return [];
        });
        scheduler.RegisterTarget("sink");

        var exception = Assert.Throws<SchedulerReentryException>(() => scheduler.Step());

        Assert.Equal(SchedulerDiagnosticCodes.Reentry, exception.Diagnostic.Code);
        Assert.Equal(0, scheduler.CurrentTick);
        Assert.Empty(scheduler.Trace);
    }

    [Fact]
    public void SnapshotRequestDuringEvaluationIsRejected()
    {
        var scheduler = new DeterministicScheduler(context =>
        {
            context.RequestSnapshot();
            return [];
        });
        scheduler.RegisterTarget("sink");

        var exception = Assert.Throws<SchedulerSnapshotBoundaryException>(() => scheduler.Step());

        Assert.Equal(SchedulerDiagnosticCodes.SnapshotBoundary, exception.Diagnostic.Code);
        Assert.Equal(0, scheduler.CurrentTick);
    }

    [Fact]
    public void PersistentDrivesRemainActiveAndReleaseIndividually()
    {
        var scheduler = new DeterministicScheduler();
        var target = scheduler.RegisterTarget("sink");
        scheduler.Accept(SchedulerCommand.SetPersistentDrive(
            "source-a", "out", "sink", target.Incarnation, "input", LogicValue.Low));
        scheduler.Accept(SchedulerCommand.SetPersistentDrive(
            "source-b", "out", "sink", target.Incarnation, "input", LogicValue.High));

        scheduler.Step();
        scheduler.Step();
        Assert.Equal(LogicValue.Unknown, scheduler.CurrentInputs[new SchedulerPortAddress("sink", 1, "input")]);

        scheduler.Accept(SchedulerCommand.ReleaseSource("source-a", applyAtTick: scheduler.CurrentTick));
        scheduler.Step();

        Assert.Equal(LogicValue.High, scheduler.CurrentInputs[new SchedulerPortAddress("sink", 1, "input")]);
    }

    [Fact]
    public void ReplacedTargetIgnoresStaleWork()
    {
        var scheduler = new DeterministicScheduler();
        var oldTarget = scheduler.RegisterTarget("sink");
        scheduler.SeedEvent(new ScheduledEvent(
            new ScheduledEventKey(
                1,
                SchedulerPhase.Deliver,
                "sink",
                oldTarget.Incarnation,
                "input",
                "source",
                "out",
                "drive",
                1),
            LogicValue.High));
        scheduler.RemoveTarget("sink");
        var replacement = scheduler.RegisterTarget("sink");

        scheduler.Step();
        var result = scheduler.Step();

        Assert.Equal(2, replacement.Incarnation);
        Assert.Empty(result.DeliveredEvents);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SchedulerDiagnosticCodes.StaleEvent);
        Assert.Empty(scheduler.CurrentInputs);
    }

    [Fact]
    public void ReplacedSourceDoesNotLoseTheReplacementDriveToStaleReleaseWork()
    {
        var scheduler = new DeterministicScheduler();
        var source = scheduler.RegisterTarget("source");
        var sink = scheduler.RegisterTarget("sink");
        scheduler.Accept(SchedulerCommand.SetPersistentDrive(
            source.StableId,
            "out",
            sink.StableId,
            sink.Incarnation,
            "input",
            LogicValue.High));
        scheduler.Step();

        scheduler.RemoveTarget(source.StableId);
        var replacement = scheduler.RegisterTarget(source.StableId);
        scheduler.Accept(SchedulerCommand.SetPersistentDrive(
            replacement.StableId,
            "out",
            sink.StableId,
            sink.Incarnation,
            "input",
            LogicValue.Low,
            applyAtTick: scheduler.CurrentTick,
            sourceIncarnation: replacement.Incarnation));

        var result = scheduler.Step();

        Assert.Contains(result.DeliveredEvents, item => item.EventKind == "drive-release");
        Assert.Equal(
            LogicValue.Low,
            scheduler.CurrentInputs[new SchedulerPortAddress("sink", sink.Incarnation, "input")]);
        Assert.Contains(
            scheduler.PersistentDrives,
            pair => pair.Key.SourceStableId == source.StableId &&
                    pair.Key.SourceIncarnation == replacement.Incarnation &&
                    pair.Value == LogicValue.Low);
    }

    [Fact]
    public void StaleSourceEventIsIgnoredAfterSourceReplacement()
    {
        var scheduler = new DeterministicScheduler();
        var source = scheduler.RegisterTarget("source");
        var sink = scheduler.RegisterTarget("sink");
        scheduler.SeedEvent(new ScheduledEvent(
            new ScheduledEventKey(
                1,
                SchedulerPhase.Deliver,
                sink.StableId,
                sink.Incarnation,
                "input",
                source.StableId,
                "out",
                "drive",
                1),
            LogicValue.High,
            SourceIncarnation: source.Incarnation));

        scheduler.RemoveTarget(source.StableId);
        scheduler.RegisterTarget(source.StableId);
        scheduler.Step();
        var result = scheduler.Step();

        Assert.Empty(result.DeliveredEvents);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SchedulerDiagnosticCodes.StaleEvent);
        Assert.Empty(scheduler.PersistentDrives);
    }

    [Fact]
    public void RemovingDrivingTargetCreatesAReleaseAndClearsItsDrives()
    {
        var scheduler = new DeterministicScheduler();
        var source = scheduler.RegisterTarget("source");
        var sink = scheduler.RegisterTarget("sink");
        scheduler.Accept(SchedulerCommand.SetPersistentDrive(
            source.StableId,
            "out",
            sink.StableId,
            sink.Incarnation,
            "input",
            LogicValue.High));
        scheduler.Step();

        scheduler.RemoveTarget(source.StableId);
        var result = scheduler.Step();

        Assert.Contains(result.DeliveredEvents, item => item.EventKind == "drive-release");
        Assert.Empty(scheduler.PersistentDrives);
    }

    [Fact]
    public void TemporalRootFiresAfterQuietMicroticks()
    {
        var scheduler = new DeterministicScheduler();
        var target = scheduler.RegisterTarget("timer");
        scheduler.AddTemporalRoot(new SchedulerTemporalRoot(
            "timer-root",
            new ScheduledEvent(
                new ScheduledEventKey(
                    3,
                    SchedulerPhase.Deliver,
                    target.StableId,
                    target.Incarnation,
                    "fire",
                    "timer",
                    "out",
                    "timer",
                    1))));

        Assert.Empty(scheduler.Step().DeliveredEvents);
        Assert.Empty(scheduler.Step().DeliveredEvents);
        Assert.Empty(scheduler.Step().DeliveredEvents);
        var result = scheduler.Step();

        Assert.Single(result.DeliveredEvents);
        Assert.Equal("timer", result.DeliveredEvents[0].EventKind);
    }

    [Fact]
    public void ReplacingATemporalRootDoesNotReviveItsOldEvent()
    {
        var scheduler = new DeterministicScheduler();
        var target = scheduler.RegisterTarget("timer");
        scheduler.AddTemporalRoot(new SchedulerTemporalRoot(
            "timer-root",
            new ScheduledEvent(new ScheduledEventKey(
                2,
                SchedulerPhase.Deliver,
                target.StableId,
                target.Incarnation,
                "fire",
                "timer",
                "out",
                "timer",
                1))));
        scheduler.CancelTemporalRoot("timer-root");
        scheduler.AddTemporalRoot(new SchedulerTemporalRoot(
            "timer-root",
            new ScheduledEvent(new ScheduledEventKey(
                3,
                SchedulerPhase.Deliver,
                target.StableId,
                target.Incarnation,
                "fire",
                "timer",
                "out",
                "timer",
                2))));

        Assert.Empty(scheduler.Step().DeliveredEvents);
        Assert.Empty(scheduler.Step().DeliveredEvents);
        Assert.Empty(scheduler.Step().DeliveredEvents);
        var result = scheduler.Step();

        Assert.Single(result.DeliveredEvents);
        Assert.Equal(3, result.DeliveredEvents[0].Key.DueTick);
    }

    [Fact]
    public void InvalidTemporalRootReplacementPreservesItsExistingEvent()
    {
        var scheduler = new DeterministicScheduler();
        var target = scheduler.RegisterTarget("timer");
        scheduler.AddTemporalRoot(new SchedulerTemporalRoot(
            "timer-root",
            new ScheduledEvent(new ScheduledEventKey(
                2,
                SchedulerPhase.Deliver,
                target.StableId,
                target.Incarnation,
                "fire",
                "timer",
                "out",
                "timer",
                1))));

        var exception = Assert.Throws<SchedulerException>(() =>
            scheduler.AddTemporalRoot(new SchedulerTemporalRoot(
                "timer-root",
                new ScheduledEvent(new ScheduledEventKey(
                    3,
                    SchedulerPhase.Record,
                    target.StableId,
                    target.Incarnation,
                    "fire",
                    "timer",
                    "out",
                    "timer",
                    2)))));

        Assert.Equal(SchedulerDiagnosticCodes.InvalidPhase, exception.Diagnostic.Code);
        Assert.Equal(2, scheduler.PendingEvents.Single().Key.DueTick);
        scheduler.Step();
        scheduler.Step();
        var result = scheduler.Step();
        Assert.Equal(2, result.DeliveredEvents.Single().Key.DueTick);
    }

    [Fact]
    public void RestoreReproducesTheRemainingHashSequence()
    {
        var scheduler = CreateTenTickScheduler();
        for (var index = 0; index < 5; index++)
        {
            scheduler.Step();
        }

        var snapshot = scheduler.CaptureSnapshot();
        var originalHashes = Enumerable.Range(0, 5).Select(_ => scheduler.Step().Hash).ToArray();
        var restored = DeterministicScheduler.Restore(snapshot);
        var restoredHashes = Enumerable.Range(0, 5).Select(_ => restored.Step().Hash).ToArray();

        Assert.Equal(originalHashes, restoredHashes);
    }

    [Fact]
    public void InvalidSnapshotDoesNotPartiallyReplaceSchedulerState()
    {
        var scheduler = new DeterministicScheduler();
        var target = scheduler.RegisterTarget("keep");
        var snapshot = scheduler.CaptureSnapshot();
        var malformed = snapshot with
        {
            Targets = snapshot.Targets.Add(new SchedulerTargetSnapshot("keep", target.Incarnation + 1, true))
        };

        var exception = Assert.Throws<SchedulerException>(() => scheduler.RestoreSnapshot(malformed));

        Assert.Equal(SchedulerDiagnosticCodes.InvalidCommand, exception.Diagnostic.Code);
        Assert.Equal(target, scheduler.GetTarget("keep"));
        Assert.Empty(scheduler.PendingEvents);
        Assert.Empty(scheduler.AcceptedCommands);
    }

    [Fact]
    public void AcceptedScheduledEventPreservesItsTemporalRootId()
    {
        var scheduler = new DeterministicScheduler();
        scheduler.RegisterTarget("sink");
        scheduler.Accept(SchedulerCommand.ScheduleEvent(
            new ScheduledEvent(
                Key("input", "source", 1),
                LogicValue.High,
                TemporalRootId: "timer-root")));

        var result = scheduler.Step();

        Assert.Equal("timer-root", result.DeliveredEvents.Single().TemporalRootId);
    }

    [Fact]
    public void ReplayOfAcceptedCommandsMatchesTheOriginalRun()
    {
        var commands = new[]
        {
            SchedulerCommand.RegisterTarget("sink"),
            SchedulerCommand.SetPersistentDrive(
                "source",
                "out",
                "sink",
                0,
                "input",
                LogicValue.High)
        };
        var original = DeterministicScheduler.Replay(commands);
        var replay = DeterministicScheduler.Replay(commands);

        var originalHash = original.Step().Hash;
        var replayHash = replay.Step().Hash;

        Assert.Equal(originalHash, replayHash);
        Assert.Equal(
            LogicValue.High,
            replay.CurrentInputs[new SchedulerPortAddress("sink", 1, "input")]);
    }

    private static DeterministicScheduler CreateTargetedScheduler(IEnumerable<ScheduledEvent> events)
    {
        var scheduler = new DeterministicScheduler();
        scheduler.RegisterTarget("sink");
        foreach (var scheduledEvent in events)
        {
            scheduler.SeedEvent(scheduledEvent);
        }

        return scheduler;
    }

    private static DeterministicScheduler CreateProposalScheduler(bool reverse)
    {
        var scheduler = new DeterministicScheduler(context =>
        {
            var proposals = new[]
            {
                new SchedulerProposal(context.Tick + 1, "sink", 1, "a", "source-b", "out", "drive", LogicValue.High, 1),
                new SchedulerProposal(context.Tick + 1, "sink", 1, "b", "source-a", "out", "drive", LogicValue.Low, 0)
            };
            return reverse ? proposals.Reverse() : proposals;
        });
        scheduler.RegisterTarget("sink");
        return scheduler;
    }

    private static DeterministicScheduler CreateSourceIncarnationProposalScheduler(bool reverse)
    {
        var scheduler = new DeterministicScheduler(context =>
        {
            var proposals = new[]
            {
                new SchedulerProposal(
                    context.Tick + 1,
                    "sink",
                    1,
                    "input",
                    "source",
                    "out",
                    "drive",
                    LogicValue.High,
                    SourceIncarnation: 1),
                new SchedulerProposal(
                    context.Tick + 1,
                    "sink",
                    1,
                    "input",
                    "source",
                    "out",
                    "drive",
                    LogicValue.High,
                    SourceIncarnation: 2)
            };
            return reverse ? proposals.Reverse() : proposals;
        });
        scheduler.RegisterTarget("sink");
        return scheduler;
    }

    private static DeterministicScheduler CreateTenTickScheduler()
    {
        var scheduler = new DeterministicScheduler();
        var target = scheduler.RegisterTarget("sink");
        scheduler.SeedEvent(new ScheduledEvent(
            new ScheduledEventKey(
                0,
                SchedulerPhase.Deliver,
                target.StableId,
                target.Incarnation,
                "input",
                "source",
                "out",
                "drive",
                1),
            LogicValue.High));
        scheduler.AddTemporalRoot(new SchedulerTemporalRoot(
            "timer",
            new ScheduledEvent(
                new ScheduledEventKey(
                    7,
                    SchedulerPhase.Deliver,
                    target.StableId,
                    target.Incarnation,
                    "timer",
                    "timer",
                    "out",
                    "timer",
                    2))));
        return scheduler;
    }

    private static ScheduledEventKey Key(string targetPort, string source, long ordinal) => new(
        0,
        SchedulerPhase.Deliver,
        "sink",
        1,
        targetPort,
        source,
        "out",
        "drive",
        ordinal);
}
