using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Cells.Tests;

public sealed class PanelDefinitionTests
{
    [Fact]
    public void CompilesCellsInRowMajorOrder()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/row-major"),
            3,
            2,
            [
                Cell("last", 2, 1, CellKind.Wire),
                Cell("first", 0, 0, CellKind.Junction),
                Cell("middle", 1, 0, CellKind.Crossing)
            ]);

        Assert.Equal(6, panel.Cells.Length);
        Assert.Equal("first", panel.Cells[0]!.Id.Value);
        Assert.Equal("middle", panel.Cells[1]!.Id.Value);
        Assert.Null(panel.Cells[2]);
        Assert.Equal("last", panel.Cells[5]!.Id.Value);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 2)]
    public void RejectsNonPositivePanelDimensions(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PanelDefinition.Create(new CircuitId("panel/invalid"), width, height, []));

    [Fact]
    public void RejectsCellsOutsidePanelAndDuplicateCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PanelDefinition.Create(
                new CircuitId("panel/outside"),
                1,
                1,
                [Cell("outside", 1, 0, CellKind.Wire)]));

        Assert.Throws<ArgumentException>(() =>
            PanelDefinition.Create(
                new CircuitId("panel/duplicate-coordinate"),
                2,
                1,
                [
                    Cell("first", 0, 0, CellKind.Wire),
                    Cell("second", 0, 0, CellKind.Wire)
                ]));
    }

    [Fact]
    public void RejectsDuplicateCellIdsAndInvalidCellParameters()
    {
        Assert.Throws<ArgumentException>(() =>
            PanelDefinition.Create(
                new CircuitId("panel/duplicate-id"),
                2,
                1,
                [
                    Cell("same", 0, 0, CellKind.Wire),
                    Cell("same", 1, 0, CellKind.Wire)
                ]));

        Assert.Throws<ArgumentException>(() =>
            Cell("bad-constant", 0, 0, CellKind.Constant, ("value", "Maybe")));

        Assert.Throws<ArgumentException>(() =>
            Cell("bad-clock", 0, 0, CellKind.Clock, ("high-ticks", "0"), ("low-ticks", "1")));

        Assert.Throws<ArgumentException>(() =>
            Cell("bad-delay", 0, 0, CellKind.Nand, ("delay", "0")));

        Assert.Throws<ArgumentException>(() =>
            Cell("unknown-parameter", 0, 0, CellKind.Wire, ("delay", "1")));
    }

    [Fact]
    public void RejectsNonCardinalOrientationAndUnsupportedHoldTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PanelCellDefinition.Create(
                new ComponentId("bad-orientation"),
                new GridCoordinate(0, 0),
                CellKind.Wire,
                (CardinalDirection)99));

        Assert.Throws<ArgumentException>(() =>
            Cell("unsupported-hold", 0, 0, CellKind.DFlipFlop, ("hold-ticks", "1")));
    }

    private static PanelCellDefinition Cell(
        string id,
        int x,
        int y,
        CellKind kind,
        params (string Name, string Value)[] parameters) =>
        PanelCellDefinition.Create(
            new ComponentId(id),
            new GridCoordinate(x, y),
            kind,
            portId: kind is CellKind.InputPort or CellKind.OutputPort
                ? new PortId(id)
                : null,
            parameters: parameters.Select(parameter =>
                new KeyValuePair<string, string>(parameter.Name, parameter.Value)));
}
