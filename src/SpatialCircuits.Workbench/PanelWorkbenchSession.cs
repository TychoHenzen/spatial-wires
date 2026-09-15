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
    private readonly CustomCellRuleRegistry _customCellRules;
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
        _customCellRules = rules;
        _chipNetwork = new PanelOwnedChipNetworkInstance(
            definition,
            _committedDefinition.ChipNetwork,
            _committedDefinition.ChipCatalog,
            rules);
        SaveId = Guid.NewGuid();
        CycleTicks = cycleTicks;
        ResetInputValues(definition);
    }

    public PanelWorkbenchSession(
        WorkbenchDefinition definition,
        int cycleTicks = 4,
        CustomCellRuleRegistry? customCellRules = null)
        : this(
            definition,
            cycleTicks,
            customCellRules ?? new CustomCellRuleRegistry([]),
            initialTick: 0)
    {
    }

    private PanelWorkbenchSession(
        WorkbenchDefinition definition,
        int cycleTicks,
        CustomCellRuleRegistry customCellRules,
        long initialTick,
        PanelOwnedChipNetworkRuntimeSnapshot? chipNetworkSnapshot = null,
        Guid? saveId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(customCellRules);
        if (cycleTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleTicks), "Cycle length must be positive.");
        }

        if (initialTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTick), "Initial tick cannot be negative.");
        }

        _committedDefinition = definition;
        _customCellRules = customCellRules;
        SaveId = saveId ?? Guid.NewGuid();
        _chipNetwork = chipNetworkSnapshot is null
            ? new PanelOwnedChipNetworkInstance(
                definition.Panel,
                definition.ChipNetwork,
                definition.ChipCatalog,
                customCellRules,
                initialTick)
            : PanelOwnedChipNetworkInstance.RestoreFromSnapshot(
                definition.Panel,
                definition.ChipNetwork,
                definition.ChipCatalog,
                chipNetworkSnapshot,
                customCellRules);
        _deviceGraph = definition.DeviceGraph is { } graph
            ? new DeviceGraphInstance(graph, initialTick)
            : null;
        CycleTicks = cycleTicks;
        ResetInputValues(definition.Panel);
    }

    public event Action? Changed;

    public WorkbenchDefinition CommittedDefinition => _committedDefinition;

    public WorkbenchDefinition Definition => LastMutation()?.Definition ?? _committedDefinition;

    public bool IsPaused { get; private set; } = true;

    public Guid SaveId { get; private set; }

    public long CurrentTick => _chipNetwork.CurrentTick;

    public CustomCellRuleRegistry CustomCellRules => _customCellRules;

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

    public ImmutableArray<AcceptedSchedulerCommand> DeviceAcceptedCommands =>
        (_deviceGraph?.AcceptedCommands ?? []).ToImmutableArray();

    public ImmutableArray<WorkbenchBehaviorAssemblySource> BehaviorAssemblySources
    {
        get
        {
            var sources = new List<WorkbenchBehaviorAssemblySource>
            {
                new("spatial-circuits/core", typeof(DeterministicScheduler).Assembly, null, false),
                new("spatial-circuits/cells", typeof(PanelRuntimeInstance).Assembly, null, false),
                new("spatial-circuits/hierarchy", typeof(DeviceGraphInstance).Assembly, null, false),
                new("spatial-circuits/workbench", typeof(PanelWorkbenchSession).Assembly, null, false)
            };
            sources.AddRange(_customCellRules.AssemblySources.Select(source => new WorkbenchBehaviorAssemblySource(
                $"custom-cell/{source.BehaviorId.Value}",
                source.Assembly,
                null,
                OptionalForOfflineReplay: false)));
            if (_deviceGraph is not null)
            {
                sources.AddRange(_deviceGraph.NodeBackendAssemblies.Select(source => new WorkbenchBehaviorAssemblySource(
                    $"node-backend/{source.DeviceId.Value}",
                    source.Assembly,
                    source.Sha256,
                    OptionalForOfflineReplay: true)));
            }

            return sources
                .OrderBy(source => source.BehaviorId, StringComparer.Ordinal)
                .ToImmutableArray();
        }
    }

    public PanelWorkbenchSessionSnapshot CaptureSnapshot()
    {
        var chipNetwork = _chipNetwork.CaptureSnapshot();
        var deviceGraph = _deviceGraph?.CaptureSnapshot();
        if (chipNetwork.OwnerPanel.Scheduler.CurrentTick != CurrentTick ||
            deviceGraph is not null && deviceGraph.Scheduler.CurrentTick != CurrentTick)
        {
            throw new InvalidOperationException("Workbench runtimes must share the same microtick boundary.");
        }

        return new PanelWorkbenchSessionSnapshot(
            SaveId,
            CurrentTick,
            _committedDefinition,
            chipNetwork,
            deviceGraph,
            _runningMutations.Select(ToSnapshot).ToImmutableArray(),
            _stagedMutations.Select(ToSnapshot).ToImmutableArray(),
            _commandLog.ToImmutableArray(),
            _inputValues.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal).ToImmutableArray(),
            _nextOrdinal,
            CycleTicks,
            IsPaused);
    }

    public static PanelWorkbenchSession RestoreSnapshot(
        PanelWorkbenchSessionSnapshot snapshot,
        CustomCellRuleRegistry? customCellRules = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.CommittedDefinition);
        ArgumentNullException.ThrowIfNull(snapshot.ChipNetwork);
        if (snapshot.SaveId == Guid.Empty || snapshot.CurrentTick < 0 || snapshot.NextOrdinal < 0 || snapshot.CycleTicks <= 0 ||
            snapshot.RunningMutations.IsDefault || snapshot.StagedMutations.IsDefault ||
            snapshot.CommandLog.IsDefault || snapshot.InputValues.IsDefault ||
            snapshot.ChipNetwork.OwnerPanel.Scheduler.CurrentTick != snapshot.CurrentTick ||
            (snapshot.DeviceGraph is null) != (snapshot.CommittedDefinition.DeviceGraph is null) ||
            snapshot.DeviceGraph is not null && snapshot.DeviceGraph.Scheduler.CurrentTick != snapshot.CurrentTick)
        {
            throw new ArgumentException("Workbench snapshot has an invalid tick, definition, or collection.", nameof(snapshot));
        }

        if (!ReferenceEquals(snapshot.DeviceGraph?.Definition, snapshot.CommittedDefinition.DeviceGraph))
        {
            throw new ArgumentException("Workbench snapshot device graph does not match its committed definition.", nameof(snapshot));
        }

        ValidatePendingMutations(snapshot);
        var rules = customCellRules ?? new CustomCellRuleRegistry([]);
        var session = new PanelWorkbenchSession(
            snapshot.CommittedDefinition,
            snapshot.CycleTicks,
            rules,
            snapshot.CurrentTick,
            snapshot.ChipNetwork,
            snapshot.SaveId);
        if (snapshot.DeviceGraph is not null)
        {
            session._deviceGraph!.RestoreSnapshot(snapshot.DeviceGraph);
        }

        var expectedInputs = snapshot.CommittedDefinition.Panel.Cells
            .OfType<PanelCellDefinition>()
            .Where(cell => cell.Kind == CellKind.InputPort && cell.PortId.HasValue)
            .Select(cell => cell.PortId!.Value)
            .Distinct()
            .ToHashSet();
        var restoredInputs = snapshot.InputValues.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (restoredInputs.Count != snapshot.InputValues.Length ||
            !restoredInputs.Keys.ToHashSet().SetEquals(expectedInputs) ||
            snapshot.InputValues.Any(pair => !Enum.IsDefined(pair.Value)))
        {
            throw new ArgumentException("Workbench snapshot input values do not match the committed panel.", nameof(snapshot));
        }

        var runtimeInputs = snapshot.ChipNetwork.OwnerPanel.Cells
            .OfType<PanelRuntimeCellSnapshot>()
            .Where(cell => cell.Kind == CellKind.InputPort && cell.PortId.HasValue)
            .GroupBy(cell => cell.PortId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(cell => cell.ExternalInput).ToArray());
        if (runtimeInputs.Count != expectedInputs.Count ||
            runtimeInputs.Any(pair => !restoredInputs.TryGetValue(pair.Key, out var value) ||
                                      pair.Value.Any(input => input != value)))
        {
            throw new ArgumentException("Workbench snapshot input values do not match panel runtime state.", nameof(snapshot));
        }

        var previousOrdinal = 0L;
        foreach (var command in snapshot.CommandLog)
        {
            if (command.AcceptedOrdinal <= previousOrdinal || command.AcceptedOrdinal > snapshot.NextOrdinal ||
                command.ApplyAtTick < 0 || !Enum.IsDefined(command.Status) ||
                command.Status == WorkbenchCommandStatus.Committed &&
                command.ApplyAtTick > snapshot.CurrentTick)
            {
                throw new ArgumentException("Workbench snapshot command log is invalid.", nameof(snapshot));
            }

            previousOrdinal = command.AcceptedOrdinal;
        }

        if (snapshot.NextOrdinal != previousOrdinal)
        {
            throw new ArgumentException("Workbench snapshot ordinal counter does not match its command log.", nameof(snapshot));
        }

        var running = snapshot.RunningMutations.Select(mutation => RestoreMutation(mutation, isRunning: true))
            .ToArray();
        var staged = snapshot.StagedMutations.Select(mutation => RestoreMutation(mutation, isRunning: false))
            .ToArray();
        var priorDefinition = snapshot.CommittedDefinition;
        for (var index = 0; index < running.Length; index++)
        {
            running[index] = RebaseGraphBackends(priorDefinition, running[index]);
            priorDefinition = running[index].Definition;
        }

        for (var index = 0; index < staged.Length; index++)
        {
            staged[index] = RebaseGraphBackends(priorDefinition, staged[index]);
            priorDefinition = staged[index].Definition;
        }
        var pendingOrdinals = running.Select(mutation => mutation.AcceptedOrdinal!.Value).ToHashSet();
        var pendingLogOrdinals = snapshot.CommandLog
            .Where(command => command.Status == WorkbenchCommandStatus.Pending)
            .Select(command => command.AcceptedOrdinal)
            .ToHashSet();
        if (!pendingOrdinals.SetEquals(pendingLogOrdinals) || previousOrdinal > snapshot.NextOrdinal)
        {
            throw new ArgumentException("Workbench snapshot pending commands do not match its command log.", nameof(snapshot));
        }

        session._inputValues.Clear();
        foreach (var pair in snapshot.InputValues)
        {
            session._inputValues.Add(pair.Key, pair.Value);
        }

        session._runningMutations.AddRange(running);
        session._stagedMutations.AddRange(staged);
        session._commandLog.AddRange(snapshot.CommandLog);
        session._nextOrdinal = snapshot.NextOrdinal;
        session.IsPaused = snapshot.IsPaused;
        return session;
    }

    private static void ValidatePendingMutations(PanelWorkbenchSessionSnapshot snapshot)
    {
        var commands = snapshot.CommandLog.ToDictionary(command => command.AcceptedOrdinal);
        var runningOrdinals = new HashSet<long>();
        var priorDefinition = snapshot.CommittedDefinition;
        foreach (var mutation in snapshot.RunningMutations)
        {
            ValidateMutationReferences(mutation);
            ValidateMutationDefinition(priorDefinition, mutation);
            if (mutation.AcceptedOrdinal is not > 0)
            {
                throw new ArgumentException("Running workbench mutation is missing its command ordinal.", nameof(snapshot));
            }

            var ordinal = mutation.AcceptedOrdinal.Value;
            if (!runningOrdinals.Add(ordinal) || !commands.TryGetValue(ordinal, out var command) ||
                command.Status != WorkbenchCommandStatus.Pending ||
                command.ApplyAtTick != mutation.ApplyAtTick ||
                !string.Equals(command.Action, mutation.Action, StringComparison.Ordinal) ||
                command.Location != mutation.Location || mutation.ApplyAtTick != snapshot.CurrentTick)
            {
                throw new ArgumentException("Running workbench mutation does not match its pending command.", nameof(snapshot));
            }

            priorDefinition = mutation.Definition;
        }

        foreach (var mutation in snapshot.StagedMutations)
        {
            ValidateMutationReferences(mutation);
            ValidateMutationDefinition(priorDefinition, mutation);
            if (mutation.AcceptedOrdinal.HasValue || mutation.ApplyAtTick.HasValue)
            {
                throw new ArgumentException("Staged workbench mutation has a command identity.", nameof(snapshot));
            }

            priorDefinition = mutation.Definition;
        }

        var pendingOrdinals = snapshot.CommandLog
            .Where(command => command.Status == WorkbenchCommandStatus.Pending)
            .Select(command => command.AcceptedOrdinal)
            .ToHashSet();
        if (!pendingOrdinals.SetEquals(runningOrdinals))
        {
            throw new ArgumentException("Workbench pending commands do not match pending mutations.", nameof(snapshot));
        }
    }

    private static void ValidateMutationDefinition(
        WorkbenchDefinition prior,
        PendingWorkbenchMutationSnapshot mutation)
    {
        var location = mutation.Location;
        switch (mutation.Action)
        {
            case "drive input":
            case "drive chip input":
            case "drive device input":
                if (!DefinitionsEqual(prior, mutation.Definition))
                {
                    throw new ArgumentException("Workbench input mutation changes its definition.", nameof(mutation));
                }

                return;
            case "paint" when location is { } paintLocation:
                if (mutation.Definition.Panel.GetCell(paintLocation) is null ||
                    !PanelsEqualExcept(prior.Panel, mutation.Definition.Panel, paintLocation) ||
                    !NonPanelDefinitionsEqual(prior, mutation.Definition))
                {
                    throw new ArgumentException("Workbench paint mutation does not match its location.", nameof(mutation));
                }

                return;
            case "erase" when location is { } eraseLocation:
                if (mutation.Definition.Panel.GetCell(eraseLocation) is not null ||
                    !PanelsEqualExcept(prior.Panel, mutation.Definition.Panel, eraseLocation) ||
                    !NonPanelDefinitionsEqual(prior, mutation.Definition))
                {
                    throw new ArgumentException("Workbench erase mutation does not match its location.", nameof(mutation));
                }

                return;
            case "rotate" when location is { } rotateLocation:
                var before = prior.Panel.GetCell(rotateLocation);
                var after = mutation.Definition.Panel.GetCell(rotateLocation);
                if (before is null || after is null ||
                    !PanelsEqualExcept(prior.Panel, mutation.Definition.Panel, rotateLocation) ||
                    before.Id != after.Id || before.Kind != after.Kind || before.PortId != after.PortId ||
                    before.BehaviorId != after.BehaviorId || !before.Parameters.SequenceEqual(after.Parameters) ||
                    after.Orientation != (CardinalDirection)(((int)before.Orientation + 1) % 4) ||
                    !NonPanelDefinitionsEqual(prior, mutation.Definition))
                {
                    throw new ArgumentException("Workbench rotate mutation does not match its location.", nameof(mutation));
                }

                return;
            case "place chip" when location is { } chipLocation:
                if (!PanelsEqual(prior.Panel, mutation.Definition.Panel) ||
                    !DeviceGraphsEqual(prior.DeviceGraph, mutation.Definition.DeviceGraph) ||
                    !prior.DevicePlacements.SequenceEqual(mutation.Definition.DevicePlacements) ||
                    !ChipPlacementAppended(prior, mutation.Definition, chipLocation))
                {
                    throw new ArgumentException("Workbench chip placement mutation is inconsistent.", nameof(mutation));
                }

                return;
            case "place device" when location is { } deviceLocation:
                if (!PanelsEqual(prior.Panel, mutation.Definition.Panel) ||
                    !ChipDefinitionsEqual(prior, mutation.Definition) ||
                    !prior.ChipPlacements.SequenceEqual(mutation.Definition.ChipPlacements) ||
                    !DevicePlacementAppended(prior, mutation.Definition, deviceLocation))
                {
                    throw new ArgumentException("Workbench device placement mutation is inconsistent.", nameof(mutation));
                }

                return;
            case "package chip":
                if (!PanelsEqual(prior.Panel, mutation.Definition.Panel) ||
                    !ChipNetworkEqual(prior, mutation.Definition) ||
                    !prior.ChipPlacements.SequenceEqual(mutation.Definition.ChipPlacements) ||
                    !DeviceGraphsEqual(prior.DeviceGraph, mutation.Definition.DeviceGraph) ||
                    !prior.DevicePlacements.SequenceEqual(mutation.Definition.DevicePlacements) ||
                    mutation.Definition.ChipCatalog.Definitions.Length != prior.ChipCatalog.Definitions.Length + 1 ||
                    !prior.ChipCatalog.Definitions.All(old => mutation.Definition.ChipCatalog.Definitions.Any(next =>
                        next.Id == old.Id && next.ContentHash == old.ContentHash)))
                {
                    throw new ArgumentException("Workbench chip packaging mutation is inconsistent.", nameof(mutation));
                }

                return;
            case "connect cable bundle":
                if (!PanelsEqual(prior.Panel, mutation.Definition.Panel) ||
                    !ChipDefinitionsEqual(prior, mutation.Definition) ||
                    !prior.ChipPlacements.SequenceEqual(mutation.Definition.ChipPlacements) ||
                    !prior.DevicePlacements.SequenceEqual(mutation.Definition.DevicePlacements) ||
                    !DeviceGraphAppended(prior.DeviceGraph, mutation.Definition.DeviceGraph))
                {
                    throw new ArgumentException("Workbench cable mutation is inconsistent.", nameof(mutation));
                }

                return;
            default:
                throw new ArgumentException("Workbench mutation definition is inconsistent with its action.", nameof(mutation));
        }
    }

    private static bool DefinitionsEqual(WorkbenchDefinition left, WorkbenchDefinition right) =>
        PanelsEqual(left.Panel, right.Panel) && NonPanelDefinitionsEqual(left, right);

    private static bool NonPanelDefinitionsEqual(WorkbenchDefinition left, WorkbenchDefinition right) =>
        ChipDefinitionsEqual(left, right) &&
        left.ChipPlacements.SequenceEqual(right.ChipPlacements) &&
        DeviceGraphsEqual(left.DeviceGraph, right.DeviceGraph) &&
        left.DevicePlacements.SequenceEqual(right.DevicePlacements);

    private static bool ChipDefinitionsEqual(WorkbenchDefinition left, WorkbenchDefinition right) =>
        ChipNetworkEqual(left, right) &&
        left.ChipCatalog.Definitions.Length == right.ChipCatalog.Definitions.Length &&
        left.ChipCatalog.Definitions.All(old => right.ChipCatalog.Definitions.Any(next =>
            next.Id == old.Id && next.ContentHash == old.ContentHash));

    private static bool ChipNetworkEqual(WorkbenchDefinition left, WorkbenchDefinition right) =>
        left.ChipNetwork.OwnerPanelId == right.ChipNetwork.OwnerPanelId &&
        left.ChipNetwork.Instances.SequenceEqual(right.ChipNetwork.Instances) &&
        left.ChipNetwork.Connections.SequenceEqual(right.ChipNetwork.Connections);

    private static bool DeviceGraphsEqual(DeviceGraphDefinition? left, DeviceGraphDefinition? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Id == right.Id && left.Devices.Length == right.Devices.Length &&
               left.Devices.All(device => right.Devices.Any(other =>
                   other.Id == device.Id && BackendsEqual(other.Backend, device.Backend))) &&
               left.Lanes.SequenceEqual(right.Lanes) && left.Bundles.SequenceEqual(right.Bundles);
    }

    private static bool PanelsEqualExcept(PanelDefinition left, PanelDefinition right, GridCoordinate excluded)
    {
        if (left.Id != right.Id || left.Width != right.Width || left.Height != right.Height ||
            left.Cells.Length != right.Cells.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Cells.Length; index++)
        {
            var location = new GridCoordinate(index % left.Width, index / left.Width);
            if (location == excluded)
            {
                continue;
            }

            var before = left.Cells[index];
            var after = right.Cells[index];
            if (before is null || after is null)
            {
                if (before is not null || after is not null)
                {
                    return false;
                }

                continue;
            }

            if (before.Id != after.Id || before.Location != after.Location || before.Kind != after.Kind ||
                before.Orientation != after.Orientation || before.PortId != after.PortId ||
                before.BehaviorId != after.BehaviorId || !before.Parameters.SequenceEqual(after.Parameters))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ChipPlacementAppended(
        WorkbenchDefinition prior,
        WorkbenchDefinition next,
        GridCoordinate location) =>
        next.ChipNetwork.Instances.Length == prior.ChipNetwork.Instances.Length + 1 &&
        prior.ChipNetwork.Instances.All(old => next.ChipNetwork.Instances.Contains(old)) &&
        next.ChipPlacements.Length == prior.ChipPlacements.Length + 1 &&
        prior.ChipPlacements.All(old => next.ChipPlacements.Contains(old)) &&
        next.ChipPlacements.Count(item => item.Location == location) == 1;

    private static bool DevicePlacementAppended(
        WorkbenchDefinition prior,
        WorkbenchDefinition next,
        GridCoordinate location)
    {
        var graphAppended = prior.DeviceGraph is null
            ? next.DeviceGraph is { } firstGraph && firstGraph.Lanes.IsEmpty &&
              firstGraph.Bundles.IsEmpty && firstGraph.Devices.Length == 1
            : DeviceGraphAppended(prior.DeviceGraph, next.DeviceGraph);
        return graphAppended &&
        next.DevicePlacements.Length == prior.DevicePlacements.Length + 1 &&
        prior.DevicePlacements.All(old => next.DevicePlacements.Contains(old)) &&
        next.DevicePlacements.Count(item => item.Location == location) == 1;
    }

    private static bool DeviceGraphAppended(DeviceGraphDefinition? prior, DeviceGraphDefinition? next)
    {
        if (prior is null || next is null || prior.Id != next.Id ||
            !prior.Lanes.SequenceEqual(next.Lanes) && prior.Lanes.Any(old => !next.Lanes.Contains(old)) ||
            !prior.Bundles.SequenceEqual(next.Bundles) && prior.Bundles.Any(old => !next.Bundles.Contains(old)))
        {
            return false;
        }

        return next.Devices.Length >= prior.Devices.Length &&
               prior.Devices.All(old => next.Devices.Any(candidate =>
                   candidate.Id == old.Id && BackendsEqual(old.Backend, candidate.Backend)));
    }

    private static void ValidateMutationReferences(PendingWorkbenchMutationSnapshot mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (mutation.Definition is null || string.IsNullOrWhiteSpace(mutation.Action))
        {
            throw new ArgumentException("Workbench mutation is missing its action or definition.", nameof(mutation));
        }

        var payloadCount = (mutation.InputPort.HasValue ? 1 : 0) +
                           (mutation.DeviceInput is null ? 0 : 1) +
                           (mutation.ChipInput is null ? 0 : 1);
        if (payloadCount > 1)
        {
            throw new ArgumentException("Workbench mutation contains multiple input payloads.", nameof(mutation));
        }

        if (mutation.InputPort is { } inputPort)
        {
            if (!string.Equals(mutation.Action, "drive input", StringComparison.Ordinal) ||
                !mutation.InputValue.HasValue ||
                !mutation.Definition.Panel.Cells.OfType<PanelCellDefinition>().Any(cell =>
                    cell.Kind == CellKind.InputPort && cell.PortId == inputPort))
            {
                throw new ArgumentException("Workbench panel input mutation does not match its definition.", nameof(mutation));
            }
        }

        if (mutation.DeviceInput is { } deviceInput)
        {
            var graph = mutation.Definition.DeviceGraph;
            var device = graph?.Devices.FirstOrDefault(item => item.Id == deviceInput.DeviceId);
            var port = device?.Ports.FirstOrDefault(item =>
                string.Equals(item.Name, deviceInput.PortName, StringComparison.Ordinal));
            if (!string.Equals(mutation.Action, "drive device input", StringComparison.Ordinal) ||
                graph is null || port is null || port.Direction != DevicePortDirection.Input ||
                deviceInput.Signal.Bits.IsDefaultOrEmpty || deviceInput.Signal.Bits.Length != port.Width ||
                deviceInput.Signal.Bits.Any(value => !Enum.IsDefined(value)) ||
                port.ValueContract == DeviceValueContract.BinaryLogic &&
                deviceInput.Signal.Bits.Contains(LogicValue.Unknown) ||
                graph.Lanes.Any(lane => lane.Target.DeviceId == deviceInput.DeviceId &&
                                       string.Equals(lane.Target.PortName, deviceInput.PortName, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Workbench device input mutation does not match its definition.", nameof(mutation));
            }
        }

        if (mutation.ChipInput is { } chipInput)
        {
            var instance = mutation.Definition.ChipNetwork.Instances.FirstOrDefault(item =>
                item.InstanceId == chipInput.InstanceId);
            var chip = instance is null
                ? null
                : mutation.Definition.ChipCatalog.Resolve(instance.DefinitionId, instance.ContentHash);
            var port = chip?.Ports.FirstOrDefault(item =>
                string.Equals(item.Name, chipInput.PortName, StringComparison.Ordinal));
            if (!string.Equals(mutation.Action, "drive chip input", StringComparison.Ordinal) ||
                port is null || port.Direction != ChipPortDirection.Input ||
                mutation.Definition.ChipNetwork.Connections.Any(connection =>
                    connection.Target.InstanceId == chipInput.InstanceId &&
                    string.Equals(connection.Target.PortName, chipInput.PortName, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Workbench chip input mutation does not match its definition.", nameof(mutation));
            }
        }

        if (payloadCount == 0 && (mutation.InputPort.HasValue ||
                                  mutation.DeviceInput is not null || mutation.ChipInput is not null))
        {
            throw new ArgumentException("Workbench mutation input payload is inconsistent.", nameof(mutation));
        }

        var requiresLocation = mutation.Action is "paint" or "erase" or "rotate" or "place chip" or
                               "place device" or "drive chip input";
        var forbidsLocation = mutation.Action is "drive input" or "drive device input" or
                              "package chip" or "connect cable bundle";
        var knownAction = requiresLocation || forbidsLocation || mutation.Action == "drive chip input";
        if (!knownAction || payloadCount == 0 && mutation.Action.StartsWith("drive ", StringComparison.Ordinal) ||
            requiresLocation != mutation.Location.HasValue ||
            forbidsLocation && mutation.Location.HasValue)
        {
            throw new ArgumentException("Workbench mutation action metadata is invalid.", nameof(mutation));
        }

        if (mutation.Action == "drive chip input")
        {
            var placement = mutation.Definition.ChipPlacements.FirstOrDefault(item =>
                item.InstanceId == mutation.ChipInput?.InstanceId);
            if (mutation.ChipInput is null || placement is null || mutation.Location != placement.Location)
            {
                throw new ArgumentException("Workbench chip input location does not match its placement.", nameof(mutation));
            }
        }

        if (requiresLocation && mutation.Location is { } location &&
            ((uint)location.X >= (uint)mutation.Definition.Panel.Width ||
             (uint)location.Y >= (uint)mutation.Definition.Panel.Height))
        {
            throw new ArgumentException("Workbench mutation location is outside its panel.", nameof(mutation));
        }
    }

    public PanelCellDefinition? GetCell(GridCoordinate location) => Definition.Panel.GetCell(location);

    public LogicValue GetOutput(PortId portId) => _chipNetwork.GetOutput(portId);

    public LogicValue GetChipOutput(ComponentId instanceId, string portName) =>
        _chipNetwork.GetInstance(instanceId).GetOutput(portName);

    public DeviceSignal GetDeviceOutput(ComponentId deviceId, string portName) =>
        (_deviceGraph ?? throw new InvalidOperationException("No device graph is active."))
        .GetOutput(deviceId, portName);

    public void AttachNodeBackend(ComponentId deviceId, DeviceNodeBackendBinding binding)
    {
        (_deviceGraph ?? throw new InvalidOperationException("No device graph is active."))
            .AttachNodeBackend(deviceId, binding);
        Changed?.Invoke();
    }

    public void ReplayNodeBackendCommands(IEnumerable<AcceptedSchedulerCommand> commands)
    {
        (_deviceGraph ?? throw new InvalidOperationException("No device graph is active."))
            .ReplayNodeBackendCommands(commands);
        Changed?.Invoke();
    }

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

    private static PendingWorkbenchMutationSnapshot ToSnapshot(PendingMutation mutation) => new(
        mutation.Action,
        mutation.Location,
        mutation.Definition,
        mutation.InputPort,
        mutation.InputValue,
        mutation.AcceptedOrdinal,
        mutation.ApplyAtTick,
        mutation.DeviceInput is { } deviceInput
            ? new WorkbenchDeviceInputDrive(deviceInput.DeviceId, deviceInput.PortName, deviceInput.Signal)
            : null,
        mutation.ChipInput is { } chipInput
            ? new WorkbenchChipInputDrive(chipInput.InstanceId, chipInput.PortName, chipInput.Value)
            : null);

    private static PendingMutation RestoreMutation(
        PendingWorkbenchMutationSnapshot snapshot,
        bool isRunning)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.Action) || snapshot.Definition is null ||
            snapshot.InputPort.HasValue != snapshot.InputValue.HasValue ||
            !Enum.IsDefined(snapshot.InputValue ?? LogicValue.Low) ||
            (snapshot.DeviceInput is not null ? 1 : 0) + (snapshot.ChipInput is not null ? 1 : 0) +
            (snapshot.InputPort.HasValue ? 1 : 0) > 1)
        {
            throw new ArgumentException("Workbench pending mutation is invalid.", nameof(snapshot));
        }

        if (isRunning)
        {
            if (snapshot.AcceptedOrdinal is not > 0 || snapshot.ApplyAtTick is null or < 0)
            {
                throw new ArgumentException("Running workbench mutations require an accepted ordinal and tick.", nameof(snapshot));
            }
        }
        else if (snapshot.AcceptedOrdinal.HasValue || snapshot.ApplyAtTick.HasValue)
        {
            throw new ArgumentException("Staged workbench mutations cannot have an accepted ordinal or tick.", nameof(snapshot));
        }

        if (snapshot.DeviceInput is { } deviceInput &&
            (string.IsNullOrWhiteSpace(deviceInput.PortName) || deviceInput.Signal.Bits.IsDefaultOrEmpty ||
             deviceInput.Signal.Bits.Any(value => !Enum.IsDefined(value))))
        {
            throw new ArgumentException("Workbench device input mutation is invalid.", nameof(snapshot));
        }

        if (snapshot.ChipInput is { } chipInput &&
            (string.IsNullOrWhiteSpace(chipInput.PortName) || !Enum.IsDefined(chipInput.Value)))
        {
            throw new ArgumentException("Workbench chip input mutation is invalid.", nameof(snapshot));
        }

        return new PendingMutation(
            snapshot.Action,
            snapshot.Location,
            snapshot.Definition,
            snapshot.InputPort,
            snapshot.InputValue,
            snapshot.AcceptedOrdinal,
            snapshot.ApplyAtTick,
            snapshot.DeviceInput is { } device
                ? new DeviceInputDrive(device.DeviceId, device.PortName, device.Signal)
                : null,
            snapshot.ChipInput is { } chip
                ? new ChipInputDrive(chip.InstanceId, chip.PortName, chip.Value)
            : null);
    }

    private static PendingMutation RebaseGraphBackends(
        WorkbenchDefinition priorDefinition,
        PendingMutation mutation)
    {
        var priorGraph = priorDefinition.DeviceGraph;
        var nextGraph = mutation.Definition.DeviceGraph;
        if (priorGraph is null)
        {
            return mutation;
        }

        if (nextGraph is null)
        {
            throw new ArgumentException("A pending workbench mutation cannot remove its device graph.");
        }

        var nextDevices = nextGraph.Devices.ToDictionary(device => device.Id);
        var existingDevices = new Dictionary<ComponentId, DeviceDefinition>();
        foreach (var priorDevice in priorGraph.Devices)
        {
            if (!nextDevices.TryGetValue(priorDevice.Id, out var nextDevice) ||
                !BackendsEqual(priorDevice.Backend, nextDevice.Backend))
            {
                throw new ArgumentException(
                    $"Pending workbench mutation changes existing device '{priorDevice.Id}'.");
            }

            existingDevices.Add(priorDevice.Id, priorDevice);
        }

        var rebasedGraph = DeviceGraphDefinition.Create(
            nextGraph.Id,
            nextGraph.Devices.Select(device => existingDevices.GetValueOrDefault(device.Id, device)),
            nextGraph.Lanes,
            nextGraph.Bundles);
        return mutation with { Definition = mutation.Definition.WithDeviceGraph(rebasedGraph) };
    }

    private static bool BackendsEqual(DeviceBackendDefinition left, DeviceBackendDefinition right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (!left.Ports.SequenceEqual(right.Ports))
        {
            return false;
        }

        return (left, right) switch
        {
            (PanelDeviceBackendDefinition leftPanel, PanelDeviceBackendDefinition rightPanel) =>
                PanelsEqual(leftPanel.Panel, rightPanel.Panel),
            (TimedDeviceBackendDefinition leftTimed, TimedDeviceBackendDefinition rightTimed) =>
                leftTimed.Changes.SequenceEqual(rightTimed.Changes),
            (NodeDeviceBackendDefinition, NodeDeviceBackendDefinition) => true,
            _ => false
        };
    }

    private static bool PanelsEqual(PanelDefinition left, PanelDefinition right)
    {
        if (left.Id != right.Id || left.Width != right.Width || left.Height != right.Height ||
            left.Cells.Length != right.Cells.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Cells.Length; index++)
        {
            var leftCell = left.Cells[index];
            var rightCell = right.Cells[index];
            if (leftCell is null || rightCell is null)
            {
                if (leftCell is not null || rightCell is not null)
                {
                    return false;
                }

                continue;
            }

            if (leftCell.Id != rightCell.Id || leftCell.Location != rightCell.Location ||
                leftCell.Kind != rightCell.Kind || leftCell.Orientation != rightCell.Orientation ||
                leftCell.PortId != rightCell.PortId || leftCell.BehaviorId != rightCell.BehaviorId ||
                !leftCell.Parameters.SequenceEqual(rightCell.Parameters))
            {
                return false;
            }
        }

        return true;
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
