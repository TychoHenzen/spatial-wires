using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace SpatialCircuits.Core;

public sealed class DeterministicScheduler
{
    private readonly SortedSet<ScheduledEvent> _pendingEvents = new(ScheduledEventComparer.Instance);
    private readonly List<AcceptedSchedulerCommand> _acceptedCommands = [];
    private readonly HashSet<long> _appliedCommandOrdinals = [];
    private readonly Dictionary<string, TargetState> _targets = new(StringComparer.Ordinal);
    private readonly Dictionary<SchedulerDriveKey, LogicValue> _drives = [];
    private readonly Dictionary<string, SchedulerTemporalRoot> _temporalRoots = new(StringComparer.Ordinal);
    private readonly HashSet<string> _cancelledTemporalRoots = new(StringComparer.Ordinal);
    private readonly List<AcceptedSchedulerCommand> _pendingInvalidations = [];
    private readonly List<SchedulerTickTrace> _trace = [];
    private Func<SchedulerEvaluationContext, IEnumerable<SchedulerProposal>?>? _evaluator;
    private ImmutableDictionary<SchedulerPortAddress, LogicValue> _currentInputs =
        ImmutableDictionary<SchedulerPortAddress, LogicValue>.Empty;
    private long _nextAcceptedOrdinal = 1;
    private long _nextCausalOrdinal = 1;
    private bool _isStepping;

    public DeterministicScheduler(
        Func<SchedulerEvaluationContext, IEnumerable<SchedulerProposal>?>? evaluator = null)
    {
        _evaluator = evaluator;
    }

    public long CurrentTick { get; private set; }

    public SchedulerPhase CurrentPhase { get; private set; } = SchedulerPhase.Idle;

    public bool IsStepping => _isStepping;

    public long NextAcceptedOrdinal => _nextAcceptedOrdinal;

    public long NextCausalOrdinal => _nextCausalOrdinal;

    public IReadOnlyList<ScheduledEvent> PendingEvents => _pendingEvents.ToArray();

    public IReadOnlyDictionary<SchedulerDriveKey, LogicValue> PersistentDrives =>
        _drives.ToImmutableDictionary();

    public IReadOnlyDictionary<SchedulerPortAddress, LogicValue> CurrentInputs => _currentInputs;

    public IReadOnlyList<SchedulerTickTrace> Trace => _trace.ToArray();

    public IReadOnlyList<AcceptedSchedulerCommand> AcceptedCommands => _acceptedCommands.ToArray();

    public SchedulerTargetHandle RegisterTarget(string stableId)
    {
        EnsureCanMutateOutsideStep();
        return RegisterTargetCore(stableId);
    }

    public void RemoveTarget(string stableId)
    {
        EnsureCanMutateOutsideStep();
        RemoveTargetCore(stableId, []);
    }

    public bool TryGetTarget(string stableId, out SchedulerTargetHandle target)
    {
        if (_targets.TryGetValue(stableId, out var state) && state.Active)
        {
            target = new SchedulerTargetHandle(stableId, state.Incarnation);
            return true;
        }

        target = default;
        return false;
    }

    public SchedulerTargetHandle GetTarget(string stableId) =>
        TryGetTarget(stableId, out var target)
            ? target
            : throw new KeyNotFoundException($"Target '{stableId}' is not active.");

    public long Accept(SchedulerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureCanMutateOutsideStep();
        if (command.ApplyAtTick < CurrentTick)
        {
            throw InvalidCommand($"Command is scheduled for past tick {command.ApplyAtTick}.");
        }

        ValidateCommandPhase(command);

        var accepted = new AcceptedSchedulerCommand(_nextAcceptedOrdinal++, command);
        _acceptedCommands.Add(accepted);
        return accepted.AcceptedOrdinal;
    }

    public void SeedEvent(ScheduledEvent scheduledEvent)
    {
        ArgumentNullException.ThrowIfNull(scheduledEvent);
        EnsureCanMutateOutsideStep();
        AddEventCore(NormalizeEvent(scheduledEvent, allowCurrentTick: true));
    }

    public void ScheduleEvent(ScheduledEvent scheduledEvent) => SeedEvent(scheduledEvent);

    public void AddTemporalRoot(SchedulerTemporalRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        EnsureCanMutateOutsideStep();
        AddTemporalRootCore(root);
    }

