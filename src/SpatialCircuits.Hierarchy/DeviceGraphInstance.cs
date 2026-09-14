using System.Collections.Immutable;
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

public sealed record DeviceGraphTickResult(long Tick, ImmutableArray<DeviceCableDelivery> CableDeliveries);

public sealed record DeviceRuntimeSnapshot(
    ComponentId DeviceId,
    PanelRuntimeSnapshot? Panel,
    ImmutableArray<KeyValuePair<string, DeviceSignal>> Inputs,
    ImmutableArray<KeyValuePair<string, DeviceSignal>> Outputs,
    ImmutableArray<DeviceTimerSnapshot> Timers);

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
    ImmutableArray<CableLaneRuntimeSnapshot> Lanes);

/// <summary>Owns device instances and advances their cable transport on integer microticks.</summary>
public sealed class DeviceGraphInstance
{
    private const string CableTransitionEvent = "device-cable-transition";
    private const string CableReleaseEvent = "device-cable-transition-release";

    private DeviceGraphDefinition _definition;
    private DeterministicScheduler _scheduler = new();
    private Dictionary<string, DeviceRuntime> _devices = new(StringComparer.Ordinal);
    private Dictionary<string, LaneRuntime> _lanes = new(StringComparer.Ordinal);
    private bool _isStepping;

    public DeviceGraphInstance(DeviceGraphDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;

        foreach (var device in definition.Devices)
        {
            _scheduler.RegisterTarget(device.Id.Value);
            _devices.Add(device.Id.Value, CreateRuntime(device, CurrentTick));
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
            device.Inputs[portName] = signal;
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
        _scheduler.RemoveTarget(laneId.Value);
        lane.Epoch = _scheduler.RegisterTarget(laneId.Value).Incarnation;
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
        var replacementDefinition = _definition.WithBackend(deviceId, backend);
        var replacement = CreateRuntime(replacementDefinition.GetDevice(deviceId), CurrentTick);

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
            var schedulerResult = _scheduler.Step();
            var deliveries = ImmutableArray.CreateBuilder<DeviceCableDelivery>();
            foreach (var scheduledEvent in schedulerResult.DeliveredEvents)
            {
                if (scheduledEvent.EventKind is CableTransitionEvent or CableReleaseEvent)
                {
                    DeliverCableEvent(scheduledEvent, schedulerResult.Tick, deliveries);
                }
            }

            ApplyDueTimers(schedulerResult.Tick);
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
                    ScheduleOutputChange(runtime.Definition.Id, port.Name, value, schedulerResult.Tick);
                }
            }

