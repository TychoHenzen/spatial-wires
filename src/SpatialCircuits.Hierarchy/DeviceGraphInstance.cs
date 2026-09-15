using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.Hierarchy;

public enum CableTransitionStatus
{
    Pending,
    Delivered,
    Invalidated
}

public sealed record CableLaneHistoryEntry(
    long Epoch,
    long CausalOrdinal,
    long ScheduledTick,
    DeviceSignal Signal,
    bool IsRelease,
    CableTransitionStatus Status,
    long? DeliveredTick);

public sealed record DeviceCableDelivery(
    ComponentId LaneId,
    long Epoch,
    long ScheduledTick,
    long DeliveredTick,
    DeviceSignal Signal,
    bool IsRelease);

public sealed record DeviceGraphTickResult(long Tick, ImmutableArray<DeviceCableDelivery> CableDeliveries)
{
    public ImmutableArray<DeviceNodeBackendFailure> NodeBackendFailures { get; init; } = [];
}

public sealed record DeviceRuntimeSnapshot(
    ComponentId DeviceId,
    PanelRuntimeSnapshot? Panel,
    ImmutableArray<KeyValuePair<string, DeviceSignal>> Inputs,
    ImmutableArray<KeyValuePair<string, DeviceSignal>> Outputs,
    ImmutableArray<DeviceTimerSnapshot> Timers)
{
    public ImmutableArray<DeviceNodeInputTransition> PendingNodeInputs { get; init; } = [];

    public long? NodeDetachApplyAtTick { get; init; }

    public bool NodeDetachRequested { get; init; }

    public string? NodeBehaviorFingerprint { get; init; }

    public ImmutableArray<DeviceNodeInputTransition> NodeInputHistory { get; init; } = [];
}

public sealed record DeviceTimerSnapshot(long DueTick, string PortName, DeviceSignal Value);

public sealed record CableLaneRuntimeSnapshot(
    ComponentId LaneId,
    bool Connected,
    long Epoch,
    DeviceSignal CurrentSignal,
    ImmutableArray<CableLaneHistoryEntry> History);

public sealed record DeviceGraphRuntimeSnapshot(
    DeviceGraphDefinition Definition,
    SchedulerSnapshot Scheduler,
    ImmutableArray<DeviceRuntimeSnapshot> Devices,
    ImmutableArray<CableLaneRuntimeSnapshot> Lanes)
{
    public ImmutableArray<AcceptedSchedulerCommand> ReplayCommands { get; init; } = [];

    public int NextReplayCommandIndex { get; init; }

    public string StateIntegrityHash { get; init; } = string.Empty;
}

/// <summary>Owns device instances and advances their cable transport on integer microticks.</summary>
public sealed class DeviceGraphInstance
{
    private const string CableTransitionEvent = "device-cable-transition";
    private const string CableReleaseEvent = "device-cable-transition-release";
    private const string NodeOutputEvent = "device-node-output";
    private const string NodeTimerEvent = "device-node-timer";
    private const string NodeBackendTargetPrefix = NodeDeviceBackendDefinition.TargetStableIdPrefix;

    private DeviceGraphDefinition _definition;
    private DeterministicScheduler _scheduler;
    private Dictionary<string, DeviceRuntime> _devices = new(StringComparer.Ordinal);
    private Dictionary<string, LaneRuntime> _lanes = new(StringComparer.Ordinal);
    private ImmutableArray<AcceptedSchedulerCommand> _replayCommands;
    private int _nextReplayCommandIndex;
    private bool _isStepping;

    public DeviceGraphInstance(DeviceGraphDefinition definition, long initialTick = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        _scheduler = new DeterministicScheduler(initialTick);

        foreach (var device in definition.Devices)
        {
            _scheduler.RegisterTarget(device.Id.Value);
            _devices.Add(device.Id.Value, CreateRuntime(device, CurrentTick));
        }

        foreach (var device in definition.Devices.Where(device =>
                     device.Backend is NodeDeviceBackendDefinition))
        {
            _scheduler.RegisterTarget(NodeBackendTargetId(device.Id));
        }

        foreach (var lane in definition.Lanes)
        {
            var target = _scheduler.RegisterTarget(lane.Id.Value);
            _lanes.Add(lane.Id.Value, new LaneRuntime(
                lane,
                target.Incarnation,
                InitialSignal(lane.Target, definition)));
        }

        foreach (var lane in _lanes.Values.OrderBy(item => item.Definition.Id.Value, StringComparer.Ordinal))
        {
            ScheduleLaneTransition(lane, ReadOutput(lane.Definition.Source), CurrentTick, isRelease: false);
        }
    }

    public DeviceGraphDefinition Definition => _definition;

    public long CurrentTick => _scheduler.CurrentTick;

    public void ExtendDefinition(DeviceGraphDefinition definition)
    {
        EnsureNotStepping();
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Id != _definition.Id)
        {
            throw new ArgumentException("Device graph updates must keep the graph identifier.", nameof(definition));
        }

        var nextDevices = definition.Devices.ToDictionary(device => device.Id);
        var nextLanes = definition.Lanes.ToDictionary(lane => lane.Id);
        var nextBundles = definition.Bundles.ToDictionary(bundle => bundle.Id);
        if (_definition.Devices.Any(device =>
                !nextDevices.TryGetValue(device.Id, out var next) || !ReferenceEquals(device.Backend, next.Backend)) ||
            _definition.Lanes.Any(lane => !nextLanes.TryGetValue(lane.Id, out var next) || next != lane) ||
            _definition.Bundles.Any(bundle =>
                !nextBundles.TryGetValue(bundle.Id, out var next) ||
                !bundle.LaneIds.SequenceEqual(next.LaneIds)))
        {
            throw new ArgumentException(
                "Device graph updates can only append devices, lanes, or bundles.",
                nameof(definition));
        }

        var existingDeviceIds = _devices.Keys.ToHashSet(StringComparer.Ordinal);
        var addedDevices = definition.Devices
            .Where(device => !existingDeviceIds.Contains(device.Id.Value))
            .ToArray();
        var existingLaneIds = _lanes.Keys.ToHashSet(StringComparer.Ordinal);
        var addedLanes = definition.Lanes
            .Where(lane => !existingLaneIds.Contains(lane.Id.Value))
            .ToArray();

        foreach (var lane in addedLanes)
        {
            _ = checked(CurrentTick + lane.Latency);
        }

        var runtimes = addedDevices.Select(device => (device, runtime: CreateRuntime(device, CurrentTick)))
            .ToArray();
        foreach (var (device, runtime) in runtimes)
        {
            _scheduler.RegisterTarget(device.Id.Value);
            _devices.Add(device.Id.Value, runtime);
            if (device.Backend is NodeDeviceBackendDefinition)
            {
                _scheduler.RegisterTarget(NodeBackendTargetId(device.Id));
            }
        }

