using System.Collections.Immutable;

namespace SpatialCircuits.Core;

public enum SchedulerPhase : byte
{
    Idle = 0,
    ApplyCommands = 1,
    Invalidate = 2,
    Deliver = 3,
    ResolveDrives = 4,
    Evaluate = 5,
    MainThreadBarrier = 6,
    ReduceProposals = 7,
    Commit = 8,
    Record = 9,
    Advance = 10
}

public enum SchedulerCommandKind : byte
{
    RegisterTarget,
    RemoveTarget,
    SetPersistentDrive,
    ReleaseSource,
    ScheduleEvent,
    AddTemporalRoot,
    CancelTemporalRoot
}

public readonly record struct SchedulerPortAddress(
    string TargetStableId,
    long TargetIncarnation,
    string PortOrLane);

public readonly record struct SchedulerDriveKey(
    string SourceStableId,
    long SourceIncarnation,
    string SourcePort,
    string TargetStableId,
    long TargetIncarnation,
    string TargetPortOrLane);

public readonly record struct SchedulerTargetHandle(string StableId, long Incarnation);

public readonly record struct ScheduledEventKey(
    long DueTick,
    SchedulerPhase Phase,
    string TargetStableId,
    long TargetIncarnation,
    string TargetPortOrLane,
    string SourceStableId,
    string SourcePort,
    string EventKind,
    long CausalOrdinal) : IComparable<ScheduledEventKey>
{
    public int CompareTo(ScheduledEventKey other)
    {
        var result = DueTick.CompareTo(other.DueTick);
        if (result != 0)
        {
            return result;
        }

        result = Phase.CompareTo(other.Phase);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(TargetStableId, other.TargetStableId);
        if (result != 0)
        {
            return result;
        }

        result = TargetIncarnation.CompareTo(other.TargetIncarnation);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(TargetPortOrLane, other.TargetPortOrLane);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(SourceStableId, other.SourceStableId);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(SourcePort, other.SourcePort);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(EventKind, other.EventKind);
        if (result != 0)
        {
            return result;
        }

        return CausalOrdinal.CompareTo(other.CausalOrdinal);
    }

    public override string ToString() => string.Join(
        "|",
        DueTick,
        (byte)Phase,
        TargetStableId,
        TargetIncarnation,
        TargetPortOrLane,
        SourceStableId,
        SourcePort,
        EventKind,
        CausalOrdinal);
}

public sealed record ScheduledEvent(
    ScheduledEventKey Key,
    LogicValue Value = LogicValue.HighImpedance,
    string Payload = "",
    string TemporalRootId = "",
    long SourceIncarnation = 0)
{
    public string TargetStableId => Key.TargetStableId;

    public long TargetIncarnation => Key.TargetIncarnation;

    public string TargetPortOrLane => Key.TargetPortOrLane;

    public string SourceStableId => Key.SourceStableId;

    public string SourcePort => Key.SourcePort;

    public string EventKind => Key.EventKind;
}

public sealed record SchedulerProposal(
    long DueTick,
    string TargetStableId,
    long TargetIncarnation,
    string TargetPortOrLane,
    string SourceStableId,
    string SourcePort,
    string EventKind,
    LogicValue Value = LogicValue.HighImpedance,
    int OutputSlot = 0,
    string Payload = "",
    long SourceIncarnation = 0)
{
    public static SchedulerProposal ForNextTick(
        string targetStableId,
        long targetIncarnation,
        string targetPortOrLane,
        string sourceStableId,
        string sourcePort,
        string eventKind,
        LogicValue value = LogicValue.HighImpedance,
        int outputSlot = 0,
        string payload = "",
        long sourceIncarnation = 0) => new(
            DueTick: long.MinValue,
            targetStableId,
            targetIncarnation,
            targetPortOrLane,
            sourceStableId,
            sourcePort,
            eventKind,
            value,
            outputSlot,
            payload,
            sourceIncarnation);
}

