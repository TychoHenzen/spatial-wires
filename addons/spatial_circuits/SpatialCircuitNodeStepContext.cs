using System.Collections.Immutable;

namespace SpatialCircuits.GodotAdapter;

public sealed record SpatialCircuitNodeInputTransition(long Tick, string PortName, string Signal);

public sealed record SpatialCircuitNodeTimer(string TimerId, long DueTick);

public sealed record SpatialCircuitNodePresentationEvent(long Tick, string EventId);

public sealed class SpatialCircuitNodeStepContext
{
    internal SpatialCircuitNodeStepContext(
        long tick,
        ImmutableArray<SpatialCircuitNodeInputTransition> committedInputs,
        ImmutableArray<SpatialCircuitNodeTimer> dueTimers,
        SpatialCircuitNodeOutputCapability outputs)
    {
        Tick = tick;
        CommittedInputs = committedInputs;
        DueTimers = dueTimers;
        Outputs = outputs;
    }

    public long Tick { get; }

    public ImmutableArray<SpatialCircuitNodeInputTransition> CommittedInputs { get; }

    public ImmutableArray<SpatialCircuitNodeTimer> DueTimers { get; }

    public SpatialCircuitNodeOutputCapability Outputs { get; }
}

public sealed class SpatialCircuitNodeOutputCapability
{
    private Func<long, string, string, bool>? _requestOutput;
    private Func<long, string, bool>? _scheduleTimer;
    private Func<long, string, bool>? _requestPresentation;
    private int _active = 1;

    internal SpatialCircuitNodeOutputCapability(
        Func<long, string, string, bool> requestOutput,
        Func<long, string, bool> scheduleTimer,
        Func<long, string, bool>? requestPresentation = null)
    {
        ArgumentNullException.ThrowIfNull(requestOutput);
        ArgumentNullException.ThrowIfNull(scheduleTimer);
        _requestOutput = requestOutput;
        _scheduleTimer = scheduleTimer;
        _requestPresentation = requestPresentation;
    }

    public bool RequestOutput(long targetTick, string portName, string signal)
    {
        if (Volatile.Read(ref _active) == 0)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(signal);
        return Volatile.Read(ref _requestOutput)?.Invoke(targetTick, portName, signal) ?? false;
    }

    public bool ScheduleTimer(long dueTick, string timerId)
    {
        if (Volatile.Read(ref _active) == 0)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        return Volatile.Read(ref _scheduleTimer)?.Invoke(dueTick, timerId) ?? false;
    }

    public bool RequestPresentation(long targetTick, string eventId)
    {
        if (Volatile.Read(ref _active) == 0)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        return Volatile.Read(ref _requestPresentation)?.Invoke(targetTick, eventId) ?? false;
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
