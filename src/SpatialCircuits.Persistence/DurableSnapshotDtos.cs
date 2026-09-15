using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Persistence;

public sealed record DurableSaveDocument(
    int SchemaVersion,
    Guid SaveId,
    DurableDefinitionManifest Definitions,
    DurableBehaviorManifest Behaviors,
    DurableSessionState Session,
    string ContentHash);

public sealed record DurableDefinitionManifest(
    int Version,
    string WorkbenchHash,
    ImmutableArray<DurableDefinitionFingerprint> Definitions);

public sealed record DurableDefinitionFingerprint(string Kind, string Id, string ContentHash);

public sealed record DurableBehaviorManifest(
    int Version,
    ImmutableArray<DurableBehaviorFingerprint> Entries);

public sealed record DurableBehaviorFingerprint(
    string BehaviorId,
    string AssemblyName,
    string AssemblyVersion,
    string ModuleVersionId,
    string Sha256,
    bool OptionalForOfflineReplay);

public sealed record DurableSessionState(
    Guid SaveId,
    long CurrentTick,
    int CycleTicks,
    bool IsPaused,
    long NextOrdinal,
    DurableWorkbenchDefinition CommittedDefinition,
    DurablePanelOwnedChipNetworkRuntimeSnapshot ChipNetwork,
    DurableDeviceGraphRuntimeState? DeviceGraph,
    ImmutableArray<DurablePendingMutation> RunningMutations,
    ImmutableArray<DurablePendingMutation> StagedMutations,
    ImmutableArray<WorkbenchCommand> CommandLog,
    ImmutableArray<DurableInputValue> InputValues);

public sealed record DurableDeviceGraphRuntimeState(
    DurableSchedulerSnapshot Scheduler,
    ImmutableArray<DurableDeviceRuntimeSnapshot> Devices,
    ImmutableArray<DurableCableLaneRuntimeSnapshot> Lanes,
    ImmutableArray<AcceptedSchedulerCommand> ReplayCommands,
    int NextReplayCommandIndex)
{
    public string StateIntegrityHash { get; init; } = string.Empty;
}

public sealed record DurablePanelOwnedChipNetworkRuntimeSnapshot(
    string OwnerPanelId,
    DurablePanelRuntimeSnapshot OwnerPanel,
    ImmutableArray<DurableChipInstanceRuntimeSnapshot> Chips);

public sealed record DurableChipInstanceRuntimeSnapshot(
    string InstanceId,
    string DefinitionId,
    string ContentHash,
    DurablePanelRuntimeSnapshot Panel,
    ImmutableArray<DurableChipInstanceRuntimeSnapshot> Children);

public sealed record DurablePanelRuntimeSnapshot(
    string PanelId,
    int Width,
    int Height,
    ImmutableArray<DurablePanelRuntimeCellSnapshot?> Cells,
    DurableSchedulerSnapshot Scheduler,
    ImmutableArray<DurableProbeSample> ProbeHistory)
{
    public ImmutableArray<SchedulerTargetSnapshot> TargetIncarnations { get; init; }

    public ImmutableArray<DurablePanelTargetIncarnationSnapshot> TargetIncarnationTimeline { get; init; }

    public string StateIntegrityHash { get; init; } = string.Empty;
}

public sealed record DurablePanelTargetIncarnationSnapshot(
    string StableId,
    long Incarnation,
    bool Active,
    string DefinitionHash);

public sealed record DurablePanelRuntimeCellSnapshot(
    string CellId,
    string Kind,
    int X,
    int Y,
    string Orientation,
    string? PortId,
    string? BehaviorId,
    ImmutableArray<DurableKeyValue> Parameters,
    string ExternalInput,
    string CommittedOutput,
    string PendingValue,
    long PendingTick,
    string FilterCandidate,
    long FilterCandidateSinceTick,
    string PreviousData,
    string PreviousClock,
    long DataChangedTick,
    long NextClockTransitionTick,
    string ObservedValue,
    ImmutableArray<DurableForwardedValue> LastForwarded,
    ImmutableArray<byte> CustomState,
    ulong CustomRandomState);

public sealed record DurableForwardedValue(string PortName, string Value);

public sealed record DurableProbeSample(string ProbeId, long Tick, string Value);

