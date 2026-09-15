using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Threading;
using Godot;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.GodotAdapter;

public sealed record SpatialCircuitNodeBindingSnapshot(
    ComponentId DeviceId,
    string BindingId,
    int BindingVersion,
    CircuitId OwnerPanelId,
    ImmutableArray<SpatialCircuitNodePresentationEvent> PresentationEvents);

public sealed class SpatialCircuitNodeBinding : IDisposable
{
    private readonly DeviceGraphInstance _graph;
    private readonly SpatialCircuitNode _node;
    private readonly DeviceNodeBackendBinding _backend;
    private readonly IReadOnlyDictionary<string, DevicePortDefinition> _outputPorts;
    private readonly int _mainThreadId;
    private readonly ComponentId _deviceId;
    private int _disposed;

    public SpatialCircuitNodeBinding(
        DeviceGraphInstance graph,
        ComponentId deviceId,
        SpatialCircuitNode node)
        : this(graph, deviceId, node, null, null, null)
    {
    }

    public SpatialCircuitNodeBinding(
        PanelWorkbenchSession session,
        NodeCellBindingDefinition binding,
        SpatialCircuitNode node)
        : this(
            ResolveGraph(session, binding),
            binding.Device.Id,
            node,
            binding.BindingId,
            binding.Version,
            binding.OwnerPanelId)
    {
    }

    private SpatialCircuitNodeBinding(
        DeviceGraphInstance graph,
        ComponentId deviceId,
        SpatialCircuitNode node,
        string? bindingId,
        int? bindingVersion,
        CircuitId? ownerPanelId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        if (!node.IsInsideTree())
        {
            throw new InvalidOperationException("SpatialCircuitNode must be inside the scene tree before binding.");
        }

        _graph = graph;
        _node = node;
        _deviceId = deviceId;
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        var definition = graph.Definition.Devices.Single(device => device.Id == deviceId);
        var nodeBackend = definition.Backend as NodeDeviceBackendDefinition
            ?? throw new ArgumentException("The selected device is not Node-backed.", nameof(deviceId));
        BindingId = bindingId ?? nodeBackend.BindingId ?? string.Empty;
        BindingVersion = bindingVersion ?? nodeBackend.BindingVersion;
        OwnerPanelId = ownerPanelId ?? new CircuitId(string.Empty);
        if (bindingId is not null &&
            (!string.Equals(nodeBackend.BindingId, bindingId, StringComparison.Ordinal) ||
             nodeBackend.BindingVersion != bindingVersion))
        {
            throw new InvalidOperationException(
                $"{NodeCellDiagnosticCodes.BindingMissing}: binding '{bindingId}' does not match the device definition.");
        }

        _outputPorts = definition.Ports
            .Where(port => port.Direction == DevicePortDirection.Output)
            .ToDictionary(port => port.Name, StringComparer.Ordinal);
        _backend = new DeviceNodeBackendBinding(OnDeviceStep);

        graph.AttachNodeBackend(deviceId, _backend);
        node.TreeExiting += OnTreeExiting;
    }

    public string BindingId { get; }

    public int BindingVersion { get; }

    public CircuitId OwnerPanelId { get; }

    public ImmutableArray<SpatialCircuitNodePresentationEvent> PresentationEvents =>
        _graph.GetNodePresentationEvents(_deviceId)
            .Select(item => new SpatialCircuitNodePresentationEvent(item.Tick, item.EventId))
            .ToImmutableArray();

    public SpatialCircuitNodeBindingSnapshot CaptureSnapshot() => new(
        _deviceId,
        BindingId,
        BindingVersion,
        OwnerPanelId,
        PresentationEvents);

    public void RestoreSnapshot(SpatialCircuitNodeBindingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.DeviceId != _deviceId ||
            !string.Equals(snapshot.BindingId, BindingId, StringComparison.Ordinal) ||
            snapshot.BindingVersion != BindingVersion || snapshot.OwnerPanelId != OwnerPanelId ||
            snapshot.PresentationEvents.IsDefault ||
            snapshot.PresentationEvents.Any(item => item.Tick < 0 || !IsStableEventId(item.EventId)))
        {
            throw new ArgumentException("Node cell binding snapshot does not match the active binding.", nameof(snapshot));
        }