        _definition = definition;
        var lanes = addedLanes.Select(lane =>
        {
            var target = _scheduler.RegisterTarget(lane.Id.Value);
            var runtime = new LaneRuntime(lane, target.Incarnation, InitialSignal(lane.Target, definition));
            _lanes.Add(lane.Id.Value, runtime);
            return runtime;
        }).OrderBy(lane => lane.Definition.Id.Value, StringComparer.Ordinal);
        foreach (var lane in lanes)
        {
            ScheduleLaneTransition(lane, ReadOutput(lane.Definition.Source), CurrentTick, isRelease: false);
        }
    }

    public ImmutableArray<AcceptedSchedulerCommand> AcceptedCommands =>
        _scheduler.AcceptedCommands.ToImmutableArray();

    public ImmutableArray<DeviceNodeBackendAssemblySource> NodeBackendAssemblies => _devices.Values
        .Where(runtime => runtime.Definition.Backend is NodeDeviceBackendDefinition)
        .OrderBy(runtime => runtime.Definition.Id.Value, StringComparer.Ordinal)
        .Select(runtime => new DeviceNodeBackendAssemblySource(
            runtime.Definition.Id,
            runtime.NodeBinding?.BehaviorAssembly,
            runtime.NodeBinding?.BehaviorAssemblyFingerprint ?? runtime.NodeBehaviorFingerprint))
        .ToImmutableArray();

    public void AttachNodeBackend(ComponentId deviceId, DeviceNodeBackendBinding binding)
    {
        EnsureNotStepping();
        ArgumentNullException.ThrowIfNull(binding);
        var runtime = GetDevice(deviceId);
        if (runtime.Definition.Backend is not NodeDeviceBackendDefinition)
        {
            throw new ArgumentException("Device does not use a Node backend definition.", nameof(deviceId));
        }

        if (!binding.IsActive || runtime.NodeBinding is not null ||
            runtime.NodeDetachApplyAtTick is not null || runtime.NodeDetachRequested)
        {
            throw new InvalidOperationException("Node backend is already attached or detaching.");
        }

        if (runtime.NodeBehaviorFingerprint is { } expectedFingerprint &&
            !string.Equals(expectedFingerprint, binding.BehaviorAssemblyFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Node backend assembly fingerprint does not match the saved behavior manifest.");
        }

        runtime.NodeBinding = binding;
        runtime.NodeBehaviorFingerprint = binding.BehaviorAssemblyFingerprint;
        var pendingPorts = runtime.PendingNodeInputs.Select(input => input.PortName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var input in runtime.Inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!pendingPorts.Contains(input.Key))
            {
                var transition = new DeviceNodeInputTransition(
                    CurrentTick,
                    input.Key,
                    input.Value);
                runtime.PendingNodeInputs.Add(transition);
                runtime.NodeInputHistory.Add(transition);
            }
        }
    }

    public void ReplayNodeBackendCommands(IEnumerable<AcceptedSchedulerCommand> commands)
    {
        EnsureNotStepping();
        ArgumentNullException.ThrowIfNull(commands);
        if (!_replayCommands.IsDefault || CurrentTick != 0 || _scheduler.AcceptedCommands.Count != 0 ||
            _devices.Values.Any(runtime => runtime.NodeBinding is { IsActive: true }))
        {
            throw new InvalidOperationException(
                "Node backend replay must be configured on a fresh graph without live Node bindings.");
        }

        var supplied = commands.ToArray();
        if (supplied.Any(item => item is null))
        {
            throw new ArgumentException("Recorded Node backend commands cannot contain null.", nameof(commands));
        }

        var recorded = supplied.OrderBy(item => item.AcceptedOrdinal).ToImmutableArray();
        if (recorded.Where((item, index) => item.AcceptedOrdinal != index + 1L).Any() ||
            recorded.Any(item => item.Command is null || item.AcceptedOrdinal <= 0 ||
                                 !IsReplayNodeBackendCommand(item.Command)))
        {
            throw new ArgumentException("Recorded Node backend commands are invalid.", nameof(commands));
        }

        _replayCommands = recorded;
        _nextReplayCommandIndex = 0;
    }

    public void SetInput(ComponentId deviceId, string portName, DeviceSignal signal)
    {
        EnsureNotStepping();
        var device = GetDevice(deviceId);
        var port = FindPort(device.Definition, portName, DevicePortDirection.Input);
        port.ValidateSignal(signal, nameof(signal));
        if (_definition.Lanes.Any(lane => lane.Target.DeviceId == deviceId &&
                                          string.Equals(lane.Target.PortName, portName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Input '{deviceId}.{portName}' is driven by a cable lane.");
        }

        if (device.Panel is null)
        {
            var changed = !device.Inputs[portName].Equals(signal);
            device.Inputs[portName] = signal;
            if (changed && device.Definition.Backend is NodeDeviceBackendDefinition)
            {
                var transition = new DeviceNodeInputTransition(CurrentTick, portName, signal);
                device.PendingNodeInputs.Add(transition);
                device.NodeInputHistory.Add(transition);
            }
        }
        else
        {
            device.Panel.SetInput(new PortId(portName), signal.Bits[0]);
        }
    }

    public DeviceSignal GetOutput(ComponentId deviceId, string portName)
    {
        EnsureNotStepping();
        var device = GetDevice(deviceId);
        FindPort(device.Definition, portName, DevicePortDirection.Output);
        return ReadOutput(deviceId, portName);
    }

    public bool IsConnected(ComponentId laneId) => GetLane(laneId).Connected;

    public ImmutableArray<CableLaneHistoryEntry> GetLaneHistory(ComponentId laneId) =>
        GetLane(laneId).History.ToImmutableArray();

    public void Disconnect(ComponentId laneId)
    {
        EnsureNotStepping();
        var lane = GetLane(laneId);
        if (!lane.Connected)
        {
            return;
        }

        _ = checked(CurrentTick + lane.Definition.Latency);
        var port = FindPort(_definition.GetDevice(lane.Definition.Target.DeviceId),
            lane.Definition.Target.PortName, DevicePortDirection.Input);
        var release = DeviceSignal.Create(Enumerable.Repeat(LogicValue.HighImpedance, port.Width));
        lane.Connected = false;
        ScheduleLaneTransition(lane, release, CurrentTick, isRelease: true);
    }

    public void Reconnect(ComponentId laneId)
    {
        EnsureNotStepping();
        var lane = GetLane(laneId);
        if (lane.Connected)
        {
            return;
        }

        var tick = CurrentTick;
        _ = checked(tick + lane.Definition.Latency);
        var currentValue = ReadOutput(lane.Definition.Source);
        lane.Epoch = _scheduler.ReplaceTarget(laneId.Value).Incarnation;
        for (var index = 0; index < lane.History.Count; index++)
        {
            var history = lane.History[index];
            if (history.Epoch != lane.Epoch && history.Status == CableTransitionStatus.Pending)
            {
                lane.History[index] = history with { Status = CableTransitionStatus.Invalidated };
            }
        }

        lane.Connected = true;
        ScheduleLaneTransition(lane, currentValue, tick, isRelease: false);
    }

    public void ReplaceBackend(ComponentId deviceId, DeviceBackendDefinition backend)
    {
        EnsureNotStepping();
        ArgumentNullException.ThrowIfNull(backend);
        var current = GetDevice(deviceId);
        if ((current.Definition.Backend is NodeDeviceBackendDefinition) !=
            (backend is NodeDeviceBackendDefinition))
        {
            throw new InvalidOperationException(
                "Node backend definitions can only be replaced with another Node backend definition.");
        }

        var replacementDefinition = _definition.WithBackend(deviceId, backend);
        var replacement = CreateRuntime(replacementDefinition.GetDevice(deviceId), CurrentTick);
        if (backend is NodeDeviceBackendDefinition)
        {
            replacement.Inputs.Clear();
            foreach (var input in current.Inputs)
            {
                replacement.Inputs.Add(input.Key, input.Value);
            }

            replacement.PendingNodeInputs.AddRange(current.PendingNodeInputs);
            replacement.NodeInputHistory.AddRange(current.NodeInputHistory);
            replacement.NodeBinding = current.NodeBinding;
            replacement.NodeBehaviorFingerprint = current.NodeBehaviorFingerprint;
            replacement.NodeDetachApplyAtTick = current.NodeDetachApplyAtTick;
            replacement.NodeDetachRequested = current.NodeDetachRequested;
        }

        var changedOutputs = replacement.Outputs
            .Where(pair => !current.Outputs.TryGetValue(pair.Key, out var oldValue) || !oldValue.Equals(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        foreach (var output in changedOutputs)
        {
            ValidateOutputTicks(deviceId, output.Key, CurrentTick);
        }

        _definition = replacementDefinition;
        _devices[deviceId.Value] = replacement;
        foreach (var output in changedOutputs)
        {
            ScheduleOutputChange(deviceId, output.Key, output.Value, CurrentTick);
        }
    }

    public DeviceGraphTickResult Step()
    {
        EnsureNotStepping();
        _isStepping = true;
        try
        {
            PrepareNodeBackendDetaches();
            var cableEventsBeforeStep = _scheduler.PendingEvents
                .Where(item => item.EventKind is CableTransitionEvent or CableReleaseEvent)
                .ToArray();
            var schedulerResult = _scheduler.Step();
            var deliveries = DeliverScheduledEvents(cableEventsBeforeStep, schedulerResult);
            StepNonNodeBackends(schedulerResult.Tick);
            var failures = RunNodeBackendCallbacks(schedulerResult.Tick);
            AcceptReplayCommandsAt(CurrentTick);
            return new DeviceGraphTickResult(schedulerResult.Tick, deliveries)
            {
                NodeBackendFailures = failures
            };
        }
        finally
        {
            _isStepping = false;
        }
    }

    private void PrepareNodeBackendDetaches()
    {
        AcceptReplayCommandsAt(_scheduler.CurrentTick);
        foreach (var runtime in _devices.Values.OrderBy(item => item.Definition.Id.Value, StringComparer.Ordinal))
        {
            if (runtime.NodeBinding is { IsActive: false })
            {
                runtime.NodeDetachRequested = true;
            }

            if (runtime.NodeDetachRequested && runtime.NodeDetachApplyAtTick is null)
            {
                QueueNodeBackendDetach(runtime, CurrentTick);
            }
        }
    }

    private ImmutableArray<DeviceCableDelivery> DeliverScheduledEvents(
        IReadOnlyCollection<ScheduledEvent> cableEventsBeforeStep,
        SchedulerTickResult schedulerResult)
    {
        var deliveries = ImmutableArray.CreateBuilder<DeviceCableDelivery>();
        foreach (var runtime in _devices.Values)
        {
            runtime.DueNodeTimers.Clear();
        }

        InvalidateStaleCableEvents(cableEventsBeforeStep, schedulerResult);
        ApplyNodeBackendDetaches(schedulerResult.Tick);
        foreach (var scheduledEvent in schedulerResult.DeliveredEvents)
        {
            if (scheduledEvent.EventKind is CableTransitionEvent or CableReleaseEvent)
            {
                DeliverCableEvent(scheduledEvent, schedulerResult.Tick, deliveries);
            }
            else if (scheduledEvent.EventKind == NodeOutputEvent)
            {
                DeliverNodeOutput(scheduledEvent, schedulerResult.Tick);
            }
            else if (scheduledEvent.EventKind == NodeTimerEvent)
            {
                DeliverNodeTimer(scheduledEvent);
            }
        }

        return deliveries.ToImmutable();
    }

    private void StepNonNodeBackends(long tick)
    {
        ApplyDueTimers(tick);
        foreach (var runtime in _devices.Values.OrderBy(item => item.Definition.Id.Value, StringComparer.Ordinal))
        {
            if (runtime.Panel is null)
            {
                continue;
            }

            runtime.Panel.Step();
            foreach (var port in runtime.Definition.Ports.Where(port => port.Direction == DevicePortDirection.Output))
            {
                var value = DeviceSignal.Scalar(runtime.Panel.GetOutput(new PortId(port.Name)));
                if (runtime.Outputs[port.Name].Equals(value))
                {
                    continue;
                }

                runtime.Outputs[port.Name] = value;
                ScheduleOutputChange(runtime.Definition.Id, port.Name, value, tick);
            }
        }
    }

    private ImmutableArray<DeviceNodeBackendFailure> RunNodeBackendCallbacks(long tick)
    {
        var stagedCommands = new List<PendingNodeCommand>();
        var failures = ImmutableArray.CreateBuilder<DeviceNodeBackendFailure>();
        foreach (var runtime in _devices.Values.OrderBy(item => item.Definition.Id.Value, StringComparer.Ordinal))
        {
            if (runtime.Definition.Backend is NodeDeviceBackendDefinition &&
                runtime.NodeBinding is { IsActive: true } binding && runtime.NodeDetachApplyAtTick is null)
            {
                InvokeNodeBackend(runtime, binding, tick, stagedCommands, failures);
            }

            runtime.PendingNodeInputs.Clear();
            runtime.DueNodeTimers.Clear();
        }

        if (failures.Count == 0)
        {
            CommitNodeCommands(stagedCommands, failures);
        }

        return failures.ToImmutable();
    }

    private void InvokeNodeBackend(
        DeviceRuntime runtime,
        DeviceNodeBackendBinding binding,
        long tick,
        List<PendingNodeCommand> stagedCommands,
        ImmutableArray<DeviceNodeBackendFailure>.Builder failures)
    {
        var backendCommands = new List<PendingNodeCommand>();
        var capability = CreateNodeOutputCapability(runtime, binding, tick, backendCommands);
        try
        {
            binding.Invoke(new DeviceNodeStepContext(
                tick,
                runtime.PendingNodeInputs.ToImmutableArray(),
                runtime.DueNodeTimers.ToImmutableArray(),
                capability));
        }
        catch (Exception exception)
        {
            failures.Add(new DeviceNodeBackendFailure(
                runtime.Definition.Id,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message));
        }
        finally
        {
            capability.Invalidate();
        }

        if (binding.IsActive)
        {
            stagedCommands.AddRange(backendCommands);
            return;
        }

        runtime.NodeDetachRequested = true;
        QueueNodeBackendDetach(runtime, CurrentTick);
    }

    public DeviceGraphRuntimeSnapshot CaptureSnapshot()
    {
        EnsureNotStepping();
        var snapshot = new DeviceGraphRuntimeSnapshot(
            _definition,
            _scheduler.CaptureSnapshot(),
            _devices.Values
                .OrderBy(runtime => runtime.Definition.Id.Value, StringComparer.Ordinal)
                .Select(runtime => new DeviceRuntimeSnapshot(
                    runtime.Definition.Id,
                    runtime.Panel?.CaptureSnapshot(),
                    runtime.Inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToImmutableArray(),
                    runtime.Outputs.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToImmutableArray(),
                    runtime.Timers.Select(timer => new DeviceTimerSnapshot(
                        timer.DueTick, timer.PortName, timer.Value)).ToImmutableArray())
                {
                    PendingNodeInputs = runtime.PendingNodeInputs.ToImmutableArray(),
                    NodeDetachApplyAtTick = runtime.NodeDetachApplyAtTick,
                    NodeDetachRequested = runtime.NodeDetachRequested ||
                                          runtime.NodeBinding is { IsActive: false },
                    NodeInputHistory = runtime.NodeInputHistory.ToImmutableArray(),
                    NodeBehaviorFingerprint = runtime.NodeBinding?.BehaviorAssemblyFingerprint ??
                                              runtime.NodeBehaviorFingerprint
                })
                .ToImmutableArray(),
            _lanes.Values
                .OrderBy(lane => lane.Definition.Id.Value, StringComparer.Ordinal)
                .Select(lane => new CableLaneRuntimeSnapshot(
                    lane.Definition.Id,
                    lane.Connected,
                    lane.Epoch,
                    lane.CurrentSignal,
                    lane.History.ToImmutableArray()))
                .ToImmutableArray())
        {
            ReplayCommands = _replayCommands.IsDefault
                ? _scheduler.AcceptedCommands.ToImmutableArray()
                : _replayCommands,
            NextReplayCommandIndex = _replayCommands.IsDefault
                ? _scheduler.AcceptedCommands.Count
                : _nextReplayCommandIndex
        };
        return snapshot with { StateIntegrityHash = ComputeStateIntegrityHash(snapshot) };
    }

    public void RestoreSnapshot(DeviceGraphRuntimeSnapshot snapshot)
    {
        EnsureNotStepping();
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Definition);
        ArgumentNullException.ThrowIfNull(snapshot.Scheduler);
        if (snapshot.Definition.Id != _definition.Id || snapshot.Devices.IsDefault || snapshot.Lanes.IsDefault ||
            snapshot.ReplayCommands.IsDefault || snapshot.NextReplayCommandIndex < 0 ||
            snapshot.NextReplayCommandIndex > snapshot.ReplayCommands.Length)
        {
            throw new ArgumentException("Device graph snapshot does not match this graph.", nameof(snapshot));
        }

        if (snapshot.ReplayCommands.IsDefaultOrEmpty && snapshot.NextReplayCommandIndex != 0)
        {
            throw new ArgumentException("Device graph replay cursor has no command list.", nameof(snapshot));
        }

        if (!snapshot.ReplayCommands.IsDefaultOrEmpty)
        {
            ValidateReplayCommands(snapshot);
        }

        ValidatePendingNodeBackendEvents(snapshot.Scheduler, snapshot.Definition);
        var currentBindings = _devices.ToDictionary(pair => pair.Key, pair => pair.Value.NodeBinding,
            StringComparer.Ordinal);
        var devices = RestoreDeviceRuntimes(snapshot);
        var scheduler = new DeterministicScheduler();
        try
        {
            scheduler.RestoreSnapshot(snapshot.Scheduler);
        }
        catch (SchedulerException exception)
        {
            throw new ArgumentException(
                $"Device graph scheduler snapshot is invalid: {exception.Message}",
                nameof(snapshot),
                exception);
        }
        ValidateSnapshotTargets(snapshot);
        ValidateNodeOutputState(snapshot.Definition, snapshot.Scheduler, devices);
        if (snapshot.NextReplayCommandIndex == snapshot.ReplayCommands.Length &&
            snapshot.Scheduler.AcceptedCommands.Length != snapshot.ReplayCommands.Length)
        {
            throw new ArgumentException("Device graph replay log does not match accepted scheduler commands or detach commands.", nameof(snapshot));
        }

        foreach (var runtime in devices.Values)
        {
            if (runtime.Definition.Backend is not NodeDeviceBackendDefinition ||
                !currentBindings.TryGetValue(runtime.Definition.Id.Value, out var binding) || binding is null)
            {
                continue;
            }

            if (runtime.NodeDetachApplyAtTick is null && !runtime.NodeDetachRequested && binding.IsActive)
            {
                if (runtime.NodeBehaviorFingerprint is { } expectedFingerprint &&
                    !string.Equals(expectedFingerprint, binding.BehaviorAssemblyFingerprint, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Device graph snapshot Node backend fingerprint does not match its live binding.",
                        nameof(snapshot));
                }

                runtime.NodeBinding = binding;
                runtime.NodeBehaviorFingerprint = binding.BehaviorAssemblyFingerprint;
            }
            else if (!binding.IsActive)
            {
                runtime.NodeDetachRequested = true;
            }
        }

        var lanes = RestoreLaneRuntimes(snapshot, scheduler);
        ValidatePendingLaneEvents(snapshot.Scheduler, lanes);
        ValidateConnectedLaneSignals(snapshot.Definition, snapshot.Scheduler, devices, lanes);
        if (!IsSha256(snapshot.StateIntegrityHash) ||
            !string.Equals(snapshot.StateIntegrityHash, ComputeStateIntegrityHash(snapshot), StringComparison.Ordinal))
        {
            throw new ArgumentException("Device graph state integrity hash does not match its state.", nameof(snapshot));
        }

        _definition = snapshot.Definition;
        _scheduler = scheduler;
        _devices = devices;
        _lanes = lanes;
        _replayCommands = snapshot.ReplayCommands.IsEmpty ? default : snapshot.ReplayCommands;
        _nextReplayCommandIndex = snapshot.NextReplayCommandIndex;
    }

    private static void ValidateConnectedLaneSignals(
        DeviceGraphDefinition definition,
        SchedulerSnapshot scheduler,
        IReadOnlyDictionary<string, DeviceRuntime> devices,
        IReadOnlyDictionary<string, LaneRuntime> lanes)
    {
        foreach (var lane in lanes.Values.Where(lane => lane.Connected))
        {
            var target = devices[lane.Definition.Target.DeviceId.Value];
            if (target.Panel is null)
            {
                if (!target.Inputs.TryGetValue(lane.Definition.Target.PortName, out var input) ||
                    !input.Equals(lane.CurrentSignal))
                {
                    throw new ArgumentException("Device graph snapshot lane signal does not match its target input.", nameof(lanes));
                }
            }
            else
            {
                var targetCells = target.Panel.CaptureSnapshot().Cells
                    .OfType<PanelRuntimeCellSnapshot>()
                    .Where(cell => cell.PortId?.Value == lane.Definition.Target.PortName)
                    .ToArray();
                if (targetCells.Length == 0 || targetCells.Any(cell =>
                        cell.ExternalInput != lane.CurrentSignal.Bits[0]))
                {
                    throw new ArgumentException("Device graph snapshot lane signal does not match its panel target.", nameof(lanes));
                }
            }

            var source = devices[lane.Definition.Source.DeviceId.Value];
            var sourceSignal = source.Panel is null
                ? source.Outputs[lane.Definition.Source.PortName]
                : DeviceSignal.Scalar(source.Panel.GetOutput(new PortId(lane.Definition.Source.PortName)));
            if (!sourceSignal.Equals(lane.CurrentSignal) && !scheduler.PendingEvents.Any(scheduledEvent =>
                    scheduledEvent.TargetStableId == lane.Definition.Id.Value &&
                    scheduledEvent.EventKind == CableTransitionEvent &&
                    scheduledEvent.Payload == sourceSignal.ToString()))
            {
                throw new ArgumentException("Device graph snapshot lane signal does not match its source output.", nameof(lanes));
            }
        }
    }

    private static void ValidateNodeOutputState(
        DeviceGraphDefinition definition,
        SchedulerSnapshot scheduler,
        IReadOnlyDictionary<string, DeviceRuntime> devices)
    {
        foreach (var device in definition.Devices.Where(device => device.Backend is NodeDeviceBackendDefinition))
        {
            var target = NodeBackendTargetId(device.Id);
            var targetSnapshot = scheduler.Targets.First(item => item.StableId == target);
            foreach (var port in device.Ports.Where(port => port.Direction == DevicePortDirection.Output))
            {
                var expected = DefaultSignal(port);
                var delivered = scheduler.Trace
                    .SelectMany(trace => trace.DeliveredEvents)
                    .Where(scheduledEvent =>
                        scheduledEvent.EventKind == NodeOutputEvent &&
                        scheduledEvent.TargetStableId == target &&
                        scheduledEvent.TargetIncarnation == targetSnapshot.Incarnation &&
                        scheduledEvent.TargetPortOrLane == port.Name)
                    .LastOrDefault();
                if (delivered is not null && DeviceSignal.TryParse(delivered.Payload, port.Width, out var signal))
                {
                    expected = signal;
                }

                if (!devices[device.Id.Value].Outputs[port.Name].Equals(expected))
                {
                    throw new ArgumentException("Device graph snapshot Node output does not match delivered events.", nameof(devices));
                }
            }
        }
    }

    private static void ValidateReplayCommands(DeviceGraphRuntimeSnapshot snapshot)
    {
        var validator = new DeviceGraphInstance(snapshot.Definition);
        var incarnations = snapshot.Definition.Devices
            .Where(device => device.Backend is NodeDeviceBackendDefinition)
            .ToDictionary(device => NodeBackendTargetId(device.Id), _ => 1L, StringComparer.Ordinal);
        var availableAt = incarnations.Keys.ToDictionary(key => key, _ => 0L, StringComparer.Ordinal);
        var previousApplyAtTick = 0L;
        for (var index = 0; index < snapshot.ReplayCommands.Length; index++)
        {
            var recorded = snapshot.ReplayCommands[index];
            if (recorded is null || recorded.AcceptedOrdinal != index + 1L || recorded.Command is null)
            {
                throw new ArgumentException("Device graph snapshot replay commands are invalid.", nameof(snapshot));
            }

            var command = recorded.Command;
            if (command.ApplyAtTick < previousApplyAtTick)
            {
                throw new ArgumentException("Device graph replay commands are not ordered by apply tick.", nameof(snapshot));
            }

            var wasAccepted = snapshot.Scheduler.AcceptedCommands.Any(command =>
                command.AcceptedOrdinal == recorded.AcceptedOrdinal && command.Command == recorded.Command);
            if ((index < snapshot.NextReplayCommandIndex) != wasAccepted ||
                index >= snapshot.NextReplayCommandIndex &&
                recorded.Command.ApplyAtTick < snapshot.Scheduler.CurrentTick)
            {
                throw new ArgumentException("Device graph replay cursor does not match accepted commands.", nameof(snapshot));
            }

            if (IsLaneTargetLifecycleCommand(snapshot.Definition, command))
            {
                previousApplyAtTick = command.ApplyAtTick;
                continue;
            }

            if (!validator.IsReplayNodeBackendCommand(command))
            {
                throw new ArgumentException("Device graph snapshot replay commands are invalid.", nameof(snapshot));
            }
            if (!incarnations.TryGetValue(command.TargetStableId, out var expectedIncarnation))
            {
                throw new ArgumentException("Device graph replay command targets an unknown Node backend.", nameof(snapshot));
            }

            if (command.Kind == SchedulerCommandKind.RemoveTarget)
            {
                if (command.TargetIncarnation != 0 || command.ApplyAtTick < availableAt[command.TargetStableId])
                {
                    throw new ArgumentException("Device graph replay detach command has an incarnation.", nameof(snapshot));
                }

                incarnations[command.TargetStableId] = checked(expectedIncarnation + 1);
                availableAt[command.TargetStableId] = checked(command.ApplyAtTick + 1);
            }
            else if (command.TargetIncarnation != expectedIncarnation ||
                     command.ApplyAtTick < availableAt[command.TargetStableId])
            {
                throw new ArgumentException("Device graph replay command targets a different backend incarnation.", nameof(snapshot));
            }

            previousApplyAtTick = command.ApplyAtTick;

        }

        if (snapshot.Scheduler.AcceptedCommands.Any(accepted =>
                !snapshot.ReplayCommands.Take(snapshot.NextReplayCommandIndex).Any(recorded =>
                    recorded.AcceptedOrdinal == accepted.AcceptedOrdinal && recorded.Command == accepted.Command)))
        {
            throw new ArgumentException("Device graph accepted commands are outside the replay prefix.", nameof(snapshot));
        }

        var appliedIncarnations = incarnations.Keys.ToDictionary(key => key, _ => 1L, StringComparer.Ordinal);
        foreach (var recorded in snapshot.ReplayCommands.Take(snapshot.NextReplayCommandIndex))
        {
            if (IsLaneTargetLifecycleCommand(snapshot.Definition, recorded.Command))
            {
                continue;
            }

            if (recorded.Command.Kind == SchedulerCommandKind.RemoveTarget &&
                snapshot.Scheduler.AppliedCommandOrdinals.Contains(recorded.AcceptedOrdinal))
            {
                appliedIncarnations[recorded.Command.TargetStableId] =
                    checked(appliedIncarnations[recorded.Command.TargetStableId] + 1);
            }
        }

        if (appliedIncarnations.Any(pair =>
                !snapshot.Scheduler.Targets.Any(target => target.StableId == pair.Key &&
                                                          target.Active && target.Incarnation == pair.Value)))
        {
            throw new ArgumentException("Device graph replay target activity does not match its accepted timeline.", nameof(snapshot));
        }
    }

    private static void ValidateSnapshotTargets(DeviceGraphRuntimeSnapshot snapshot)
    {
        var expectedTargets = snapshot.Definition.Devices.Select(device => device.Id.Value)
            .Concat(snapshot.Definition.Devices
                .Where(device => device.Backend is NodeDeviceBackendDefinition)
                .Select(device => NodeBackendTargetId(device.Id)))
            .Concat(snapshot.Definition.Lanes.Select(lane => lane.Id.Value))
            .ToHashSet(StringComparer.Ordinal);
        var targets = snapshot.Scheduler.Targets.ToDictionary(target => target.StableId, StringComparer.Ordinal);
        if (!targets.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedTargets) ||
            targets.Values.Any(target => !target.Active))
        {
            throw new ArgumentException("Device graph snapshot targets do not match its definitions.", nameof(snapshot));
        }

        if (snapshot.Scheduler.Drives.Any(drive =>
                !targets.TryGetValue(drive.Key.SourceStableId, out var source) ||
                source.Incarnation != drive.Key.SourceIncarnation ||
                !targets.TryGetValue(drive.Key.TargetStableId, out var target) ||
                !target.Active || target.Incarnation != drive.Key.TargetIncarnation ||
                !IsDeviceDrive(snapshot.Definition, drive.Key)))
        {
            throw new ArgumentException("Device graph snapshot contains an unresolved scheduler drive.", nameof(snapshot));
        }

        foreach (var trace in snapshot.Scheduler.Trace)
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
                    !IsDeviceDrive(snapshot.Definition, drive.Key)) ||
                state.PendingEvents.Any(scheduledEvent =>
                    !traceTargets.TryGetValue(scheduledEvent.TargetStableId, out var target) ||
                    scheduledEvent.TargetIncarnation != target.Incarnation ||
                    !traceTargets.TryGetValue(scheduledEvent.SourceStableId, out var source) ||
                    scheduledEvent.SourceIncarnation != source.Incarnation ||
                    !IsDeviceTraceEvent(snapshot.Definition, scheduledEvent)) ||
                trace.DeliveredEvents.Any(scheduledEvent =>
                    !traceTargets.TryGetValue(scheduledEvent.TargetStableId, out var target) ||
                    !target.Active || target.Incarnation != scheduledEvent.TargetIncarnation ||
                    !traceTargets.TryGetValue(scheduledEvent.SourceStableId, out var source) ||
                    source.Incarnation != scheduledEvent.SourceIncarnation ||
                    !IsDeviceTraceEvent(snapshot.Definition, scheduledEvent)) ||
                trace.ResolvedInputs.Any(input =>
                {
                    if (!traceTargets.TryGetValue(input.Key.TargetStableId, out var target) ||
                        !target.Active || target.Incarnation != input.Key.TargetIncarnation)
                    {
                        return true;
                    }

                    return !IsDeviceSchedulerAddress(snapshot.Definition, input.Key);
                }))
            {
                throw new ArgumentException("Device graph trace state contains an unresolved reference.", nameof(snapshot));
            }
        }

    }

    private static bool IsDeviceTraceEvent(DeviceGraphDefinition definition, ScheduledEvent scheduledEvent)
    {
        var lane = definition.Lanes.FirstOrDefault(candidate => candidate.Id.Value == scheduledEvent.TargetStableId);
        if (lane is not null)
        {
            var sourceDevice = definition.GetDevice(lane.Source.DeviceId);
            var sourceTarget = sourceDevice.Backend is NodeDeviceBackendDefinition
                ? NodeBackendTargetId(sourceDevice.Id)
                : sourceDevice.Id.Value;
            return scheduledEvent.EventKind is CableTransitionEvent or CableReleaseEvent &&
                   scheduledEvent.TargetPortOrLane == lane.Target.PortName &&
                   scheduledEvent.SourceStableId == sourceTarget &&
                   scheduledEvent.SourcePort == lane.Source.PortName;
        }

        var nodeTarget = definition.Devices.FirstOrDefault(device =>
            device.Backend is NodeDeviceBackendDefinition &&
            NodeBackendTargetId(device.Id) == scheduledEvent.TargetStableId);
        if (nodeTarget is null)
        {
            return false;
        }

        return scheduledEvent.SourceStableId == scheduledEvent.TargetStableId &&
               scheduledEvent.SourcePort == scheduledEvent.TargetPortOrLane &&
               scheduledEvent.EventKind is NodeOutputEvent or NodeTimerEvent &&
               (nodeTarget.Ports.Any(port => port.Direction == DevicePortDirection.Output &&
                                             port.Name == scheduledEvent.TargetPortOrLane) ||
                scheduledEvent.EventKind == NodeTimerEvent && ChipData.IsStableId(scheduledEvent.TargetPortOrLane));
    }

    private static bool IsDeviceSchedulerAddress(
        DeviceGraphDefinition definition,
        SchedulerPortAddress address)
    {
        var lane = definition.Lanes.FirstOrDefault(candidate => candidate.Id.Value == address.TargetStableId);
        if (lane is not null)
        {
            return string.Equals(address.PortOrLane, lane.Target.PortName, StringComparison.Ordinal);
        }

        var device = definition.Devices.FirstOrDefault(candidate => candidate.Id.Value == address.TargetStableId);
        if (device is not null)
        {
            return device.Ports.Any(port => port.Direction == DevicePortDirection.Input &&
                                            port.Name == address.PortOrLane);
        }

        return false;
    }

    private static bool IsDeviceDrive(DeviceGraphDefinition definition, SchedulerDriveKey key)
    {
        var lane = definition.Lanes.FirstOrDefault(candidate => candidate.Id.Value == key.TargetStableId);
        if (lane is not null)
        {
            var sourceDevice = definition.GetDevice(lane.Source.DeviceId);
            var sourceTarget = sourceDevice.Backend is NodeDeviceBackendDefinition
                ? NodeBackendTargetId(sourceDevice.Id)
                : sourceDevice.Id.Value;
            return key.SourceStableId == sourceTarget &&
                   key.SourcePort == lane.Source.PortName &&
                   key.TargetPortOrLane == lane.Target.PortName;
        }

        var targetDevice = definition.Devices.FirstOrDefault(candidate => candidate.Id.Value == key.TargetStableId);
        if (targetDevice is not null)
        {
            var targetPort = targetDevice.Ports.FirstOrDefault(port =>
                port.Direction == DevicePortDirection.Input && port.Name == key.TargetPortOrLane);
            var sourceDevice = definition.Devices.FirstOrDefault(candidate =>
                candidate.Id.Value == key.SourceStableId);
            if (sourceDevice is null && key.SourceStableId.StartsWith(NodeBackendTargetPrefix, StringComparison.Ordinal))
            {
                sourceDevice = definition.Devices.FirstOrDefault(candidate =>
                    candidate.Backend is NodeDeviceBackendDefinition &&
                    NodeBackendTargetId(candidate.Id) == key.SourceStableId);
            }

            return targetPort is not null && sourceDevice is not null &&
                   definition.Lanes.Any(lane =>
                       lane.Source.DeviceId == sourceDevice.Id &&
                       lane.Source.PortName == key.SourcePort &&
                       lane.Target.DeviceId == targetDevice.Id &&
                       lane.Target.PortName == key.TargetPortOrLane) &&
                   sourceDevice.Ports.Any(port => port.Direction == DevicePortDirection.Output &&
                                                 port.Name == key.SourcePort);
        }

        var nodeTarget = definition.Devices.FirstOrDefault(candidate =>
            candidate.Backend is NodeDeviceBackendDefinition &&
            NodeBackendTargetId(candidate.Id) == key.TargetStableId);
        return nodeTarget is not null && key.SourceStableId == key.TargetStableId &&
               key.SourcePort == key.TargetPortOrLane &&
               nodeTarget.Ports.Any(port => port.Direction == DevicePortDirection.Output &&
                                            port.Name == key.TargetPortOrLane);
    }

    private static void ValidatePendingNodeBackendEvents(
        SchedulerSnapshot scheduler,
        DeviceGraphDefinition definition)
    {
        var nodeDevices = definition.Devices
            .Where(device => device.Backend is NodeDeviceBackendDefinition)
            .ToDictionary(device => NodeBackendTargetId(device.Id), StringComparer.Ordinal);
        var targets = scheduler.Targets.ToDictionary(target => target.StableId, StringComparer.Ordinal);
        var acceptedCommands = scheduler.AcceptedCommands
            .Where(command => scheduler.AppliedCommandOrdinals.Contains(command.AcceptedOrdinal) &&
                              command.Command.Kind == SchedulerCommandKind.ScheduleEvent &&
                              nodeDevices.ContainsKey(command.Command.TargetStableId) &&
                              command.Command.EventKind is NodeOutputEvent or NodeTimerEvent)
            .ToArray();
        var matchedCommands = new HashSet<long>();
        var nodeEvents = scheduler.PendingEvents
            .Concat(scheduler.Trace.SelectMany(trace => trace.DeliveredEvents))
            .Where(scheduledEvent => scheduledEvent.EventKind is NodeOutputEvent or NodeTimerEvent)
            .ToArray();
        if (scheduler.PendingEvents.Any(scheduledEvent =>
                scheduledEvent.EventKind is not (NodeOutputEvent or NodeTimerEvent or CableTransitionEvent or CableReleaseEvent)))
        {
            throw new ArgumentException("Device graph snapshot contains an unsupported scheduler event.", nameof(scheduler));
        }

        foreach (var scheduledEvent in nodeEvents)
        {
            if (!nodeDevices.TryGetValue(scheduledEvent.TargetStableId, out var device) ||
                !targets.TryGetValue(scheduledEvent.TargetStableId, out var target))
            {
                throw new ArgumentException("Device graph snapshot Node backend event has no matching target.", nameof(scheduler));
            }

            if (scheduledEvent.EventKind is not (NodeOutputEvent or NodeTimerEvent) ||
                scheduledEvent.Key.Phase != SchedulerPhase.Deliver ||
                scheduledEvent.TargetIncarnation <= 0 ||
                scheduledEvent.TargetIncarnation > target.Incarnation ||
                scheduledEvent.SourceIncarnation != scheduledEvent.TargetIncarnation ||
                !string.Equals(scheduledEvent.SourceStableId, scheduledEvent.TargetStableId, StringComparison.Ordinal) ||
                !string.Equals(scheduledEvent.SourcePort, scheduledEvent.TargetPortOrLane, StringComparison.Ordinal))
            {
                throw new ArgumentException("Device graph snapshot Node backend events are invalid.", nameof(scheduler));
            }

            if (scheduledEvent.EventKind == NodeOutputEvent)
            {
                var port = device.Ports.FirstOrDefault(candidate =>
                    candidate.Direction == DevicePortDirection.Output &&
                    string.Equals(candidate.Name, scheduledEvent.TargetPortOrLane, StringComparison.Ordinal));
                if (port is null || !DeviceSignal.TryParse(scheduledEvent.Payload, port.Width, out var signal) ||
                    scheduledEvent.Value != signal.Bits[0] ||
                    !string.Equals(scheduledEvent.Payload, signal.ToString(), StringComparison.Ordinal))
                {
                    throw new ArgumentException("Device graph snapshot Node output event is invalid.", nameof(scheduler));
                }

                port.ValidateSignal(signal, nameof(scheduler));
            }
            else if (!ChipData.IsStableId(scheduledEvent.TargetPortOrLane) ||
                     !string.Equals(scheduledEvent.Payload, scheduledEvent.TargetPortOrLane, StringComparison.Ordinal) ||
                     scheduledEvent.Value != LogicValue.HighImpedance)
            {
                throw new ArgumentException("Device graph snapshot Node timer event is invalid.", nameof(scheduler));
            }

            var matchingCommand = acceptedCommands.FirstOrDefault(accepted =>
                !matchedCommands.Contains(accepted.AcceptedOrdinal) &&
                MatchesNodeBackendEvent(scheduledEvent, accepted));
            if (matchingCommand is null)
            {
                throw new ArgumentException(
                    "Device graph snapshot Node event has no applied accepted command.",
                    nameof(scheduler));
            }

            matchedCommands.Add(matchingCommand.AcceptedOrdinal);
        }

        if (acceptedCommands.Any(accepted =>
                !matchedCommands.Contains(accepted.AcceptedOrdinal) &&
                (!targets.TryGetValue(accepted.Command.TargetStableId, out var target) ||
                 target.Active && target.Incarnation == accepted.Command.TargetIncarnation)))
        {
            throw new ArgumentException("Device graph snapshot Node command has no corresponding event.", nameof(scheduler));
        }
    }

    private static bool MatchesNodeBackendEvent(
        ScheduledEvent scheduledEvent,
        AcceptedSchedulerCommand accepted) =>
        accepted.Command.Kind == SchedulerCommandKind.ScheduleEvent &&
        scheduledEvent.Key.CausalOrdinal == accepted.AcceptedOrdinal &&
        scheduledEvent.Key.DueTick == accepted.Command.DueTick &&
        scheduledEvent.Key.Phase == accepted.Command.EventPhase &&
        string.Equals(scheduledEvent.TargetStableId, accepted.Command.TargetStableId, StringComparison.Ordinal) &&
        scheduledEvent.TargetIncarnation == accepted.Command.TargetIncarnation &&
        string.Equals(scheduledEvent.TargetPortOrLane, accepted.Command.TargetPortOrLane, StringComparison.Ordinal) &&
        string.Equals(scheduledEvent.SourceStableId, accepted.Command.SourceStableId, StringComparison.Ordinal) &&
        string.Equals(scheduledEvent.SourcePort, accepted.Command.SourcePort, StringComparison.Ordinal) &&
        string.Equals(scheduledEvent.EventKind, accepted.Command.EventKind, StringComparison.Ordinal) &&
        scheduledEvent.Value == accepted.Command.Value &&
        string.Equals(scheduledEvent.Payload, accepted.Command.Payload, StringComparison.Ordinal) &&
        string.Equals(scheduledEvent.TemporalRootId, accepted.Command.RootId, StringComparison.Ordinal) &&
        scheduledEvent.SourceIncarnation == accepted.Command.SourceIncarnation;

    private static bool IsLaneTargetLifecycleCommand(
        DeviceGraphDefinition definition,
        SchedulerCommand command) =>
        command.Kind is SchedulerCommandKind.RegisterTarget or SchedulerCommandKind.RemoveTarget &&
        definition.Lanes.Any(lane => lane.Id.Value == command.TargetStableId);

    private static Dictionary<string, DeviceRuntime> RestoreDeviceRuntimes(DeviceGraphRuntimeSnapshot snapshot)
    {
        var deviceSnapshots = snapshot.Devices.ToDictionary(item => item.DeviceId);
        if (deviceSnapshots.Count != snapshot.Definition.Devices.Length ||
            !deviceSnapshots.Keys.ToHashSet().SetEquals(snapshot.Definition.Devices.Select(device => device.Id)))
        {
            throw new ArgumentException("Device graph snapshot device states do not match its definitions.", nameof(snapshot));
        }

        ValidatePendingNodeCommands(snapshot.Scheduler, snapshot.Definition, deviceSnapshots);

        var devices = new Dictionary<string, DeviceRuntime>(StringComparer.Ordinal);
        foreach (var definition in snapshot.Definition.Devices)
        {
            devices.Add(definition.Id.Value, RestoreDeviceRuntime(
                definition,
                deviceSnapshots[definition.Id],
                snapshot.Scheduler.CurrentTick));
        }

        return devices;
    }

    private static void ValidatePendingNodeCommands(
        SchedulerSnapshot scheduler,
        DeviceGraphDefinition definition,
        IReadOnlyDictionary<ComponentId, DeviceRuntimeSnapshot> deviceSnapshots)
    {
        var nodeDevices = definition.Devices
            .Where(device => device.Backend is NodeDeviceBackendDefinition)
            .ToDictionary(device => NodeBackendTargetId(device.Id), StringComparer.Ordinal);
        var targets = scheduler.Targets.ToDictionary(target => target.StableId, StringComparer.Ordinal);
        var pendingDetaches = scheduler.AcceptedCommands
            .Where(command => !scheduler.AppliedCommandOrdinals.Contains(command.AcceptedOrdinal) &&
                              command.Command.Kind == SchedulerCommandKind.RemoveTarget)
            .GroupBy(command => command.Command.TargetStableId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        ValidateNodeDetachSnapshotState(scheduler, nodeDevices, deviceSnapshots, pendingDetaches);
        ValidateNodeCommandLog(scheduler, definition, nodeDevices, targets);
    }

    private static void ValidateNodeDetachSnapshotState(
        SchedulerSnapshot scheduler,
        IReadOnlyDictionary<string, DeviceDefinition> nodeDevices,
        IReadOnlyDictionary<ComponentId, DeviceRuntimeSnapshot> deviceSnapshots,
        IReadOnlyDictionary<string, AcceptedSchedulerCommand[]> pendingDetaches)
    {
        foreach (var (targetStableId, device) in nodeDevices)
        {
            var deviceSnapshot = deviceSnapshots[device.Id];
            var commands = pendingDetaches.TryGetValue(targetStableId, out var pending)
                ? pending
                : [];
            if (deviceSnapshot.NodeDetachApplyAtTick is { } detachAtTick
                    ? detachAtTick != scheduler.CurrentTick || commands.Length != 1 ||
                      commands[0].Command.ApplyAtTick != detachAtTick
                    : commands.Length != 0 || deviceSnapshot.NodeDetachRequested)
            {
                throw new ArgumentException(
                    "Device graph snapshot Node detach commands do not match detach state.",
                    nameof(scheduler));
            }
        }
    }

    private static void ValidateNodeCommandLog(
        SchedulerSnapshot scheduler,
        DeviceGraphDefinition definition,
        IReadOnlyDictionary<string, DeviceDefinition> nodeDevices,
        IReadOnlyDictionary<string, SchedulerTargetSnapshot> targets)
    {
        foreach (var accepted in scheduler.AcceptedCommands)
        {
            var command = accepted.Command;
            if (IsLaneTargetLifecycleCommand(definition, command))
            {
                continue;
            }

            if (!nodeDevices.TryGetValue(command.TargetStableId, out var device))
            {
                throw new ArgumentException(
                    "Device graph snapshot contains a command outside the Node backend boundary.",
                    nameof(scheduler));
            }

            var isPending = !scheduler.AppliedCommandOrdinals.Contains(accepted.AcceptedOrdinal);
            if (command.Kind == SchedulerCommandKind.RemoveTarget)
            {
                if (isPending && command.ApplyAtTick != scheduler.CurrentTick)
                {
                    throw new ArgumentException("Device graph snapshot Node detach command is invalid.", nameof(scheduler));
                }

                continue;
            }

            ValidateNodeScheduleCommand(
                device,
                command,
                scheduler.CurrentTick,
                targets[NodeBackendTargetId(device.Id)].Incarnation,
                isPending);
        }
    }

    private static DeviceRuntime RestoreDeviceRuntime(
        DeviceDefinition definition,
        DeviceRuntimeSnapshot snapshot,
        long currentTick)
    {
        var runtime = CreateRuntime(definition, 0);
        ValidateDeviceSnapshotShape(definition, snapshot, runtime, currentTick);
        if (snapshot.Panel is not null)
        {
            runtime.Panel!.RestoreSnapshot(snapshot.Panel);
        }

        RestoreDeviceSignals(definition, snapshot, runtime);
        RestoreDeviceTimers(definition, snapshot, runtime, currentTick);
        runtime.PendingNodeInputs.AddRange(snapshot.PendingNodeInputs);
        runtime.NodeInputHistory.AddRange(snapshot.NodeInputHistory);
        runtime.NodeDetachApplyAtTick = snapshot.NodeDetachApplyAtTick;
        runtime.NodeDetachRequested = snapshot.NodeDetachRequested;
        runtime.NodeBehaviorFingerprint = snapshot.NodeBehaviorFingerprint;
        return runtime;
    }

    private static void ValidateDeviceSnapshotShape(
        DeviceDefinition definition,
        DeviceRuntimeSnapshot snapshot,
        DeviceRuntime runtime,
        long currentTick)
    {
        var usesNodeBackend = definition.Backend is NodeDeviceBackendDefinition;
        if (snapshot.Inputs.IsDefault || snapshot.Outputs.IsDefault || snapshot.Timers.IsDefault ||
            snapshot.PendingNodeInputs.IsDefault || snapshot.NodeInputHistory.IsDefault ||
            snapshot.Inputs.Select(pair => pair.Key).Distinct(StringComparer.Ordinal).Count() != snapshot.Inputs.Length ||
            snapshot.Outputs.Select(pair => pair.Key).Distinct(StringComparer.Ordinal).Count() != snapshot.Outputs.Length ||
            !snapshot.Inputs.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
                .SetEquals(runtime.Panel is null
                    ? definition.Ports.Where(port => port.Direction == DevicePortDirection.Input).Select(port => port.Name)
                    : []) ||
            !snapshot.Outputs.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
                .SetEquals(definition.Ports.Where(port => port.Direction == DevicePortDirection.Output)
                    .Select(port => port.Name)))
        {
            throw new ArgumentException("Device graph snapshot output state is invalid.", nameof(snapshot));
        }

        if ((runtime.Panel is null) != (snapshot.Panel is null))
        {
            throw new ArgumentException("Device graph snapshot backend state is invalid.", nameof(snapshot));
        }

        if ((!usesNodeBackend && (snapshot.PendingNodeInputs.Length > 0 ||
                                  snapshot.NodeDetachApplyAtTick is not null || snapshot.NodeDetachRequested ||
                                  snapshot.NodeBehaviorFingerprint is not null)) ||
            (snapshot.NodeBehaviorFingerprint is { } fingerprint && !IsSha256(fingerprint)) ||
            (snapshot.NodeDetachApplyAtTick is { } detachAt && detachAt < currentTick) ||
            snapshot.PendingNodeInputs.Any(input => input is null || input.Tick < 0 || input.Tick > currentTick))
        {
            throw new ArgumentException("Device graph snapshot Node backend state is invalid.", nameof(snapshot));
        }

        foreach (var input in snapshot.PendingNodeInputs)
        {
            var port = definition.Ports.FirstOrDefault(candidate =>
                candidate.Direction == DevicePortDirection.Input &&
                string.Equals(candidate.Name, input.PortName, StringComparison.Ordinal));
            if (port is null)
            {
                throw new ArgumentException("Device graph snapshot Node input is invalid.", nameof(snapshot));
            }

            port.ValidateSignal(input.Signal, nameof(snapshot));
        }

        foreach (var input in snapshot.NodeInputHistory)
        {
            if (input is null || input.Tick < 0 || input.Tick > currentTick)
            {
                throw new ArgumentException("Device graph snapshot Node input history is invalid.", nameof(snapshot));
            }

            var port = definition.Ports.FirstOrDefault(candidate =>
                candidate.Direction == DevicePortDirection.Input &&
                string.Equals(candidate.Name, input.PortName, StringComparison.Ordinal));
            if (port is null)
            {
                throw new ArgumentException("Device graph snapshot Node input history is invalid.", nameof(snapshot));
            }

            port.ValidateSignal(input.Signal, nameof(snapshot));
        }

        var expectedPendingInputs = snapshot.NodeInputHistory
            .Where(input => input.Tick == currentTick)
            .ToImmutableArray();
        if (!expectedPendingInputs.SequenceEqual(snapshot.PendingNodeInputs))
        {
            throw new ArgumentException("Device graph snapshot pending Node inputs do not match input history.", nameof(snapshot));
        }

        if (definition.Backend is TimedDeviceBackendDefinition timed)
        {
            ValidateTimedDeviceState(definition, snapshot, timed, currentTick);
        }

        foreach (var group in snapshot.PendingNodeInputs.GroupBy(input => input.PortName, StringComparer.Ordinal))
        {
            var last = group.Last();
            var current = snapshot.Inputs.FirstOrDefault(input => input.Key == group.Key);
            if (current.Key is null || !current.Value.Equals(last.Signal))
            {
                throw new ArgumentException("Device graph snapshot pending Node input does not match its current input.", nameof(snapshot));
            }
        }
    }

    private static void ValidateTimedDeviceState(
        DeviceDefinition definition,
        DeviceRuntimeSnapshot snapshot,
        TimedDeviceBackendDefinition timed,
        long currentTick)
    {
        var expectedOutputs = definition.Ports
            .Where(port => port.Direction == DevicePortDirection.Output)
            .ToDictionary(port => port.Name, DefaultSignal, StringComparer.Ordinal);
        var expectedTimers = new List<DeviceTimerSnapshot>();
        foreach (var change in timed.Changes)
        {
            var dueTick = change.DelayTicks;
            if (dueTick < currentTick)
            {
                expectedOutputs[change.PortName] = change.Value;
            }
            else
            {
                expectedTimers.Add(new DeviceTimerSnapshot(dueTick, change.PortName, change.Value));
            }
        }

        if (snapshot.Outputs.Any(output =>
                !expectedOutputs.TryGetValue(output.Key, out var expected) ||
                !expected.Equals(output.Value)) ||
            !snapshot.Timers.SequenceEqual(expectedTimers.OrderBy(timer => timer.DueTick)
                .ThenBy(timer => timer.PortName, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Timed device snapshot state does not match its backend schedule.", nameof(snapshot));
        }
    }

    private static void RestoreDeviceSignals(
        DeviceDefinition definition,
        DeviceRuntimeSnapshot snapshot,
        DeviceRuntime runtime)
    {
        runtime.Inputs.Clear();
        runtime.Outputs.Clear();
        foreach (var (name, value) in snapshot.Inputs)
        {
            FindPort(definition, name, DevicePortDirection.Input).ValidateSignal(value, nameof(snapshot));
            runtime.Inputs.Add(name, value);
        }

        foreach (var (name, value) in snapshot.Outputs)
        {
            FindPort(definition, name, DevicePortDirection.Output).ValidateSignal(value, nameof(snapshot));
            runtime.Outputs.Add(name, value);
        }

        if (runtime.Panel is not null && runtime.Outputs.Any(pair =>
                !pair.Value.Equals(DeviceSignal.Scalar(runtime.Panel.GetOutput(new PortId(pair.Key))))))
        {
            throw new ArgumentException("Device graph snapshot panel outputs do not match panel state.", nameof(snapshot));
        }
    }

    private static void RestoreDeviceTimers(
        DeviceDefinition definition,
        DeviceRuntimeSnapshot snapshot,
        DeviceRuntime runtime,
        long currentTick)
    {
        if ((runtime.Panel is not null || definition.Backend is not TimedDeviceBackendDefinition) &&
            snapshot.Timers.Length > 0)
        {
            throw new ArgumentException("Device snapshot contains timers for a backend without timers.", nameof(snapshot));
        }

        var timers = snapshot.Timers.ToArray();
        if (timers.Any(timer => timer is null || timer.DueTick < currentTick) ||
            timers.Select(timer => (timer.DueTick, timer.PortName)).Distinct().Count() != timers.Length ||
            !timers.SequenceEqual(timers.OrderBy(timer => timer.DueTick)
                .ThenBy(timer => timer.PortName, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Timed device snapshot state is invalid.", nameof(snapshot));
        }

        foreach (var timer in timers)
        {
            FindPort(definition, timer.PortName, DevicePortDirection.Output)
                .ValidateSignal(timer.Value, nameof(snapshot));
        }

        runtime.Timers.Clear();
        foreach (var timer in timers)
        {
            runtime.Timers.Enqueue(new TimedDeviceTimer(timer.DueTick, timer.PortName, timer.Value));
        }
    }

    private static Dictionary<string, LaneRuntime> RestoreLaneRuntimes(
        DeviceGraphRuntimeSnapshot snapshot,
        DeterministicScheduler scheduler)
    {
        var laneSnapshots = snapshot.Lanes.ToDictionary(item => item.LaneId);
        if (laneSnapshots.Count != snapshot.Definition.Lanes.Length ||
            !laneSnapshots.Keys.ToHashSet().SetEquals(snapshot.Definition.Lanes.Select(lane => lane.Id)))
        {
            throw new ArgumentException("Device graph snapshot lane states do not match its definitions.", nameof(snapshot));
        }

        var lanes = new Dictionary<string, LaneRuntime>(StringComparer.Ordinal);
        foreach (var definition in snapshot.Definition.Lanes)
        {
            var saved = laneSnapshots[definition.Id];
            var sourcePort = FindPort(snapshot.Definition.GetDevice(definition.Source.DeviceId),
                definition.Source.PortName, DevicePortDirection.Output);
            sourcePort.ValidateSignal(saved.CurrentSignal, nameof(snapshot));
            if (saved.Epoch != scheduler.GetTarget(definition.Id.Value).Incarnation || saved.Epoch <= 0)
            {
                throw new ArgumentException("Device graph snapshot lane epoch is invalid.", nameof(snapshot));
            }

            var lane = new LaneRuntime(definition, saved.Epoch, saved.CurrentSignal)
            {
                Connected = saved.Connected
            };
            if (saved.History.IsDefault)
            {
                throw new ArgumentException("Device graph snapshot lane history is invalid.", nameof(snapshot));
            }

            lane.History.AddRange(saved.History);
            for (var index = 0; index < lane.History.Count; index++)
            {
                var item = lane.History[index];
                sourcePort.ValidateSignal(item.Signal, nameof(snapshot));
                if (item.CausalOrdinal <= 0 || item.ScheduledTick < 0 ||
                    !Enum.IsDefined(item.Status) || !lane.HistoryByOrdinal.TryAdd(item.CausalOrdinal, index))
                {
                    throw new ArgumentException("Device graph snapshot lane history is invalid.", nameof(snapshot));
                }
            }

            lanes.Add(definition.Id.Value, lane);
        }

        return lanes;
    }

    private void DeliverCableEvent(
        ScheduledEvent scheduledEvent,
        long tick,
        ImmutableArray<DeviceCableDelivery>.Builder deliveries)
    {
        var lane = GetLane(new ComponentId(scheduledEvent.TargetStableId));
        if (!lane.HistoryByOrdinal.TryGetValue(scheduledEvent.Key.CausalOrdinal, out var index))
        {
            throw new InvalidOperationException("Delivered cable event has no lane history entry.");
        }

        var history = lane.History[index];
        if (history.Status != CableTransitionStatus.Pending || history.Epoch != lane.Epoch ||
            !DeviceSignal.TryParse(scheduledEvent.Payload,
                FindPort(_definition.GetDevice(lane.Definition.Source.DeviceId),
                    lane.Definition.Source.PortName, DevicePortDirection.Output).Width, out var signal) ||
            !history.Signal.Equals(signal))
        {
            throw new InvalidOperationException("Delivered cable event does not match its pending lane history.");
        }

        lane.History[index] = history with
        {
            Status = CableTransitionStatus.Delivered,
            DeliveredTick = tick
        };
        lane.CurrentSignal = signal;
        var device = GetDevice(lane.Definition.Target.DeviceId);
        if (device.Panel is null)
        {
            device.Inputs[lane.Definition.Target.PortName] = signal;
        }
        else
        {
            device.Panel.SetInput(new PortId(lane.Definition.Target.PortName), signal.Bits[0]);
        }

        if (device.Definition.Backend is NodeDeviceBackendDefinition)
        {
            var transition = new DeviceNodeInputTransition(
                tick,
                lane.Definition.Target.PortName,
                signal);
            device.PendingNodeInputs.Add(transition);
            device.NodeInputHistory.Add(transition);
        }

        deliveries.Add(new DeviceCableDelivery(
            lane.Definition.Id,
            history.Epoch,
            history.ScheduledTick,
            tick,
            signal,
            history.IsRelease));
    }

    private void ApplyDueTimers(long tick)
    {
        foreach (var runtime in _devices.Values.OrderBy(item => item.Definition.Id.Value, StringComparer.Ordinal))
        {
            while (runtime.Timers.Count > 0 && runtime.Timers.Peek().DueTick <= tick)
            {
                var timer = runtime.Timers.Peek();
                if (timer.DueTick < tick)
                {
                    throw new InvalidOperationException("Timed device contains a timer in the past.");
                }

                runtime.Timers.Dequeue();
                if (runtime.Outputs[timer.PortName].Equals(timer.Value))
                {
                    continue;
                }

                runtime.Outputs[timer.PortName] = timer.Value;
                ScheduleOutputChange(runtime.Definition.Id, timer.PortName, timer.Value, tick);
            }
        }
    }

    private void ScheduleOutputChange(ComponentId deviceId, string portName, DeviceSignal value, long tick)
    {
        ValidateOutputTicks(deviceId, portName, tick);
        foreach (var lane in _lanes.Values.Where(lane => lane.Connected &&
                     lane.Definition.Source.DeviceId == deviceId &&
                     string.Equals(lane.Definition.Source.PortName, portName, StringComparison.Ordinal))
                 .OrderBy(lane => lane.Definition.Id.Value, StringComparer.Ordinal))
        {
            ScheduleLaneTransition(lane, value, tick, isRelease: false);
        }
    }

    private DeviceNodeOutputCapability CreateNodeOutputCapability(
        DeviceRuntime runtime,
        DeviceNodeBackendBinding binding,
        long tick,
        ICollection<PendingNodeCommand> stagedCommands)
    {
        var targetStableId = NodeBackendTargetId(runtime.Definition.Id);
        var targetIncarnation = _scheduler.GetTarget(targetStableId).Incarnation;
        var callbackThreadId = System.Environment.CurrentManagedThreadId;

        return new DeviceNodeOutputCapability(
            (targetTick, portName, signal) => StageNodeOutputCommand(
                runtime, binding, tick, callbackThreadId, targetStableId, targetIncarnation,
                stagedCommands, targetTick, portName, signal),
            (dueTick, timerId) => StageNodeTimerCommand(
                runtime, binding, tick, callbackThreadId, targetStableId, targetIncarnation,
                stagedCommands, dueTick, timerId));
    }

    private bool StageNodeOutputCommand(
        DeviceRuntime runtime,
        DeviceNodeBackendBinding binding,
        long tick,
        int callbackThreadId,
        string targetStableId,
        long targetIncarnation,
        ICollection<PendingNodeCommand> stagedCommands,
        long targetTick,
        string portName,
        DeviceSignal signal)
    {
        if (!IsCurrentNodeCallback(runtime, binding, callbackThreadId, targetStableId, targetIncarnation) ||
            targetTick <= tick)
        {
            return false;
        }

        var port = FindPort(runtime.Definition, portName, DevicePortDirection.Output);
        port.ValidateSignal(signal, nameof(signal));
        ValidateOutputTicks(runtime.Definition.Id, portName, targetTick);
        var scheduledEvent = new ScheduledEvent(
            new ScheduledEventKey(
                targetTick,
                SchedulerPhase.Deliver,
                targetStableId,
                targetIncarnation,
                portName,
                targetStableId,
                portName,
                NodeOutputEvent,
                CausalOrdinal: 0),
            signal.Bits[0],
            signal.ToString(),
            SourceIncarnation: targetIncarnation);
        stagedCommands.Add(new PendingNodeCommand(
            runtime.Definition.Id,
            SchedulerCommand.ScheduleEvent(scheduledEvent, _scheduler.CurrentTick) with
            {
                CausalOriginTick = tick
            }));
        return true;
    }

    private bool StageNodeTimerCommand(
        DeviceRuntime runtime,
        DeviceNodeBackendBinding binding,
        long tick,
        int callbackThreadId,
        string targetStableId,
        long targetIncarnation,
        ICollection<PendingNodeCommand> stagedCommands,
        long dueTick,
        string timerId)
    {
        if (!IsCurrentNodeCallback(runtime, binding, callbackThreadId, targetStableId, targetIncarnation) ||
            dueTick <= tick)
        {
            return false;
        }

        if (!ChipData.IsStableId(timerId))
        {
            throw new ArgumentException("Node timer identifier is not stable data.", nameof(timerId));
        }

        var scheduledEvent = new ScheduledEvent(
            new ScheduledEventKey(
                dueTick,
                SchedulerPhase.Deliver,
                targetStableId,
                targetIncarnation,
                timerId,
                targetStableId,
                timerId,
                NodeTimerEvent,
                CausalOrdinal: 0),
            Payload: timerId,
            SourceIncarnation: targetIncarnation);
        stagedCommands.Add(new PendingNodeCommand(
            runtime.Definition.Id,
            SchedulerCommand.ScheduleEvent(scheduledEvent, _scheduler.CurrentTick) with
            {
                CausalOriginTick = tick
            }));
        return true;
    }

    private bool IsCurrentNodeCallback(
        DeviceRuntime runtime,
        DeviceNodeBackendBinding binding,
        int callbackThreadId,
        string targetStableId,
        long targetIncarnation) =>
        System.Environment.CurrentManagedThreadId == callbackThreadId &&
        binding.IsActive && ReferenceEquals(runtime.NodeBinding, binding) &&
        _scheduler.GetTarget(targetStableId).Incarnation == targetIncarnation;

    private void CommitNodeCommands(
        IReadOnlyCollection<PendingNodeCommand> commands,
        ImmutableArray<DeviceNodeBackendFailure>.Builder failures)
    {
        if (commands.Count == 0)
        {
            return;
        }

        try
        {
            _ = checked(_scheduler.NextAcceptedOrdinal + commands.Count);
            foreach (var pending in commands)
            {
                ValidatePendingNodeCommand(pending);
            }
        }
        catch (Exception exception)
        {
            failures.Add(new DeviceNodeBackendFailure(
                commands.First().DeviceId,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message));
            return;
        }

        var before = _scheduler.CaptureSnapshot();
        try
        {
            foreach (var pending in commands)
            {
                _scheduler.Accept(pending.Command);
            }
        }
        catch (Exception exception)
        {
            _scheduler.RestoreSnapshot(before);
            failures.Add(new DeviceNodeBackendFailure(
                commands.First().DeviceId,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message));
        }
    }

    private void ValidatePendingNodeCommand(PendingNodeCommand pending)
    {
        var runtime = GetDevice(pending.DeviceId);
        var targetStableId = NodeBackendTargetId(pending.DeviceId);
        var targetIncarnation = _scheduler.GetTarget(targetStableId).Incarnation;
        ValidateNodeScheduleCommand(
            runtime.Definition,
            pending.Command,
            _scheduler.CurrentTick,
            targetIncarnation,
            isPending: true);
    }

    private static void ValidateNodeScheduleCommand(
        DeviceDefinition device,
        SchedulerCommand command,
        long currentTick,
        long targetIncarnation,
        bool isPending)
    {
        var targetStableId = NodeBackendTargetId(device.Id);
        if (device.Backend is not NodeDeviceBackendDefinition ||
            command.Kind != SchedulerCommandKind.ScheduleEvent ||
            command.ApplyAtTick > currentTick ||
            (isPending && command.ApplyAtTick != currentTick) ||
            command.DueTick < command.ApplyAtTick ||
            command.EventPhase != SchedulerPhase.Deliver ||
            !string.Equals(command.TargetStableId, targetStableId, StringComparison.Ordinal) ||
            command.TargetIncarnation <= 0 || command.TargetIncarnation > targetIncarnation ||
            (isPending && command.TargetIncarnation != targetIncarnation) ||
            !HasNodeCausalDelay(command) ||
            !string.Equals(command.SourceStableId, targetStableId, StringComparison.Ordinal) ||
            command.SourceIncarnation != command.TargetIncarnation ||
            !string.Equals(command.SourcePort, command.TargetPortOrLane, StringComparison.Ordinal))
        {
            throw new ArgumentException("Node backend command does not match its graph boundary.");
        }

        if (command.EventKind == NodeOutputEvent)
        {
            var port = FindPort(device, command.TargetPortOrLane, DevicePortDirection.Output);
            if (!DeviceSignal.TryParse(command.Payload, port.Width, out var signal))
            {
                throw new ArgumentException("Node output command contains an invalid signal.");
            }

            port.ValidateSignal(signal, nameof(command));
            if (command.Value != signal.Bits[0] ||
                !string.Equals(command.Payload, signal.ToString(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Node output command signal fields disagree.");
            }

            return;
        }

        if (command.EventKind == NodeTimerEvent &&
            ChipData.IsStableId(command.TargetPortOrLane) &&
            string.Equals(command.Payload, command.TargetPortOrLane, StringComparison.Ordinal) &&
            command.Value == LogicValue.HighImpedance)
        {
            return;
        }

        throw new ArgumentException("Node backend command type is invalid.");
    }

    private static bool HasNodeCausalDelay(SchedulerCommand command) =>
        command.DueTick > command.ApplyAtTick ||
        command.CausalOriginTick is { } originTick && originTick < command.ApplyAtTick;

    private void QueueNodeBackendDetach(DeviceRuntime runtime, long applyAtTick)
    {
        if (runtime.Definition.Backend is not NodeDeviceBackendDefinition ||
            runtime.NodeDetachApplyAtTick is not null)
        {
            return;
        }

        var targetStableId = NodeBackendTargetId(runtime.Definition.Id);
        var before = _scheduler.CaptureSnapshot();
        try
        {
            _scheduler.Accept(SchedulerCommand.RemoveTarget(targetStableId, applyAtTick));
        }
        catch
        {
            _scheduler.RestoreSnapshot(before);
            throw;
        }

        runtime.NodeDetachApplyAtTick = applyAtTick;
        runtime.NodeDetachRequested = false;
    }

    private void ApplyNodeBackendDetaches(long tick)
    {
        foreach (var runtime in _devices.Values
                     .Where(runtime => runtime.NodeDetachApplyAtTick is { } applyAtTick && applyAtTick <= tick)
                     .OrderBy(runtime => runtime.Definition.Id.Value, StringComparer.Ordinal))
        {
            var deviceId = runtime.Definition.Id;
            _ = _scheduler.RegisterTarget(NodeBackendTargetId(deviceId));
            runtime.NodeBinding?.Invalidate();
            runtime.NodeBinding = null;
            runtime.NodeDetachApplyAtTick = null;
            runtime.NodeDetachRequested = false;

            foreach (var lane in _lanes.Values.Where(lane => lane.Definition.Source.DeviceId == deviceId))
            {
                for (var index = 0; index < lane.History.Count; index++)
                {
                    if (lane.History[index].Status == CableTransitionStatus.Pending)
                    {
                        lane.History[index] = lane.History[index] with
                        {
                            Status = CableTransitionStatus.Invalidated
                        };
                    }
                }
            }

            foreach (var port in runtime.Definition.Ports
                         .Where(port => port.Direction == DevicePortDirection.Output)
                         .OrderBy(port => port.Name, StringComparer.Ordinal))
            {
                var release = DeviceSignal.Create(Enumerable.Repeat(LogicValue.HighImpedance, port.Width));
                var connectedLanes = _lanes.Values.Where(lane => lane.Connected &&
                    lane.Definition.Source.DeviceId == deviceId &&
                    string.Equals(lane.Definition.Source.PortName, port.Name, StringComparison.Ordinal)).ToArray();
                var changed = !runtime.Outputs[port.Name].Equals(release) ||
                              connectedLanes.Any(lane => !lane.CurrentSignal.Equals(release));
                runtime.Outputs[port.Name] = release;
                if (changed)
                {
                    ScheduleOutputChange(deviceId, port.Name, release, tick);
                }
            }

            foreach (var lane in _lanes.Values.Where(lane => !lane.Connected &&
                         lane.Definition.Source.DeviceId == deviceId &&
                         !lane.CurrentSignal.Bits.All(bit => bit == LogicValue.HighImpedance)))
            {
                var input = FindPort(_definition.GetDevice(lane.Definition.Target.DeviceId),
                    lane.Definition.Target.PortName, DevicePortDirection.Input);
                var release = DeviceSignal.Create(Enumerable.Repeat(LogicValue.HighImpedance, input.Width));
                ScheduleLaneTransition(lane, release, tick, isRelease: true);
            }
        }
    }

    private void InvalidateStaleCableEvents(
        IEnumerable<ScheduledEvent> beforeStep,
        SchedulerTickResult result)
    {
        var deliveredKeys = result.DeliveredEvents.Select(item => item.Key).ToHashSet();
        foreach (var scheduledEvent in beforeStep.Where(item =>
                     item.Key.DueTick == result.Tick &&
                     item.EventKind is CableTransitionEvent or CableReleaseEvent &&
                     !deliveredKeys.Contains(item.Key)))
        {
            if (!_lanes.TryGetValue(scheduledEvent.TargetStableId, out var lane) ||
                !lane.HistoryByOrdinal.TryGetValue(scheduledEvent.Key.CausalOrdinal, out var index))
            {
                continue;
            }

            if (lane.History[index].Status == CableTransitionStatus.Pending)
            {
                lane.History[index] = lane.History[index] with { Status = CableTransitionStatus.Invalidated };
            }
        }
    }

    private void DeliverNodeOutput(ScheduledEvent scheduledEvent, long tick)
    {
        var runtime = FindNodeBackendRuntime(scheduledEvent.TargetStableId)
            ?? throw new InvalidOperationException("Node output event has no Node backend.");
        var portName = scheduledEvent.Key.TargetPortOrLane;
        var port = FindPort(runtime.Definition, portName, DevicePortDirection.Output);
        if (!DeviceSignal.TryParse(scheduledEvent.Payload, port.Width, out var signal))
        {
            throw new InvalidOperationException("Node output event contains an invalid signal.");
        }

        if (runtime.Outputs[portName].Equals(signal))
        {
            return;
        }

        runtime.Outputs[portName] = signal;
        ScheduleOutputChange(runtime.Definition.Id, portName, signal, tick);
    }

    private void DeliverNodeTimer(ScheduledEvent scheduledEvent)
    {
        var runtime = FindNodeBackendRuntime(scheduledEvent.TargetStableId);
        if (runtime?.NodeBinding is { IsActive: true })
        {
            runtime.DueNodeTimers.Add(new DeviceNodeTimer(
                scheduledEvent.Key.TargetPortOrLane,
                scheduledEvent.Key.DueTick));
        }
    }

    private void AcceptReplayCommandsAt(long applyAtTick)
    {
        if (_replayCommands.IsDefault)
        {
            return;
        }

        if (_nextReplayCommandIndex < _replayCommands.Length &&
            _replayCommands[_nextReplayCommandIndex].Command.ApplyAtTick < applyAtTick)
        {
            throw new InvalidOperationException("Recorded Node commands missed their replay boundary.");
        }

        while (_nextReplayCommandIndex < _replayCommands.Length &&
               _replayCommands[_nextReplayCommandIndex].Command.ApplyAtTick == applyAtTick)
        {
            var recorded = _replayCommands[_nextReplayCommandIndex];
            if (recorded.Command.Kind == SchedulerCommandKind.ScheduleEvent &&
                _scheduler.GetTarget(recorded.Command.TargetStableId).Incarnation !=
                recorded.Command.TargetIncarnation)
            {
                throw new InvalidOperationException("Recorded Node command targets a different backend incarnation.");
            }

            var acceptedOrdinal = _scheduler.Accept(recorded.Command);
            if (acceptedOrdinal != recorded.AcceptedOrdinal)
            {
                throw new InvalidOperationException("Recorded Node command ordinals do not match replay order.");
            }

            if (recorded.Command.Kind == SchedulerCommandKind.RemoveTarget)
            {
                var runtime = FindNodeBackendRuntime(recorded.Command.TargetStableId)
                    ?? throw new InvalidOperationException("Recorded Node detach has no Node backend.");
                runtime.NodeDetachApplyAtTick = applyAtTick;
                runtime.NodeDetachRequested = false;
            }

            _nextReplayCommandIndex++;
        }
    }

    private DeviceRuntime? FindNodeBackendRuntime(string targetStableId)
    {
        if (!targetStableId.StartsWith(NodeBackendTargetPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var deviceId = targetStableId[NodeBackendTargetPrefix.Length..];
        return _devices.TryGetValue(deviceId, out var runtime) &&
               runtime.Definition.Backend is NodeDeviceBackendDefinition
            ? runtime
            : null;
    }

    private static string NodeBackendTargetId(ComponentId deviceId) =>
        NodeDeviceBackendDefinition.TargetStableId(deviceId);

    private string OutputSourceTargetId(ComponentId deviceId) =>
        _definition.GetDevice(deviceId).Backend is NodeDeviceBackendDefinition
            ? NodeBackendTargetId(deviceId)
            : deviceId.Value;

    private bool IsReplayNodeBackendCommand(SchedulerCommand command)
    {
        var runtime = FindNodeBackendRuntime(command.TargetStableId);
        if (runtime is null || command.ApplyAtTick < CurrentTick)
        {
            return false;
        }

        if (command.Kind == SchedulerCommandKind.RemoveTarget)
        {
            return true;
        }

        if (command.Kind != SchedulerCommandKind.ScheduleEvent ||
            command.EventPhase != SchedulerPhase.Deliver || !HasNodeCausalDelay(command) ||
            command.TargetIncarnation <= 0 || command.SourceIncarnation != command.TargetIncarnation ||
            !string.Equals(command.SourceStableId, command.TargetStableId, StringComparison.Ordinal) ||
            !string.Equals(command.SourcePort, command.TargetPortOrLane, StringComparison.Ordinal))
        {
            return false;
        }

        if (command.EventKind == NodeOutputEvent)
        {
            var port = runtime.Definition.Ports.FirstOrDefault(candidate =>
                candidate.Direction == DevicePortDirection.Output &&
                string.Equals(candidate.Name, command.TargetPortOrLane, StringComparison.Ordinal));
            if (port is null || !DeviceSignal.TryParse(command.Payload, port.Width, out var signal))
            {
                return false;
            }

            return (port.ValueContract != DeviceValueContract.BinaryLogic ||
                    !signal.Bits.Contains(LogicValue.Unknown)) && command.Value == signal.Bits[0] &&
                   string.Equals(command.Payload, signal.ToString(), StringComparison.Ordinal);
        }

        return command.EventKind == NodeTimerEvent && ChipData.IsStableId(command.TargetPortOrLane) &&
               string.Equals(command.Payload, command.TargetPortOrLane, StringComparison.Ordinal) &&
               command.Value == LogicValue.HighImpedance;
    }

    private void ScheduleLaneTransition(LaneRuntime lane, DeviceSignal value, long tick, bool isRelease)
    {
        var dueTick = checked(tick + lane.Definition.Latency);
        var sourceStableId = OutputSourceTargetId(lane.Definition.Source.DeviceId);
        var sourceTarget = _scheduler.GetTarget(sourceStableId);
        var ordinal = _scheduler.NextCausalOrdinal;
        var eventKind = isRelease ? CableReleaseEvent : CableTransitionEvent;
        _scheduler.SeedEvent(new ScheduledEvent(
            new ScheduledEventKey(
                dueTick,
                SchedulerPhase.Deliver,
                lane.Definition.Id.Value,
                lane.Epoch,
                lane.Definition.Target.PortName,
                sourceStableId,
                lane.Definition.Source.PortName,
                eventKind,
                ordinal),
            value.Bits[0],
            value.ToString(),
            SourceIncarnation: sourceTarget.Incarnation));
        lane.AddHistory(new CableLaneHistoryEntry(
            lane.Epoch,
            ordinal,
            dueTick,
            value,
            isRelease,
            CableTransitionStatus.Pending,
            null));
    }

    private static DeviceRuntime CreateRuntime(DeviceDefinition definition, long fromTick)
    {
        var panel = definition.Backend is PanelDeviceBackendDefinition panelBackend
            ? new PanelRuntimeInstance(panelBackend.Panel, initialTick: fromTick)
            : null;
        var inputs = panel is null
            ? definition.Ports.Where(port => port.Direction == DevicePortDirection.Input)
                .ToDictionary(port => port.Name, DefaultSignal, StringComparer.Ordinal)
            : new Dictionary<string, DeviceSignal>(StringComparer.Ordinal);
        var outputs = new Dictionary<string, DeviceSignal>(StringComparer.Ordinal);
        foreach (var port in definition.Ports.Where(port => port.Direction == DevicePortDirection.Output))
        {
            outputs.Add(port.Name, panel is null
                ? DefaultSignal(port)
                : DeviceSignal.Scalar(panel.GetOutput(new PortId(port.Name))));
        }

        var timers = definition.Backend is TimedDeviceBackendDefinition timed
            ? new Queue<TimedDeviceTimer>(timed.Changes.Select(change => new TimedDeviceTimer(
                    checked(fromTick + change.DelayTicks), change.PortName, change.Value))
                .OrderBy(timer => timer.DueTick)
                .ThenBy(timer => timer.PortName, StringComparer.Ordinal))
            : new Queue<TimedDeviceTimer>();
        return new DeviceRuntime(definition, panel, inputs, outputs, timers);
    }

    private DeviceSignal ReadOutput(DevicePortEndpoint endpoint) => ReadOutput(endpoint.DeviceId, endpoint.PortName);

    private DeviceSignal ReadOutput(ComponentId deviceId, string portName)
    {
        var device = GetDevice(deviceId);
        return device.Panel is null
            ? device.Outputs[portName]
            : DeviceSignal.Scalar(device.Panel.GetOutput(new PortId(portName)));
    }

    private static DeviceSignal InitialSignal(DevicePortEndpoint endpoint, DeviceGraphDefinition definition)
    {
        var port = FindPort(definition.GetDevice(endpoint.DeviceId), endpoint.PortName, DevicePortDirection.Input);
        return DeviceSignal.Create(Enumerable.Repeat(LogicValue.HighImpedance, port.Width));
    }

    private static DeviceSignal DefaultSignal(DevicePortDefinition port) =>
        DeviceSignal.Create(Enumerable.Repeat(
            port.ValueContract == DeviceValueContract.BinaryLogic ? LogicValue.Low : LogicValue.HighImpedance,
            port.Width));

    private DeviceRuntime GetDevice(ComponentId deviceId) =>
        _devices.TryGetValue(deviceId.Value, out var runtime)
            ? runtime
            : throw new KeyNotFoundException($"Device '{deviceId}' is not in this graph.");

    private LaneRuntime GetLane(ComponentId laneId) =>
        _lanes.TryGetValue(laneId.Value, out var runtime)
            ? runtime
            : throw new KeyNotFoundException($"Cable lane '{laneId}' is not in this graph.");

    private static DevicePortDefinition FindPort(
        DeviceDefinition device,
        string portName,
        DevicePortDirection direction) =>
        device.Ports.FirstOrDefault(port => port.Direction == direction &&
                                            string.Equals(port.Name, portName, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Device {direction.ToString().ToLowerInvariant()} port '{portName}' is not defined.");

    private void ValidateOutputTicks(ComponentId deviceId, string portName, long tick)
    {
        foreach (var lane in _lanes.Values.Where(lane => lane.Connected &&
                     lane.Definition.Source.DeviceId == deviceId &&
                     string.Equals(lane.Definition.Source.PortName, portName, StringComparison.Ordinal))
                 .OrderBy(lane => lane.Definition.Id.Value, StringComparer.Ordinal))
        {
            _ = checked(tick + lane.Definition.Latency);
        }
    }

    private static void ValidatePendingLaneEvents(
        SchedulerSnapshot scheduler,
        IReadOnlyDictionary<string, LaneRuntime> lanes)
    {
        var known = lanes.Values.SelectMany(lane => lane.History.Select(entry => (lane, entry)))
            .ToDictionary(pair => pair.entry.CausalOrdinal);
        var pendingEvents = scheduler.PendingEvents.Where(item =>
                item.EventKind is CableTransitionEvent or CableReleaseEvent)
            .ToDictionary(item => item.Key.CausalOrdinal);
        var deliveredEvents = scheduler.Trace
            .SelectMany(trace => trace.DeliveredEvents.Select(scheduledEvent => (scheduledEvent, trace.Tick)))
            .Where(item => item.scheduledEvent.EventKind is CableTransitionEvent or CableReleaseEvent)
            .ToDictionary(item => item.scheduledEvent.Key.CausalOrdinal);
        foreach (var scheduledEvent in pendingEvents.Values)
        {
            if (!known.TryGetValue(scheduledEvent.Key.CausalOrdinal, out var saved) ||
                !string.Equals(saved.lane.Definition.Id.Value, scheduledEvent.TargetStableId, StringComparison.Ordinal) ||
                saved.entry.Epoch != scheduledEvent.TargetIncarnation ||
                saved.entry.ScheduledTick != scheduledEvent.Key.DueTick ||
                saved.entry.Status == CableTransitionStatus.Delivered ||
                (scheduledEvent.EventKind == CableReleaseEvent) != saved.entry.IsRelease ||
                !string.Equals(saved.entry.Signal.ToString(), scheduledEvent.Payload, StringComparison.Ordinal) ||
                scheduledEvent.TargetPortOrLane != saved.lane.Definition.Target.PortName ||
                scheduledEvent.SourcePort != saved.lane.Definition.Source.PortName)
            {
                throw new ArgumentException("Device graph snapshot pending cable events do not match lane history.", nameof(scheduler));
            }
        }

        foreach (var (scheduledEvent, deliveredTick) in deliveredEvents.Values)
        {
            if (!known.TryGetValue(scheduledEvent.Key.CausalOrdinal, out var saved) ||
                !string.Equals(saved.lane.Definition.Id.Value, scheduledEvent.TargetStableId, StringComparison.Ordinal) ||
                saved.entry.Epoch != scheduledEvent.TargetIncarnation ||
                saved.entry.ScheduledTick != scheduledEvent.Key.DueTick ||
                saved.entry.Status != CableTransitionStatus.Delivered ||
                saved.entry.DeliveredTick != deliveredTick ||
                (scheduledEvent.EventKind == CableReleaseEvent) != saved.entry.IsRelease ||
                !string.Equals(saved.entry.Signal.ToString(), scheduledEvent.Payload, StringComparison.Ordinal) ||
                scheduledEvent.TargetPortOrLane != saved.lane.Definition.Target.PortName ||
                scheduledEvent.SourcePort != saved.lane.Definition.Source.PortName)
            {
                throw new ArgumentException("Device graph snapshot delivered cable events do not match lane history.", nameof(scheduler));
            }
        }

        foreach (var (lane, entry) in known.Values)
        {
            var isPending = pendingEvents.ContainsKey(entry.CausalOrdinal);
            var isDelivered = deliveredEvents.ContainsKey(entry.CausalOrdinal);
            if (entry.Status == CableTransitionStatus.Pending && !isPending ||
                entry.Status == CableTransitionStatus.Invalidated && entry.Epoch == lane.Epoch && !isPending &&
                entry.ScheduledTick >= scheduler.CurrentTick ||
                entry.Status == CableTransitionStatus.Delivered &&
                (isPending || !isDelivered || entry.DeliveredTick is null || entry.DeliveredTick < entry.ScheduledTick) ||
                entry.Status != CableTransitionStatus.Delivered && entry.DeliveredTick is not null)
            {
                throw new ArgumentException("Device graph snapshot lane history is inconsistent.", nameof(scheduler));
            }
        }
    }

    private void EnsureNotStepping()
    {
        if (_isStepping)
        {
            throw new InvalidOperationException("The device graph cannot be changed during a simulation step.");
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ComputeStateIntegrityHash(DeviceGraphRuntimeSnapshot snapshot)
    {
        var payload = new
        {
            SchedulerTraceIntegrity = snapshot.Scheduler.TraceIntegrityHash,
            snapshot.Scheduler.TraceStartTick,
            snapshot.Devices,
            snapshot.Lanes,
            snapshot.ReplayCommands,
            snapshot.NextReplayCommandIndex
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload))).ToLowerInvariant();
    }

    private sealed class DeviceRuntime(
        DeviceDefinition definition,
        PanelRuntimeInstance? panel,
        Dictionary<string, DeviceSignal> inputs,
        Dictionary<string, DeviceSignal> outputs,
        Queue<TimedDeviceTimer> timers)
    {
        public DeviceDefinition Definition { get; } = definition;

        public PanelRuntimeInstance? Panel { get; } = panel;

        public Dictionary<string, DeviceSignal> Inputs { get; } = inputs;

        public Dictionary<string, DeviceSignal> Outputs { get; } = outputs;

        public Queue<TimedDeviceTimer> Timers { get; } = timers;

        public List<DeviceNodeInputTransition> PendingNodeInputs { get; } = [];

        public List<DeviceNodeInputTransition> NodeInputHistory { get; } = [];

        public List<DeviceNodeTimer> DueNodeTimers { get; } = [];

        public DeviceNodeBackendBinding? NodeBinding { get; set; }

        public string? NodeBehaviorFingerprint { get; set; }

        public long? NodeDetachApplyAtTick { get; set; }

        public bool NodeDetachRequested { get; set; }
    }

    private sealed record TimedDeviceTimer(long DueTick, string PortName, DeviceSignal Value);

    private sealed record PendingNodeCommand(ComponentId DeviceId, SchedulerCommand Command);

    private sealed class LaneRuntime(CableLaneDefinition definition, long epoch, DeviceSignal currentSignal)
    {
        public CableLaneDefinition Definition { get; } = definition;

        public long Epoch { get; set; } = epoch;

        public bool Connected { get; set; } = true;

        public DeviceSignal CurrentSignal { get; set; } = currentSignal;

        public List<CableLaneHistoryEntry> History { get; } = [];

        public Dictionary<long, int> HistoryByOrdinal { get; } = [];

        public void AddHistory(CableLaneHistoryEntry entry)
        {
            HistoryByOrdinal.Add(entry.CausalOrdinal, History.Count);
            History.Add(entry);
        }
    }
}
