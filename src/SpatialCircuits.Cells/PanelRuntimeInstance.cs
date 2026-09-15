using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpatialCircuits.Core;

namespace SpatialCircuits.Cells;

public sealed record ProbeSample(ComponentId ProbeId, long Tick, LogicValue Value)
{
}

public sealed record PanelTickResult(
    long Tick,
    string Hash,
    ImmutableArray<ProbeSample> ProbeSamples,
    ImmutableSortedDictionary<string, string> Outputs,
    ImmutableArray<SchedulerDiagnostic> Diagnostics)
{
}

public sealed class PanelRuntimeInstance
{
    private const string NandCommitEvent = "nand-commit";
    private const string FlipFlopCommitEvent = "dff-commit";
    private readonly DeterministicScheduler _scheduler;
    private readonly CustomCellRuleRegistry _customCellRuleRegistry;
    private readonly List<ProbeSample> _probeHistory = [];
    private readonly List<PanelTargetIncarnationSnapshot> _targetIncarnationTimeline = [];
    private PanelCellDefinition?[] _cells;
    private RuntimeCellState[] _states;
    private CustomCellRuleBinding?[] _customRules;
    private SchedulerTargetHandle?[] _targetHandles;
    private ImmutableArray<CellPortSpec>[] _portSpecs;
    private ImmutableArray<RuntimeConnection>[] _outgoing;
    private Dictionary<PortId, int[]> _inputPortCells = [];
    private Dictionary<PortId, int> _outputPortCells = [];
    private RuntimeCellState[]? _workingStates;
    private bool _isStepping;

    public PanelRuntimeInstance(
        PanelDefinition definition,
        CustomCellRuleRegistry? customCellRules = null,
        long initialTick = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Id = definition.Id;
        Width = definition.Width;
        Height = definition.Height;
        _cells = definition.Cells.ToArray();
        _customCellRuleRegistry = customCellRules ?? CustomCellRuleRegistry.Empty;
        _customRules = ResolveCustomRules(_cells);
        _states = _cells
            .Select((cell, index) => CreateRuntimeCellState(cell, _customRules[index]))
            .ToArray();
        _targetHandles = new SchedulerTargetHandle?[_cells.Length];
        _scheduler = new DeterministicScheduler(initialTick, Evaluate);
        for (var index = 0; index < _cells.Length; index++)
        {
            if (IsActive(_cells[index]))
            {
                var handle = _scheduler.RegisterTarget(_cells[index]!.Id.Value);
                _targetHandles[index] = handle;
                _targetIncarnationTimeline.Add(CreateTargetIncarnationSnapshot(
                    _cells[index]!,
                    handle.Incarnation,
                    true));
            }
        }

        (_portSpecs, _outgoing) = BuildTopology(_cells, _customRules);
        RebuildPanelPorts();
    }

    public CircuitId Id { get; }

    public int Width { get; }

    public int Height { get; }

    public long CurrentTick => _scheduler.CurrentTick;

    public ImmutableArray<ProbeSample> ProbeHistory => _probeHistory.ToImmutableArray();

    public PanelCellDefinition? GetCell(GridCoordinate location) => _cells[IndexOf(location)];

    public void SetInput(PortId portId, LogicValue value)
    {
        EnsureNotStepping();
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Input must be a four-state logic value.");
        }

        if (!_inputPortCells.TryGetValue(portId, out var cells))
        {
            throw new KeyNotFoundException($"Panel input '{portId}' is not defined.");
        }