public sealed record SchedulerCommand(
    SchedulerCommandKind Kind,
    long ApplyAtTick,
    string TargetStableId = "",
    long TargetIncarnation = 0,
    string TargetPortOrLane = "",
    string SourceStableId = "",
    string SourcePort = "",
    string EventKind = "",
    LogicValue Value = LogicValue.HighImpedance,
    long DueTick = 0,
    string RootId = "",
    string Payload = "",
    long SourceIncarnation = 0,
    SchedulerPhase EventPhase = SchedulerPhase.Deliver)
{
    public static SchedulerCommand RegisterTarget(string targetStableId, long applyAtTick = 0) => new(
        SchedulerCommandKind.RegisterTarget,
        applyAtTick,
        TargetStableId: targetStableId);

    public static SchedulerCommand RemoveTarget(string targetStableId, long applyAtTick = 0) => new(
        SchedulerCommandKind.RemoveTarget,
        applyAtTick,
        TargetStableId: targetStableId);

    public static SchedulerCommand SetPersistentDrive(
        string sourceStableId,
        string sourcePort,
        string targetStableId,
        long targetIncarnation,
        string targetPortOrLane,
        LogicValue value,
        long applyAtTick = 0,
        long sourceIncarnation = 0) => new(
            SchedulerCommandKind.SetPersistentDrive,
            applyAtTick,
            targetStableId,
            targetIncarnation,
            targetPortOrLane,
            sourceStableId,
            sourcePort,
            Value: value,
            SourceIncarnation: sourceIncarnation);

    public static SchedulerCommand ReleaseSource(
        string sourceStableId,
        long applyAtTick = 0,
        long sourceIncarnation = 0) => new(
            SchedulerCommandKind.ReleaseSource,
            applyAtTick,
            SourceStableId: sourceStableId,
            SourceIncarnation: sourceIncarnation);

    public static SchedulerCommand ScheduleEvent(ScheduledEvent scheduledEvent, long applyAtTick = 0) => new(
        SchedulerCommandKind.ScheduleEvent,
        applyAtTick,
        scheduledEvent.TargetStableId,
        scheduledEvent.TargetIncarnation,
        scheduledEvent.TargetPortOrLane,
        scheduledEvent.SourceStableId,
        scheduledEvent.SourcePort,
        scheduledEvent.EventKind,
        scheduledEvent.Value,
        scheduledEvent.Key.DueTick,
        scheduledEvent.TemporalRootId,
        scheduledEvent.Payload,
        scheduledEvent.SourceIncarnation,
        scheduledEvent.Key.Phase);

    public static SchedulerCommand AddTemporalRoot(
        string rootId,
        ScheduledEvent scheduledEvent,
        long applyAtTick = 0) => new(
            SchedulerCommandKind.AddTemporalRoot,
            applyAtTick,
            scheduledEvent.TargetStableId,
            scheduledEvent.TargetIncarnation,
            scheduledEvent.TargetPortOrLane,
            scheduledEvent.SourceStableId,
            scheduledEvent.SourcePort,
            scheduledEvent.EventKind,
            scheduledEvent.Value,
            scheduledEvent.Key.DueTick,
            rootId,
            scheduledEvent.Payload,
            scheduledEvent.SourceIncarnation,
            scheduledEvent.Key.Phase);

    public static SchedulerCommand CancelTemporalRoot(string rootId, long applyAtTick = 0) => new(
        SchedulerCommandKind.CancelTemporalRoot,
        applyAtTick,
        RootId: rootId);
}

public sealed record AcceptedSchedulerCommand(long AcceptedOrdinal, SchedulerCommand Command);

public sealed record SchedulerTemporalRoot(string RootId, ScheduledEvent Event);

public sealed record SchedulerDiagnostic(
    string Code,
    string Message,
    long Tick,
    SchedulerPhase Phase);