        _graph.RestoreNodePresentationEvents(
            _deviceId,
            snapshot.PresentationEvents.Select(item => new DeviceNodePresentationEvent(
                item.Tick,
                item.EventId)).ToImmutableArray());
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        EnsureMainThread();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _graph.InvalidateNodeBackend(_deviceId, _backend);
        if (GodotObject.IsInstanceValid(_node))
        {
            _node.TreeExiting -= OnTreeExiting;
        }
    }

    private void OnDeviceStep(DeviceNodeStepContext context)
    {
        EnsureMainThread();
        if (!GodotObject.IsInstanceValid(_node))
        {
            _backend.Invalidate();
            return;
        }

        var outputs = new SpatialCircuitNodeOutputCapability(
            (tick, portName, signal) => RequestOutput(context, tick, portName, signal),
            (tick, timerId) => RequestTimer(context, tick, timerId),
            (tick, eventId) => RequestPresentation(context, tick, eventId));
        var nodeContext = new SpatialCircuitNodeStepContext(
            context.Tick,
            context.CommittedInputs.Select(input => new SpatialCircuitNodeInputTransition(
                input.Tick,
                input.PortName,
                input.Signal.ToString())).ToImmutableArray(),
            context.DueTimers.Select(timer => new SpatialCircuitNodeTimer(
                timer.TimerId,
                timer.DueTick)).ToImmutableArray(),
            outputs);

        try
        {
            _node.DispatchDeviceStep(nodeContext);
        }
        finally
        {
            outputs.Invalidate();
        }
    }

    private bool RequestOutput(
        DeviceNodeStepContext context,
        long targetTick,
        string portName,
        string signal)
    {
        if (!IsMainThread() || !_backend.IsActive || targetTick <= context.Tick ||
            !_outputPorts.TryGetValue(portName, out var port) ||
            !DeviceSignal.TryParse(signal, port.Width, out var value))
        {
            return false;
        }

        return context.Outputs.RequestOutput(targetTick, portName, value);
    }

    private bool RequestTimer(DeviceNodeStepContext context, long dueTick, string timerId)
    {
        return IsMainThread() && _backend.IsActive && dueTick > context.Tick &&
               context.Outputs.ScheduleTimer(dueTick, timerId);
    }

    private bool RequestPresentation(DeviceNodeStepContext context, long targetTick, string eventId)
    {
        if (!IsMainThread() || !_backend.IsActive || targetTick <= context.Tick || !IsStableEventId(eventId))
        {
            return false;
        }

        if (!context.Outputs.RequestPresentation(targetTick, eventId))
        {
            return false;
        }

        return true;
    }

    private void OnTreeExiting() => _graph.InvalidateNodeBackend(_deviceId, _backend);

    private bool IsMainThread() => System.Environment.CurrentManagedThreadId == _mainThreadId;

    private void EnsureMainThread()
    {
        if (!IsMainThread())
        {
            throw new InvalidOperationException("SpatialCircuitNode callbacks must run on their bound main thread.");
        }
    }

    private static DeviceGraphInstance ResolveGraph(
        PanelWorkbenchSession session,
        NodeCellBindingDefinition binding)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(binding);
        if (session.CommittedDefinition.Panel.Id != binding.OwnerPanelId)
        {
            throw new InvalidOperationException(
                $"{NodeCellDiagnosticCodes.BindingOwnerMismatch}: binding '{binding.BindingId}' belongs to another panel.");
        }

        var graph = session.DeviceGraph ?? throw new InvalidOperationException(
            $"{NodeCellDiagnosticCodes.BindingMissing}: the owning panel has no device graph.");
        var device = graph.Definition.Devices.FirstOrDefault(item => item.Id == binding.Device.Id);
        if (device is null || device.Backend is not NodeDeviceBackendDefinition backend ||
            !string.Equals(backend.BindingId, binding.BindingId, StringComparison.Ordinal) ||
            backend.BindingVersion != binding.Version)
        {
            throw new InvalidOperationException(
                $"{NodeCellDiagnosticCodes.BindingMissing}: binding '{binding.BindingId}' is not in the owning panel.");
        }

        return graph;
    }

    private static bool IsStableEventId(string? value) =>
        value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
}