    public void CancelTemporalRoot(string rootId)
    {
        EnsureCanMutateOutsideStep();
        CancelTemporalRootCore(rootId);
    }

    public SchedulerTickResult Step()
    {
        if (_isStepping)
        {
            throw Reentry("A scheduler step cannot begin while another step is active.");
        }

        var rollback = CaptureSnapshotCore();
        var tick = CurrentTick;
        var delivered = ImmutableArray<ScheduledEvent>.Empty;
        var resolvedInputs = ImmutableDictionary<SchedulerPortAddress, LogicValue>.Empty;
        var diagnostics = new List<SchedulerDiagnostic>();

        _isStepping = true;
        try
        {
            CurrentPhase = SchedulerPhase.ApplyCommands;
            ApplyAcceptedCommands(diagnostics);

            CurrentPhase = SchedulerPhase.Invalidate;
            ApplyInvalidations(diagnostics);

            CurrentPhase = SchedulerPhase.Deliver;
            delivered = DeliverDueEvents(diagnostics);

            CurrentPhase = SchedulerPhase.ResolveDrives;
            resolvedInputs = ResolveInputs();
            _currentInputs = resolvedInputs;

            CurrentPhase = SchedulerPhase.Evaluate;
            var proposals = Evaluate(resolvedInputs, delivered);

            CurrentPhase = SchedulerPhase.MainThreadBarrier;

            CurrentPhase = SchedulerPhase.ReduceProposals;
            ReduceProposals(proposals);

            CurrentPhase = SchedulerPhase.Commit;

            CurrentPhase = SchedulerPhase.Record;
            var hash = ComputeHash(tick, delivered, resolvedInputs, diagnostics);
            var trace = new SchedulerTickTrace(
                tick,
                delivered,
                resolvedInputs,
                diagnostics.ToImmutableArray(),
                hash);
            _trace.Add(trace);

            CurrentPhase = SchedulerPhase.Advance;
            CurrentTick++;
            return new SchedulerTickResult(
                tick,
                delivered,
                resolvedInputs,
                diagnostics.ToImmutableArray(),
                hash);
        }
        catch
        {
            RestoreSnapshotCore(rollback);
            throw;
        }
        finally
        {
            _pendingInvalidations.Clear();
            _isStepping = false;
            CurrentPhase = SchedulerPhase.Idle;
        }
    }

    public SchedulerTickResult Step(
        Func<SchedulerEvaluationContext, IEnumerable<SchedulerProposal>?> evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        if (_isStepping)
        {
            throw Reentry("A scheduler step cannot begin while another step is active.");
        }

        var previous = _evaluator;
        _evaluator = evaluator;
        try
        {
            return Step();
        }
        finally
        {
            _evaluator = previous;
        }
    }

    public SchedulerSnapshot CaptureSnapshot()
    {
        if (_isStepping)
        {
            throw SnapshotBoundary(
                "Causal snapshots are available only after the final phase of a microtick.");
        }

        return CaptureSnapshotCore();
    }

    public void RestoreSnapshot(SchedulerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_isStepping)
        {
            throw Reentry("A snapshot cannot be restored while a scheduler step is active.");
        }

