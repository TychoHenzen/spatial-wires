using System.Collections.Immutable;
using System.Threading;
using Godot;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.GodotAdapter;

public sealed class SpatialCircuitNodeBinding : IDisposable
{
    private readonly SpatialCircuitNode _node;
    private readonly DeviceNodeBackendBinding _backend;
    private readonly IReadOnlyDictionary<string, DevicePortDefinition> _outputPorts;
    private readonly int _mainThreadId;
    private int _disposed;

    public SpatialCircuitNodeBinding(
        DeviceGraphInstance graph,
        ComponentId deviceId,
        SpatialCircuitNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        if (!node.IsInsideTree())
        {
            throw new InvalidOperationException("SpatialCircuitNode must be inside the scene tree before binding.");
        }

        _node = node;
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        var definition = graph.Definition.Devices.Single(device => device.Id == deviceId);
        _outputPorts = definition.Ports
            .Where(port => port.Direction == DevicePortDirection.Output)
            .ToDictionary(port => port.Name, StringComparer.Ordinal);
        _backend = new DeviceNodeBackendBinding(OnDeviceStep);

        graph.AttachNodeBackend(deviceId, _backend);
        node.TreeExiting += OnTreeExiting;
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

        _backend.Invalidate();
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
            (tick, timerId) => RequestTimer(context, tick, timerId));
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

    private void OnTreeExiting() => _backend.Invalidate();

    private bool IsMainThread() => System.Environment.CurrentManagedThreadId == _mainThreadId;

    private void EnsureMainThread()
    {
        if (!IsMainThread())
        {
            throw new InvalidOperationException("SpatialCircuitNode callbacks must run on their bound main thread.");
        }
    }
}
