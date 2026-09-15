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

    public static WorkbenchDefinition Restore(
        PanelDefinition panel,
        ChipDefinitionCatalog chipCatalog,
        PanelOwnedChipNetworkDefinition chipNetwork,
        IEnumerable<WorkbenchChipPlacement> chipPlacements,
        DeviceGraphDefinition? deviceGraph,
        IEnumerable<WorkbenchDevicePlacement> devicePlacements)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(chipCatalog);
        ArgumentNullException.ThrowIfNull(chipNetwork);
        ArgumentNullException.ThrowIfNull(chipPlacements);
        ArgumentNullException.ThrowIfNull(devicePlacements);

        var chips = chipPlacements.ToImmutableArray();
        var devices = devicePlacements.ToImmutableArray();
        var diagnostics = chipNetwork.Validate(panel, chipCatalog);
        if (!diagnostics.IsDefaultOrEmpty)
        {
            throw new ChipDefinitionException(diagnostics);
        }

        if (chips.IsDefault || chips.Length != chipNetwork.Instances.Length ||
            chips.Select(item => item.InstanceId).Distinct().Count() != chips.Length)
        {
            throw new ArgumentException("Chip placements do not match the child network.", nameof(chipPlacements));
        }

        if (devices.IsDefault || devices.Select(item => item.DeviceId).Distinct().Count() != devices.Length ||
            (deviceGraph?.Devices.Length ?? 0) != devices.Length)
        {
            throw new ArgumentException("Device placements do not match the device graph.", nameof(devicePlacements));
        }

        var occupied = new HashSet<GridCoordinate>();
        var networkInstances = chipNetwork.Instances.ToDictionary(item => item.InstanceId);
        foreach (var placement in chips)
        {
            if (!networkInstances.TryGetValue(placement.InstanceId, out var instance) ||
                instance.DefinitionId != placement.DefinitionId ||
                !string.Equals(instance.ContentHash, placement.ContentHash, StringComparison.Ordinal) ||
                !chipCatalog.TryResolve(placement.DefinitionId, placement.ContentHash, out _))
            {
                throw new ArgumentException(
                    $"Chip placement '{placement.InstanceId}' does not match a pinned chip instance.",
                    nameof(chipPlacements));
            }

            EnsureFreeLocation(panel, placement.Location, occupied, nameof(chipPlacements));
        }

        if (deviceGraph is null)
        {
            if (!devices.IsDefaultOrEmpty)
            {
                throw new ArgumentException("Device placements require a device graph.", nameof(devicePlacements));
            }
        }
        else
        {
            var graphDevices = deviceGraph.Devices.Select(device => device.Id).ToHashSet();
            if (!graphDevices.SetEquals(devices.Select(placement => placement.DeviceId)))
            {
                throw new ArgumentException("Device placements do not match the device graph.", nameof(devicePlacements));
            }

            foreach (var placement in devices)
            {
                EnsureFreeLocation(panel, placement.Location, occupied, nameof(devicePlacements));
            }
        }

        return new WorkbenchDefinition(
            panel,
            chipCatalog,
            chipNetwork,
            chips,
            deviceGraph,
            devices);
    }

    private static void EnsureFreeLocation(
        PanelDefinition panel,
        GridCoordinate location,
        HashSet<GridCoordinate> occupied,
        string parameterName)
    {
        if ((uint)location.X >= (uint)panel.Width || (uint)location.Y >= (uint)panel.Height ||
            panel.GetCell(location) is not null || !occupied.Add(location))
        {
            throw new ArgumentException("Asset placement is outside the panel or overlaps another cell.", parameterName);
        }
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
