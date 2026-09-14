using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Workbench;

public sealed record WorkbenchChipPlacement(
    ComponentId InstanceId,
    DefinitionId DefinitionId,
    string ContentHash,
    GridCoordinate Location);

public sealed record WorkbenchDevicePlacement(ComponentId DeviceId, GridCoordinate Location);

public sealed class WorkbenchDefinition
{
    private WorkbenchDefinition(
        PanelDefinition panel,
        ChipDefinitionCatalog chipCatalog,
        PanelOwnedChipNetworkDefinition chipNetwork,
        ImmutableArray<WorkbenchChipPlacement> chipPlacements,
        DeviceGraphDefinition? deviceGraph,
        ImmutableArray<WorkbenchDevicePlacement> devicePlacements)
    {
        Panel = panel;
        ChipCatalog = chipCatalog;
        ChipNetwork = chipNetwork;
        ChipPlacements = chipPlacements;
        DeviceGraph = deviceGraph;
        DevicePlacements = devicePlacements;
    }

    public PanelDefinition Panel { get; }

    public ChipDefinitionCatalog ChipCatalog { get; }

    public PanelOwnedChipNetworkDefinition ChipNetwork { get; }

    public ImmutableArray<WorkbenchChipPlacement> ChipPlacements { get; }

    public DeviceGraphDefinition? DeviceGraph { get; }

    public ImmutableArray<WorkbenchDevicePlacement> DevicePlacements { get; }

    public static WorkbenchDefinition Create(PanelDefinition panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return new WorkbenchDefinition(
            panel,
            ChipDefinitionCatalog.Create([]),
            PanelOwnedChipNetworkDefinition.Create(panel.Id, []),
            [],
            null,
            []);
    }

    internal WorkbenchDefinition WithPanel(PanelDefinition panel) =>
        new(panel, ChipCatalog, ChipNetwork, ChipPlacements, DeviceGraph, DevicePlacements);

    internal WorkbenchDefinition WithChipState(
        ChipDefinitionCatalog catalog,
        PanelOwnedChipNetworkDefinition network,
        ImmutableArray<WorkbenchChipPlacement> placements) =>
        new(Panel, catalog, network, placements, DeviceGraph, DevicePlacements);

    internal WorkbenchDefinition WithDeviceState(
        DeviceGraphDefinition graph,
        ImmutableArray<WorkbenchDevicePlacement> placements) =>
        new(Panel, ChipCatalog, ChipNetwork, ChipPlacements, graph, placements);

    internal WorkbenchDefinition WithDeviceGraph(DeviceGraphDefinition graph) =>
        new(Panel, ChipCatalog, ChipNetwork, ChipPlacements, graph, DevicePlacements);
}
