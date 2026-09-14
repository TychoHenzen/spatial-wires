using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
                _targetHandles[index] = _scheduler.RegisterTarget(_cells[index]!.Id.Value);
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

        return new PanelRuntimeSnapshot(
            Id,
            Width,
            Height,
            cells.ToImmutable(),
            _scheduler.CaptureSnapshot(),
            _probeHistory.ToImmutableArray());
    }

    public void RestoreSnapshot(PanelRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureNotStepping();
        ArgumentNullException.ThrowIfNull(snapshot.Scheduler);
        if (snapshot.PanelId != Id || snapshot.Width != Width || snapshot.Height != Height ||
            snapshot.Cells.IsDefault || snapshot.Cells.Length != _cells.Length ||
            snapshot.ProbeHistory.IsDefault)
        {
            throw new ArgumentException("Panel runtime snapshot does not match this panel.", nameof(snapshot));
        }

        var restoredStates = new RuntimeCellState[_cells.Length];
        for (var index = 0; index < _cells.Length; index++)
        {
            var cell = _cells[index];
            var saved = snapshot.Cells[index];
            if (!MatchesDefinition(cell, saved))
            {
                throw new ArgumentException("Panel runtime snapshot does not match this panel.", nameof(snapshot));
            }

            restoredStates[index] = saved is null
                ? new RuntimeCellState(null)
                : RestoreCellState(cell!, saved, _customRules[index]);
        }

        _scheduler.RestoreSnapshot(snapshot.Scheduler);
        _states = restoredStates;
        _probeHistory.Clear();
        _probeHistory.AddRange(snapshot.ProbeHistory);
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

        if (IsActive(previous))
        {
            _scheduler.RemoveTarget(previous!.Id.Value);
        }

        _cells = validatedCells;
        _customRules = validatedRules;
        _states[index] = CreateRuntimeCellState(replacement, _customRules[index]);
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
            !Enum.IsDefined(snapshot.ObservedValue))
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