public sealed record DurableDeviceRuntimeSnapshot(
    string DeviceId,
    DurablePanelRuntimeSnapshot? Panel,
    ImmutableArray<DurableSignalPort> Inputs,
    ImmutableArray<DurableSignalPort> Outputs,
    ImmutableArray<DurableDeviceTimer> Timers,
    ImmutableArray<DurableNodeInputTransition> PendingNodeInputs,
    long? NodeDetachApplyAtTick,
    bool NodeDetachRequested,
    string? NodeBehaviorFingerprint)
{
    public ImmutableArray<DurableNodeInputTransition> NodeInputHistory { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<DurableNodePresentationEvent> PresentationEvents { get; init; }
}

public sealed record DurableSignalPort(string PortName, string Signal);

public sealed record DurableDeviceTimer(long DueTick, string PortName, string Signal);

public sealed record DurableNodeInputTransition(long Tick, string PortName, string Signal);

public sealed record DurableNodePresentationEvent(long Tick, string EventId);

public sealed record DurableCableLaneRuntimeSnapshot(
    string LaneId,
    bool Connected,
    long Epoch,
    string CurrentSignal,
    ImmutableArray<DurableCableLaneHistoryEntry> History);

public sealed record DurableCableLaneHistoryEntry(
    long Epoch,
    long CausalOrdinal,
    long ScheduledTick,
    string Signal,
    bool IsRelease,
    string Status,
    long? DeliveredTick);

public sealed record DurableSchedulerSnapshot(
    long CurrentTick,
    long NextAcceptedOrdinal,
    long NextCausalOrdinal,
    ImmutableArray<ScheduledEvent> PendingEvents,
    ImmutableArray<AcceptedSchedulerCommand> AcceptedCommands,
    ImmutableArray<long> AppliedCommandOrdinals,
    ImmutableArray<SchedulerTargetSnapshot> Targets,
    ImmutableArray<SchedulerDriveSnapshot> Drives,
    ImmutableArray<SchedulerTemporalRoot> TemporalRoots,
    ImmutableArray<string> CancelledTemporalRoots,
    ImmutableArray<DurableSchedulerTickTrace> Trace)
{
    public ImmutableArray<SchedulerTargetSnapshot> TargetHistory { get; init; }

    public string StateIntegrityHash { get; init; } = string.Empty;

    public string TraceIntegrityHash { get; init; } = string.Empty;

    public long TraceStartTick { get; init; }
}

public sealed record DurableSchedulerTickTrace(
    long Tick,
    ImmutableArray<ScheduledEvent> DeliveredEvents,
    ImmutableArray<DurableResolvedInput> ResolvedInputs,
    ImmutableArray<SchedulerDiagnostic> Diagnostics,
    string Hash)
{
    public DurableSchedulerTraceState? State { get; init; }
}

public sealed record DurableSchedulerTraceState(
    long NextAcceptedOrdinal,
    long NextCausalOrdinal,
    ImmutableArray<AcceptedSchedulerCommand> AcceptedCommands,
    ImmutableArray<long> AppliedCommandOrdinals,
    ImmutableArray<SchedulerTargetSnapshot> Targets,
    ImmutableArray<SchedulerDriveSnapshot> Drives,
    ImmutableArray<ScheduledEvent> PendingEvents,
    ImmutableArray<SchedulerTemporalRoot> TemporalRoots,
    ImmutableArray<string> CancelledTemporalRoots);

public sealed record DurableResolvedInput(SchedulerPortAddress Address, string Value);

public sealed record DurablePendingMutation(
    string Action,
    GridCoordinate? Location,
    DurableWorkbenchDefinition Definition,
    string? InputPort,
    string? InputValue,
    long? AcceptedOrdinal,
    long? ApplyAtTick,
    DurableDeviceInputDrive? DeviceInput,
    DurableChipInputDrive? ChipInput);

public sealed record DurableDeviceInputDrive(ComponentId DeviceId, string PortName, string Signal);

public sealed record DurableChipInputDrive(ComponentId InstanceId, string PortName, string Value);

public sealed record DurableInputValue(string PortId, string Value);

public sealed record DurableWorkbenchDefinition(
    DurablePanelDefinition Panel,
    ImmutableArray<DurableChipDefinition> ChipDefinitions,
    DurableChipNetworkDefinition ChipNetwork,
    ImmutableArray<DurableChipPlacement> ChipPlacements,
    DurableDeviceGraphDefinition? DeviceGraph,
    ImmutableArray<DurableDevicePlacement> DevicePlacements);

public sealed record DurablePanelDefinition(
    string Id,
    int Width,
    int Height,
    ImmutableArray<DurablePanelCell> Cells,
    string ContentHash);

public sealed record DurablePanelCell(
    string Id,
    int X,
    int Y,
    string Kind,
    string Orientation,
    string? PortId,
    string? BehaviorId,
    ImmutableArray<DurableKeyValue> Parameters);

public sealed record DurableKeyValue(string Key, string Value);

public sealed record DurableChipDefinition(
    string Id,
    DurablePanelDefinition SourcePanel,
    ImmutableArray<DurableChipPort> Ports,
    int SchemaVersion,
    int BehaviorVersion,
    string Symbol,
    ImmutableArray<DurableKeyValue> Parameters,
    DurableChipNetworkDefinition ChildNetwork,
    string ContentHash);

public sealed record DurableChipPort(string Name, string PanelPortId, string Direction);

public sealed record DurableChipNetworkDefinition(
    string OwnerPanelId,
    ImmutableArray<DurableChipInstance> Instances,
    ImmutableArray<DurableChipConnection> Connections);

public sealed record DurableChipInstance(string InstanceId, string DefinitionId, string ContentHash);

public sealed record DurableChipConnection(DurableChipEndpoint Source, DurableChipEndpoint Target);

public sealed record DurableChipEndpoint(string? InstanceId, string PortName);

public sealed record DurableChipPlacement(
    string InstanceId,
    string DefinitionId,
    string ContentHash,
    int X,
    int Y);

public sealed record DurableDevicePlacement(string DeviceId, int X, int Y);

public sealed record DurableDeviceGraphDefinition(
    string Id,
    ImmutableArray<DurableDeviceDefinition> Devices,
    ImmutableArray<DurableCableLane> Lanes,
    ImmutableArray<DurableCableBundle> Bundles,
    string ContentHash);

public sealed record DurableDeviceDefinition(string Id, DurableDeviceBackend Backend);

public sealed record DurableDeviceBackend(
    string Kind,
    ImmutableArray<DurableDevicePort> Ports,
    DurablePanelDefinition? Panel,
    ImmutableArray<DurableTimedOutputChange> Changes)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BindingVersion { get; init; }
}

public sealed record DurableDevicePort(string Name, string Direction, int Width, string ValueContract);

public sealed record DurableTimedOutputChange(long DelayTicks, string PortName, string Signal);

public sealed record DurableCableLane(
    string Id,
    DurableDevicePortEndpoint Source,
    DurableDevicePortEndpoint Target,
    long Latency);

public sealed record DurableDevicePortEndpoint(string DeviceId, string PortName);

public sealed record DurableCableBundle(string Id, ImmutableArray<string> LaneIds);
