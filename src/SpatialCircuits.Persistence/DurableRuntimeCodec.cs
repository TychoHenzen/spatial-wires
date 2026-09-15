using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Persistence;

internal static class DurableRuntimeCodec
{
    public static DurableSessionState ToDto(PanelWorkbenchSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new DurableSessionState(
            snapshot.SaveId,
            snapshot.CurrentTick,
            snapshot.CycleTicks,
            snapshot.IsPaused,
            snapshot.NextOrdinal,
            DurableDefinitionCodec.ToDto(snapshot.CommittedDefinition),
            ToDto(snapshot.ChipNetwork),
            snapshot.DeviceGraph is null ? null : ToDto(snapshot.DeviceGraph),
            snapshot.RunningMutations.Select(ToDto).ToImmutableArray(),
            snapshot.StagedMutations.Select(ToDto).ToImmutableArray(),
            snapshot.CommandLog,
            snapshot.InputValues.Select(pair => new DurableInputValue(pair.Key.Value, pair.Value.ToString()))
                .OrderBy(pair => pair.PortId, StringComparer.Ordinal)
                .ToImmutableArray());
    }

    public static PanelWorkbenchSession Restore(DurableSessionState dto, CustomCellRuleRegistry? customCellRules)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.SaveId == Guid.Empty || dto.CurrentTick < 0 || dto.NextOrdinal < 0 || dto.CycleTicks <= 0 ||
            dto.RunningMutations.IsDefault || dto.StagedMutations.IsDefault || dto.CommandLog.IsDefault ||
            dto.InputValues.IsDefault)
        {
            throw new InvalidDataException("Durable session DTO is incomplete.");
        }

        var definition = DurableDefinitionCodec.FromDto(dto.CommittedDefinition);
        var deviceGraph = dto.DeviceGraph is null
            ? null
            : ToDomain(dto.DeviceGraph, definition.DeviceGraph
                ?? throw new InvalidDataException("Device graph runtime has no matching definition."));
        var snapshot = new PanelWorkbenchSessionSnapshot(
            dto.SaveId,
            dto.CurrentTick,
            definition,
            FromDto(dto.ChipNetwork),
            deviceGraph,
            dto.RunningMutations.Select(FromDto).ToImmutableArray(),
            dto.StagedMutations.Select(FromDto).ToImmutableArray(),
            dto.CommandLog,
            dto.InputValues.Select(pair => new KeyValuePair<PortId, LogicValue>(
                new PortId(pair.PortId),
                ParseLogicValue(pair.Value))).ToImmutableArray(),
            dto.NextOrdinal,
            dto.CycleTicks,
            dto.IsPaused);
        return PanelWorkbenchSession.RestoreSnapshot(snapshot, customCellRules);
    }

    private static DurablePendingMutation ToDto(PendingWorkbenchMutationSnapshot mutation) => new(
        mutation.Action,
        mutation.Location,
        DurableDefinitionCodec.ToDto(mutation.Definition),
        mutation.InputPort?.Value,
        mutation.InputValue?.ToString(),
        mutation.AcceptedOrdinal,
        mutation.ApplyAtTick,
        mutation.DeviceInput is { } device
            ? new DurableDeviceInputDrive(device.DeviceId, device.PortName, device.Signal.ToString())
            : null,
        mutation.ChipInput is { } chip
            ? new DurableChipInputDrive(chip.InstanceId, chip.PortName, chip.Value.ToString())
            : null);

    private static PendingWorkbenchMutationSnapshot FromDto(DurablePendingMutation mutation) => new(
        mutation.Action,
        mutation.Location,
        DurableDefinitionCodec.FromDto(mutation.Definition),
        mutation.InputPort is null ? null : new PortId(mutation.InputPort),
        mutation.InputValue is null ? null : ParseLogicValue(mutation.InputValue),
        mutation.AcceptedOrdinal,
        mutation.ApplyAtTick,
        mutation.DeviceInput is { } device
            ? new WorkbenchDeviceInputDrive(device.DeviceId, device.PortName, ParseSignal(device.Signal))
            : null,
        mutation.ChipInput is { } chip
            ? new WorkbenchChipInputDrive(chip.InstanceId, chip.PortName, ParseLogicValue(chip.Value))
            : null);

    private static DurablePanelOwnedChipNetworkRuntimeSnapshot ToDto(
        PanelOwnedChipNetworkRuntimeSnapshot snapshot) => new(
        snapshot.OwnerPanelId.Value,
        ToDto(snapshot.OwnerPanel),
        snapshot.Chips.Select(ToDto).ToImmutableArray());

    private static PanelOwnedChipNetworkRuntimeSnapshot FromDto(
        DurablePanelOwnedChipNetworkRuntimeSnapshot snapshot) => new(
        new CircuitId(snapshot.OwnerPanelId),
        FromDto(snapshot.OwnerPanel),
        snapshot.Chips.Select(FromDto).ToImmutableArray());

    private static DurableChipInstanceRuntimeSnapshot ToDto(ChipInstanceRuntimeSnapshot snapshot) => new(
        snapshot.InstanceId.Value,
        snapshot.DefinitionId.Value,
        snapshot.ContentHash,
        ToDto(snapshot.Panel),
        snapshot.Children.Select(ToDto).ToImmutableArray());

    private static ChipInstanceRuntimeSnapshot FromDto(DurableChipInstanceRuntimeSnapshot snapshot) => new(
        new ComponentId(snapshot.InstanceId),
        new DefinitionId(snapshot.DefinitionId),
        snapshot.ContentHash,
        FromDto(snapshot.Panel),
        snapshot.Children.Select(FromDto).ToImmutableArray());

    private static DurablePanelRuntimeSnapshot ToDto(PanelRuntimeSnapshot snapshot) => new(
        snapshot.PanelId.Value,
        snapshot.Width,
        snapshot.Height,
        snapshot.Cells.Select(cell => cell is null ? null : ToDto(cell)).ToImmutableArray(),
        ToDto(snapshot.Scheduler),
        snapshot.ProbeHistory.Select(sample => new DurableProbeSample(
            sample.ProbeId.Value,
            sample.Tick,
            sample.Value.ToString())).ToImmutableArray())
    {
        TargetIncarnations = snapshot.TargetIncarnations,
        TargetIncarnationTimeline = snapshot.TargetIncarnationTimeline.Select(entry =>
            new DurablePanelTargetIncarnationSnapshot(
                entry.StableId,
                entry.Incarnation,
                entry.Active,
                entry.DefinitionHash)).ToImmutableArray(),
        StateIntegrityHash = snapshot.StateIntegrityHash
    };

    private static PanelRuntimeSnapshot FromDto(DurablePanelRuntimeSnapshot snapshot)
    {
        if (snapshot.Cells.IsDefault || snapshot.ProbeHistory.IsDefault)
        {
            throw new InvalidDataException("Panel runtime snapshot is missing cell or probe state.");
        }

        return new PanelRuntimeSnapshot(
            new CircuitId(snapshot.PanelId),
            snapshot.Width,
            snapshot.Height,
            snapshot.Cells.Select(cell => cell is null ? null : FromDto(cell)).ToImmutableArray(),
            FromDto(snapshot.Scheduler),
            snapshot.ProbeHistory.Select(sample => new ProbeSample(
                new ComponentId(sample.ProbeId),
                sample.Tick,
                ParseLogicValue(sample.Value))).ToImmutableArray())
        {
            TargetIncarnations = snapshot.TargetIncarnations,
            TargetIncarnationTimeline = snapshot.TargetIncarnationTimeline.Select(entry =>
                new PanelTargetIncarnationSnapshot(
                    entry.StableId,
                    entry.Incarnation,
                    entry.Active,
                    entry.DefinitionHash)).ToImmutableArray(),
            StateIntegrityHash = snapshot.StateIntegrityHash
        };
    }

    private static DurablePanelRuntimeCellSnapshot ToDto(PanelRuntimeCellSnapshot cell) => new(
        cell.CellId.Value,
        cell.Kind.ToString(),
        cell.Location.X,
        cell.Location.Y,
        cell.Orientation.ToString(),
        cell.PortId?.Value,
        cell.BehaviorId?.Value,
        cell.Parameters.Select(pair => new DurableKeyValue(pair.Key, pair.Value)).ToImmutableArray(),
        cell.ExternalInput.ToString(),
        cell.CommittedOutput.ToString(),
        cell.PendingValue.ToString(),
        cell.PendingTick,
        cell.FilterCandidate.ToString(),
        cell.FilterCandidateSinceTick,
        cell.PreviousData.ToString(),
        cell.PreviousClock.ToString(),
        cell.DataChangedTick,
        cell.NextClockTransitionTick,
        cell.ObservedValue.ToString(),
        cell.LastForwarded.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DurableForwardedValue(pair.Key, pair.Value.ToString()))
            .ToImmutableArray(),
        cell.CustomState,
        cell.CustomRandomState);

    private static PanelRuntimeCellSnapshot FromDto(DurablePanelRuntimeCellSnapshot cell)
    {
        if (!Enum.TryParse<CellKind>(cell.Kind, false, out var kind) || !Enum.IsDefined(kind) ||
            !Enum.TryParse<CardinalDirection>(cell.Orientation, false, out var orientation) ||
            !Enum.IsDefined(orientation) || cell.Parameters.IsDefault || cell.LastForwarded.IsDefault ||
            cell.CustomState.IsDefault)
        {
            throw new InvalidDataException("Panel runtime cell snapshot is invalid.");
        }

        return new PanelRuntimeCellSnapshot(
            new ComponentId(cell.CellId),
            kind,
            new GridCoordinate(cell.X, cell.Y),
            orientation,
            cell.PortId is null ? null : new PortId(cell.PortId),
            cell.Parameters.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            cell.BehaviorId is null ? null : new BehaviorId(cell.BehaviorId),
            ParseLogicValue(cell.ExternalInput),
            ParseLogicValue(cell.CommittedOutput),
            ParseLogicValue(cell.PendingValue),
            cell.PendingTick,
            ParseLogicValue(cell.FilterCandidate),
            cell.FilterCandidateSinceTick,
            ParseLogicValue(cell.PreviousData),
            ParseLogicValue(cell.PreviousClock),
            cell.DataChangedTick,
            cell.NextClockTransitionTick,
            ParseLogicValue(cell.ObservedValue),
            cell.LastForwarded.Select(pair => new KeyValuePair<string, LogicValue>(
                pair.PortName,
                ParseLogicValue(pair.Value))).ToImmutableArray(),
            cell.CustomState,
            cell.CustomRandomState);
    }

    private static DurableDeviceGraphRuntimeState ToDto(DeviceGraphRuntimeSnapshot snapshot) => new(
        ToDto(snapshot.Scheduler),
        snapshot.Devices.Select(ToDto).ToImmutableArray(),
        snapshot.Lanes.Select(ToDto).ToImmutableArray(),
        snapshot.ReplayCommands,
        snapshot.NextReplayCommandIndex)
    {
        StateIntegrityHash = snapshot.StateIntegrityHash
    };

    private static DeviceGraphRuntimeSnapshot ToDomain(
        DurableDeviceGraphRuntimeState snapshot,
        DeviceGraphDefinition definition)
    {
        if (snapshot.Devices.IsDefault || snapshot.Lanes.IsDefault || snapshot.ReplayCommands.IsDefault)
        {
            throw new InvalidDataException("Device graph runtime snapshot is incomplete.");
        }

        return new DeviceGraphRuntimeSnapshot(
            definition,
            FromDto(snapshot.Scheduler),
            snapshot.Devices.Select(FromDto).ToImmutableArray(),
            snapshot.Lanes.Select(FromDto).ToImmutableArray())
        {
            ReplayCommands = snapshot.ReplayCommands,
            NextReplayCommandIndex = snapshot.NextReplayCommandIndex,
            StateIntegrityHash = snapshot.StateIntegrityHash
        };
    }

    private static DurableDeviceRuntimeSnapshot ToDto(DeviceRuntimeSnapshot device) => new(
        device.DeviceId.Value,
        device.Panel is null ? null : ToDto(device.Panel),
        device.Inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DurableSignalPort(pair.Key, pair.Value.ToString())).ToImmutableArray(),
        device.Outputs.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DurableSignalPort(pair.Key, pair.Value.ToString())).ToImmutableArray(),
        device.Timers.Select(timer => new DurableDeviceTimer(
            timer.DueTick,
            timer.PortName,
            timer.Value.ToString())).ToImmutableArray(),
        device.PendingNodeInputs.Select(input => new DurableNodeInputTransition(
            input.Tick,
            input.PortName,
            input.Signal.ToString())).ToImmutableArray(),
        device.NodeDetachApplyAtTick,
        device.NodeDetachRequested,
        device.NodeBehaviorFingerprint)
    {
        NodeInputHistory = device.NodeInputHistory.Select(input => new DurableNodeInputTransition(
            input.Tick,
            input.PortName,
            input.Signal.ToString())).ToImmutableArray()
    };

    private static DeviceRuntimeSnapshot FromDto(DurableDeviceRuntimeSnapshot device)
    {
        if (device.Inputs.IsDefault || device.Outputs.IsDefault || device.Timers.IsDefault ||
            device.PendingNodeInputs.IsDefault)
        {
            throw new InvalidDataException("Device runtime snapshot collections are missing.");
        }

        return new DeviceRuntimeSnapshot(
            new ComponentId(device.DeviceId),
            device.Panel is null ? null : FromDto(device.Panel),
            device.Inputs.Select(input => new KeyValuePair<string, DeviceSignal>(input.PortName, ParseSignal(input.Signal)))
                .ToImmutableArray(),
            device.Outputs.Select(output => new KeyValuePair<string, DeviceSignal>(output.PortName, ParseSignal(output.Signal)))
                .ToImmutableArray(),
            device.Timers.Select(timer => new DeviceTimerSnapshot(
                timer.DueTick,
                timer.PortName,
                ParseSignal(timer.Signal))).ToImmutableArray())
        {
            PendingNodeInputs = device.PendingNodeInputs.Select(input => new DeviceNodeInputTransition(
                input.Tick,
                input.PortName,
                ParseSignal(input.Signal))).ToImmutableArray(),
            NodeDetachApplyAtTick = device.NodeDetachApplyAtTick,
            NodeDetachRequested = device.NodeDetachRequested,
            NodeBehaviorFingerprint = device.NodeBehaviorFingerprint,
            NodeInputHistory = device.NodeInputHistory.Select(input => new DeviceNodeInputTransition(
                input.Tick,
                input.PortName,
                ParseSignal(input.Signal))).ToImmutableArray()
        };
    }

    private static DurableCableLaneRuntimeSnapshot ToDto(CableLaneRuntimeSnapshot lane) => new(
        lane.LaneId.Value,
        lane.Connected,
        lane.Epoch,
        lane.CurrentSignal.ToString(),
        lane.History.Select(entry => new DurableCableLaneHistoryEntry(
            entry.Epoch,
            entry.CausalOrdinal,
            entry.ScheduledTick,
            entry.Signal.ToString(),
            entry.IsRelease,
            entry.Status.ToString(),
            entry.DeliveredTick)).ToImmutableArray());

    private static CableLaneRuntimeSnapshot FromDto(DurableCableLaneRuntimeSnapshot lane)
    {
        if (lane.History.IsDefault)
        {
            throw new InvalidDataException("Cable lane history is missing.");
        }

        return new CableLaneRuntimeSnapshot(
            new ComponentId(lane.LaneId),
            lane.Connected,
            lane.Epoch,
            ParseSignal(lane.CurrentSignal),
            lane.History.Select(entry =>
            {
                if (!Enum.TryParse<CableTransitionStatus>(entry.Status, false, out var status) || !Enum.IsDefined(status))
                {
                    throw new InvalidDataException("Cable transition status is unsupported.");
                }

                return new CableLaneHistoryEntry(
                    entry.Epoch,
                    entry.CausalOrdinal,
                    entry.ScheduledTick,
                    ParseSignal(entry.Signal),
                    entry.IsRelease,
                    status,
                    entry.DeliveredTick);
            }).ToImmutableArray());
    }

    private static DurableSchedulerSnapshot ToDto(SchedulerSnapshot snapshot) => new(
        snapshot.CurrentTick,
        snapshot.NextAcceptedOrdinal,
        snapshot.NextCausalOrdinal,
        snapshot.PendingEvents.OrderBy(item => item.Key).ToImmutableArray(),
        snapshot.AcceptedCommands.OrderBy(item => item.AcceptedOrdinal).ToImmutableArray(),
        snapshot.AppliedCommandOrdinals.Order().ToImmutableArray(),
        snapshot.Targets.OrderBy(item => item.StableId, StringComparer.Ordinal).ToImmutableArray(),
        snapshot.Drives.OrderBy(item => DriveSortKey(item.Key), StringComparer.Ordinal).ToImmutableArray(),
        snapshot.TemporalRoots.OrderBy(item => item.RootId, StringComparer.Ordinal).ToImmutableArray(),
        snapshot.CancelledTemporalRoots.Order(StringComparer.Ordinal).ToImmutableArray(),
        snapshot.Trace.OrderBy(item => item.Tick).Select(ToDto).ToImmutableArray())
    {
        TargetHistory = snapshot.TargetHistory,
        StateIntegrityHash = snapshot.StateIntegrityHash,
        TraceIntegrityHash = snapshot.TraceIntegrityHash,
        TraceStartTick = snapshot.TraceStartTick
    };

    private static SchedulerSnapshot FromDto(DurableSchedulerSnapshot snapshot)
    {
        if (snapshot.PendingEvents.IsDefault || snapshot.AcceptedCommands.IsDefault ||
            snapshot.AppliedCommandOrdinals.IsDefault || snapshot.Targets.IsDefault ||
            snapshot.Drives.IsDefault || snapshot.TemporalRoots.IsDefault ||
            snapshot.CancelledTemporalRoots.IsDefault || snapshot.Trace.IsDefault)
        {
            throw new InvalidDataException("Scheduler snapshot is missing causal collections.");
        }

        return new SchedulerSnapshot(
            snapshot.CurrentTick,
            snapshot.NextAcceptedOrdinal,
            snapshot.NextCausalOrdinal,
            snapshot.PendingEvents,
            snapshot.AcceptedCommands,
            snapshot.AppliedCommandOrdinals.ToImmutableHashSet(),
            snapshot.Targets,
            snapshot.Drives,
            snapshot.TemporalRoots,
            snapshot.CancelledTemporalRoots.ToImmutableHashSet(StringComparer.Ordinal),
            snapshot.Trace.Select(FromDto).ToImmutableArray())
        {
            TargetHistory = snapshot.TargetHistory,
            StateIntegrityHash = snapshot.StateIntegrityHash,
            TraceIntegrityHash = snapshot.TraceIntegrityHash,
            TraceStartTick = snapshot.TraceStartTick
        };
    }

    private static DurableSchedulerTickTrace ToDto(SchedulerTickTrace trace) => new(
        trace.Tick,
        trace.DeliveredEvents.OrderBy(item => item.Key).ToImmutableArray(),
        trace.ResolvedInputs.OrderBy(pair => AddressSortKey(pair.Key), StringComparer.Ordinal)
            .Select(pair => new DurableResolvedInput(pair.Key, pair.Value.ToString()))
            .ToImmutableArray(),
        trace.Diagnostics,
        trace.Hash)
    {
        State = trace.State is null ? null : ToDto(trace.State)
    };

    private static SchedulerTickTrace FromDto(DurableSchedulerTickTrace trace)
    {
        if (trace.DeliveredEvents.IsDefault || trace.ResolvedInputs.IsDefault || trace.Diagnostics.IsDefault)
        {
            throw new InvalidDataException("Scheduler trace is incomplete.");
        }

        var inputs = trace.ResolvedInputs.ToImmutableDictionary(
            input => input.Address,
            input => ParseLogicValue(input.Value));
        return new SchedulerTickTrace(trace.Tick, trace.DeliveredEvents, inputs, trace.Diagnostics, trace.Hash)
        {
            State = trace.State is null ? null : FromDto(trace.State)
        };
    }

    private static DurableSchedulerTraceState ToDto(SchedulerTraceState state) => new(
        state.NextAcceptedOrdinal,
        state.NextCausalOrdinal,
        state.AcceptedCommands.OrderBy(item => item.AcceptedOrdinal).ToImmutableArray(),
        state.AppliedCommandOrdinals.Order().ToImmutableArray(),
        state.Targets.OrderBy(item => item.StableId, StringComparer.Ordinal).ToImmutableArray(),
        state.Drives.OrderBy(item => DriveSortKey(item.Key), StringComparer.Ordinal).ToImmutableArray(),
        state.PendingEvents.OrderBy(item => item.Key).ToImmutableArray(),
        state.TemporalRoots.OrderBy(item => item.RootId, StringComparer.Ordinal).ToImmutableArray(),
        state.CancelledTemporalRoots.Order(StringComparer.Ordinal).ToImmutableArray());

    private static SchedulerTraceState FromDto(DurableSchedulerTraceState state)
    {
        if (state.AcceptedCommands.IsDefault || state.AppliedCommandOrdinals.IsDefault ||
            state.Targets.IsDefault || state.Drives.IsDefault || state.PendingEvents.IsDefault ||
            state.TemporalRoots.IsDefault || state.CancelledTemporalRoots.IsDefault)
        {
            throw new InvalidDataException("Scheduler trace state is incomplete.");
        }

        return new SchedulerTraceState(
            state.NextAcceptedOrdinal,
            state.NextCausalOrdinal,
            state.AcceptedCommands,
            state.AppliedCommandOrdinals.ToImmutableHashSet(),
            state.Targets,
            state.Drives,
            state.PendingEvents,
            state.TemporalRoots,
            state.CancelledTemporalRoots.ToImmutableHashSet(StringComparer.Ordinal));
    }

    private static string DriveSortKey(SchedulerDriveKey key) =>
        $"{key.SourceStableId}\0{key.SourceIncarnation:D20}\0{key.SourcePort}\0" +
        $"{key.TargetStableId}\0{key.TargetIncarnation:D20}\0{key.TargetPortOrLane}";

    private static string AddressSortKey(SchedulerPortAddress address) =>
        $"{address.TargetStableId}\0{address.TargetIncarnation:D20}\0{address.PortOrLane}";

    private static LogicValue ParseLogicValue(string value) =>
        Enum.TryParse<LogicValue>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"Logic value '{value}' is unsupported.");

    private static DeviceSignal ParseSignal(string value) =>
        DeviceSignal.TryParse(value, value.Length, out var parsed)
            ? parsed
            : throw new InvalidDataException("Device signal is invalid.");

}