        RestoreSnapshotCore(snapshot);
    }

    public static DeterministicScheduler Restore(
        SchedulerSnapshot snapshot,
        Func<SchedulerEvaluationContext, IEnumerable<SchedulerProposal>?>? evaluator = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var scheduler = new DeterministicScheduler(evaluator);
        scheduler.RestoreSnapshot(snapshot);
        return scheduler;
    }

    public static DeterministicScheduler Replay(
        IEnumerable<SchedulerCommand> commands,
        Func<SchedulerEvaluationContext, IEnumerable<SchedulerProposal>?>? evaluator = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var scheduler = new DeterministicScheduler(evaluator);
        foreach (var command in commands)
        {
            scheduler.Accept(command);
        }

        return scheduler;
    }

    public void ReplayCommands(IEnumerable<SchedulerCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (var command in commands)
        {
            Accept(command);
        }
    }

    private IReadOnlyList<SchedulerProposal> Evaluate(
        ImmutableDictionary<SchedulerPortAddress, LogicValue> resolvedInputs,
        ImmutableArray<ScheduledEvent> delivered)
    {
        if (_evaluator is null)
        {
            return [];
        }

        var context = new SchedulerEvaluationContext(
            this,
            CurrentTick,
            delivered,
            resolvedInputs,
            _drives.ToImmutableDictionary());
        var returned = _evaluator(context) ?? [];
        return context.Proposals.Concat(returned).ToArray();
    }

    private void ApplyAcceptedCommands(ICollection<SchedulerDiagnostic> diagnostics)
    {
        foreach (var accepted in _acceptedCommands
                     .Where(item =>
                         !_appliedCommandOrdinals.Contains(item.AcceptedOrdinal) &&
                         item.Command.ApplyAtTick <= CurrentTick)
                     .OrderBy(item => item.AcceptedOrdinal))
        {
            if (accepted.Command.Kind == SchedulerCommandKind.RemoveTarget)
            {
                _pendingInvalidations.Add(accepted);
            }
            else
            {
                ApplyCommand(accepted, diagnostics);
            }

            _appliedCommandOrdinals.Add(accepted.AcceptedOrdinal);
        }
    }

    private void ApplyInvalidations(ICollection<SchedulerDiagnostic> diagnostics)
    {
        foreach (var accepted in _pendingInvalidations)
        {
            ApplyCommand(accepted, diagnostics);
        }
    }

    private void ApplyCommand(
        AcceptedSchedulerCommand accepted,
        ICollection<SchedulerDiagnostic> diagnostics)
    {
        var command = accepted.Command;
        switch (command.Kind)
        {
            case SchedulerCommandKind.RegisterTarget:
                RegisterTargetCore(command.TargetStableId);
                break;
            case SchedulerCommandKind.RemoveTarget:
                RemoveTargetCore(command.TargetStableId, diagnostics);
                break;
            case SchedulerCommandKind.SetPersistentDrive:
                SetDriveCore(command);
                break;
            case SchedulerCommandKind.ReleaseSource:
                ReleaseSourceCore(command);
                break;
            case SchedulerCommandKind.ScheduleEvent:
                AddEventCore(CreateCommandEvent(
                    command,
                    temporalRootId: "",
                    causalOrdinal: accepted.AcceptedOrdinal));
                break;
            case SchedulerCommandKind.AddTemporalRoot:
                var rootEvent = CreateCommandEvent(
                    command,
                    command.RootId,
                    accepted.AcceptedOrdinal);
                AddTemporalRootCore(new SchedulerTemporalRoot(command.RootId, rootEvent));
                break;
            case SchedulerCommandKind.CancelTemporalRoot:
                CancelTemporalRootCore(command.RootId);
                break;
            default:
                throw InvalidCommand($"Unsupported scheduler command '{command.Kind}'.");
        }
    }

    private ImmutableArray<ScheduledEvent> DeliverDueEvents(
        ICollection<SchedulerDiagnostic> diagnostics)
    {
        var due = _pendingEvents
            .Where(scheduledEvent => scheduledEvent.Key.DueTick == CurrentTick)
            .ToArray();
        foreach (var scheduledEvent in due)
        {
            _pendingEvents.Remove(scheduledEvent);
        }

        var delivered = ImmutableArray.CreateBuilder<ScheduledEvent>();
        foreach (var scheduledEvent in due)
        {
            if (!string.IsNullOrEmpty(scheduledEvent.TemporalRootId) &&
                _cancelledTemporalRoots.Contains(scheduledEvent.TemporalRootId))
            {
                diagnostics.Add(new SchedulerDiagnostic(
                    SchedulerDiagnosticCodes.CancelledTemporalRoot,
                    $"Temporal root '{scheduledEvent.TemporalRootId}' was cancelled before delivery.",
                    CurrentTick,
                    CurrentPhase));
                continue;
            }

            if (!IsCurrentTarget(scheduledEvent.Key))
            {
                diagnostics.Add(new SchedulerDiagnostic(
                    SchedulerDiagnosticCodes.StaleEvent,
                    $"Event for target '{scheduledEvent.TargetStableId}' incarnation " +
                    $"{scheduledEvent.TargetIncarnation} was ignored.",
                    CurrentTick,
                    CurrentPhase));
                continue;
            }

            ApplyDeliveredEvent(scheduledEvent);
            delivered.Add(scheduledEvent);
            if (!string.IsNullOrEmpty(scheduledEvent.TemporalRootId))
            {
                _temporalRoots.Remove(scheduledEvent.TemporalRootId);
            }
        }

        return delivered.ToImmutable();
    }

    private void ApplyDeliveredEvent(ScheduledEvent scheduledEvent)
    {
        if (scheduledEvent.EventKind is "drive" or "persistent-drive")
        {
            var key = new SchedulerDriveKey(
                scheduledEvent.SourceStableId,
                scheduledEvent.SourceIncarnation,
                scheduledEvent.SourcePort,
                scheduledEvent.TargetStableId,
                scheduledEvent.TargetIncarnation,
                scheduledEvent.TargetPortOrLane);
            if (scheduledEvent.Value == LogicValue.HighImpedance)
            {
                _drives.Remove(key);
            }
            else
            {
                _drives[key] = scheduledEvent.Value;
            }
        }
        else if (scheduledEvent.EventKind == "drive-release")
        {
            _drives.Remove(new SchedulerDriveKey(
                scheduledEvent.SourceStableId,
                scheduledEvent.SourceIncarnation,
                scheduledEvent.SourcePort,
                scheduledEvent.TargetStableId,
                scheduledEvent.TargetIncarnation,
                scheduledEvent.TargetPortOrLane));
        }
    }

    private ImmutableDictionary<SchedulerPortAddress, LogicValue> ResolveInputs()
    {
        var resolved = new Dictionary<SchedulerPortAddress, LogicValue>();
        foreach (var group in _drives
                     .Where(pair => IsCurrentTarget(pair.Key.TargetStableId, pair.Key.TargetIncarnation))
                     .GroupBy(pair => new SchedulerPortAddress(
                         pair.Key.TargetStableId,
                         pair.Key.TargetIncarnation,
                         pair.Key.TargetPortOrLane)))
        {
            resolved[group.Key] = DriveResolver.Resolve(group.Select(pair => pair.Value));
        }

        return resolved.ToImmutableDictionary();
    }

    private void ReduceProposals(IReadOnlyList<SchedulerProposal> proposals)
    {
        var normalized = proposals
            .Select(NormalizeProposal)
            .ToArray();
        foreach (var proposal in normalized)
        {
            if (proposal.DueTick <= CurrentTick)
            {
                throw ZeroDelay($"Causal proposal must be scheduled after tick {CurrentTick}.");
            }
        }

        var ordered = normalized
            .OrderBy(proposal => proposal.DueTick)
            .ThenBy(proposal => proposal.TargetStableId, StringComparer.Ordinal)
            .ThenBy(proposal => proposal.TargetIncarnation)
            .ThenBy(proposal => proposal.TargetPortOrLane, StringComparer.Ordinal)
            .ThenBy(proposal => proposal.SourceStableId, StringComparer.Ordinal)
            .ThenBy(proposal => proposal.SourcePort, StringComparer.Ordinal)
            .ThenBy(proposal => proposal.OutputSlot)
            .ThenBy(proposal => proposal.EventKind, StringComparer.Ordinal)
            .ThenBy(proposal => proposal.Value)
            .ThenBy(proposal => proposal.Payload, StringComparer.Ordinal)
            .ToArray();

        var nextCausalOrdinal = _nextCausalOrdinal;
        var events = new List<ScheduledEvent>(ordered.Length);
        foreach (var proposal in ordered)
        {
            var key = new ScheduledEventKey(
                proposal.DueTick,
                SchedulerPhase.Deliver,
                proposal.TargetStableId,
                proposal.TargetIncarnation,
                proposal.TargetPortOrLane,
                proposal.SourceStableId,
                proposal.SourcePort,
                proposal.EventKind,
                nextCausalOrdinal++);
            var sourceIncarnation = proposal.SourceIncarnation;
            if (sourceIncarnation == 0 &&
                _targets.TryGetValue(proposal.SourceStableId, out var source) &&
                source.Active)
            {
                sourceIncarnation = source.Incarnation;
            }

            events.Add(new ScheduledEvent(
                key,
                proposal.Value,
                proposal.Payload,
                SourceIncarnation: sourceIncarnation));
        }

        foreach (var scheduledEvent in events)
        {
            AddEventCore(scheduledEvent);
        }

        _nextCausalOrdinal = nextCausalOrdinal;
    }

    private SchedulerProposal NormalizeProposal(SchedulerProposal proposal) =>
        proposal.DueTick == long.MinValue
            ? proposal with { DueTick = checked(CurrentTick + 1) }
            : proposal;

    private SchedulerTargetHandle RegisterTargetCore(string stableId)
    {
        ValidateStableId(stableId, nameof(stableId));
        if (_targets.TryGetValue(stableId, out var existing) && existing.Active)
        {
            return new SchedulerTargetHandle(stableId, existing.Incarnation);
        }

        var incarnation = existing is null ? 1 : checked(existing.Incarnation + 1);
        _targets[stableId] = new TargetState(incarnation, true);
        return new SchedulerTargetHandle(stableId, incarnation);
    }

    private void RemoveTargetCore(
        string stableId,
        ICollection<SchedulerDiagnostic> diagnostics)
    {
        ValidateStableId(stableId, nameof(stableId));
        if (!_targets.TryGetValue(stableId, out var state) || !state.Active)
        {
            return;
        }

        var outgoing = _drives
            .Where(pair =>
                pair.Key.SourceStableId == stableId &&
                pair.Key.SourceIncarnation == state.Incarnation)
            .OrderBy(pair => pair.Key.TargetStableId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.TargetIncarnation)
            .ThenBy(pair => pair.Key.TargetPortOrLane, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.SourcePort, StringComparer.Ordinal)
            .ToArray();
        foreach (var drive in outgoing)
        {
            var releaseKey = new ScheduledEventKey(
                CurrentTick,
                SchedulerPhase.Deliver,
                drive.Key.TargetStableId,
                drive.Key.TargetIncarnation,
                drive.Key.TargetPortOrLane,
                drive.Key.SourceStableId,
                drive.Key.SourcePort,
                "drive-release",
                _nextCausalOrdinal++);
            AddEventCore(new ScheduledEvent(
                releaseKey,
                SourceIncarnation: state.Incarnation));
        }

        foreach (var key in _drives.Keys
                     .Where(key =>
                         key.SourceStableId == stableId || key.TargetStableId == stableId)
                     .ToArray())
        {
            _drives.Remove(key);
        }

        _targets[stableId] = state with { Active = false };
        diagnostics.Add(new SchedulerDiagnostic(
            "SCHEDULER_TARGET_REMOVED",
            $"Target '{stableId}' incarnation {state.Incarnation} was invalidated.",
            CurrentTick,
            CurrentPhase));
    }

    private void SetDriveCore(SchedulerCommand command)
    {
        var incarnation = command.TargetIncarnation;
        if (incarnation == 0 && _targets.TryGetValue(command.TargetStableId, out var target))
        {
            incarnation = target.Incarnation;
        }

        if (incarnation == 0 || !IsCurrentTarget(command.TargetStableId, incarnation))
        {
            throw InvalidCommand(
                $"Drive target '{command.TargetStableId}' incarnation {incarnation} is not active.");
        }

        var sourceIncarnation = command.SourceIncarnation;
        if (sourceIncarnation == 0 &&
            _targets.TryGetValue(command.SourceStableId, out var source) &&
            source.Active)
        {
            sourceIncarnation = source.Incarnation;
        }

        if (sourceIncarnation != 0 &&
            _targets.TryGetValue(command.SourceStableId, out source) &&
            (!source.Active || source.Incarnation != sourceIncarnation))
        {
            throw InvalidCommand(
                $"Drive source '{command.SourceStableId}' incarnation {sourceIncarnation} is not active.");
        }

        var key = new SchedulerDriveKey(
            command.SourceStableId,
            sourceIncarnation,
            command.SourcePort,
            command.TargetStableId,
            incarnation,
            command.TargetPortOrLane);
        if (command.Value == LogicValue.HighImpedance)
        {
            _drives.Remove(key);
        }
        else
        {
            _drives[key] = command.Value;
        }
    }

    private void ReleaseSourceCore(SchedulerCommand command)
    {
        var sourceIncarnation = command.SourceIncarnation;
        if (sourceIncarnation == 0 &&
            _targets.TryGetValue(command.SourceStableId, out var source) &&
            source.Active)
        {
            sourceIncarnation = source.Incarnation;
        }

        foreach (var key in _drives.Keys
                     .Where(key =>
                         key.SourceStableId == command.SourceStableId &&
                         (sourceIncarnation == 0 ||
                          key.SourceIncarnation == sourceIncarnation))
                     .ToArray())
        {
            _drives.Remove(key);
        }
    }

    private void AddTemporalRootCore(SchedulerTemporalRoot root)
    {
        ValidateStableId(root.RootId, nameof(root.RootId));
        RemovePendingTemporalRootEvents(root.RootId);
        var scheduledEvent = NormalizeEvent(root.Event with { TemporalRootId = root.RootId }, true);
        _temporalRoots[root.RootId] = new SchedulerTemporalRoot(root.RootId, scheduledEvent);
        _cancelledTemporalRoots.Remove(root.RootId);
        AddEventCore(scheduledEvent);
    }

    private void CancelTemporalRootCore(string rootId)
    {
        ValidateStableId(rootId, nameof(rootId));
        RemovePendingTemporalRootEvents(rootId);
        _temporalRoots.Remove(rootId);
        _cancelledTemporalRoots.Add(rootId);
    }

    private void RemovePendingTemporalRootEvents(string rootId)
    {
        foreach (var scheduledEvent in _pendingEvents
                     .Where(scheduledEvent => scheduledEvent.TemporalRootId == rootId)
                     .ToArray())
        {
            _pendingEvents.Remove(scheduledEvent);
        }
    }

    private ScheduledEvent CreateCommandEvent(
        SchedulerCommand command,
        string temporalRootId,
        long causalOrdinal)
    {
        var key = new ScheduledEventKey(
            command.DueTick,
            command.EventPhase,
            command.TargetStableId,
            command.TargetIncarnation,
            command.TargetPortOrLane,
            command.SourceStableId,
            command.SourcePort,
            command.EventKind,
            causalOrdinal);
        return NormalizeEvent(
            new ScheduledEvent(
                key,
                command.Value,
                command.Payload,
                temporalRootId,
                command.SourceIncarnation),
            true);
    }

    private ScheduledEvent NormalizeEvent(ScheduledEvent scheduledEvent, bool allowCurrentTick)
    {
        if (scheduledEvent.Key.Phase != SchedulerPhase.Deliver)
        {
            throw InvalidPhase(
                $"Scheduled event phase '{scheduledEvent.Key.Phase}' is not supported by this scheduler.");
        }

        if (!allowCurrentTick && scheduledEvent.Key.DueTick <= CurrentTick)
        {
            throw ZeroDelay($"Event must be scheduled after tick {CurrentTick}.");
        }

        if (scheduledEvent.Key.DueTick < CurrentTick)
        {
            throw InvalidCommand($"Event is scheduled for past tick {scheduledEvent.Key.DueTick}.");
        }

        var key = scheduledEvent.Key;
        if (key.TargetIncarnation == 0 &&
            _targets.TryGetValue(key.TargetStableId, out var target))
        {
            key = key with { TargetIncarnation = target.Incarnation };
        }

        var sourceIncarnation = scheduledEvent.SourceIncarnation;
        if (sourceIncarnation == 0 &&
            _targets.TryGetValue(scheduledEvent.SourceStableId, out var source) &&
            source.Active)
        {
            sourceIncarnation = source.Incarnation;
        }

        if (key.CausalOrdinal <= 0)
        {
            key = key with { CausalOrdinal = _nextCausalOrdinal++ };
        }
        else if (key.CausalOrdinal >= _nextCausalOrdinal)
        {
            _nextCausalOrdinal = checked(key.CausalOrdinal + 1);
        }

        return scheduledEvent with
        {
            Key = key,
            SourceIncarnation = sourceIncarnation
        };
    }

    private void AddEventCore(ScheduledEvent scheduledEvent)
    {
        if (!_pendingEvents.Add(scheduledEvent))
        {
            throw InvalidCommand($"Scheduled event key '{scheduledEvent.Key}' is duplicated.");
        }
    }

    private bool IsCurrentTarget(ScheduledEventKey key) =>
        string.IsNullOrEmpty(key.TargetStableId) ||
        IsCurrentTarget(key.TargetStableId, key.TargetIncarnation);

    private bool IsCurrentTarget(string stableId, long incarnation) =>
        _targets.TryGetValue(stableId, out var state) &&
        state.Active &&
        state.Incarnation == incarnation;

    private SchedulerSnapshot CaptureSnapshotCore() => new(
        CurrentTick,
        _nextAcceptedOrdinal,
        _nextCausalOrdinal,
        _pendingEvents.ToImmutableArray(),
        _acceptedCommands.ToImmutableArray(),
        _appliedCommandOrdinals.ToImmutableHashSet(),
        _targets
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SchedulerTargetSnapshot(
                pair.Key,
                pair.Value.Incarnation,
                pair.Value.Active))
            .ToImmutableArray(),
        _drives
            .OrderBy(pair => pair.Key.TargetStableId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.TargetIncarnation)
            .ThenBy(pair => pair.Key.TargetPortOrLane, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.SourceStableId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.SourcePort, StringComparer.Ordinal)
            .Select(pair => new SchedulerDriveSnapshot(pair.Key, pair.Value))
            .ToImmutableArray(),
        _temporalRoots
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToImmutableArray(),
        _cancelledTemporalRoots.ToImmutableHashSet(StringComparer.Ordinal),
        _trace.ToImmutableArray());

    private void RestoreSnapshotCore(SchedulerSnapshot snapshot)
    {
        foreach (var scheduledEvent in snapshot.PendingEvents
                     .Concat(snapshot.TemporalRoots.Select(root => root.Event)))
        {
            if (scheduledEvent.Key.Phase != SchedulerPhase.Deliver)
            {
                throw InvalidPhase(
                    $"Snapshot contains unsupported event phase '{scheduledEvent.Key.Phase}'.");
            }
        }

        CurrentTick = snapshot.CurrentTick;
        _nextAcceptedOrdinal = snapshot.NextAcceptedOrdinal;
        _nextCausalOrdinal = snapshot.NextCausalOrdinal;
        _pendingEvents.Clear();
        foreach (var scheduledEvent in snapshot.PendingEvents)
        {
            _pendingEvents.Add(scheduledEvent);
        }

        _acceptedCommands.Clear();
        _acceptedCommands.AddRange(snapshot.AcceptedCommands);
        _appliedCommandOrdinals.Clear();
        _appliedCommandOrdinals.UnionWith(snapshot.AppliedCommandOrdinals);

        _targets.Clear();
        foreach (var target in snapshot.Targets)
        {
            _targets.Add(target.StableId, new TargetState(target.Incarnation, target.Active));
        }

        _drives.Clear();
        foreach (var drive in snapshot.Drives)
        {
            _drives.Add(drive.Key, drive.Value);
        }

        _temporalRoots.Clear();
        foreach (var root in snapshot.TemporalRoots)
        {
            _temporalRoots.Add(root.RootId, root);
        }

        _cancelledTemporalRoots.Clear();
        _cancelledTemporalRoots.UnionWith(snapshot.CancelledTemporalRoots);
        _trace.Clear();
        _trace.AddRange(snapshot.Trace);
        _currentInputs = snapshot.Trace.Length == 0
            ? ImmutableDictionary<SchedulerPortAddress, LogicValue>.Empty
            : snapshot.Trace[^1].ResolvedInputs;
    }

    private string ComputeHash(
        long tick,
        IEnumerable<ScheduledEvent> delivered,
        IEnumerable<KeyValuePair<SchedulerPortAddress, LogicValue>> resolvedInputs,
        IEnumerable<SchedulerDiagnostic> diagnostics)
    {
        var builder = new StringBuilder();
        builder.Append("tick=").Append(tick).Append('\n');
        builder.Append("next-accepted=").Append(_nextAcceptedOrdinal).Append('\n');
        builder.Append("next-causal=").Append(_nextCausalOrdinal).Append('\n');
        foreach (var accepted in _acceptedCommands.OrderBy(item => item.AcceptedOrdinal))
        {
            builder.Append("command=").Append(accepted.AcceptedOrdinal).Append('|');
            AppendCommand(builder, accepted.Command);
            builder.Append("|applied=").Append(_appliedCommandOrdinals.Contains(accepted.AcceptedOrdinal)).Append('\n');
        }

        foreach (var target in _targets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append("target=").Append(target.Key).Append('|')
                .Append(target.Value.Incarnation).Append('|')
                .Append(target.Value.Active).Append('\n');
        }

        foreach (var drive in _drives
                     .OrderBy(pair => pair.Key.TargetStableId, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.TargetIncarnation)
                     .ThenBy(pair => pair.Key.TargetPortOrLane, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.SourceStableId, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.SourcePort, StringComparer.Ordinal))
        {
            builder.Append("drive=").Append(drive.Key).Append('|').Append((byte)drive.Value).Append('\n');
        }

        foreach (var scheduledEvent in _pendingEvents)
        {
            builder.Append("pending=").Append(scheduledEvent.Key).Append('|')
                .Append((byte)scheduledEvent.Value).Append('|')
                .Append(scheduledEvent.Payload).Append('|')
                .Append(scheduledEvent.TemporalRootId).Append('|')
                .Append(scheduledEvent.SourceIncarnation).Append('\n');
        }

        foreach (var root in _temporalRoots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append("root=").Append(root.Key).Append('|').Append(root.Value.Event.Key).Append('\n');
        }

        foreach (var root in _cancelledTemporalRoots.OrderBy(root => root, StringComparer.Ordinal))
        {
            builder.Append("cancelled-root=").Append(root).Append('\n');
        }

        foreach (var scheduledEvent in delivered.OrderBy(item => item.Key))
        {
            builder.Append("delivered=").Append(scheduledEvent.Key).Append('|')
                .Append((byte)scheduledEvent.Value).Append('|')
                .Append(scheduledEvent.Payload).Append('|')
                .Append(scheduledEvent.SourceIncarnation).Append('\n');
        }

        foreach (var input in resolvedInputs
                     .OrderBy(pair => pair.Key.TargetStableId, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.TargetIncarnation)
                     .ThenBy(pair => pair.Key.PortOrLane, StringComparer.Ordinal))
        {
            builder.Append("input=").Append(input.Key).Append('|').Append((byte)input.Value).Append('\n');
        }

        foreach (var diagnostic in diagnostics)
        {
            builder.Append("diagnostic=").Append(diagnostic.Code).Append('|').Append(diagnostic.Message).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void AppendCommand(StringBuilder builder, SchedulerCommand command)
    {
        builder.Append((byte)command.Kind).Append('|')
            .Append(command.ApplyAtTick).Append('|')
            .Append(command.TargetStableId).Append('|')
            .Append(command.TargetIncarnation).Append('|')
            .Append(command.TargetPortOrLane).Append('|')
            .Append(command.SourceStableId).Append('|')
            .Append(command.SourcePort).Append('|')
            .Append(command.EventKind).Append('|')
            .Append((byte)command.Value).Append('|')
            .Append(command.DueTick).Append('|')
            .Append(command.RootId).Append('|')
            .Append(command.Payload).Append('|')
            .Append(command.SourceIncarnation).Append('|')
            .Append((byte)command.EventPhase);
    }

    private void EnsureCanMutateOutsideStep()
    {
        if (_isStepping)
        {
            throw Reentry("Causal state cannot be mutated directly during a scheduler step.");
        }
    }

    private static void ValidateStableId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    }

    private void ValidateCommandPhase(SchedulerCommand command)
    {
        if ((command.Kind is SchedulerCommandKind.ScheduleEvent or SchedulerCommandKind.AddTemporalRoot) &&
            command.EventPhase != SchedulerPhase.Deliver)
        {
            throw InvalidPhase(
                $"Scheduled command phase '{command.EventPhase}' is not supported by this scheduler.");
        }
    }

    private SchedulerException InvalidCommand(string message) => new SchedulerException(new SchedulerDiagnostic(
        SchedulerDiagnosticCodes.InvalidCommand,
        message,
        CurrentTick,
        CurrentPhase));

    private SchedulerException InvalidPhase(string message) => new SchedulerException(new SchedulerDiagnostic(
        SchedulerDiagnosticCodes.InvalidPhase,
        message,
        CurrentTick,
        CurrentPhase));

    private SchedulerReentryException Reentry(string message) => new(new SchedulerDiagnostic(
        SchedulerDiagnosticCodes.Reentry,
        message,
        CurrentTick,
        CurrentPhase));

    private SchedulerCausalException ZeroDelay(string message) => new(new SchedulerDiagnostic(
        SchedulerDiagnosticCodes.ZeroDelayProposal,
        message,
        CurrentTick,
        CurrentPhase));

    private SchedulerSnapshotBoundaryException SnapshotBoundary(string message) => new(new SchedulerDiagnostic(
        SchedulerDiagnosticCodes.SnapshotBoundary,
        message,
        CurrentTick,
        CurrentPhase));

    private sealed record TargetState(long Incarnation, bool Active);

    private sealed class ScheduledEventComparer : IComparer<ScheduledEvent>
    {
        public static ScheduledEventComparer Instance { get; } = new();

        public int Compare(ScheduledEvent? x, ScheduledEvent? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return x.Key.CompareTo(y.Key);
        }
    }
}
