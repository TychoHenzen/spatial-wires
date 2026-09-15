using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Persistence;

public static class DurableDefinitionCodec
{
    public static DurableWorkbenchDefinition ToDto(WorkbenchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new DurableWorkbenchDefinition(
            ToDto(definition.Panel),
            definition.ChipCatalog.Definitions
                .OrderBy(chip => chip.Id.Value, StringComparer.Ordinal)
                .ThenBy(chip => chip.ContentHash, StringComparer.Ordinal)
                .Select(ToDto)
                .ToImmutableArray(),
            ToDto(definition.ChipNetwork),
            definition.ChipPlacements
                .OrderBy(placement => placement.InstanceId.Value, StringComparer.Ordinal)
                .Select(placement => new DurableChipPlacement(
                    placement.InstanceId.Value,
                    placement.DefinitionId.Value,
                    placement.ContentHash,
                    placement.Location.X,
                    placement.Location.Y))
                .ToImmutableArray(),
            definition.DeviceGraph is null ? null : ToDto(definition.DeviceGraph),
            definition.DevicePlacements
                .OrderBy(placement => placement.DeviceId.Value, StringComparer.Ordinal)
                .Select(placement => new DurableDevicePlacement(
                    placement.DeviceId.Value,
                    placement.Location.X,
                    placement.Location.Y))
                .ToImmutableArray());
    }

    public static WorkbenchDefinition FromDto(DurableWorkbenchDefinition dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.ChipDefinitions.IsDefault || dto.ChipPlacements.IsDefault || dto.DevicePlacements.IsDefault)
        {
            throw new InvalidDataException("Workbench definition contains a missing collection.");
        }

