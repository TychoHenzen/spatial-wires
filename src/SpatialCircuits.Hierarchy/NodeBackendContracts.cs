using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SpatialCircuits.Core;

namespace SpatialCircuits.Hierarchy;

public static class NodeCellDiagnosticCodes
{
    public const string BindingInvalid = "node-cell.binding-invalid";
    public const string BindingMissing = "node-cell.binding-missing";
    public const string BindingOwnerMismatch = "node-cell.binding-owner-mismatch";
    public const string BindingVersionMismatch = "node-cell.binding-version-mismatch";
    public const string WorkerRequiresMainThread = "node-cell.worker-main-thread-required";
}

public sealed record NodeCellBindingDefinition(
    CircuitId OwnerPanelId,
    string BindingId,
    int Version,
    DeviceDefinition Device)
{
    public ImmutableArray<DevicePortDefinition> Ports => Device.Ports;

    public static NodeCellBindingDefinition Create(
        CircuitId ownerPanelId,
        string bindingId,
        int version,
        ComponentId deviceId,
        IEnumerable<DevicePortDefinition> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        if (!ChipData.IsStableId(ownerPanelId.Value) || !ChipData.IsStableId(bindingId) || version <= 0)
        {
            throw new ArgumentException("Node cell binding identity is invalid.");
        }

        var backend = NodeDeviceBackendDefinition.Create(ports, bindingId, version);
        return new NodeCellBindingDefinition(
            ownerPanelId,
            bindingId,
            version,
            DeviceDefinition.Create(deviceId, backend));
    }
}

public sealed record NodeCellBindingPlaceholder(
    string BindingId,
    int Version,
    string DiagnosticCode);

public sealed record NodeCellBindingResolution(
    NodeCellBindingDefinition? Definition,
    NodeCellBindingPlaceholder? Placeholder)
{
    public bool IsPlaceholder => Placeholder is not null;
}

public sealed class NodeCellBindingRegistry
{
    private readonly Dictionary<(string BindingId, int Version), NodeCellBindingDefinition> _bindings = [];

    public void Register(NodeCellBindingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_bindings.TryAdd((definition.BindingId, definition.Version), definition))
        {
            throw new InvalidOperationException(
                $"Node cell binding '{definition.BindingId}' version {definition.Version} is already registered.");
        }
    }

    public NodeCellBindingResolution Resolve(
        CircuitId ownerPanelId,
        string bindingId,
        int version)
    {
        if (!ChipData.IsStableId(ownerPanelId.Value) || !ChipData.IsStableId(bindingId) || version <= 0)
        {
            return Missing(bindingId, version, NodeCellDiagnosticCodes.BindingInvalid);
        }

        if (!_bindings.TryGetValue((bindingId, version), out var definition))
        {
            var code = _bindings.Keys.Any(key => key.BindingId == bindingId)
                ? NodeCellDiagnosticCodes.BindingVersionMismatch
                : NodeCellDiagnosticCodes.BindingMissing;
            return Missing(bindingId, version, code);
        }

        return definition.OwnerPanelId == ownerPanelId
            ? new NodeCellBindingResolution(definition, null)
            : Missing(bindingId, version, NodeCellDiagnosticCodes.BindingOwnerMismatch);
    }

    private static NodeCellBindingResolution Missing(string bindingId, int version, string code) =>
        new(null, new NodeCellBindingPlaceholder(bindingId, version, code));
}

public sealed record DeviceNodeInputTransition(long Tick, string PortName, DeviceSignal Signal);

public sealed record DeviceNodeTimer(string TimerId, long DueTick);

public sealed record DeviceNodePresentationEvent(long Tick, string EventId);

public sealed record DeviceNodeBackendFailure(ComponentId DeviceId, string ExceptionType, string Message);

public sealed record DeviceNodeBackendAssemblySource(
    ComponentId DeviceId,
    System.Reflection.Assembly? Assembly,
    string? Sha256);

