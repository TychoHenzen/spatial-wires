using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.Runner;

public sealed record PanelScenarioPlan(
    string PanelId,
    int Width,
    int Height,
    int Microticks,
    ImmutableArray<PanelCellPlan> Cells,
    ImmutableArray<PanelInputChange> Inputs,
    ImmutableArray<PanelProbeExpectation> Expectations)
{
    public PanelDefinition CreatePanelDefinition()
    {
        var cells = Cells.Select(cell => PanelCellDefinition.Create(
            new ComponentId(cell.CellId),
            new GridCoordinate(cell.X, cell.Y),
            ParseKind(cell.Kind),
            ParseOrientation(cell.Orientation),
            cell.PortId is null ? null : new PortId(cell.PortId),
            cell.Parameters));
        return PanelDefinition.Create(new CircuitId(PanelId), Width, Height, cells);
    }

    private static CellKind ParseKind(string kind) => kind switch
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
}

public sealed record PanelCellPlan(
    string CellId,
    int X,
    int Y,
    string Kind,
    string Orientation,
    string? PortId,
    ImmutableArray<KeyValuePair<string, string>> Parameters);

public sealed record PanelInputChange(int Tick, string PortId, string Value);

public sealed record PanelProbeExpectation(int Tick, string ProbeId, string Value);
