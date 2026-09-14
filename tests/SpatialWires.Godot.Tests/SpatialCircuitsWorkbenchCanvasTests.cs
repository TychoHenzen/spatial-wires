using GdUnit4;
using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.GodotAdapter;
using SpatialCircuits.Workbench;
using static GdUnit4.Assertions;

namespace SpatialWires.Godot.Tests;

[TestSuite]
public sealed class SpatialCircuitsWorkbenchCanvasTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void PanelCellsRenderInsideOneBatchedCanvasControl()
    {
        var cells = Enumerable.Range(0, 500)
            .Select(index => PanelCellDefinition.Create(
                new ComponentId($"wire/{index}"),
                new GridCoordinate(index % 50, index / 50),
                CellKind.Wire));
        var panel = PanelDefinition.Create(new CircuitId("panel/batched-canvas"), 50, 10, cells);
        var canvas = new SpatialCircuitsWorkbenchCanvas();

        try
        {
            canvas.Bind(new PanelWorkbenchSession(panel));

            AssertThat(canvas.GetChildCount()).IsEqual(0);
            AssertThat(canvas.CustomMinimumSize).IsEqual(new Vector2(50 * SpatialCircuitsWorkbenchCanvas.CellSize,
                10 * SpatialCircuitsWorkbenchCanvas.CellSize));
        }
        finally
        {
            canvas.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void LeftClickMapsToThePanelCellCoordinate()
    {
        var canvas = new SpatialCircuitsWorkbenchCanvas();
        GridCoordinate? clicked = null;
        canvas.Bind(new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/canvas-click"), 4, 4, [])));
        canvas.CellPressed += coordinate => clicked = coordinate;

        try
        {
            canvas._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Position = new Vector2(80, 96),
                Pressed = true
            });

            AssertThat(clicked is { X: 2, Y: 3 }).IsTrue();
        }
        finally
        {
            canvas.Free();
        }
    }
}
