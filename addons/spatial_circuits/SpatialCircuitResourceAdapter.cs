using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.GodotAdapter;

public static class SpatialCircuitResourceAdapter
{
    public static SpatialCircuitPanelResource ToResource(PanelDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var resource = new SpatialCircuitPanelResource
        {
            PanelId = definition.Id.Value,
            Width = definition.Width,
            Height = definition.Height
        };
        foreach (var cell in definition.Cells.OfType<PanelCellDefinition>())
        {
            var cellResource = new SpatialCircuitCellResource
            {
                CellId = cell.Id.Value,
                X = cell.Location.X,
                Y = cell.Location.Y,
                Kind = FormatCellKind(cell.Kind),
                Orientation = FormatOrientation(cell.Orientation),
                PortId = cell.PortId?.Value ?? string.Empty,
                BehaviorId = cell.BehaviorId?.Value ?? string.Empty
            };
            foreach (var parameter in cell.Parameters)
            {
                cellResource.Parameters[parameter.Key] = parameter.Value;
            }

            resource.Cells.Add(cellResource);
        }

        return resource;
    }

    public static SpatialCircuitResource ToResource(ChipDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var resource = new SpatialCircuitResource
        {
            DefinitionId = definition.Id.Value,
            SourcePanel = ToResource(definition.SourcePanel),
            SchemaVersion = definition.SchemaVersion,
            BehaviorVersion = definition.BehaviorVersion,
            Symbol = definition.Symbol
        };
        foreach (var port in definition.Ports)
        {
            resource.Ports.Add(new SpatialCircuitChipPortResource
            {
                Name = port.Name,
                PanelPortId = port.PanelPortId.Value,
                Direction = FormatChipPortDirection(port.Direction)
            });
        }

        foreach (var parameter in definition.Parameters)
        {
            resource.Parameters[parameter.Key] = parameter.Value;
        }

        foreach (var instance in definition.ChildNetwork.Instances)
        {
            resource.ChildNetwork.Instances.Add(new SpatialCircuitChildInstanceResource
            {
                InstanceId = instance.InstanceId.Value,
                DefinitionId = instance.DefinitionId.Value,
                ContentHash = instance.ContentHash
            });
        }

        foreach (var connection in definition.ChildNetwork.Connections)
        {
            resource.ChildNetwork.Connections.Add(new SpatialCircuitChildConnectionResource
            {
                SourceInstanceId = connection.Source.InstanceId?.Value ?? string.Empty,
                SourcePortName = connection.Source.PortName,
                TargetInstanceId = connection.Target.InstanceId?.Value ?? string.Empty,
                TargetPortName = connection.Target.PortName
            });
        }

        return resource;
    }

    public static PanelDefinition ToPanelDefinition(SpatialCircuitPanelResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(resource.Cells);

        return PanelDefinition.Create(
            new CircuitId(resource.PanelId),
            resource.Width,
            resource.Height,
            resource.Cells.Select(CreateCell));
    }

    public static ChipDefinition ToDefinition(SpatialCircuitResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(resource.SourcePanel);
        ArgumentNullException.ThrowIfNull(resource.Ports);
        ArgumentNullException.ThrowIfNull(resource.Parameters);
        ArgumentNullException.ThrowIfNull(resource.ChildNetwork);

        return ChipDefinition.Create(
            new DefinitionId(resource.DefinitionId),
            ToPanelDefinition(resource.SourcePanel),
            resource.Ports.Select(port =>
            {
                ArgumentNullException.ThrowIfNull(port);
                return new ChipPortDefinition(
                    port.Name,
                    new PortId(port.PanelPortId),
                    ParseChipPortDirection(port.Direction));
            }),
            resource.SchemaVersion,
            resource.BehaviorVersion,
            resource.Symbol,
            resource.Parameters.Select(parameter =>
                new KeyValuePair<string, string>(parameter.Key, parameter.Value)),
            ToChildNetwork(resource));
    }

    private static PanelCellDefinition CreateCell(SpatialCircuitCellResource cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(cell.Parameters);

        return PanelCellDefinition.Create(
            new ComponentId(cell.CellId),
            new GridCoordinate(cell.X, cell.Y),
            ParseCellKind(cell.Kind),
            ParseOrientation(cell.Orientation),
            string.IsNullOrEmpty(cell.PortId) ? null : new PortId(cell.PortId),
            cell.Parameters.Select(parameter =>
                new KeyValuePair<string, string>(parameter.Key, parameter.Value)),
            string.IsNullOrEmpty(cell.BehaviorId) ? null : new BehaviorId(cell.BehaviorId));
    }

