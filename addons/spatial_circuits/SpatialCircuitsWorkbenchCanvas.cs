using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.GodotAdapter;

[Tool]
public partial class SpatialCircuitsWorkbenchCanvas : Control
{
    public const float CellSize = 32f;

    private PanelWorkbenchSession? _session;

    public event Action<GridCoordinate>? CellPressed;

    public void Bind(PanelWorkbenchSession session)
    {
        if (_session is not null)
        {
            _session.Changed -= OnSessionChanged;
        }

        _session = session;
        _session.Changed += OnSessionChanged;
        UpdateCanvasMinimumSize();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_session is null)
        {
            return;
        }

        var definition = _session.Definition;
        var panel = definition.Panel;
        var size = new Vector2(panel.Width * CellSize, panel.Height * CellSize);
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.08f, 0.09f, 0.11f));
        DrawCableBundles(definition);

        var grid = new Color(0.24f, 0.27f, 0.3f);
        for (var x = 0; x <= panel.Width; x++)
        {
            var position = x * CellSize;
            DrawLine(new Vector2(position, 0), new Vector2(position, size.Y), grid);
        }

        for (var y = 0; y <= panel.Height; y++)
        {
            var position = y * CellSize;
            DrawLine(new Vector2(0, position), new Vector2(size.X, position), grid);
        }

        // ponytail: redraw the whole bounded panel; virtualize if large panels make this slow.
        foreach (var cell in panel.Cells.OfType<PanelCellDefinition>())
        {
            DrawCell(cell);
        }

        foreach (var placement in definition.ChipPlacements)
        {
            DrawRect(CellRect(placement.Location).Grow(-4), new Color(0.95f, 0.68f, 0.24f), false, 2);
        }

        foreach (var placement in definition.DevicePlacements)
        {
            DrawCircle(CellRect(placement.Location).GetCenter(), 9, new Color(0.35f, 0.8f, 0.95f));
        }

        if (_session.Selection is { } selection)
        {
            DrawRect(CellRect(selection).Grow(-2), new Color(0.95f, 0.8f, 0.25f), false, 2);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_session is null || @event is not InputEventMouseButton mouse ||
            mouse.ButtonIndex != MouseButton.Left || !mouse.Pressed)
        {
            return;
        }

        var coordinate = new GridCoordinate(
            Mathf.FloorToInt(mouse.Position.X / CellSize),
            Mathf.FloorToInt(mouse.Position.Y / CellSize));
        var panel = _session.Definition.Panel;
        if ((uint)coordinate.X >= (uint)panel.Width || (uint)coordinate.Y >= (uint)panel.Height)
        {
            return;
        }

        CellPressed?.Invoke(coordinate);
        AcceptEvent();
    }

    public override void _ExitTree()
    {
        if (_session is not null)
        {
            _session.Changed -= OnSessionChanged;
            _session = null;
        }
    }

    private void OnSessionChanged()
    {
        UpdateCanvasMinimumSize();
        QueueRedraw();
    }

    private void UpdateCanvasMinimumSize()
    {
        if (_session is not null)
        {
            var panel = _session.Definition.Panel;
            CustomMinimumSize = new Vector2(panel.Width * CellSize, panel.Height * CellSize);
        }
    }

    private void DrawCell(PanelCellDefinition cell)
    {
        var rect = CellRect(cell.Location);
        var center = rect.Position + rect.Size / 2;
        var direction = Direction(cell.Orientation);
        var ink = new Color(0.88f, 0.91f, 0.95f);

        switch (cell.Kind)
        {
            case CellKind.Wire:
                DrawLine(center - direction * 13, center + direction * 13, ink, 3);
                break;
            case CellKind.Junction:
                DrawLine(center - direction * 13, center + direction * 13, ink, 3);
                DrawCircle(center, 4, new Color(1f, 0.76f, 0.15f));
                break;
            case CellKind.Crossing:
                DrawLine(center - direction * 13, center + direction * 13, ink, 3);
                DrawLine(center - new Vector2(direction.Y, direction.X) * 13,
                    center + new Vector2(direction.Y, direction.X) * 13, ink, 3);
                break;
            case CellKind.InputPort:
            case CellKind.OutputPort:
                DrawCircle(center, 9, new Color(0.95f, 0.68f, 0.12f));
                DrawLine(center, center + direction * 14, ink, 3);
                break;
            case CellKind.Probe:
                DrawCircle(center, 9, new Color(0.2f, 0.7f, 0.85f));
                DrawLine(center - direction * 8, center + direction * 8, ink, 2);
                break;
            default:
                DrawRect(rect.Grow(-6), CellColor(cell.Kind));
                DrawLine(center, center + direction * 12, ink, 3);
                break;
        }
    }

    private void DrawCableBundles(WorkbenchDefinition definition)
    {
        if (definition.DeviceGraph is not { } graph)
        {
            return;
        }

        var locations = definition.DevicePlacements.ToDictionary(item => item.DeviceId, item => item.Location);
        foreach (var lane in graph.Lanes)
        {
            if (locations.TryGetValue(lane.Source.DeviceId, out var source) &&
                locations.TryGetValue(lane.Target.DeviceId, out var target))
            {
                DrawLine(CellRect(source).GetCenter(), CellRect(target).GetCenter(),
                    new Color(0.35f, 0.8f, 0.95f), 2);
            }
        }
    }

    private static Rect2 CellRect(GridCoordinate coordinate) =>
        new(coordinate.X * CellSize, coordinate.Y * CellSize, CellSize, CellSize);

    private static Vector2 Direction(CardinalDirection direction) => direction switch
    {
        CardinalDirection.North => new Vector2(0, -1),
        CardinalDirection.East => new Vector2(1, 0),
        CardinalDirection.South => new Vector2(0, 1),
        _ => new Vector2(-1, 0)
    };

    private static Color CellColor(CellKind kind) => kind switch
    {
        CellKind.Nand => new Color(0.72f, 0.36f, 0.34f),
        CellKind.DFlipFlop => new Color(0.46f, 0.38f, 0.75f),
        CellKind.Clock => new Color(0.32f, 0.58f, 0.8f),
        CellKind.StabilityFilter => new Color(0.35f, 0.62f, 0.48f),
        CellKind.Custom => new Color(0.58f, 0.48f, 0.7f),
        _ => new Color(0.65f, 0.68f, 0.72f)
    };
}
