using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Workbench;

public sealed record WorkbenchDiagnostic(string Code, string Message);

public enum WorkbenchCommandStatus
{
    Pending,
    Committed
}

public sealed record WorkbenchCommand(
    long AcceptedOrdinal,
    long ApplyAtTick,
    string Action,
    GridCoordinate? Location,
    WorkbenchCommandStatus Status);

public sealed class PanelWorkbenchSession
{
    private readonly PanelOwnedChipNetworkInstance _chipNetwork;
    private DeviceGraphInstance? _deviceGraph;
    private readonly List<PendingMutation> _runningMutations = [];
    private readonly List<PendingMutation> _stagedMutations = [];
    private readonly List<WorkbenchCommand> _commandLog = [];
    private readonly Dictionary<PortId, LogicValue> _inputValues = [];
    private WorkbenchDefinition _committedDefinition;
    private long _nextOrdinal;

    public PanelWorkbenchSession(
        PanelDefinition definition,
        int cycleTicks = 4,
        CustomCellRuleRegistry? customCellRules = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (cycleTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleTicks), "Cycle length must be positive.");
        }

        var rules = customCellRules ?? new CustomCellRuleRegistry([]);
        _committedDefinition = WorkbenchDefinition.Create(definition);
        _chipNetwork = new PanelOwnedChipNetworkInstance(
            definition,
            _committedDefinition.ChipNetwork,
            _committedDefinition.ChipCatalog,
            rules);
        CycleTicks = cycleTicks;
        ResetInputValues(definition);
    }

    public event Action? Changed;

    public WorkbenchDefinition CommittedDefinition => _committedDefinition;

    public WorkbenchDefinition Definition => LastMutation()?.Definition ?? _committedDefinition;

    public bool IsPaused { get; private set; } = true;

    public long CurrentTick => _chipNetwork.CurrentTick;

    private int _cycleTicks;

    public int CycleTicks
    {
        get => _cycleTicks;
        set => _cycleTicks = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Cycle length must be positive.");
    }

    public GridCoordinate? Selection { get; private set; }

    public WorkbenchDiagnostic? LastDiagnostic { get; private set; }

    public ImmutableArray<WorkbenchCommand> CommandLog => _commandLog.ToImmutableArray();

    public ImmutableArray<ProbeSample> ProbeHistory => _chipNetwork.ProbeHistory;

    public PanelCellDefinition? GetCell(GridCoordinate location) => Definition.Panel.GetCell(location);

    public LogicValue GetOutput(PortId portId) => _chipNetwork.GetOutput(portId);

    public LogicValue GetChipOutput(ComponentId instanceId, string portName) =>
        _chipNetwork.GetInstance(instanceId).GetOutput(portName);

    public DeviceSignal GetDeviceOutput(ComponentId deviceId, string portName) =>
        (_deviceGraph ?? throw new InvalidOperationException("No device graph is active."))
        .GetOutput(deviceId, portName);

    public ImmutableArray<CableLaneHistoryEntry> GetCableLaneHistory(ComponentId laneId) =>
        (_deviceGraph ?? throw new InvalidOperationException("No device graph is active."))
        .GetLaneHistory(laneId);

    public ImmutableArray<ProbeSample> Waveform(ComponentId probeId) =>
        _chipNetwork.ProbeHistory.Where(sample => sample.ProbeId == probeId).ToImmutableArray();

    public int StagedEditCount => _stagedMutations.Count;

    public int PendingCommandCount => _runningMutations.Count;

    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        Changed?.Invoke();
    }

    public bool TrySelect(GridCoordinate location, out WorkbenchDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (!IsInside(Definition.Panel, location))
        {
            diagnostic = SetDiagnostic("workbench.selection.out-of-bounds", "Selection is outside the panel.");
            return false;
        }

        Selection = location;
        LastDiagnostic = null;
        Changed?.Invoke();
        return true;
    }

    public bool TryPaint(
        GridCoordinate location,
        CellKind kind,
        CardinalDirection orientation,
        out WorkbenchDiagnostic? diagnostic,
        PortId? portId = null,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        BehaviorId? behaviorId = null)
    {
        if (kind == CellKind.Empty)
        {
            diagnostic = SetDiagnostic("workbench.paint.empty", "Use erase to clear a cell.");
            return false;
        }

        try
        {
            if (kind is CellKind.InputPort or CellKind.OutputPort && !portId.HasValue)
            {
                portId = new PortId($"port/{location.X}/{location.Y}");
            }

            var cell = PanelCellDefinition.Create(
                new ComponentId($"cell/{location.Y}/{location.X}"),
                location,
                kind,
                orientation,
                portId,
                parameters,
                behaviorId);
            return TryPaint(cell, out diagnostic);
        }
        catch (ArgumentException exception)
        {
            diagnostic = SetDiagnostic("workbench.paint.invalid", exception.Message);
            return false;
        }
    }

    public bool TryPaint(PanelCellDefinition cell, out WorkbenchDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return TryEdit(
            "paint",
            cell.Location,
            "workbench.paint.invalid",
            current =>
            {
                _ = current.Panel.GetCell(cell.Location);
                if (current.ChipPlacements.Any(item => item.Location == cell.Location) ||
                    current.DevicePlacements.Any(item => item.Location == cell.Location))
                {
                    throw new InvalidOperationException("A chip or device already occupies this cell.");
                }

                var cells = current.Panel.Cells
                    .OfType<PanelCellDefinition>()
                    .Where(existing => existing.Location != cell.Location)
                    .Append(cell);
                var panel = PanelDefinition.Create(current.Panel.Id, current.Panel.Width, current.Panel.Height, cells);
                return current.WithPanel(panel);
            },
            out diagnostic);
    }

    public bool TryErase(GridCoordinate location, out WorkbenchDiagnostic? diagnostic) => TryEdit(
        "erase",
        location,
        "workbench.erase.empty",
        current =>
        {
            if (current.Panel.GetCell(location) is null)
            {
                throw new InvalidOperationException("There is no cell to erase.");
            }

            var panel = PanelDefinition.Create(
                current.Panel.Id,
                current.Panel.Width,
                current.Panel.Height,
                current.Panel.Cells.OfType<PanelCellDefinition>().Where(cell => cell.Location != location));
            return current.WithPanel(panel);
        },
        out diagnostic);

    public bool TryRotate(GridCoordinate location, out WorkbenchDiagnostic? diagnostic) => TryEdit(
        "rotate",
        location,
        "workbench.rotate.empty",
        current =>
        {
            var cell = current.Panel.GetCell(location)
                ?? throw new InvalidOperationException("There is no cell to rotate.");
            var orientation = (CardinalDirection)(((int)cell.Orientation + 1) % 4);
            var rotated = PanelCellDefinition.Create(
                cell.Id,
                cell.Location,
                cell.Kind,
                orientation,
                cell.PortId,
                cell.Parameters,
                cell.BehaviorId);
            var cells = current.Panel.Cells
                .OfType<PanelCellDefinition>()
                .Where(existing => existing.Location != location)
                .Append(rotated);
            return current.WithPanel(PanelDefinition.Create(
                current.Panel.Id,
                current.Panel.Width,
                current.Panel.Height,
                cells));
        },
        out diagnostic);

    public bool TryDriveInput(PortId portId, LogicValue value, out WorkbenchDiagnostic? diagnostic)
    {
        if (!Enum.IsDefined(value) || !Definition.Panel.Cells.OfType<PanelCellDefinition>().Any(cell =>
                cell.Kind == CellKind.InputPort && cell.PortId == portId))
        {
            diagnostic = SetDiagnostic("workbench.input.invalid", $"Input port '{portId}' is not defined.");
            return false;
        }

        return QueueMutation(
            new PendingMutation("drive input", null, Definition, portId, value, null, null),
            out diagnostic);
    }

    public bool TryDriveChipInput(
        ComponentId instanceId,
        string portName,
        LogicValue value,
        out WorkbenchDiagnostic? diagnostic)
    {
        var instance = Definition.ChipNetwork.Instances.FirstOrDefault(item => item.InstanceId == instanceId);
        ChipDefinition? chip = null;
        if (instance is not null)
        {
            Definition.ChipCatalog.TryResolve(instance.DefinitionId, instance.ContentHash, out chip);
        }

        var port = chip?.Ports.FirstOrDefault(item =>
            string.Equals(item.Name, portName, StringComparison.Ordinal));
        if (!Enum.IsDefined(value) || port is null || port.Direction != ChipPortDirection.Input ||
            Definition.ChipNetwork.Connections.Any(connection =>
                connection.Target.InstanceId == instanceId &&
                string.Equals(connection.Target.PortName, portName, StringComparison.Ordinal)))
        {
            diagnostic = SetDiagnostic(
                "workbench.chip-input.invalid",
                $"Input '{instanceId}.{portName}' is missing, connected, or has an invalid value.");
            return false;
        }

        var placement = Definition.ChipPlacements.FirstOrDefault(item => item.InstanceId == instanceId);
        return QueueMutation(
            new PendingMutation(
                "drive chip input",
                placement?.Location,
                Definition,
                null,
                null,
                null,
                null,
                ChipInput: new ChipInputDrive(instanceId, portName, value)),
            out diagnostic);
    }

    public bool TryDriveDeviceInput(
        ComponentId deviceId,
        string portName,
        DeviceSignal signal,
        out WorkbenchDiagnostic? diagnostic)
    {
        var graph = Definition.DeviceGraph;
        var device = graph?.Devices.FirstOrDefault(item => item.Id == deviceId);
        var port = device?.Ports.FirstOrDefault(item =>
            string.Equals(item.Name, portName, StringComparison.Ordinal));
        if (graph is null || port is null || port.Direction != DevicePortDirection.Input ||
            signal.Bits.IsDefaultOrEmpty || signal.Bits.Length != port.Width ||
            signal.Bits.Any(value => !Enum.IsDefined(value)) ||
            port.ValueContract == DeviceValueContract.BinaryLogic && signal.Bits.Contains(LogicValue.Unknown) ||
            graph.Lanes.Any(lane => lane.Target.DeviceId == deviceId &&
                                    string.Equals(lane.Target.PortName, portName, StringComparison.Ordinal)))
        {
            diagnostic = SetDiagnostic(
                "workbench.device-input.invalid",
                $"Input '{deviceId}.{portName}' is missing, cable-driven, or has an invalid signal.");
            return false;
        }

        return QueueMutation(
            new PendingMutation(
                "drive device input",
                null,
                Definition,
                null,
                null,
                null,
                null,
                new DeviceInputDrive(deviceId, portName, signal)),
            out diagnostic);
    }

    public LogicValue GetInputValue(PortId portId)
    {
        var pending = _runningMutations.Concat(_stagedMutations)
            .LastOrDefault(mutation => mutation.InputPort == portId);
        if (pending is not null)
        {
            return pending.InputValue!.Value;
        }

        return _inputValues.GetValueOrDefault(portId, LogicValue.HighImpedance);
    }

    public LogicValue GetCommittedInputValue(PortId portId) =>
        _inputValues.GetValueOrDefault(portId, LogicValue.HighImpedance);

    public bool TryPackagePanelAsChip(
        DefinitionId definitionId,
        string symbol,
        out ChipDefinition? chip,
        out WorkbenchDiagnostic? diagnostic) => TryEditWithResult(
        "package chip",
        null,
        "workbench.chip.invalid",
        current =>
        {
            var ports = current.Panel.Cells
                .OfType<PanelCellDefinition>()
                .Where(cell => cell.Kind is CellKind.InputPort or CellKind.OutputPort)
                .Select(cell => new ChipPortDefinition(
                    cell.PortId!.Value.Value,
                    cell.PortId.Value,
                    cell.Kind == CellKind.InputPort ? ChipPortDirection.Input : ChipPortDirection.Output))
                .DistinctBy(port => port.PanelPortId.Value);
            var packaged = ChipDefinition.Create(definitionId, current.Panel, ports, 1, 1, symbol);
            var catalog = ChipDefinitionCatalog.Create(
                current.ChipCatalog.Definitions.Append(packaged)
                    .DistinctBy(item => (item.Id.Value, item.ContentHash)));
            return (current.WithChipState(catalog, current.ChipNetwork, current.ChipPlacements), packaged);
        },
        out chip,
        out diagnostic);

    public bool TryPlaceChip(
        ChipDefinition chip,
        ComponentId instanceId,
        GridCoordinate location,
        out WorkbenchDiagnostic? diagnostic)
    {
        if (chip is null)
        {
            diagnostic = SetDiagnostic("workbench.chip.invalid", "Chip definition is required.");
            return false;
        }

        return TryEdit(
        "place chip",
        location,
        "workbench.chip.invalid",
        current =>
        {
            EnsurePlaceable(current, location, current.ChipPlacements.Select(item => item.Location)
                .Concat(current.DevicePlacements.Select(item => item.Location)));
            var catalog = ChipDefinitionCatalog.Create(
                current.ChipCatalog.Definitions.Append(chip)
                    .DistinctBy(item => (item.Id.Value, item.ContentHash)));
            var instance = ChipInstanceDefinition.Create(instanceId, chip.Id, chip.ContentHash);
            var network = PanelOwnedChipNetworkDefinition.Create(
                current.Panel.Id,
                current.ChipNetwork.Instances.Append(instance),
                current.ChipNetwork.Connections);
            var errors = network.Validate(current.Panel, catalog);
            if (!errors.IsDefaultOrEmpty)
            {
                throw new ChipDefinitionException(errors);
            }

            return current.WithChipState(
                catalog,
                network,
                current.ChipPlacements.Add(new WorkbenchChipPlacement(
                    instanceId, chip.Id, chip.ContentHash, location)));
        },
        out diagnostic);
    }

    public bool TryPlaceDevice(
        DeviceDefinition device,
        GridCoordinate location,
        out WorkbenchDiagnostic? diagnostic) => TryEdit(
        "place device",
        location,
        "workbench.device.invalid",
        current =>
        {
            EnsurePlaceable(current, location, current.ChipPlacements.Select(item => item.Location)
                .Concat(current.DevicePlacements.Select(item => item.Location)));
            var graph = DeviceGraphDefinition.Create(
                current.DeviceGraph?.Id ?? new DefinitionId("graph/workbench"),
                (current.DeviceGraph?.Devices ?? []).Append(device),
                current.DeviceGraph?.Lanes ?? [],
                current.DeviceGraph?.Bundles ?? []);
            return current.WithDeviceState(
                graph,
                current.DevicePlacements.Add(new WorkbenchDevicePlacement(device.Id, location)));
        },
        out diagnostic);

    public bool TryAddCableBundle(
        CableBundleDefinition bundle,
        IEnumerable<CableLaneDefinition> lanes,
        out WorkbenchDiagnostic? diagnostic) => TryEdit(
        "connect cable bundle",
        null,
        "workbench.cable.invalid",
        current =>
        {
            if (current.DeviceGraph is null)
            {
                throw new InvalidOperationException("Place devices before adding a cable bundle.");
            }

            var graph = DeviceGraphDefinition.Create(
                current.DeviceGraph.Id,
                current.DeviceGraph.Devices,
                current.DeviceGraph.Lanes.Concat(lanes),
                current.DeviceGraph.Bundles.Append(bundle));
            return current.WithDeviceGraph(graph);
        },
        out diagnostic);

    public bool CommitStaged(out WorkbenchDiagnostic? diagnostic)
    {
        if (!IsPaused)
        {
            diagnostic = SetDiagnostic("workbench.commit.running", "Pause before committing staged edits.");
            return false;
        }

        CommitPendingMutations();
        diagnostic = null;
        LastDiagnostic = null;
        Changed?.Invoke();
        return true;
    }

    public PanelTickResult StepMicrotick(bool commitStaged = true)
    {
        if (commitStaged)
        {
            CommitPendingMutations();
        }
        else
        {
            CommitRunningMutations();
        }

        var tick = CurrentTick;
        if (_deviceGraph is not null)
        {
            if (_deviceGraph.CurrentTick != tick)
            {
                throw new InvalidOperationException("Panel and device runtimes must share the same microtick.");
            }

            var graphTick = _deviceGraph.Step().Tick;
            if (graphTick != tick)
            {
                throw new InvalidOperationException("Device graph stepped at a different microtick.");
            }
        }

        var result = _chipNetwork.Step().OwnerPanel;
        if (result.Tick != tick)
        {
            throw new InvalidOperationException("Panel runtime stepped at a different microtick.");
        }

        Changed?.Invoke();
        return result;
    }

    public ImmutableArray<PanelTickResult> StepConfiguredCycles()
    {
        if (CycleTicks <= 0)
        {
            throw new InvalidOperationException("Cycle length must be positive.");
        }

        var results = ImmutableArray.CreateBuilder<PanelTickResult>(CycleTicks);
        for (var index = 0; index < CycleTicks; index++)
        {
            results.Add(StepMicrotick());
        }

        return results.ToImmutable();
    }

    private bool TryEdit(
        string action,
        GridCoordinate? location,
        string diagnosticCode,
        Func<WorkbenchDefinition, WorkbenchDefinition> edit,
        out WorkbenchDiagnostic? diagnostic) => TryEditWithResult(
        action,
        location,
        diagnosticCode,
        current => (edit(current), true),
        out _,
        out diagnostic);

    private bool TryEditWithResult<TResult>(
        string action,
        GridCoordinate? location,
        string diagnosticCode,
        Func<WorkbenchDefinition, (WorkbenchDefinition Definition, TResult Result)> edit,
        out TResult? result,
        out WorkbenchDiagnostic? diagnostic)
    {
        try
        {
            var candidate = edit(Definition);
            if (QueueMutation(new PendingMutation(action, location, candidate.Definition, null, null, null, null),
                    out diagnostic))
            {
                result = candidate.Result;
                return true;
            }

            result = default;
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                          KeyNotFoundException or ChipDefinitionException)
        {
            result = default;
            diagnostic = SetDiagnostic(diagnosticCode, exception.Message);
            return false;
        }
    }

    private bool QueueMutation(PendingMutation mutation, out WorkbenchDiagnostic? diagnostic)
    {
        if (IsPaused || _stagedMutations.Count > 0)
        {
            _stagedMutations.Add(mutation);
            diagnostic = null;
            LastDiagnostic = null;
            Changed?.Invoke();
            return true;
        }

        var ordinal = ++_nextOrdinal;
        var accepted = mutation with { AcceptedOrdinal = ordinal, ApplyAtTick = CurrentTick };
        _runningMutations.Add(accepted);
        _commandLog.Add(new WorkbenchCommand(
            ordinal,
            CurrentTick,
            mutation.Action,
            mutation.Location,
            WorkbenchCommandStatus.Pending));
        diagnostic = null;
        LastDiagnostic = null;
        Changed?.Invoke();
        return true;
    }

    private void CommitPendingMutations()
    {
        CommitRunningMutations();
        foreach (var mutation in _stagedMutations)
        {
            var ordinal = ++_nextOrdinal;
            var accepted = mutation with { AcceptedOrdinal = ordinal, ApplyAtTick = CurrentTick };
            _commandLog.Add(new WorkbenchCommand(
                ordinal,
                CurrentTick,
                mutation.Action,
                mutation.Location,
                WorkbenchCommandStatus.Pending));
            ApplyMutation(accepted);
            SetCommandStatus(ordinal, WorkbenchCommandStatus.Committed);
        }

        _stagedMutations.Clear();
    }

    private void CommitRunningMutations()
    {
        foreach (var mutation in _runningMutations)
        {
            ApplyMutation(mutation);
            SetCommandStatus(mutation.AcceptedOrdinal!.Value, WorkbenchCommandStatus.Committed);
        }

        _runningMutations.Clear();
    }

    private void ApplyMutation(PendingMutation mutation)
    {
        if (mutation.ChipInput is { } chipInput)
        {
            _chipNetwork.GetInstance(chipInput.InstanceId).SetInput(chipInput.PortName, chipInput.Value);
            return;
        }

        if (mutation.DeviceInput is { } deviceInput)
        {
            (_deviceGraph ?? throw new InvalidOperationException("No device graph is active."))
                .SetInput(deviceInput.DeviceId, deviceInput.PortName, deviceInput.Signal);
            return;
        }

        if (mutation.InputPort is { } portId)
        {
            var value = mutation.InputValue!.Value;
            _chipNetwork.SetInput(portId, value);
            _inputValues[portId] = value;
        }
        else
        {
            var previousChipNetwork = _committedDefinition.ChipNetwork;
            var previousChipCatalog = _committedDefinition.ChipCatalog;
            var previousGraph = _committedDefinition.DeviceGraph;
            if (!ReferenceEquals(_committedDefinition.Panel, mutation.Definition.Panel))
            {
                ApplyPanelChanges(
                    _committedDefinition.Panel,
                    mutation.Definition.Panel,
                    mutation.Location ?? throw new InvalidOperationException("Panel edits require a cell location."));
            }

            _committedDefinition = mutation.Definition;
            if (!ReferenceEquals(previousChipNetwork, mutation.Definition.ChipNetwork) ||
                !ReferenceEquals(previousChipCatalog, mutation.Definition.ChipCatalog))
            {
                _chipNetwork.ExtendDefinition(
                    mutation.Definition.ChipNetwork,
                    mutation.Definition.ChipCatalog);
            }

            if (!ReferenceEquals(previousGraph, mutation.Definition.DeviceGraph))
            {
                ApplyDeviceGraphChanges(mutation.Definition.DeviceGraph);
            }
        }
    }

    private void ApplyDeviceGraphChanges(DeviceGraphDefinition? definition)
    {
        if (definition is null)
        {
            if (_deviceGraph is not null)
            {
                throw new InvalidOperationException("An active device graph cannot be removed.");
            }

            return;
        }

        if (_deviceGraph is null)
        {
            _deviceGraph = new DeviceGraphInstance(definition, CurrentTick);
        }
        else
        {
            _deviceGraph.ExtendDefinition(definition);
        }
    }

    private void ApplyPanelChanges(PanelDefinition previous, PanelDefinition next, GridCoordinate location)
    {
        var before = previous.GetCell(location);
        var after = next.GetCell(location);
        if (ReferenceEquals(before, after))
        {
            return;
        }

        _chipNetwork.UpdateOwnerPanel(next, location);

        var inputPorts = next.Cells.OfType<PanelCellDefinition>()
            .Where(cell => cell.Kind == CellKind.InputPort && cell.PortId.HasValue)
            .Select(cell => cell.PortId!.Value)
            .Distinct()
            .ToArray();
        foreach (var port in _inputValues.Keys.Where(port => !inputPorts.Contains(port)).ToArray())
        {
            _inputValues.Remove(port);
        }

        foreach (var port in inputPorts)
        {
            if (!_inputValues.ContainsKey(port))
            {
                _inputValues.Add(port, LogicValue.HighImpedance);
            }

            _chipNetwork.SetInput(port, _inputValues[port]);
        }
    }

    private void ResetInputValues(PanelDefinition definition)
    {
        foreach (var cell in definition.Cells.OfType<PanelCellDefinition>()
                     .Where(cell => cell.Kind == CellKind.InputPort && cell.PortId.HasValue))
        {
            _inputValues[new PortId(cell.PortId!.Value.Value)] = LogicValue.HighImpedance;
        }
    }

    private void SetCommandStatus(long ordinal, WorkbenchCommandStatus status)
    {
        var index = _commandLog.FindIndex(command => command.AcceptedOrdinal == ordinal);
        if (index >= 0)
        {
            _commandLog[index] = _commandLog[index] with { Status = status };
        }
    }

    private PendingMutation? LastMutation() =>
        _stagedMutations.LastOrDefault() ?? _runningMutations.LastOrDefault();

    private static void EnsurePlaceable(
        WorkbenchDefinition definition,
        GridCoordinate location,
        IEnumerable<GridCoordinate> usedLocations)
    {
        if (!IsInside(definition.Panel, location))
        {
            throw new ArgumentOutOfRangeException(nameof(location), "Placement is outside the panel.");
        }

        if (definition.Panel.GetCell(location) is not null || usedLocations.Contains(location))
        {
            throw new InvalidOperationException("Placement location is occupied.");
        }
    }

    private static bool IsInside(PanelDefinition panel, GridCoordinate location) =>
        (uint)location.X < (uint)panel.Width && (uint)location.Y < (uint)panel.Height;

    private WorkbenchDiagnostic SetDiagnostic(string code, string message)
    {
        LastDiagnostic = new WorkbenchDiagnostic(code, message);
        Changed?.Invoke();
        return LastDiagnostic;
    }

    private sealed record PendingMutation(
        string Action,
        GridCoordinate? Location,
        WorkbenchDefinition Definition,
        PortId? InputPort,
        LogicValue? InputValue,
        long? AcceptedOrdinal,
        long? ApplyAtTick,
        DeviceInputDrive? DeviceInput = null,
        ChipInputDrive? ChipInput = null);

    private sealed record DeviceInputDrive(ComponentId DeviceId, string PortName, DeviceSignal Signal);

    private sealed record ChipInputDrive(ComponentId InstanceId, string PortName, LogicValue Value);
}