public sealed class DeviceNodeStepContext
{
    internal DeviceNodeStepContext(
        long tick,
        ImmutableArray<DeviceNodeInputTransition> committedInputs,
        ImmutableArray<DeviceNodeTimer> dueTimers,
        DeviceNodeOutputCapability outputs)
    {
        Tick = tick;
        CommittedInputs = committedInputs;
        DueTimers = dueTimers;
        Outputs = outputs;
    }

    public long Tick { get; }

    public ImmutableArray<DeviceNodeInputTransition> CommittedInputs { get; }

    public ImmutableArray<DeviceNodeTimer> DueTimers { get; }

    public DeviceNodeOutputCapability Outputs { get; }
}

public sealed class DeviceNodeOutputCapability
{
    private Func<long, string, DeviceSignal, bool>? _requestOutput;
    private Func<long, string, bool>? _scheduleTimer;
    private Func<long, string, bool>? _requestPresentation;
    private int _active = 1;

    internal DeviceNodeOutputCapability(
        Func<long, string, DeviceSignal, bool> requestOutput,
        Func<long, string, bool> scheduleTimer,
        Func<long, string, bool>? requestPresentation = null)
    {
        _requestOutput = requestOutput;
        _scheduleTimer = scheduleTimer;
        _requestPresentation = requestPresentation;
    }

    public bool RequestOutput(long targetTick, string portName, DeviceSignal signal)
    {
        var request = Volatile.Read(ref _requestOutput);
        return Volatile.Read(ref _active) != 0 && request is not null &&
               request(targetTick, portName, signal);
    }

    public bool ScheduleTimer(long dueTick, string timerId)
    {
        var request = Volatile.Read(ref _scheduleTimer);
        return Volatile.Read(ref _active) != 0 && request is not null && request(dueTick, timerId);
    }

    public bool RequestPresentation(long targetTick, string eventId)
    {
        var request = Volatile.Read(ref _requestPresentation);
        return Volatile.Read(ref _active) != 0 && request is not null &&
               request(targetTick, eventId);
    }

    internal void Invalidate()
    {
        if (Interlocked.Exchange(ref _active, 0) == 0)
        {
            return;
        }

        Interlocked.Exchange(ref _requestOutput, null);
        Interlocked.Exchange(ref _scheduleTimer, null);
        Interlocked.Exchange(ref _requestPresentation, null);
    }
}

public sealed class DeviceNodeBackendBinding
{
    private Action<DeviceNodeStepContext>? _onStep;
    private readonly System.Reflection.Assembly _behaviorAssembly;
    private int _active = 1;

    public DeviceNodeBackendBinding(Action<DeviceNodeStepContext> onStep)
    {
        ArgumentNullException.ThrowIfNull(onStep);
        _onStep = onStep;
        _behaviorAssembly = onStep.Method.Module.Assembly;
        BehaviorAssemblyFingerprint = Fingerprint(_behaviorAssembly);
    }

    public bool IsActive => Volatile.Read(ref _active) != 0;

    public System.Reflection.Assembly BehaviorAssembly => _behaviorAssembly;

    public string BehaviorAssemblyFingerprint { get; }

    public void Invalidate()
    {
        if (Interlocked.Exchange(ref _active, 0) != 0)
        {
            Interlocked.Exchange(ref _onStep, null);
        }
    }

    internal void Invoke(DeviceNodeStepContext context)
    {
        if (!IsActive)
        {
            return;
        }

        Volatile.Read(ref _onStep)?.Invoke(context);
    }

    private static string Fingerprint(System.Reflection.Assembly assembly)
    {
        var path = assembly.Location;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        var identity = string.Join(
            "\0",
            assembly.FullName ?? string.Empty,
            assembly.ManifestModule.ModuleVersionId.ToString("D"),
            assembly.GetName().Version?.ToString() ?? "0.0.0.0");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
}
