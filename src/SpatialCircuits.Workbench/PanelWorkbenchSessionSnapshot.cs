using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Workbench;

public sealed record WorkbenchDeviceInputDrive(
    ComponentId DeviceId,
    string PortName,
    DeviceSignal Signal);

public sealed record WorkbenchChipInputDrive(
    ComponentId InstanceId,
    string PortName,
    LogicValue Value);

public sealed record PendingWorkbenchMutationSnapshot(
    string Action,
    GridCoordinate? Location,
    WorkbenchDefinition Definition,
    PortId? InputPort,
    LogicValue? InputValue,
    long? AcceptedOrdinal,
    long? ApplyAtTick,
    WorkbenchDeviceInputDrive? DeviceInput,
    WorkbenchChipInputDrive? ChipInput);

public sealed record PanelWorkbenchSessionSnapshot(
    Guid SaveId,
    long CurrentTick,
    WorkbenchDefinition CommittedDefinition,
    PanelOwnedChipNetworkRuntimeSnapshot ChipNetwork,
    DeviceGraphRuntimeSnapshot? DeviceGraph,
    ImmutableArray<PendingWorkbenchMutationSnapshot> RunningMutations,
    ImmutableArray<PendingWorkbenchMutationSnapshot> StagedMutations,
    ImmutableArray<WorkbenchCommand> CommandLog,
    ImmutableArray<KeyValuePair<PortId, LogicValue>> InputValues,
    long NextOrdinal,
    int CycleTicks,
    bool IsPaused);

public sealed record WorkbenchBehaviorAssemblySource(
    string BehaviorId,
    System.Reflection.Assembly? Assembly,
    string? Sha256,
    bool OptionalForOfflineReplay);
