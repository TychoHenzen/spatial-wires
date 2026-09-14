using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.GodotAdapter;

public static class SpatialCircuitResourceAdapter
{
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
                new KeyValuePair<string, string>(parameter.Key, parameter.Value)));
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