    private static PanelOwnedChipNetworkDefinition ToChildNetwork(SpatialCircuitResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource.ChildNetwork.Instances);
        ArgumentNullException.ThrowIfNull(resource.ChildNetwork.Connections);
        var instances = resource.ChildNetwork.Instances.Select(instance =>
        {
            ArgumentNullException.ThrowIfNull(instance);
            return ChipInstanceDefinition.Create(
                new ComponentId(instance.InstanceId),
                new DefinitionId(instance.DefinitionId),
                instance.ContentHash);
        });
        var connections = resource.ChildNetwork.Connections.Select(connection =>
        {
            ArgumentNullException.ThrowIfNull(connection);
            return new ChipPortConnection(
                ToEndpoint(connection.SourceInstanceId, connection.SourcePortName),
                ToEndpoint(connection.TargetInstanceId, connection.TargetPortName));
        });
        return PanelOwnedChipNetworkDefinition.Create(
            new CircuitId(resource.SourcePanel.PanelId),
            instances,
            connections);
    }

    private static ChipPortEndpoint ToEndpoint(string instanceId, string portName) =>
        string.IsNullOrEmpty(instanceId)
            ? ChipPortEndpoint.ParentPanel(new PortId(portName))
            : ChipPortEndpoint.ChildChip(new ComponentId(instanceId), portName);

    private static CellKind ParseCellKind(string kind) => kind switch
    {
        "empty" => CellKind.Empty,
        "wire" => CellKind.Wire,
        "junction" => CellKind.Junction,
        "crossing" => CellKind.Crossing,
        "constant" => CellKind.Constant,
        "input-port" => CellKind.InputPort,
        "output-port" => CellKind.OutputPort,
        "probe" => CellKind.Probe,
        "nand" => CellKind.Nand,
        "clock" => CellKind.Clock,
        "d-flip-flop" => CellKind.DFlipFlop,
        "stability-filter" => CellKind.StabilityFilter,
        "custom" => CellKind.Custom,
        _ => throw new ArgumentException($"Cell kind '{kind}' is not supported.", nameof(kind))
    };

    private static CardinalDirection ParseOrientation(string orientation) => orientation switch
    {
        "north" => CardinalDirection.North,
        "east" => CardinalDirection.East,
        "south" => CardinalDirection.South,
        "west" => CardinalDirection.West,
        _ => throw new ArgumentException(
            $"Cell orientation '{orientation}' must be cardinal.",
            nameof(orientation))
    };

    private static ChipPortDirection ParseChipPortDirection(string direction) => direction switch
    {
        "input" => ChipPortDirection.Input,
        "output" => ChipPortDirection.Output,
        _ => throw new ArgumentException(
            $"Chip port direction '{direction}' must be input or output.",
            nameof(direction))
    };

    private static string FormatCellKind(CellKind kind) => kind switch
    {
        CellKind.Empty => "empty",
        CellKind.Wire => "wire",
        CellKind.Junction => "junction",
        CellKind.Crossing => "crossing",
        CellKind.Constant => "constant",
        CellKind.InputPort => "input-port",
        CellKind.OutputPort => "output-port",
        CellKind.Probe => "probe",
        CellKind.Nand => "nand",
        CellKind.Clock => "clock",
        CellKind.DFlipFlop => "d-flip-flop",
        CellKind.StabilityFilter => "stability-filter",
        CellKind.Custom => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Cell kind is unsupported.")
    };

    private static string FormatOrientation(CardinalDirection orientation) => orientation switch
    {
        CardinalDirection.North => "north",
        CardinalDirection.East => "east",
        CardinalDirection.South => "south",
        CardinalDirection.West => "west",
        _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Orientation is unsupported.")
    };

    private static string FormatChipPortDirection(ChipPortDirection direction) => direction switch
    {
        ChipPortDirection.Input => "input",
        ChipPortDirection.Output => "output",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Chip port direction is unsupported.")
    };
}

public sealed class SpatialCircuitDefinitionPublisher
{
    public ChipDefinition? CurrentDefinition { get; private set; }

    public bool TryPublish(SpatialCircuitResource resource, out string diagnostic)
    {
        ChipDefinition candidate;
        try
        {
            candidate = SpatialCircuitResourceAdapter.ToDefinition(resource);
        }
        catch (ArgumentException exception)
        {
            diagnostic = exception.Message;
            return false;
        }

        if (!string.Equals(CurrentDefinition?.ContentHash, candidate.ContentHash, StringComparison.Ordinal))
        {
            CurrentDefinition = candidate;
        }

        diagnostic = string.Empty;
        return true;
    }
}
