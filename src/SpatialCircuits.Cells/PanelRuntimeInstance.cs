using System.Collections.Immutable;
using SpatialCircuits.Core;

namespace SpatialCircuits.Cells;

public sealed record ProbeSample(ComponentId ProbeId, long Tick, LogicValue Value);

public sealed record PanelTickResult(
    long Tick,
    string Hash,
    ImmutableArray<ProbeSample> ProbeSamples,
    ImmutableSortedDictionary<string, string> Outputs,
    ImmutableArray<SchedulerDiagnostic> Diagnostics);

public sealed class PanelRuntimeInstance
{
    private const string NandCommitEvent = "nand-commit";
    private const string FlipFlopCommitEvent = "dff-commit";
    private readonly DeterministicScheduler _scheduler;
    private readonly List<ProbeSample> _probeHistory = [];
    private PanelCellDefinition?[] _cells;
    private RuntimeCellState[] _states;
    private SchedulerTargetHandle?[] _targetHandles;
    private ImmutableArray<CellPortSpec>[] _portSpecs;
    private ImmutableArray<RuntimeConnection>[] _outgoing;
    private Dictionary<PortId, int[]> _inputPortCells = [];
    private Dictionary<PortId, int> _outputPortCells = [];
    private RuntimeCellState[]? _workingStates;
    private bool _isStepping;

    public PanelRuntimeInstance(PanelDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Id = definition.Id;
        Width = definition.Width;
        Height = definition.Height;
        _cells = definition.Cells.ToArray();
        _states = _cells.Select(cell => new RuntimeCellState(cell)).ToArray();
        _targetHandles = new SchedulerTargetHandle?[_cells.Length];
        _scheduler = new DeterministicScheduler(Evaluate);
        for (var index = 0; index < _cells.Length; index++)
        {
            if (IsActive(_cells[index]))
            {
                _targetHandles[index] = _scheduler.RegisterTarget(_cells[index]!.Id.Value);
            }
        }

        (_portSpecs, _outgoing) = BuildTopology(_cells);
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
                result.Hash,
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

        if (IsActive(removed))
        {
            _scheduler.RemoveTarget(removed.Id.Value);
        }

        _cells = validated.Cells.ToArray();
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

        if (IsActive(previous))
        {
            _scheduler.RemoveTarget(previous!.Id.Value);
        }

        _cells = validated.Cells.ToArray();
        _states[index] = new RuntimeCellState(replacement);
        _targetHandles[index] = IsActive(replacement)
            ? _scheduler.RegisterTarget(replacement.Id.Value)
            : null;
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
            default:
                throw new ArgumentOutOfRangeException(nameof(cell), cell.Kind, "Cell kind is not supported.");
        }

        return outputs;
    }

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
        (_portSpecs, _outgoing) = BuildTopology(_cells);
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

    private (ImmutableArray<CellPortSpec>[] Ports, ImmutableArray<RuntimeConnection>[] Outgoing)
        BuildTopology(PanelCellDefinition?[] cells)
    {
        var ports = cells.Select(cell => cell is null || cell.Kind == CellKind.Empty
            ? ImmutableArray<CellPortSpec>.Empty
            : PanelCellPorts.For(cell)).ToArray();
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

        internal Dictionary<string, LogicValue> LastForwarded { get; private set; }

        internal RuntimeCellState Clone()
        {
            var clone = (RuntimeCellState)MemberwiseClone();
            clone.LastForwarded = new Dictionary<string, LogicValue>(LastForwarded, StringComparer.Ordinal);
            return clone;
        }
    }
}