        var panel = FromDto(dto.Panel);
        var chips = dto.ChipDefinitions.Select(FromDto).ToArray();
        var catalog = ChipDefinitionCatalog.Create(chips);
        var network = FromDto(dto.ChipNetwork);
        var deviceGraph = dto.DeviceGraph is null ? null : FromDto(dto.DeviceGraph);
        var chipPlacements = dto.ChipPlacements.Select(placement => new WorkbenchChipPlacement(
            new ComponentId(placement.InstanceId),
            new DefinitionId(placement.DefinitionId),
            placement.ContentHash,
            new GridCoordinate(placement.X, placement.Y)));
        var devicePlacements = dto.DevicePlacements.Select(placement => new WorkbenchDevicePlacement(
            new ComponentId(placement.DeviceId),
            new GridCoordinate(placement.X, placement.Y)));
        return WorkbenchDefinition.Restore(
            panel,
            catalog,
            network,
            chipPlacements,
            deviceGraph,
            devicePlacements);
    }

    public static DurablePanelDefinition ToDto(PanelDefinition panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var cells = panel.Cells
            .OfType<PanelCellDefinition>()
            .Select(cell => new DurablePanelCell(
                cell.Id.Value,
                cell.Location.X,
                cell.Location.Y,
                cell.Kind.ToString(),
                cell.Orientation.ToString(),
                cell.PortId?.Value,
                cell.BehaviorId?.Value,
                ToDto(cell.Parameters)))
            .ToImmutableArray();
        var withoutHash = new DurablePanelDefinition(panel.Id.Value, panel.Width, panel.Height, cells, string.Empty);
        return withoutHash with { ContentHash = Hash(withoutHash) };
    }

    public static PanelDefinition FromDto(DurablePanelDefinition dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (!IsHash(dto.ContentHash) || dto.Cells.IsDefault || dto.Width <= 0 || dto.Height <= 0)
        {
            throw new InvalidDataException("Panel definition DTO is incomplete.");
        }

        if (!string.Equals(Hash(dto with { ContentHash = string.Empty }), dto.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Panel definition content hash does not match its data.");
        }

        var cells = dto.Cells.Select(cell =>
        {
            if (!Enum.TryParse<CellKind>(cell.Kind, ignoreCase: false, out var kind) || !Enum.IsDefined(kind) ||
                !Enum.TryParse<CardinalDirection>(cell.Orientation, ignoreCase: false, out var orientation) ||
                !Enum.IsDefined(orientation))
            {
                throw new InvalidDataException("Panel cell kind or orientation is unsupported.");
            }

            return PanelCellDefinition.Create(
                new ComponentId(cell.Id),
                new GridCoordinate(cell.X, cell.Y),
                kind,
                orientation,
                cell.PortId is null ? null : new PortId(cell.PortId),
                FromDto(cell.Parameters),
                cell.BehaviorId is null ? null : new BehaviorId(cell.BehaviorId));
        });
        var panel = PanelDefinition.Create(new CircuitId(dto.Id), dto.Width, dto.Height, cells);
        if (!string.Equals(ToDto(panel).ContentHash, dto.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Panel definition is not in canonical cell order.");
        }

        return panel;
    }

    private static DurableChipDefinition ToDto(ChipDefinition chip) => new(
        chip.Id.Value,
        ToDto(chip.SourcePanel),
        chip.Ports.Select(port => new DurableChipPort(
            port.Name,
            port.PanelPortId.Value,
            port.Direction.ToString())).ToImmutableArray(),
        chip.SchemaVersion,
        chip.BehaviorVersion,
        chip.Symbol,
        ToDto(chip.Parameters),
        ToDto(chip.ChildNetwork),
        chip.ContentHash);

    private static ChipDefinition FromDto(DurableChipDefinition dto)
    {
        if (dto.Ports.IsDefault || dto.Parameters.IsDefault || !IsHash(dto.ContentHash))
        {
            throw new InvalidDataException("Chip definition DTO is incomplete.");
        }

        var ports = dto.Ports.Select(port =>
        {
            if (!Enum.TryParse<ChipPortDirection>(port.Direction, ignoreCase: false, out var direction) ||
                !Enum.IsDefined(direction))
            {
                throw new InvalidDataException("Chip port direction is unsupported.");
            }

            return new ChipPortDefinition(port.Name, new PortId(port.PanelPortId), direction);
        });
        var definition = ChipDefinition.Create(
            new DefinitionId(dto.Id),
            FromDto(dto.SourcePanel),
            ports,
            dto.SchemaVersion,
            dto.BehaviorVersion,
            dto.Symbol,
            FromDto(dto.Parameters),
            FromDto(dto.ChildNetwork));
        if (!string.Equals(definition.ContentHash, dto.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Chip definition '{dto.Id}' content hash does not match its data.");
        }

        return definition;
    }

    private static DurableChipNetworkDefinition ToDto(PanelOwnedChipNetworkDefinition network) => new(
        network.OwnerPanelId.Value,
        network.Instances.Select(instance => new DurableChipInstance(
            instance.InstanceId.Value,
            instance.DefinitionId.Value,
            instance.ContentHash)).ToImmutableArray(),
        network.Connections.Select(connection => new DurableChipConnection(
            ToDto(connection.Source),
            ToDto(connection.Target))).ToImmutableArray());

    private static PanelOwnedChipNetworkDefinition FromDto(DurableChipNetworkDefinition dto)
    {
        if (dto.Instances.IsDefault || dto.Connections.IsDefault)
        {
            throw new InvalidDataException("Chip network DTO is incomplete.");
        }

        return PanelOwnedChipNetworkDefinition.Create(
            new CircuitId(dto.OwnerPanelId),
            dto.Instances.Select(instance => ChipInstanceDefinition.Create(
                new ComponentId(instance.InstanceId),
                new DefinitionId(instance.DefinitionId),
                instance.ContentHash)),
            dto.Connections.Select(connection => new ChipPortConnection(
                FromDto(connection.Source),
                FromDto(connection.Target))));
    }

    private static DurableChipEndpoint ToDto(ChipPortEndpoint endpoint) => new(
        endpoint.InstanceId?.Value,
        endpoint.PortName);

    private static ChipPortEndpoint FromDto(DurableChipEndpoint endpoint) => endpoint.InstanceId is { } instanceId
        ? ChipPortEndpoint.ChildChip(new ComponentId(instanceId), endpoint.PortName)
        : ChipPortEndpoint.ParentPanel(new PortId(endpoint.PortName));

    public static DurableDeviceGraphDefinition ToDto(DeviceGraphDefinition graph)
    {
        var withoutHash = new DurableDeviceGraphDefinition(
            graph.Id.Value,
            graph.Devices.Select(device => new DurableDeviceDefinition(
                device.Id.Value,
                ToDto(device.Backend))).ToImmutableArray(),
            graph.Lanes.Select(lane => new DurableCableLane(
                lane.Id.Value,
                new DurableDevicePortEndpoint(lane.Source.DeviceId.Value, lane.Source.PortName),
                new DurableDevicePortEndpoint(lane.Target.DeviceId.Value, lane.Target.PortName),
                lane.Latency)).ToImmutableArray(),
            graph.Bundles.Select(bundle => new DurableCableBundle(
                bundle.Id.Value,
                bundle.LaneIds.Select(id => id.Value).ToImmutableArray())).ToImmutableArray(),
            string.Empty);
        return withoutHash with { ContentHash = Hash(withoutHash) };
    }

    private static DeviceGraphDefinition FromDto(DurableDeviceGraphDefinition dto)
    {
        if (dto.Devices.IsDefault || dto.Lanes.IsDefault || dto.Bundles.IsDefault || !IsHash(dto.ContentHash) ||
            !string.Equals(Hash(dto with { ContentHash = string.Empty }), dto.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Device graph DTO is incomplete or has a mismatched content hash.");
        }

        var devices = dto.Devices.Select(device => DeviceDefinition.Create(
            new ComponentId(device.Id),
            FromDto(device.Backend)));
        var lanes = dto.Lanes.Select(lane => CableLaneDefinition.Create(
            new ComponentId(lane.Id),
            DevicePortEndpoint.Create(new ComponentId(lane.Source.DeviceId), lane.Source.PortName),
            DevicePortEndpoint.Create(new ComponentId(lane.Target.DeviceId), lane.Target.PortName),
            lane.Latency));
        var bundles = dto.Bundles.Select(bundle => CableBundleDefinition.Create(
            new ComponentId(bundle.Id),
            bundle.LaneIds.Select(id => new ComponentId(id))));
        var graph = DeviceGraphDefinition.Create(new DefinitionId(dto.Id), devices, lanes, bundles);
        if (!string.Equals(ToDto(graph).ContentHash, dto.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Device graph DTO is not in canonical order.");
        }

        return graph;
    }

    private static DurableDeviceBackend ToDto(DeviceBackendDefinition backend) => backend switch
    {
        PanelDeviceBackendDefinition panel => new DurableDeviceBackend(
            "panel",
            ToDto(panel.Ports),
            ToDto(panel.Panel),
            []),
        TimedDeviceBackendDefinition timed => new DurableDeviceBackend(
            "timed",
            ToDto(timed.Ports),
            null,
            timed.Changes.Select(change => new DurableTimedOutputChange(
                change.DelayTicks,
                change.PortName,
                change.Value.ToString())).ToImmutableArray()),
        NodeDeviceBackendDefinition node => new DurableDeviceBackend(
            "node",
            ToDto(node.Ports),
            null,
            [])
        {
            BindingId = node.BindingId,
            BindingVersion = node.BindingVersion
        },
        _ => throw new InvalidDataException($"Device backend type '{backend.GetType().Name}' is unsupported.")
    };

    private static DeviceBackendDefinition FromDto(DurableDeviceBackend dto)
    {
        if (dto.Ports.IsDefault || dto.Changes.IsDefault)
        {
            throw new InvalidDataException("Device backend DTO is incomplete.");
        }

        var ports = FromDto(dto.Ports).ToArray();
        return dto.Kind switch
        {
            "panel" when dto.Panel is not null && dto.Changes.IsEmpty =>
                ValidatePanelPorts(PanelDeviceBackendDefinition.Create(FromDto(dto.Panel)), ports),
            "timed" when dto.Panel is null => TimedDeviceBackendDefinition.Create(
                ports,
                dto.Changes.Select(change => new TimedOutputChange(
                    change.DelayTicks,
                    change.PortName,
                    ParseSignal(change.Signal)))),
            "node" when dto.Panel is null && dto.Changes.IsEmpty => NodeDeviceBackendDefinition.Create(
                ports,
                dto.BindingId,
                dto.BindingVersion),
            _ => throw new InvalidDataException("Device backend kind or payload is inconsistent.")
        };
    }

    private static PanelDeviceBackendDefinition ValidatePanelPorts(
        PanelDeviceBackendDefinition backend,
        IReadOnlyCollection<DevicePortDefinition> ports)
    {
        if (!backend.Ports.SequenceEqual(ports))
        {
            throw new InvalidDataException("Panel device ports do not match its source panel.");
        }

        return backend;
    }

    private static DurableDevicePort ToDto(DevicePortDefinition port) => new(
        port.Name,
        port.Direction.ToString(),
        port.Width,
        port.ValueContract.ToString());

    private static ImmutableArray<DurableDevicePort> ToDto(ImmutableArray<DevicePortDefinition> ports) =>
        ports.Select(ToDto).ToImmutableArray();

    private static ImmutableArray<DevicePortDefinition> FromDto(ImmutableArray<DurableDevicePort> ports) =>
        ports.Select(port =>
        {
            if (!Enum.TryParse<DevicePortDirection>(port.Direction, ignoreCase: false, out var direction) ||
                !Enum.IsDefined(direction) ||
                !Enum.TryParse<DeviceValueContract>(port.ValueContract, ignoreCase: false, out var valueContract) ||
                !Enum.IsDefined(valueContract))
            {
                throw new InvalidDataException("Device port direction or value contract is unsupported.");
            }

            return DevicePortDefinition.Create(port.Name, direction, port.Width, valueContract);
        }).ToImmutableArray();

    private static LogicValue ParseLogicValue(string value) =>
        Enum.TryParse<LogicValue>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"Logic value '{value}' is unsupported.");

    private static DeviceSignal ParseSignal(string value) =>
        DeviceSignal.TryParse(value, value.Length, out var parsed)
            ? parsed
            : throw new InvalidDataException("Device signal is invalid.");

    private static ImmutableArray<DurableKeyValue> ToDto(ImmutableSortedDictionary<string, string> values) =>
        values.Select(pair => new DurableKeyValue(pair.Key, pair.Value)).ToImmutableArray();

    private static ImmutableSortedDictionary<string, string> FromDto(ImmutableArray<DurableKeyValue> values)
    {
        if (values.IsDefault)
        {
            throw new InvalidDataException("Definition parameter list is missing.");
        }

        return values.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Hash<T>(T value) => DurableSnapshotJson.Hash(DurableSnapshotJson.Serialize(value));
}