            return new DeviceGraphTickResult(schedulerResult.Tick, deliveries.ToImmutable());
        }
        finally
        {
            _isStepping = false;
        }
    }

    public DeviceGraphRuntimeSnapshot CaptureSnapshot()
    {
        EnsureNotStepping();
        return new DeviceGraphRuntimeSnapshot(
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
                        timer.DueTick, timer.PortName, timer.Value)).ToImmutableArray()))
                .ToImmutableArray(),
            _lanes.Values
                .OrderBy(lane => lane.Definition.Id.Value, StringComparer.Ordinal)
                .Select(lane => new CableLaneRuntimeSnapshot(
                    lane.Definition.Id,
                    lane.Connected,
                    lane.Epoch,
                    lane.CurrentSignal,
                    lane.History.ToImmutableArray()))
                .ToImmutableArray());
    }

    public void RestoreSnapshot(DeviceGraphRuntimeSnapshot snapshot)
    {
        EnsureNotStepping();
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Definition);
        ArgumentNullException.ThrowIfNull(snapshot.Scheduler);
        if (snapshot.Definition.Id != _definition.Id || snapshot.Devices.IsDefault || snapshot.Lanes.IsDefault)
        {
            throw new ArgumentException("Device graph snapshot does not match this graph.", nameof(snapshot));
        }

        var scheduler = new DeterministicScheduler();
        scheduler.RestoreSnapshot(snapshot.Scheduler);
        ValidateSnapshotTargets(snapshot);
        var devices = RestoreDeviceRuntimes(snapshot);
        var lanes = RestoreLaneRuntimes(snapshot, scheduler);
        ValidatePendingLaneEvents(snapshot.Scheduler, lanes);
        _definition = snapshot.Definition;
        _scheduler = scheduler;
        _devices = devices;
        _lanes = lanes;
    }

    private static void ValidateSnapshotTargets(DeviceGraphRuntimeSnapshot snapshot)
    {
        var expectedTargets = snapshot.Definition.Devices.Select(device => device.Id.Value)
            .Concat(snapshot.Definition.Lanes.Select(lane => lane.Id.Value))
            .ToHashSet(StringComparer.Ordinal);
        var targets = snapshot.Scheduler.Targets.ToDictionary(target => target.StableId, StringComparer.Ordinal);
        if (!targets.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedTargets) ||
            targets.Values.Any(target => !target.Active))
        {
            throw new ArgumentException("Device graph snapshot targets do not match its definitions.", nameof(snapshot));
        }

    }

    private static Dictionary<string, DeviceRuntime> RestoreDeviceRuntimes(DeviceGraphRuntimeSnapshot snapshot)
    {
        var deviceSnapshots = snapshot.Devices.ToDictionary(item => item.DeviceId);
        if (deviceSnapshots.Count != snapshot.Definition.Devices.Length ||
            !deviceSnapshots.Keys.ToHashSet().SetEquals(snapshot.Definition.Devices.Select(device => device.Id)))
        {
            throw new ArgumentException("Device graph snapshot device states do not match its definitions.", nameof(snapshot));
        }

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

    private static DeviceRuntime RestoreDeviceRuntime(
        DeviceDefinition definition,
        DeviceRuntimeSnapshot snapshot,
        long currentTick)
    {
        var runtime = CreateRuntime(definition, 0);
        ValidateDeviceSnapshotShape(definition, snapshot, runtime);
        if (snapshot.Panel is not null)
        {
            runtime.Panel!.RestoreSnapshot(snapshot.Panel);
        }

        RestoreDeviceSignals(definition, snapshot, runtime);
        RestoreDeviceTimers(definition, snapshot, runtime, currentTick);
        return runtime;
    }

    private static void ValidateDeviceSnapshotShape(
        DeviceDefinition definition,
        DeviceRuntimeSnapshot snapshot,
        DeviceRuntime runtime)
    {
        if (snapshot.Inputs.IsDefault || snapshot.Outputs.IsDefault || snapshot.Timers.IsDefault ||
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

    private void ScheduleLaneTransition(LaneRuntime lane, DeviceSignal value, long tick, bool isRelease)
    {
        var dueTick = checked(tick + lane.Definition.Latency);
        var sourceTarget = _scheduler.GetTarget(lane.Definition.Source.DeviceId.Value);
        var ordinal = _scheduler.NextCausalOrdinal;
        var eventKind = isRelease ? CableReleaseEvent : CableTransitionEvent;
        _scheduler.SeedEvent(new ScheduledEvent(
            new ScheduledEventKey(
                dueTick,
                SchedulerPhase.Deliver,
                lane.Definition.Id.Value,
                lane.Epoch,
                lane.Definition.Target.PortName,
                lane.Definition.Source.DeviceId.Value,
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
            ? new PanelRuntimeInstance(panelBackend.Panel)
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
        foreach (var scheduledEvent in pendingEvents.Values)
        {
            if (!known.TryGetValue(scheduledEvent.Key.CausalOrdinal, out var saved) ||
                !string.Equals(saved.lane.Definition.Id.Value, scheduledEvent.TargetStableId, StringComparison.Ordinal) ||
                saved.entry.Epoch != scheduledEvent.TargetIncarnation ||
                saved.entry.ScheduledTick != scheduledEvent.Key.DueTick ||
                saved.entry.Status == CableTransitionStatus.Delivered ||
                (scheduledEvent.EventKind == CableReleaseEvent) != saved.entry.IsRelease ||
                !string.Equals(saved.entry.Signal.ToString(), scheduledEvent.Payload, StringComparison.Ordinal))
            {
                throw new ArgumentException("Device graph snapshot pending cable events do not match lane history.", nameof(scheduler));
            }
        }

        foreach (var (lane, entry) in known.Values)
        {
            var isPending = pendingEvents.ContainsKey(entry.CausalOrdinal);
            if (entry.Status == CableTransitionStatus.Pending && !isPending ||
                entry.Status == CableTransitionStatus.Invalidated && entry.Epoch == lane.Epoch && !isPending ||
                entry.Status == CableTransitionStatus.Delivered &&
                (isPending || entry.DeliveredTick is null || entry.DeliveredTick < entry.ScheduledTick) ||
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
    }

    private sealed record TimedDeviceTimer(long DueTick, string PortName, DeviceSignal Value);

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
