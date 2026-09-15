using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SpatialCircuits.Core;

namespace SpatialCircuits.Hierarchy;

public sealed record DeviceNodeInputTransition(long Tick, string PortName, DeviceSignal Signal);

public sealed record DeviceNodeTimer(string TimerId, long DueTick);

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
    private int _active = 1;

    internal DeviceNodeOutputCapability(
        Func<long, string, DeviceSignal, bool> requestOutput,
        Func<long, string, bool> scheduleTimer)
    {
        _requestOutput = requestOutput;
        _scheduleTimer = scheduleTimer;
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

    internal void Invalidate()
    {
        if (Interlocked.Exchange(ref _active, 0) == 0)
        {
            return;
        }

        Interlocked.Exchange(ref _requestOutput, null);
        Interlocked.Exchange(ref _scheduleTimer, null);
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