        foreach (var index in cells)
        {
            _states[index].ExternalInput = value;
        }
    }

    public LogicValue GetOutput(PortId portId)
    {
        if (!_outputPortCells.TryGetValue(portId, out var index))
        {
            throw new KeyNotFoundException($"Panel output '{portId}' is not defined.");
        }

        return _states[index].ObservedValue;
    }

    public PanelTickResult Step()
    {
        EnsureNotStepping();
        _isStepping = true;
        var workingStates = _states.Select(state => state.Clone()).ToArray();
        _workingStates = workingStates;
        try
        {
            var result = _scheduler.Step();
            _states = workingStates;
            var probes = ImmutableArray.CreateBuilder<ProbeSample>();
            for (var index = 0; index < _cells.Length; index++)
            {
                var cell = _cells[index];
                if (cell is null || cell.Kind is not (CellKind.OutputPort or CellKind.Probe))
                {
                    continue;
                }

                var observed = ReadInput(result.ResolvedInputs, index, "in");
                _states[index].ObservedValue = observed;
                if (cell.Kind == CellKind.Probe)
                {
                    probes.Add(new ProbeSample(cell.Id, result.Tick, observed));
                }
            }

            var samples = probes.ToImmutable();
            _probeHistory.AddRange(samples);
            return new PanelTickResult(
                result.Tick,
                ComputeRuntimeHash(result.Hash),
                samples,
                CaptureOutputs(),
                result.Diagnostics);
        }
        finally
        {
            _workingStates = null;
            _isStepping = false;
        }
    }

    public PanelRuntimeSnapshot CaptureSnapshot()
    {
        EnsureNotStepping();
        var cells = ImmutableArray.CreateBuilder<PanelRuntimeCellSnapshot?>(_cells.Length);
        for (var index = 0; index < _cells.Length; index++)
        {
            var cell = _cells[index];
            cells.Add(cell is null ? null : CaptureCellSnapshot(cell, _states[index]));
        }

        var schedulerSnapshot = _scheduler.CaptureSnapshot();
        var snapshot = new PanelRuntimeSnapshot(
            Id,
            Width,
            Height,
            cells.ToImmutable(),
            schedulerSnapshot,
            _probeHistory.ToImmutableArray())
        {
            TargetIncarnations = schedulerSnapshot.Targets,
            TargetIncarnationTimeline = _targetIncarnationTimeline.ToImmutableArray()
        };
        return snapshot with { StateIntegrityHash = ComputeStateIntegrityHash(snapshot) };
    }

    public void RestoreSnapshot(PanelRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureNotStepping();
        ArgumentNullException.ThrowIfNull(snapshot.Scheduler);
        if (snapshot.PanelId != Id || snapshot.Width != Width || snapshot.Height != Height ||
            snapshot.Cells.IsDefault || snapshot.Cells.Length != _cells.Length ||
            snapshot.ProbeHistory.IsDefault || snapshot.TargetIncarnations.IsDefault ||
            snapshot.TargetIncarnationTimeline.IsDefault)
        {
            throw new ArgumentException("Panel runtime snapshot does not match this panel.", nameof(snapshot));
        }

        ValidateProbeHistory(
            snapshot.ProbeHistory,
            snapshot.Scheduler.CurrentTick,
            snapshot.Scheduler.Targets.Select(target => target.StableId).ToHashSet(StringComparer.Ordinal));
        var restoredStates = new RuntimeCellState[_cells.Length];
        for (var index = 0; index < _cells.Length; index++)
        {
            var cell = _cells[index];
            var saved = snapshot.Cells[index];
            if (!MatchesDefinition(cell, saved))
            {
                throw new ArgumentException("Panel runtime snapshot does not match this panel.", nameof(snapshot));
            }

            if (saved is not null)
            {
                ValidateCellTimestamps(cell!, saved, snapshot.Scheduler);
            }

            restoredStates[index] = saved is null
                ? new RuntimeCellState(null)
                : RestoreCellState(cell!, saved, _customRules[index]);
        }

        ValidateSchedulerTargets(snapshot.Scheduler);
        if (!snapshot.TargetIncarnations.SequenceEqual(snapshot.Scheduler.Targets))
        {
            throw new ArgumentException("Panel runtime target incarnations do not match its scheduler state.", nameof(snapshot));
        }
        ValidateTargetIncarnationTimeline(snapshot);
        ValidateLastForwardedValues(snapshot);
        if (!IsSha256(snapshot.StateIntegrityHash) ||
            !string.Equals(snapshot.StateIntegrityHash, ComputeStateIntegrityHash(snapshot), StringComparison.Ordinal))
        {
            throw new ArgumentException("Panel runtime state integrity hash does not match its state.", nameof(snapshot));
        }

        _scheduler.RestoreSnapshot(snapshot.Scheduler);
        for (var index = 0; index < _cells.Length; index++)
        {
            if (_cells[index] is not { } cell || !IsActive(cell))
            {
                _targetHandles[index] = null;
                continue;
            }

            _targetHandles[index] = _scheduler.TryGetTarget(cell.Id.Value, out var handle)
                ? handle
                : throw new ArgumentException("Panel runtime snapshot is missing an active cell target.", nameof(snapshot));
        }
        _states = restoredStates;
        _probeHistory.Clear();
        _probeHistory.AddRange(snapshot.ProbeHistory);
        _targetIncarnationTimeline.Clear();
        _targetIncarnationTimeline.AddRange(snapshot.TargetIncarnationTimeline.Select(entry =>
        {
            var index = Array.FindIndex(_cells, cell => cell?.Id.Value == entry.StableId);
            return entry.Active && index >= 0 && _cells[index] is { } cell &&
                   cell.Kind == CellKind.Custom && snapshot.Cells[index] is { } saved &&
                   saved.BehaviorId != cell.BehaviorId
                ? entry with { DefinitionHash = ComputeCellDefinitionHash(cell) }
                : entry;
        }));
    }

    private void ValidateProbeHistory(
        ImmutableArray<ProbeSample> history,
        long currentTick,
        IReadOnlySet<string> knownTargetIds)
    {
        var cells = _cells
            .OfType<PanelCellDefinition>()
            .ToDictionary(cell => cell.Id.Value, StringComparer.Ordinal);
        if (history.Any(sample => sample is null || !StableData.IsStableId(sample.ProbeId.Value) ||
                                 !knownTargetIds.Contains(sample.ProbeId.Value) ||
                                 cells.TryGetValue(sample.ProbeId.Value, out var cell) &&
                                 cell.Kind != CellKind.Probe ||
                                 sample.Tick < 0 || sample.Tick >= currentTick ||
                                 !Enum.IsDefined(sample.Value)))
        {
            throw new ArgumentException("Panel runtime probe history is invalid.", nameof(history));
        }
    }

    private static void ValidateCellTimestamps(
        PanelCellDefinition cell,
        PanelRuntimeCellSnapshot snapshot,
        SchedulerSnapshot scheduler)
    {
        var currentTick = scheduler.CurrentTick;
        if (snapshot.PendingTick < -1 || snapshot.PendingTick >= 0 && snapshot.PendingTick < currentTick ||
            snapshot.PendingTick < 0 && snapshot.PendingValue != LogicValue.HighImpedance ||
            snapshot.PendingTick >= 0 && snapshot.PendingValue == LogicValue.HighImpedance ||
            cell.Kind != CellKind.Nand && (snapshot.PendingTick != -1 ||
                                           snapshot.PendingValue != LogicValue.HighImpedance) ||
            snapshot.FilterCandidateSinceTick < 0 || snapshot.FilterCandidateSinceTick > currentTick ||
            snapshot.DataChangedTick < 0 || snapshot.DataChangedTick > currentTick ||
            snapshot.NextClockTransitionTick < 0 ||
            cell.Kind == CellKind.Clock && snapshot.NextClockTransitionTick < currentTick ||
            cell.Kind != CellKind.Clock && snapshot.NextClockTransitionTick != 0)
        {
            throw new ArgumentException("Panel runtime snapshot contains impossible cell timestamps.", nameof(snapshot));
        }

        if (cell.Kind == CellKind.Nand && snapshot.PendingTick >= 0)
        {
            var target = scheduler.Targets.FirstOrDefault(item => item.StableId == cell.Id.Value);
            if (target is null || scheduler.PendingEvents.Count(scheduledEvent =>
                    scheduledEvent.TargetStableId == cell.Id.Value &&
                    scheduledEvent.TargetIncarnation == target.Incarnation &&
                    scheduledEvent.Key.DueTick == snapshot.PendingTick &&
                    scheduledEvent.EventKind == NandCommitEvent &&
                    scheduledEvent.Value == snapshot.PendingValue) != 1)
            {
                throw new ArgumentException("Panel runtime Nand state has no matching pending event.", nameof(snapshot));
            }
        }

        if (cell.Kind == CellKind.Clock && scheduler.TraceStartTick == 0 &&
            scheduler.Targets.FirstOrDefault(target => target.StableId == cell.Id.Value)?.Incarnation == 1)
        {
            var expected = ExpectedClockState(cell, currentTick);
            if (snapshot.CommittedOutput != expected.Output ||
                snapshot.NextClockTransitionTick != expected.NextTransitionTick)
            {
                throw new ArgumentException("Panel runtime clock state does not match its tick.", nameof(snapshot));
            }
        }

        if (cell.Kind == CellKind.DFlipFlop && scheduler.Trace.Length > 0)
        {
            var target = scheduler.Targets.FirstOrDefault(item => item.StableId == cell.Id.Value);
            var trace = scheduler.Trace[^1];
            var data = LogicValue.HighImpedance;
            var clock = LogicValue.HighImpedance;
            if (target is not null)
            {
                if (trace.ResolvedInputs.TryGetValue(
                    new SchedulerPortAddress(cell.Id.Value, target.Incarnation, "data"),
                    out var resolvedData))
                {
                    data = resolvedData;
                }

                if (trace.ResolvedInputs.TryGetValue(
                    new SchedulerPortAddress(cell.Id.Value, target.Incarnation, "clock"),
                    out var resolvedClock))
                {
                    clock = resolvedClock;
                }
            }

            if (target is not null && trace.Tick == currentTick - 1 &&
                (snapshot.PreviousData != data || snapshot.PreviousClock != clock))
            {
                throw new ArgumentException("Panel runtime D flip-flop state does not match its last inputs.", nameof(snapshot));
            }

            var expectedDataChangedTick = 0L;
            var previousData = LogicValue.HighImpedance;
            if (target is not null)
            {
                foreach (var historical in scheduler.Trace)
                {
                    var historicalData = TraceInput(
                        historical,
                        cell.Id.Value,
                        target.Incarnation,
                        "data");
                    if (historicalData != previousData)
                    {
                        previousData = historicalData;
                        expectedDataChangedTick = historical.Tick;
                    }
                }
            }

            if (target is not null && snapshot.DataChangedTick != expectedDataChangedTick)
            {
                throw new ArgumentException("Panel runtime D flip-flop data timestamp does not match its inputs.", nameof(snapshot));
            }
        }

        if (scheduler.TraceStartTick == 0 &&
            scheduler.Targets.FirstOrDefault(target => target.StableId == cell.Id.Value)?.Incarnation == 1)
        {
            if (scheduler.Trace.Length == 0 && cell.Kind == CellKind.DFlipFlop &&
                (snapshot.PreviousData != LogicValue.HighImpedance || snapshot.PreviousClock != LogicValue.Low))
            {
                throw new ArgumentException("Panel runtime D flip-flop initial state is invalid.", nameof(snapshot));
            }

            var expectedOutput = cell.Kind switch
            {
                CellKind.Nand => LogicValue.HighImpedance,
                CellKind.DFlipFlop => LogicValue.Unknown,
                _ => snapshot.CommittedOutput
            };
            foreach (var trace in scheduler.Trace)
            {
                foreach (var delivered in trace.DeliveredEvents.Where(delivered =>
                             delivered.TargetStableId == cell.Id.Value &&
                             delivered.EventKind is NandCommitEvent or FlipFlopCommitEvent))
                {
                    expectedOutput = delivered.Value;
                }
            }

            if (cell.Kind is (CellKind.Nand or CellKind.DFlipFlop) &&
                snapshot.CommittedOutput != expectedOutput)
            {
                throw new ArgumentException("Panel runtime standard cell output does not match its causal events.", nameof(snapshot));
            }

            if (cell.Kind == CellKind.Nand)
            {
                var expectedPendingTick = -1L;
                var expectedPendingValue = LogicValue.HighImpedance;
                if (scheduler.Trace.Length > 0)
                {
                    var trace = scheduler.Trace[^1];
                    var target = scheduler.Targets.First(item => item.StableId == cell.Id.Value);
                    var candidate = Nand(
                        TraceInput(trace, cell.Id.Value, target.Incarnation, "a"),
                        TraceInput(trace, cell.Id.Value, target.Incarnation, "b"));
                    if (candidate != snapshot.CommittedOutput)
                    {
                        expectedPendingTick = checked(trace.Tick + cell.RuntimeParameters.DelayTicks);
                        expectedPendingValue = candidate;
                    }
                }

                if (snapshot.PendingTick != expectedPendingTick || snapshot.PendingValue != expectedPendingValue)
                {
                    throw new ArgumentException("Panel runtime Nand pending state does not match its inputs.", nameof(snapshot));
                }
            }

            if (scheduler.Trace.Length == 0 && snapshot.LastForwarded.Length > 0)
            {
                throw new ArgumentException("Panel runtime initial forwarding state is invalid.", nameof(snapshot));
            }

            if (cell.Kind == CellKind.StabilityFilter)
            {
                var expectedCandidate = LogicValue.HighImpedance;
                var expectedFilterOutput = LogicValue.HighImpedance;
                var expectedSinceTick = 0L;
                foreach (var trace in scheduler.Trace)
                {
                    var address = new SchedulerPortAddress(
                        cell.Id.Value,
                        scheduler.Targets.First(target => target.StableId == cell.Id.Value).Incarnation,
                        "in");
                    var input = trace.ResolvedInputs.TryGetValue(address, out var resolved)
                        ? resolved
                        : LogicValue.HighImpedance;
                    if (input != expectedCandidate)
                    {
                        if (expectedCandidate != expectedFilterOutput &&
                            trace.Tick - expectedSinceTick >= cell.RuntimeParameters.ConsecutiveTicks)
                        {
                            expectedFilterOutput = expectedCandidate;
                        }

                        expectedCandidate = input;
                        expectedSinceTick = trace.Tick;
                    }

                    if (input != expectedFilterOutput &&
                        trace.Tick - expectedSinceTick >= cell.RuntimeParameters.ConsecutiveTicks)
                    {
                        expectedFilterOutput = input;
                    }
                }

                if (snapshot.FilterCandidate != expectedCandidate ||
                    snapshot.CommittedOutput != expectedFilterOutput ||
                    snapshot.FilterCandidateSinceTick != expectedSinceTick)
                {
                    throw new ArgumentException("Panel runtime stability filter state does not match its inputs.", nameof(snapshot));
                }
            }

            if (cell.Kind is CellKind.OutputPort or CellKind.Probe)
            {
                var target = scheduler.Targets.First(item => item.StableId == cell.Id.Value);
                var expectedObserved = scheduler.Trace.Length == 0
                    ? LogicValue.HighImpedance
                    : TraceInput(scheduler.Trace[^1], cell.Id.Value, target.Incarnation, "in");
                if (snapshot.ObservedValue != expectedObserved)
                {
                    throw new ArgumentException("Panel runtime observed value does not match its last input.", nameof(snapshot));
                }
            }
        }
    }

    private static (LogicValue Output, long NextTransitionTick) ExpectedClockState(
        PanelCellDefinition cell,
        long currentTick)
    {
        var output = LogicValue.Low;
        var nextTransitionTick = (long)cell.RuntimeParameters.LowTicks;
        var lastEvaluatedTick = currentTick - 1;
        while (lastEvaluatedTick >= nextTransitionTick)
        {
            output = output == LogicValue.Low ? LogicValue.High : LogicValue.Low;
            var duration = output == LogicValue.High
                ? cell.RuntimeParameters.HighTicks
                : cell.RuntimeParameters.LowTicks;
            nextTransitionTick = checked(nextTransitionTick + duration);
        }

        return (output, nextTransitionTick);
    }

    private static LogicValue TraceInput(
        SchedulerTickTrace trace,
        string targetStableId,
        long targetIncarnation,
        string portName) =>
        trace.ResolvedInputs.TryGetValue(
            new SchedulerPortAddress(targetStableId, targetIncarnation, portName),
            out var value)
            ? value
            : LogicValue.HighImpedance;

    private void ValidateSchedulerTargets(SchedulerSnapshot scheduler)
    {
        var activeCellIds = _cells
            .OfType<PanelCellDefinition>()
            .Where(IsActive)
            .Select(cell => cell.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        var activeTargetIds = scheduler.Targets
            .Where(target => target.Active)
            .Select(target => target.StableId)
            .ToHashSet(StringComparer.Ordinal);
        if (!activeTargetIds.SetEquals(activeCellIds) ||
            scheduler.Targets.Any(target => target.Incarnation <= 0))
        {
            throw new ArgumentException("Panel runtime scheduler targets do not match its active cells.", nameof(scheduler));
        }

        var targets = scheduler.Targets.ToDictionary(target => target.StableId, StringComparer.Ordinal);
        if (scheduler.PendingEvents.Any(scheduledEvent =>
                !targets.TryGetValue(scheduledEvent.TargetStableId, out var target) ||
                string.IsNullOrWhiteSpace(scheduledEvent.SourceStableId) ||
                !targets.TryGetValue(scheduledEvent.SourceStableId, out var source) ||
                !IsKnownPanelEventIncarnation(scheduler, scheduledEvent, target, source) ||
                !IsPanelEvent(scheduledEvent, targets)))
        {
            throw new ArgumentException("Panel runtime scheduler events contain an unresolved reference.", nameof(scheduler));
        }

        if (scheduler.PendingEvents.Any(scheduledEvent =>
                scheduledEvent.EventKind == FlipFlopCommitEvent &&
                (!scheduler.Trace.Any(trace => trace.State?.PendingEvents.Contains(scheduledEvent) == true) ||
                 !HasDFlipFlopClockEdge(scheduler, scheduledEvent))))
        {
            throw new ArgumentException("Panel runtime D flip-flop event has no recorded clock edge.", nameof(scheduler));
        }

        foreach (var trace in scheduler.Trace)
        {
            if (trace.State is not { } state)
            {
                continue;
            }

            var traceTargets = state.Targets.ToDictionary(target => target.StableId, StringComparer.Ordinal);
            if (state.Drives.Any(drive =>
                    !traceTargets.TryGetValue(drive.Key.SourceStableId, out var source) ||
                    !source.Active || source.Incarnation != drive.Key.SourceIncarnation ||
                    !traceTargets.TryGetValue(drive.Key.TargetStableId, out var target) ||
                    !target.Active || target.Incarnation != drive.Key.TargetIncarnation ||
                    !IsPanelDrive(drive.Key, targets)) ||
                state.PendingEvents.Any(scheduledEvent =>
                    !traceTargets.TryGetValue(scheduledEvent.TargetStableId, out var target) ||
                    !traceTargets.TryGetValue(scheduledEvent.SourceStableId, out var source) ||
                    scheduledEvent.EventKind != "drive-release" &&
                    (scheduledEvent.TargetIncarnation != target.Incarnation ||
                     scheduledEvent.SourceIncarnation != source.Incarnation) ||
                    !IsPanelEvent(scheduledEvent, targets)) ||
                trace.DeliveredEvents.Any(scheduledEvent =>
                    !traceTargets.TryGetValue(scheduledEvent.TargetStableId, out var target) ||
                    target.Incarnation != scheduledEvent.TargetIncarnation ||
                    !target.Active ||
                    !traceTargets.TryGetValue(scheduledEvent.SourceStableId, out var source) ||
                    source.Incarnation != scheduledEvent.SourceIncarnation ||
                    !IsPanelEvent(scheduledEvent, targets)) ||
                trace.ResolvedInputs.Any(input =>
                {
                    var index = Array.FindIndex(_cells, cell => cell?.Id.Value == input.Key.TargetStableId);
                    var currentTarget = targets.TryGetValue(input.Key.TargetStableId, out var current)
                        ? current
                        : null;
                    var isHistoricalIncarnation = currentTarget is null ||
                                                   currentTarget.Incarnation != input.Key.TargetIncarnation;
                    return !traceTargets.TryGetValue(input.Key.TargetStableId, out var target) ||
                           !target.Active || target.Incarnation != input.Key.TargetIncarnation ||
                           !isHistoricalIncarnation && (index < 0 || !_portSpecs[index].Any(port =>
                               port.Name == input.Key.PortOrLane && port.CanReceive));
                }))
            {
                throw new ArgumentException("Panel runtime trace state contains an unresolved reference.", nameof(scheduler));
            }
        }

        if (scheduler.Drives.Any(drive =>
                !targets.TryGetValue(drive.Key.SourceStableId, out var source) ||
                !source.Active || source.Incarnation != drive.Key.SourceIncarnation ||
                !targets.TryGetValue(drive.Key.TargetStableId, out var target) ||
                !target.Active || target.Incarnation != drive.Key.TargetIncarnation ||
                !IsPanelDrive(drive.Key, targets)))
        {
            throw new ArgumentException("Panel runtime scheduler drives contain an unresolved reference.", nameof(scheduler));
        }
    }

    private void ValidateLastForwardedValues(PanelRuntimeSnapshot snapshot)
    {
        var targets = snapshot.Scheduler.Targets
            .ToDictionary(target => target.StableId, StringComparer.Ordinal);
        var latestTraceState = snapshot.Scheduler.Trace.LastOrDefault()?.State;
        foreach (var scheduledEvent in snapshot.Scheduler.PendingEvents.Where(scheduledEvent =>
                     scheduledEvent.EventKind == "drive" &&
                     targets.TryGetValue(scheduledEvent.SourceStableId, out var source) &&
                     source.Active && source.Incarnation == scheduledEvent.SourceIncarnation &&
                     targets.TryGetValue(scheduledEvent.TargetStableId, out var target) &&
                     target.Active && target.Incarnation == scheduledEvent.TargetIncarnation))
        {
            if (latestTraceState is null || !latestTraceState.PendingEvents.Contains(scheduledEvent))
            {
                throw new ArgumentException("Panel runtime pending drive has no causal transition.", nameof(snapshot));
            }
        }

        for (var index = 0; index < _cells.Length; index++)
        {
            var cell = _cells[index];
            var saved = snapshot.Cells[index];
            if (!IsActive(cell) || saved is null ||
                !targets.TryGetValue(cell!.Id.Value, out var sourceTarget) || !sourceTarget.Active)
            {
                continue;
            }

            foreach (var pair in saved.LastForwarded)
            {
                var connection = _outgoing[index].SingleOrDefault(candidate => candidate.SourcePort == pair.Key);
                if (connection is null ||
                    _cells[connection.TargetIndex] is not { } targetCell ||
                    !targets.TryGetValue(targetCell.Id.Value, out var targetTarget) ||
                    !targetTarget.Active)
                {
                    throw new ArgumentException("Panel runtime forwarding state has no current connection.", nameof(snapshot));
                }

                var key = new SchedulerDriveKey(
                    cell.Id.Value,
                    sourceTarget.Incarnation,
                    pair.Key,
                    targetCell.Id.Value,
                    targetTarget.Incarnation,
                    connection.TargetPort);
                var expectedOutput = cell.Kind switch
                {
                    CellKind.InputPort => saved.ExternalInput,
                    CellKind.Constant => cell.RuntimeParameters.ConstantValue,
                    CellKind.Clock => ExpectedClockState(cell, snapshot.Scheduler.CurrentTick).Output,
                    CellKind.Nand or CellKind.DFlipFlop or CellKind.StabilityFilter => saved.CommittedOutput,
                    _ => (LogicValue?)null
                };
                if (expectedOutput is { } output && pair.Value != output)
                {
                    throw new ArgumentException("Panel runtime forwarding value does not match its source output.", nameof(snapshot));
                }
                var hasCurrentDrive = snapshot.Scheduler.Drives.Any(drive =>
                    drive.Key == key && drive.Value == pair.Value);
                var hasPendingTransition = snapshot.Scheduler.PendingEvents.Any(scheduledEvent =>
                    scheduledEvent.EventKind == "drive" &&
                    scheduledEvent.TargetStableId == key.TargetStableId &&
                    scheduledEvent.TargetIncarnation == key.TargetIncarnation &&
                    scheduledEvent.TargetPortOrLane == key.TargetPortOrLane &&
                    scheduledEvent.SourceStableId == key.SourceStableId &&
                    scheduledEvent.SourceIncarnation == key.SourceIncarnation &&
                    scheduledEvent.SourcePort == key.SourcePort &&
                    scheduledEvent.Value == pair.Value);
                var hasDeliveredTransition = snapshot.Scheduler.Trace.Any(trace =>
                    trace.DeliveredEvents.Any(scheduledEvent =>
                        scheduledEvent.EventKind == "drive" &&
                        scheduledEvent.TargetStableId == key.TargetStableId &&
                        scheduledEvent.TargetIncarnation == key.TargetIncarnation &&
                        scheduledEvent.TargetPortOrLane == key.TargetPortOrLane &&
                        scheduledEvent.SourceStableId == key.SourceStableId &&
                        scheduledEvent.SourceIncarnation == key.SourceIncarnation &&
                        scheduledEvent.SourcePort == key.SourcePort &&
                        scheduledEvent.Value == pair.Value));
                if (!hasCurrentDrive && !hasPendingTransition && !hasDeliveredTransition)
                {
                    throw new ArgumentException("Panel runtime forwarding value has no causal transition.", nameof(snapshot));
                }
            }
        }
    }

    private void ValidateTargetIncarnationTimeline(PanelRuntimeSnapshot snapshot)
    {
        var targets = snapshot.Scheduler.Targets
            .ToDictionary(target => target.StableId, StringComparer.Ordinal);
        foreach (var group in snapshot.TargetIncarnationTimeline
                     .GroupBy(entry => entry.StableId, StringComparer.Ordinal))
        {
            var entries = group.OrderBy(entry => entry.Incarnation).ToArray();
            if (entries.Length == 0 || entries[0].Incarnation != 1 ||
                entries.Select((entry, index) => entry.Incarnation == index + 1L).Any(valid => !valid) ||
                entries.SkipLast(1).Any(entry => entry.Active) ||
                entries.Any(entry => entry.Incarnation <= 0 || !IsSha256(entry.DefinitionHash)) ||
                !targets.TryGetValue(group.Key, out var target) ||
                target.Incarnation != entries[^1].Incarnation ||
                target.Active != entries[^1].Active)
            {
                throw new ArgumentException("Panel runtime target incarnation timeline is invalid.", nameof(snapshot));
            }

            var index = Array.FindIndex(_cells, cell => cell?.Id.Value == group.Key);
            var saved = index >= 0 ? snapshot.Cells[index] : null;
            var allowsCustomMigration = index >= 0 && _cells[index] is { Kind: CellKind.Custom } current &&
                                         saved is { BehaviorId: not null } && saved.BehaviorId != current.BehaviorId;
            if (target.Active && (index < 0 || _cells[index] is not { } cell ||
                                  !allowsCustomMigration && entries[^1].DefinitionHash != ComputeCellDefinitionHash(cell)))
            {
                throw new ArgumentException("Panel runtime target incarnation definition is invalid.", nameof(snapshot));
            }
        }

        if (snapshot.TargetIncarnationTimeline
                .Select(entry => entry.StableId)
                .ToHashSet(StringComparer.Ordinal)
                .Count != targets.Count)
        {
            throw new ArgumentException("Panel runtime target incarnation timeline is incomplete.", nameof(snapshot));
        }
    }

    private void MarkTargetIncarnationInactive(string stableId, long incarnation)
    {
        var index = _targetIncarnationTimeline.FindIndex(entry =>
            entry.StableId == stableId && entry.Incarnation == incarnation);
        if (index >= 0)
        {
            _targetIncarnationTimeline[index] = _targetIncarnationTimeline[index] with { Active = false };
        }
    }

    private static PanelTargetIncarnationSnapshot CreateTargetIncarnationSnapshot(
        PanelCellDefinition cell,
        long incarnation,
        bool active) =>
        new(cell.Id.Value, incarnation, active, ComputeCellDefinitionHash(cell));

    private static string ComputeCellDefinitionHash(PanelCellDefinition cell)
    {
        var payload = new
        {
            Id = cell.Id.Value,
            Kind = cell.Kind.ToString(),
            Location = cell.Location,
            Orientation = cell.Orientation.ToString(),
            PortId = cell.PortId?.Value,
            BehaviorId = cell.BehaviorId?.Value,
            Parameters = cell.Parameters
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload))).ToLowerInvariant();
    }

    private bool IsPanelEvent(
        ScheduledEvent scheduledEvent,
        IReadOnlyDictionary<string, SchedulerTargetSnapshot>? currentTargets = null)
    {
        var targetIndex = Array.FindIndex(_cells, cell => cell?.Id.Value == scheduledEvent.TargetStableId);
        var sourceIndex = Array.FindIndex(_cells, cell => cell?.Id.Value == scheduledEvent.SourceStableId);
        if (targetIndex < 0 || sourceIndex < 0)
        {
            return true;
        }

        var targetCell = _cells[targetIndex]!;
        var sourceCell = _cells[sourceIndex]!;
        var stale = currentTargets is not null &&
                    currentTargets.TryGetValue(scheduledEvent.TargetStableId, out var currentTarget) &&
                    currentTargets.TryGetValue(scheduledEvent.SourceStableId, out var currentSource) &&
                    (currentTarget.Incarnation != scheduledEvent.TargetIncarnation ||
                     currentSource.Incarnation != scheduledEvent.SourceIncarnation);
        if (scheduledEvent.EventKind == NandCommitEvent || scheduledEvent.EventKind == FlipFlopCommitEvent)
        {
            return targetIndex == sourceIndex &&
                   scheduledEvent.TargetPortOrLane == "timer" && scheduledEvent.SourcePort == "timer" &&
                   targetCell.Kind is CellKind.Nand or CellKind.DFlipFlop;
        }

        if (scheduledEvent.EventKind is not ("drive" or "drive-release"))
        {
            return false;
        }

        if (stale)
        {
            return true;
        }

        return _portSpecs[targetIndex].Any(port =>
                   port.Name == scheduledEvent.TargetPortOrLane && port.CanReceive) &&
               _portSpecs[sourceIndex].Any(port =>
                   port.Name == scheduledEvent.SourcePort && port.CanDrive) &&
               (scheduledEvent.EventKind == "drive-release" || _outgoing[sourceIndex].Any(connection =>
                   connection.TargetIndex == targetIndex &&
                   connection.SourcePort == scheduledEvent.SourcePort &&
                   connection.TargetPort == scheduledEvent.TargetPortOrLane));
    }

    private static bool IsKnownPanelEventIncarnation(
        SchedulerSnapshot scheduler,
        ScheduledEvent scheduledEvent,
        SchedulerTargetSnapshot target,
        SchedulerTargetSnapshot source)
    {
        if (scheduledEvent.TargetIncarnation == target.Incarnation &&
            scheduledEvent.SourceIncarnation == source.Incarnation)
        {
            return true;
        }

        return scheduler.Trace.Any(trace => trace.State is { } state &&
            state.Targets.Any(candidate => candidate.StableId == scheduledEvent.TargetStableId &&
                                          candidate.Incarnation == scheduledEvent.TargetIncarnation) &&
            state.Targets.Any(candidate => candidate.StableId == scheduledEvent.SourceStableId &&
                                          candidate.Incarnation == scheduledEvent.SourceIncarnation));
    }

    private bool HasDFlipFlopClockEdge(
        SchedulerSnapshot scheduler,
        ScheduledEvent scheduledEvent)
    {
        var index = Array.FindIndex(_cells, cell => cell?.Id.Value == scheduledEvent.TargetStableId);
        if (index < 0 || _cells[index]!.Kind != CellKind.DFlipFlop)
        {
            return false;
        }

        return scheduler.Trace.Any(trace =>
        {
            var triggerTick = scheduledEvent.Key.DueTick - _cells[index]!.RuntimeParameters.ClockToOutputTicks;
            if (trace.Tick != triggerTick || trace.State is null ||
                !trace.State.Targets.Any(target => target.StableId == scheduledEvent.TargetStableId &&
                                                   target.Incarnation == scheduledEvent.TargetIncarnation))
            {
                return false;
            }

            var clockAddress = new SchedulerPortAddress(
                scheduledEvent.TargetStableId,
                scheduledEvent.TargetIncarnation,
                "clock");
            var clock = trace.ResolvedInputs.TryGetValue(clockAddress, out var resolved)
                ? resolved
                : LogicValue.HighImpedance;
            var previous = scheduler.Trace.FirstOrDefault(previousTrace => previousTrace.Tick == triggerTick - 1);
            var previousClock = previous is null ||
                                !previous.ResolvedInputs.TryGetValue(clockAddress, out var priorResolved)
                ? LogicValue.Low
                : priorResolved;
            var dataAddress = new SchedulerPortAddress(
                scheduledEvent.TargetStableId,
                scheduledEvent.TargetIncarnation,
                "data");
            var data = trace.ResolvedInputs.TryGetValue(dataAddress, out var resolvedData)
                ? resolvedData
                : LogicValue.HighImpedance;
            var previousData = LogicValue.HighImpedance;
            var dataChangedTick = 0L;
            foreach (var historical in scheduler.Trace.Where(historical => historical.Tick <= triggerTick))
            {
                var historicalData = TraceInput(
                    historical,
                    scheduledEvent.TargetStableId,
                    scheduledEvent.TargetIncarnation,
                    "data");
                if (historicalData != previousData)
                {
                    previousData = historicalData;
                    dataChangedTick = historical.Tick;
                }
            }

            var expectedValue = triggerTick - dataChangedTick >= _cells[index]!.RuntimeParameters.SetupTicks
                ? data
                : LogicValue.Unknown;
            return clock == LogicValue.High && previousClock != LogicValue.High &&
                   scheduledEvent.Value == expectedValue;
        });
    }

    private bool IsPanelDrive(
        SchedulerDriveKey key,
        IReadOnlyDictionary<string, SchedulerTargetSnapshot>? currentTargets = null)
    {
        var sourceIndex = Array.FindIndex(_cells, cell => cell?.Id.Value == key.SourceStableId);
        var targetIndex = Array.FindIndex(_cells, cell => cell?.Id.Value == key.TargetStableId);
        if (currentTargets is not null && currentTargets.TryGetValue(key.SourceStableId, out var currentSource) &&
            currentTargets.TryGetValue(key.TargetStableId, out var currentTarget) &&
            (currentSource.Incarnation != key.SourceIncarnation || currentTarget.Incarnation != key.TargetIncarnation))
        {
            return true;
        }

        return sourceIndex < 0 || targetIndex < 0 ||
               _portSpecs[sourceIndex].Any(port => port.Name == key.SourcePort && port.CanDrive) &&
               _portSpecs[targetIndex].Any(port => port.Name == key.TargetPortOrLane && port.CanReceive) &&
               _outgoing[sourceIndex].Any(connection =>
                   connection.TargetIndex == targetIndex &&
                   connection.SourcePort == key.SourcePort &&
                   connection.TargetPort == key.TargetPortOrLane);
    }

    public bool RemoveCell(ComponentId cellId)
    {
        EnsureNotStepping();
        var index = Array.FindIndex(_cells, cell => cell?.Id == cellId);
        if (index < 0)
        {
            return false;
        }

        var candidateCells = (PanelCellDefinition?[])_cells.Clone();
        var removed = candidateCells[index]!;
        candidateCells[index] = null;
        var validated = PanelDefinition.Create(
            Id,
            Width,
            Height,
            candidateCells.OfType<PanelCellDefinition>());
        var validatedCells = validated.Cells.ToArray();
        var validatedRules = ResolveCustomRules(validatedCells);

        if (IsActive(removed))
        {
            MarkTargetIncarnationInactive(removed.Id.Value, _targetHandles[index]!.Value.Incarnation);
            _scheduler.RemoveTarget(removed.Id.Value);
        }

        _cells = validatedCells;
        _customRules = validatedRules;
        _states[index] = new RuntimeCellState(null);
        _targetHandles[index] = null;
        RefreshTopology(index);
        return true;
    }

    public void ReplaceCell(PanelCellDefinition replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        EnsureNotStepping();
        var index = IndexOf(replacement.Location);
        var candidateCells = (PanelCellDefinition?[])_cells.Clone();
        var previous = candidateCells[index];
        candidateCells[index] = replacement;
        var validated = PanelDefinition.Create(
            Id,
            Width,
            Height,
            candidateCells.OfType<PanelCellDefinition>());
        var validatedCells = validated.Cells.ToArray();
        var validatedRules = ResolveCustomRules(validatedCells);
        SchedulerTargetHandle? replacementHandle = null;

        if (IsActive(previous))
        {
            MarkTargetIncarnationInactive(previous!.Id.Value, _targetHandles[index]!.Value.Incarnation);
            replacementHandle = previous.Id == replacement.Id
                ? _scheduler.ReplaceTarget(previous.Id.Value)
                : null;
            if (replacementHandle is null)
            {
                _scheduler.RemoveTarget(previous.Id.Value);
            }
        }

        _cells = validatedCells;
        _customRules = validatedRules;
        _states[index] = CreateRuntimeCellState(replacement, _customRules[index]);
        if (IsActive(replacement))
        {
            var handle = replacementHandle ?? _scheduler.RegisterTarget(replacement.Id.Value);
            _targetHandles[index] = handle;
            _targetIncarnationTimeline.Add(CreateTargetIncarnationSnapshot(
                replacement,
                handle.Incarnation,
                true));
        }
        else
        {
            _targetHandles[index] = null;
        }
        RefreshTopology(index);
    }

    private IReadOnlyList<SchedulerProposal> Evaluate(SchedulerEvaluationContext context)
    {
        var states = _workingStates
            ?? throw new InvalidOperationException("Panel evaluation is outside a panel step.");
        var proposals = new List<SchedulerProposal>();
        for (var index = 0; index < _cells.Length; index++)
        {
            var cell = _cells[index];
            if (!IsActive(cell))
            {
                continue;
            }

            var state = states[index];
            var outputs = EvaluateCell(context, index, cell!, state, proposals);
            foreach (var connection in _outgoing[index])
            {
                if (!outputs.TryGetValue(connection.SourcePort, out var value))
                {
                    continue;
                }

                var previous = state.LastForwarded.GetValueOrDefault(
                    connection.SourcePort,
                    LogicValue.HighImpedance);
                if (previous == value)
                {
                    continue;
                }

                var source = cell!;
                var target = _cells[connection.TargetIndex]!;
                var targetHandle = _targetHandles[connection.TargetIndex]!.Value;
                proposals.Add(SchedulerProposal.ForNextTick(
                    target.Id.Value,
                    targetHandle.Incarnation,
                    connection.TargetPort,
                    source.Id.Value,
                    connection.SourcePort,
                    "drive",
                    value,
                    sourceIncarnation: _targetHandles[index]!.Value.Incarnation));
                state.LastForwarded[connection.SourcePort] = value;
            }
        }

        return proposals;
    }

    private Dictionary<string, LogicValue> EvaluateCell(
        SchedulerEvaluationContext context,
        int index,
        PanelCellDefinition cell,
        RuntimeCellState state,
        ICollection<SchedulerProposal> proposals)
    {
        var outputs = new Dictionary<string, LogicValue>(StringComparer.Ordinal);
        switch (cell.Kind)
        {
            case CellKind.Wire:
            case CellKind.Junction:
            case CellKind.Crossing:
                EvaluateConnectedCell(context, index, cell, outputs);
                break;
            case CellKind.Constant:
                outputs["out"] = cell.RuntimeParameters.ConstantValue;
                break;
            case CellKind.InputPort:
                outputs["out"] = state.ExternalInput;
                break;
            case CellKind.OutputPort:
            case CellKind.Probe:
            case CellKind.Empty:
                break;
            case CellKind.Nand:
                EvaluateNand(context, index, cell, state, outputs, proposals);
                break;
            case CellKind.Clock:
                outputs["out"] = EvaluateClock(context.Tick, cell, state);
                break;
            case CellKind.DFlipFlop:
                EvaluateFlipFlop(context, index, cell, state, outputs, proposals);
                break;
            case CellKind.StabilityFilter:
                outputs["out"] = EvaluateStabilityFilter(context, index, cell, state);
                break;
            case CellKind.Custom:
                EvaluateCustomCell(context, index, cell, state, outputs);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cell), cell.Kind, "Cell kind is not supported.");
        }

        return outputs;
    }

    private void EvaluateCustomCell(
        SchedulerEvaluationContext context,
        int index,
        PanelCellDefinition cell,
        RuntimeCellState state,
        IDictionary<string, LogicValue> outputs)
    {
        var binding = _customRules[index]
            ?? throw new CustomCellRuleException(
                CustomCellDiagnosticCodes.RegistrationMissing,
                $"Custom cell rule '{cell.BehaviorId}' is not registered.");
        var inputs = ImmutableSortedDictionary.CreateBuilder<string, LogicValue>(StringComparer.Ordinal);
        foreach (var port in binding.Ports.Where(port => port.CanReceive))
        {
            inputs.Add(port.Name, ReadInput(context, index, port.Name));
        }

        var evaluation = new CustomCellEvaluationContext(
            context.Tick,
            inputs.ToImmutable(),
            cell.Parameters,
            CopyBytes(state.CustomState),
            state.CustomRandomState);
        CustomCellTransition transition;
        try
        {
            transition = binding.Rule.Evaluate(evaluation);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new CustomCellRuleException(
                CustomCellDiagnosticCodes.TransitionInvalid,
                $"Custom cell rule '{cell.BehaviorId}' failed to evaluate.",
                exception);
        }

        if (transition is null || transition.Proposals.IsDefault || transition.NextState.IsDefault)
        {
            throw InvalidCustomTransition(cell);
        }

        var portByName = binding.Ports.ToDictionary(port => port.Name, StringComparer.Ordinal);
        var candidateOutputs = new Dictionary<string, LogicValue>(StringComparer.Ordinal);
        foreach (var proposal in transition.Proposals)
        {
            var outputPort = proposal.OutputPort;
            if (outputPort is null ||
                !portByName.TryGetValue(outputPort, out var port) ||
                !port.CanDrive ||
                !Enum.IsDefined(proposal.Value) ||
                !candidateOutputs.TryAdd(outputPort, proposal.Value))
            {
                throw InvalidCustomTransition(cell);
            }
        }

        var candidateState = CopyBytes(transition.NextState);
        ValidateCustomState(binding.Rule, candidateState, cell);
        state.CustomState = candidateState;
        state.CustomRandomState = transition.NextRandomState;
        foreach (var output in candidateOutputs)
        {
            outputs.Add(output.Key, output.Value);
        }
    }

    private static CustomCellRuleException InvalidCustomTransition(PanelCellDefinition cell) =>
        new(
            CustomCellDiagnosticCodes.TransitionInvalid,
            $"Custom cell '{cell.Id}' returned an invalid transition.");

    private void EvaluateConnectedCell(
        SchedulerEvaluationContext context,
        int index,
        PanelCellDefinition cell,
        IDictionary<string, LogicValue> outputs)
    {
        foreach (var output in _portSpecs[index].Where(port => port.CanDrive))
        {
            var value = DriveResolver.Resolve(_portSpecs[index]
                .Where(port => port.Lane == output.Lane &&
                    port.Name != output.Name &&
                    port.CanReceive)
                .Select(port => ReadInput(context, index, port.Name)));
            outputs[output.Name] = value;
        }
    }

    private void EvaluateNand(
        SchedulerEvaluationContext context,
        int index,
        PanelCellDefinition cell,
        RuntimeCellState state,
        IDictionary<string, LogicValue> outputs,
        ICollection<SchedulerProposal> proposals)
    {
        var handle = _targetHandles[index]!.Value;

        // Due timers mature before this tick's resolved inputs update the candidate.
        foreach (var scheduled in context.DeliveredEvents.Where(item =>
                     item.TargetStableId == cell.Id.Value &&
                     item.TargetIncarnation == handle.Incarnation &&
                     item.EventKind == NandCommitEvent))
        {
            if (scheduled.Key.DueTick == state.PendingTick &&
                scheduled.Value == state.PendingValue)
            {
                state.CommittedOutput = scheduled.Value;
                state.PendingTick = -1;
                state.PendingValue = LogicValue.HighImpedance;
            }
        }

        var candidate = Nand(
            ReadInput(context, index, "a"),
            ReadInput(context, index, "b"));
        if (candidate == state.CommittedOutput)
        {
            state.PendingTick = -1;
            state.PendingValue = LogicValue.HighImpedance;
        }
        else if (state.PendingTick <= context.Tick || state.PendingValue != candidate)
        {
            var dueTick = checked(context.Tick + cell.RuntimeParameters.DelayTicks);
            state.PendingValue = candidate;
            state.PendingTick = dueTick;
            proposals.Add(new SchedulerProposal(
                dueTick,
                cell.Id.Value,
                handle.Incarnation,
                "timer",
                cell.Id.Value,
                "timer",
                NandCommitEvent,
                candidate,
                SourceIncarnation: handle.Incarnation));
        }

        outputs["out"] = state.CommittedOutput;
    }

    private static LogicValue EvaluateClock(long tick, PanelCellDefinition cell, RuntimeCellState state)
    {
        while (tick >= state.NextClockTransitionTick)
        {
            state.CommittedOutput = state.CommittedOutput == LogicValue.Low
                ? LogicValue.High
                : LogicValue.Low;
            var duration = state.CommittedOutput == LogicValue.High
                ? cell.RuntimeParameters.HighTicks
                : cell.RuntimeParameters.LowTicks;
            state.NextClockTransitionTick = checked(state.NextClockTransitionTick + duration);
        }

        return state.CommittedOutput;
    }

    private void EvaluateFlipFlop(
        SchedulerEvaluationContext context,
        int index,
        PanelCellDefinition cell,
        RuntimeCellState state,
        IDictionary<string, LogicValue> outputs,
        ICollection<SchedulerProposal> proposals)
    {
        var handle = _targetHandles[index]!.Value;
        foreach (var scheduled in context.DeliveredEvents.Where(item =>
                     item.TargetStableId == cell.Id.Value &&
                     item.TargetIncarnation == handle.Incarnation &&
                     item.EventKind == FlipFlopCommitEvent))
        {
            state.CommittedOutput = scheduled.Value;
        }

        var data = ReadInput(context, index, "data");
        if (data != state.PreviousData)
        {
            state.PreviousData = data;
            state.DataChangedTick = context.Tick;
        }

        var clock = ReadInput(context, index, "clock");
        if (clock == LogicValue.High && state.PreviousClock != LogicValue.High)
        {
            var setupSatisfied = context.Tick - state.DataChangedTick >=
                cell.RuntimeParameters.SetupTicks;
            var sample = setupSatisfied ? data : LogicValue.Unknown;
            var dueTick = checked(context.Tick + cell.RuntimeParameters.ClockToOutputTicks);
            proposals.Add(new SchedulerProposal(
                dueTick,
                cell.Id.Value,
                handle.Incarnation,
                "timer",
                cell.Id.Value,
                "timer",
                FlipFlopCommitEvent,
                sample,
                SourceIncarnation: handle.Incarnation));
        }

        state.PreviousClock = clock;
        outputs["out"] = state.CommittedOutput;
    }

    private LogicValue EvaluateStabilityFilter(
        SchedulerEvaluationContext context,
        int index,
        PanelCellDefinition cell,
        RuntimeCellState state)
    {
        var candidate = ReadInput(context, index, "in");
        if (candidate != state.FilterCandidate)
        {
            if (state.FilterCandidate != state.CommittedOutput &&
                context.Tick - state.FilterCandidateSinceTick >= cell.RuntimeParameters.ConsecutiveTicks)
            {
                state.CommittedOutput = state.FilterCandidate;
            }

            state.FilterCandidate = candidate;
            state.FilterCandidateSinceTick = context.Tick;
        }

        if (candidate != state.CommittedOutput &&
            context.Tick - state.FilterCandidateSinceTick >= cell.RuntimeParameters.ConsecutiveTicks)
        {
            state.CommittedOutput = candidate;
        }

        return state.CommittedOutput;
    }

    private static LogicValue Nand(LogicValue first, LogicValue second)
    {
        if (first == LogicValue.Low || second == LogicValue.Low)
        {
            return LogicValue.High;
        }

        return first == LogicValue.High && second == LogicValue.High
            ? LogicValue.Low
            : LogicValue.Unknown;
    }

    private LogicValue ReadInput(SchedulerEvaluationContext context, int index, string pin) =>
        ReadInput(context.Inputs, index, pin);

    private LogicValue ReadInput(
        ImmutableDictionary<SchedulerPortAddress, LogicValue> inputs,
        int index,
        string pin)
    {
        var cell = _cells[index]!;
        var handle = _targetHandles[index]!.Value;
        return inputs.TryGetValue(
            new SchedulerPortAddress(cell.Id.Value, handle.Incarnation, pin),
            out var value)
            ? value
            : LogicValue.HighImpedance;
    }

    private ImmutableSortedDictionary<string, string> CaptureOutputs()
    {
        var outputs = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var pair in _outputPortCells.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
        {
            outputs[pair.Key.Value] = _states[pair.Value].ObservedValue.ToString();
        }

        return outputs.ToImmutable();
    }

    private void RefreshTopology(int changedIndex)
    {
        (_portSpecs, _outgoing) = BuildTopology(_cells, _customRules);
        RebuildPanelPorts();
        ResetAdjacentOutputCaches(changedIndex);
    }

    private void ResetAdjacentOutputCaches(int changedIndex)
    {
        var changed = _cells[changedIndex];
        var location = changed?.Location ?? new GridCoordinate(changedIndex % Width, changedIndex / Width);
        for (var index = 0; index < _cells.Length; index++)
        {
            if (!IsActive(_cells[index]) || index == changedIndex)
            {
                continue;
            }

            foreach (var port in _portSpecs[index].Where(port => port.CanDrive))
            {
                if (TryGetNeighbor(index, port.Direction, out var neighbor) && neighbor == changedIndex)
                {
                    _states[index].LastForwarded.Remove(port.Name);
                }
            }
        }
    }

    private void RebuildPanelPorts()
    {
        _inputPortCells = _cells
            .Select((cell, index) => (cell, index))
            .Where(item => item.cell?.Kind == CellKind.InputPort)
            .GroupBy(item => item.cell!.PortId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.index).ToArray());
        _outputPortCells = _cells
            .Select((cell, index) => (cell, index))
            .Where(item => item.cell?.Kind == CellKind.OutputPort)
            .ToDictionary(item => item.cell!.PortId!.Value, item => item.index);
    }

    private CustomCellRuleBinding?[] ResolveCustomRules(PanelCellDefinition?[] cells)
    {
        var registrations = new CustomCellRuleRegistration?[cells.Length];
        for (var index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            if (cell?.Kind != CellKind.Custom)
            {
                continue;
            }

            if (cell.BehaviorId is not { } behaviorId)
            {
                throw new CustomCellRuleException(
                    CustomCellDiagnosticCodes.RegistrationMissing,
                    $"Custom cell '{cell.Id}' has no behavior identifier.");
            }

            var registration = _customCellRuleRegistry.Resolve(behaviorId);
            try
            {
                registration.ParameterValidator(cell.Parameters);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new CustomCellRuleException(
                    CustomCellDiagnosticCodes.ParametersInvalid,
                    $"Parameters are invalid for custom cell '{cell.Id}'.",
                    exception);
            }

            registrations[index] = registration;
        }

        var rules = new CustomCellRuleBinding?[cells.Length];
        for (var index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            if (cell?.Kind != CellKind.Custom)
            {
                continue;
            }

            var registration = registrations[index]!;
            var rule = _customCellRuleRegistry.CreateRule(registration);
            ImmutableArray<byte> initialState;
            try
            {
                initialState = rule.CreateInitialState(cell.Parameters);
                ValidateCustomState(rule, initialState, cell);
            }
            catch (CustomCellRuleException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new CustomCellRuleException(
                    CustomCellDiagnosticCodes.StateInvalid,
                    $"Initial state is invalid for custom cell '{cell.Id}'.",
                    exception);
            }

            rules[index] = new CustomCellRuleBinding(
                rule,
                registration.Ports,
                CopyBytes(initialState));
        }

        return rules;
    }

    private RuntimeCellState CreateRuntimeCellState(
        PanelCellDefinition? cell,
        CustomCellRuleBinding? binding)
    {
        var state = new RuntimeCellState(cell);
        if (cell?.Kind == CellKind.Custom)
        {
            var behaviorId = cell.BehaviorId!.Value;
            state.CustomState = CopyBytes(binding!.InitialState);
            state.CustomRandomState = CreateCustomRandomSeed(Id, cell.Id, behaviorId);
        }

        return state;
    }

    private static ulong CreateCustomRandomSeed(CircuitId panelId, ComponentId cellId, BehaviorId behaviorId)
    {
        var seedMaterial = string.Concat(panelId.Value, "\0", cellId.Value, "\0", behaviorId.Value);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seedMaterial));
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

    private static ImmutableArray<byte> CopyBytes(ImmutableArray<byte> bytes) =>
        bytes.ToArray().ToImmutableArray();

    private static void ValidateCustomState(
        ICustomCellRule rule,
        ImmutableArray<byte> state,
        PanelCellDefinition cell)
    {
        if (state.IsDefault)
        {
            throw new CustomCellRuleException(
                CustomCellDiagnosticCodes.StateInvalid,
                $"Custom cell rule '{cell.BehaviorId}' returned invalid state.");
        }

        try
        {
            rule.ValidateState(cell.Parameters, state);
        }
        catch (CustomCellRuleException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new CustomCellRuleException(
                CustomCellDiagnosticCodes.StateInvalid,
                $"Custom cell rule '{cell.BehaviorId}' returned invalid state.",
                exception);
        }
    }

    private static PanelRuntimeCellSnapshot CaptureCellSnapshot(
        PanelCellDefinition cell,
        RuntimeCellState state) =>
        new(
            cell.Id,
            cell.Kind,
            cell.Location,
            cell.Orientation,
            cell.PortId,
            cell.Parameters,
            cell.BehaviorId,
            state.ExternalInput,
            state.CommittedOutput,
            state.PendingValue,
            state.PendingTick,
            state.FilterCandidate,
            state.FilterCandidateSinceTick,
            state.PreviousData,
            state.PreviousClock,
            state.DataChangedTick,
            state.NextClockTransitionTick,
            state.ObservedValue,
            state.LastForwarded
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToImmutableArray(),
            CopyBytes(state.CustomState),
            state.CustomRandomState);

    private static bool MatchesDefinition(
        PanelCellDefinition? cell,
        PanelRuntimeCellSnapshot? snapshot)
    {
        if (cell is null)
        {
            return snapshot is null;
        }

        return snapshot is not null &&
               snapshot.CellId == cell.Id &&
               snapshot.Kind == cell.Kind &&
               snapshot.Location == cell.Location &&
               snapshot.Orientation == cell.Orientation &&
               snapshot.PortId == cell.PortId &&
               snapshot.Parameters is not null &&
               snapshot.Parameters.SequenceEqual(cell.Parameters) &&
               (cell.Kind == CellKind.Custom || snapshot.BehaviorId == cell.BehaviorId);
    }

    private RuntimeCellState RestoreCellState(
        PanelCellDefinition cell,
        PanelRuntimeCellSnapshot snapshot,
        CustomCellRuleBinding? binding)
    {
        if (snapshot.LastForwarded.IsDefault ||
            snapshot.LastForwarded.Any(pair =>
                string.IsNullOrEmpty(pair.Key) ||
                !StableData.IsParameterName(pair.Key) ||
                !Enum.IsDefined(pair.Value)) ||
            snapshot.LastForwarded.Select(pair => pair.Key)
                .Distinct(StringComparer.Ordinal)
                .Count() != snapshot.LastForwarded.Length ||
            !Enum.IsDefined(snapshot.ExternalInput) ||
            !Enum.IsDefined(snapshot.CommittedOutput) ||
            !Enum.IsDefined(snapshot.PendingValue) ||
            !Enum.IsDefined(snapshot.FilterCandidate) ||
            !Enum.IsDefined(snapshot.PreviousData) ||
            !Enum.IsDefined(snapshot.PreviousClock) ||
            !Enum.IsDefined(snapshot.ObservedValue) ||
            snapshot.LastForwarded.Any(pair =>
            {
                var index = Array.FindIndex(_cells, candidate => candidate?.Id == cell.Id);
                return index >= 0 && !_portSpecs[index].Any(port =>
                    port.Name == pair.Key && port.CanDrive) ||
                    index >= 0 && !_outgoing[index].Any(connection =>
                        connection.SourcePort == pair.Key);
            }))
        {
            throw new ArgumentException("Panel runtime snapshot contains invalid cell state.", nameof(snapshot));
        }

        var state = new RuntimeCellState(cell)
        {
            ExternalInput = snapshot.ExternalInput,
            CommittedOutput = snapshot.CommittedOutput,
            PendingValue = snapshot.PendingValue,
            PendingTick = snapshot.PendingTick,
            FilterCandidate = snapshot.FilterCandidate,
            FilterCandidateSinceTick = snapshot.FilterCandidateSinceTick,
            PreviousData = snapshot.PreviousData,
            PreviousClock = snapshot.PreviousClock,
            DataChangedTick = snapshot.DataChangedTick,
            NextClockTransitionTick = snapshot.NextClockTransitionTick,
            ObservedValue = snapshot.ObservedValue,
            LastForwarded = snapshot.LastForwarded.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal)
        };

        if (cell.Kind != CellKind.Custom)
        {
            if (snapshot.BehaviorId.HasValue)
            {
                throw new ArgumentException("Panel runtime snapshot contains an unexpected behavior identifier.", nameof(snapshot));
            }

            return state;
        }

        if (binding is null ||
            snapshot.BehaviorId is not { } sourceBehaviorId ||
            !CircuitValidator.IsSupportedBehaviorId(sourceBehaviorId) ||
            snapshot.CustomState.IsDefault)
        {
            throw new ArgumentException("Panel runtime snapshot contains invalid custom cell state.", nameof(snapshot));
        }

        var candidateState = CopyBytes(snapshot.CustomState);
        var migrated = sourceBehaviorId != cell.BehaviorId;
        if (migrated)
        {
            try
            {
                if (!binding.Rule.TryMigrateState(sourceBehaviorId, CopyBytes(candidateState), out var migratedState))
                {
                    throw new CustomCellRuleException(
                        CustomCellDiagnosticCodes.MigrationUnavailable,
                        $"Custom cell rule '{cell.BehaviorId}' cannot migrate state from '{sourceBehaviorId}'.");
                }

                if (migratedState.IsDefault)
                {
                    throw new InvalidOperationException("Migration returned invalid state.");
                }

                candidateState = CopyBytes(migratedState);
                ValidateCustomState(binding.Rule, candidateState, cell);
            }
            catch (CustomCellRuleException exception)
                when (exception.Code == CustomCellDiagnosticCodes.MigrationUnavailable)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new CustomCellRuleException(
                    CustomCellDiagnosticCodes.MigrationFailed,
                    $"Custom cell rule '{cell.BehaviorId}' could not migrate state from '{sourceBehaviorId}'.",
                    exception);
            }
        }
        else
        {
            ValidateCustomState(binding.Rule, candidateState, cell);
        }

        state.CustomState = candidateState;
        state.CustomRandomState = snapshot.CustomRandomState;
        return state;
    }

    private string ComputeRuntimeHash(string schedulerHash)
    {
        var customIndexes = _cells
            .Select((cell, index) => (cell, index))
            .Where(item => item.cell?.Kind == CellKind.Custom)
            .ToArray();
        if (customIndexes.Length == 0)
        {
            return schedulerHash;
        }

        var builder = new StringBuilder(schedulerHash);
        foreach (var (cell, index) in customIndexes)
        {
            var state = _states[index];
            builder.Append('\0').Append(cell!.Id.Value)
                .Append('\0').Append(cell.BehaviorId!.Value.Value)
                .Append('\0').Append(Convert.ToBase64String(state.CustomState.ToArray()))
                .Append('\0').Append(state.CustomRandomState.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private (ImmutableArray<CellPortSpec>[] Ports, ImmutableArray<RuntimeConnection>[] Outgoing)
        BuildTopology(PanelCellDefinition?[] cells, CustomCellRuleBinding?[] customRules)
    {
        var ports = cells.Select((cell, index) => cell is null || cell.Kind == CellKind.Empty
            ? ImmutableArray<CellPortSpec>.Empty
            : PanelCellPorts.For(cell, customRules[index]?.Ports ?? default)).ToArray();
        var outgoing = Enumerable.Range(0, cells.Length)
            .Select(_ => ImmutableArray.CreateBuilder<RuntimeConnection>())
            .ToArray();

        for (var sourceIndex = 0; sourceIndex < cells.Length; sourceIndex++)
        {
            var source = cells[sourceIndex];
            if (!IsActive(source))
            {
                continue;
            }

            foreach (var sourcePort in ports[sourceIndex].Where(port => port.CanDrive))
            {
                if (!TryGetNeighbor(sourceIndex, sourcePort.Direction, out var targetIndex) ||
                    !IsActive(cells[targetIndex]))
                {
                    continue;
                }

                var targetPort = ports[targetIndex].FirstOrDefault(port =>
                    port.CanReceive &&
                    port.Direction == PanelCellPorts.Opposite(sourcePort.Direction));
                if (targetPort.Name is null)
                {
                    continue;
                }

                outgoing[sourceIndex].Add(new RuntimeConnection(
                    sourcePort.Name,
                    targetIndex,
                    targetPort.Name));
            }
        }

        return (ports, outgoing.Select(builder => builder.ToImmutable()).ToArray());
    }

    private bool TryGetNeighbor(int index, CardinalDirection direction, out int neighbor)
    {
        var x = index % Width;
        var y = index / Width;
        switch (direction)
        {
            case CardinalDirection.North when y > 0:
                neighbor = index - Width;
                return true;
            case CardinalDirection.East when x + 1 < Width:
                neighbor = index + 1;
                return true;
            case CardinalDirection.South when y + 1 < Height:
                neighbor = index + Width;
                return true;
            case CardinalDirection.West when x > 0:
                neighbor = index - 1;
                return true;
            default:
                neighbor = -1;
                return false;
        }
    }

    private int IndexOf(GridCoordinate location)
    {
        if ((uint)location.X >= (uint)Width || (uint)location.Y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(location), "Cell coordinate is outside the panel.");
        }

        return location.Y * Width + location.X;
    }

    private static bool IsActive(PanelCellDefinition? cell) =>
        cell is not null && cell.Kind != CellKind.Empty;

    private static string ComputeStateIntegrityHash(PanelRuntimeSnapshot snapshot)
    {
        var payload = new
        {
            snapshot.PanelId,
            snapshot.Width,
            snapshot.Height,
            snapshot.Cells,
            snapshot.ProbeHistory,
            snapshot.TargetIncarnations,
            snapshot.TargetIncarnationTimeline,
            SchedulerTraceIntegrity = snapshot.Scheduler.TraceIntegrityHash,
            snapshot.Scheduler.TraceStartTick
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload))).ToLowerInvariant();
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private void EnsureNotStepping()
    {
        if (_isStepping)
        {
            throw new InvalidOperationException("Panel state cannot be changed during a step.");
        }
    }

    private sealed record RuntimeConnection(string SourcePort, int TargetIndex, string TargetPort);

    private sealed class RuntimeCellState
    {
        internal RuntimeCellState(PanelCellDefinition? cell)
        {
            LastForwarded = new Dictionary<string, LogicValue>(StringComparer.Ordinal);
            if (cell is null)
            {
                return;
            }

            if (cell.Kind == CellKind.Clock)
            {
                CommittedOutput = LogicValue.Low;
                NextClockTransitionTick = cell.RuntimeParameters.LowTicks;
            }
            else if (cell.Kind == CellKind.DFlipFlop)
            {
                CommittedOutput = LogicValue.Unknown;
            }
        }

        internal LogicValue ExternalInput { get; set; } = LogicValue.HighImpedance;

        internal LogicValue CommittedOutput { get; set; } = LogicValue.HighImpedance;

        internal LogicValue PendingValue { get; set; } = LogicValue.HighImpedance;

        internal long PendingTick { get; set; } = -1;

        internal LogicValue FilterCandidate { get; set; } = LogicValue.HighImpedance;

        internal long FilterCandidateSinceTick { get; set; }

        internal LogicValue PreviousData { get; set; } = LogicValue.HighImpedance;

        internal LogicValue PreviousClock { get; set; } = LogicValue.Low;

        internal long DataChangedTick { get; set; }

        internal long NextClockTransitionTick { get; set; }

        internal LogicValue ObservedValue { get; set; } = LogicValue.HighImpedance;

        internal ImmutableArray<byte> CustomState { get; set; } = ImmutableArray<byte>.Empty;

        internal ulong CustomRandomState { get; set; }

        internal Dictionary<string, LogicValue> LastForwarded { get; set; }

        internal RuntimeCellState Clone()
        {
            var clone = (RuntimeCellState)MemberwiseClone();
            clone.LastForwarded = new Dictionary<string, LogicValue>(LastForwarded, StringComparer.Ordinal);
            return clone;
        }
    }
}