public static class SchedulerDiagnosticCodes
{
    public const string ZeroDelayProposal = "SCHEDULER_ZERO_DELAY";
    public const string Reentry = "SCHEDULER_REENTRY";
    public const string SnapshotBoundary = "SCHEDULER_SNAPSHOT_BOUNDARY";
    public const string StaleEvent = "SCHEDULER_STALE_EVENT";
    public const string CancelledTemporalRoot = "SCHEDULER_ROOT_CANCELLED";
    public const string InvalidCommand = "SCHEDULER_COMMAND_INVALID";
    public const string InvalidPhase = "SCHEDULER_PHASE_INVALID";
}

public class SchedulerException : InvalidOperationException
{
    public SchedulerException(SchedulerDiagnostic diagnostic)
        : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }

    public SchedulerDiagnostic Diagnostic { get; }
}

public sealed class SchedulerReentryException : SchedulerException
{
    internal SchedulerReentryException(SchedulerDiagnostic diagnostic)
        : base(diagnostic)
    {
    }
}

public sealed class SchedulerCausalException : SchedulerException
{
    internal SchedulerCausalException(SchedulerDiagnostic diagnostic)
        : base(diagnostic)
    {
    }
}

public sealed class SchedulerSnapshotBoundaryException : SchedulerException
{
    internal SchedulerSnapshotBoundaryException(SchedulerDiagnostic diagnostic)
        : base(diagnostic)
    {
    }
}

public sealed record SchedulerTickTrace(
    long Tick,
    ImmutableArray<ScheduledEvent> DeliveredEvents,
    ImmutableDictionary<SchedulerPortAddress, LogicValue> ResolvedInputs,
    ImmutableArray<SchedulerDiagnostic> Diagnostics,
    string Hash);

public sealed record SchedulerTickResult(
    long Tick,
    ImmutableArray<ScheduledEvent> DeliveredEvents,
    ImmutableDictionary<SchedulerPortAddress, LogicValue> ResolvedInputs,
    ImmutableArray<SchedulerDiagnostic> Diagnostics,
    string Hash);

public sealed record SchedulerTargetSnapshot(
    string StableId,
    long Incarnation,
    bool Active);

public sealed record SchedulerDriveSnapshot(
    SchedulerDriveKey Key,
    LogicValue Value);

public sealed record SchedulerSnapshot(
    long CurrentTick,
    long NextAcceptedOrdinal,
    long NextCausalOrdinal,
    ImmutableArray<ScheduledEvent> PendingEvents,
    ImmutableArray<AcceptedSchedulerCommand> AcceptedCommands,
    ImmutableHashSet<long> AppliedCommandOrdinals,
    ImmutableArray<SchedulerTargetSnapshot> Targets,
    ImmutableArray<SchedulerDriveSnapshot> Drives,
    ImmutableArray<SchedulerTemporalRoot> TemporalRoots,
    ImmutableHashSet<string> CancelledTemporalRoots,
    ImmutableArray<SchedulerTickTrace> Trace);

public sealed class SchedulerEvaluationContext
{
    private readonly DeterministicScheduler _scheduler;
    private readonly List<SchedulerProposal> _proposals = [];

    internal SchedulerEvaluationContext(
        DeterministicScheduler scheduler,
        long tick,
        ImmutableArray<ScheduledEvent> deliveredEvents,
        ImmutableDictionary<SchedulerPortAddress, LogicValue> inputs,
        ImmutableDictionary<SchedulerDriveKey, LogicValue> drives)
    {
        _scheduler = scheduler;
        Tick = tick;
        DeliveredEvents = deliveredEvents;
        Inputs = inputs;
        Drives = drives;
    }

    public long Tick { get; }

    public ImmutableArray<ScheduledEvent> DeliveredEvents { get; }

    public ImmutableDictionary<SchedulerPortAddress, LogicValue> Inputs { get; }

    public ImmutableDictionary<SchedulerDriveKey, LogicValue> Drives { get; }

    public void Propose(SchedulerProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        _proposals.Add(proposal);
    }

    public SchedulerSnapshot RequestSnapshot() => _scheduler.CaptureSnapshot();

    public SchedulerTickResult Step() => _scheduler.Step();

    internal IReadOnlyList<SchedulerProposal> Proposals => _proposals;
}
