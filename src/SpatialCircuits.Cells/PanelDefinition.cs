using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using SpatialCircuits.Core;

namespace SpatialCircuits.Cells;

public enum CellKind
{
    Empty,
    Wire,
    Junction,
    Crossing,
    Constant,
    InputPort,
    OutputPort,
    Probe,
    Nand,
    Clock,
    DFlipFlop,
    StabilityFilter,
    Custom
}

public enum CardinalDirection
{
    North,
    East,
    South,
    West
}

public sealed class PanelCellDefinition
{
    private PanelCellDefinition(
        ComponentId id,
        GridCoordinate location,
        CellKind kind,
        CardinalDirection orientation,
        PortId? portId,
        BehaviorId? behaviorId,
        ImmutableSortedDictionary<string, string> parameters,
        CellRuntimeParameters runtimeParameters)
    {
        Id = id;
        Location = location;
        Kind = kind;
        Orientation = orientation;
        PortId = portId;
        BehaviorId = behaviorId;
        Parameters = parameters;
        RuntimeParameters = runtimeParameters;
    }

    public ComponentId Id { get; }

    public GridCoordinate Location { get; }

    public CellKind Kind { get; }

    public CardinalDirection Orientation { get; }

    public PortId? PortId { get; }

    public BehaviorId? BehaviorId { get; }

    public ImmutableSortedDictionary<string, string> Parameters { get; }

    internal CellRuntimeParameters RuntimeParameters { get; }

    public static PanelCellDefinition Create(
        ComponentId id,
        GridCoordinate location,
        CellKind kind,
        CardinalDirection orientation = CardinalDirection.East,
        PortId? portId = null,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        BehaviorId? behaviorId = null)
    {
        if (!StableData.IsStableId(id.Value))
        {
            throw new ArgumentException("Cell identifier is not stable data.", nameof(id));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Cell kind is not supported.");
        }

        if (!Enum.IsDefined(orientation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(orientation),
                orientation,
                "Cell orientation must be cardinal.");
        }

        if (kind == CellKind.Custom)
        {
            if (behaviorId is not { } behaviorIdValue ||
                !CircuitValidator.IsSupportedBehaviorId(behaviorIdValue))
            {
                throw new ArgumentException(
                    "Custom cells require a namespaced, versioned behavior identifier.",
                    nameof(behaviorId));
            }
        }
        else if (behaviorId.HasValue)
        {
            throw new ArgumentException(
                "Only custom cells may have a behavior identifier.",
                nameof(behaviorId));
        }

        var copiedParameters = (parameters ?? [])
            .ToImmutableSortedDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
        var runtimeParameters = CellRuntimeParameters.Parse(kind, portId, copiedParameters);
        if (portId is { } value && !StableData.IsStableId(value.Value))
        {
            throw new ArgumentException("Panel port identifier is not stable data.", nameof(portId));
        }

        return new PanelCellDefinition(
            id,
            location,
            kind,
            orientation,
            portId,
            behaviorId,
            copiedParameters,
            runtimeParameters);
    }
}

public sealed class PanelDefinition
{
    public const int MaximumCellCount = 1_000_000;

    private PanelDefinition(
        CircuitId id,
        int width,
        int height,
        ImmutableArray<PanelCellDefinition?> cells)
    {
        Id = id;
        Width = width;
        Height = height;
        Cells = cells;
    }

    public CircuitId Id { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Cells in row-major order. Empty coordinates are null.</summary>
    public ImmutableArray<PanelCellDefinition?> Cells { get; }

    public PanelCellDefinition? GetCell(GridCoordinate location)
    {
        if ((uint)location.X >= (uint)Width || (uint)location.Y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(location), "Cell coordinate is outside the panel.");
        }

        return Cells[location.Y * Width + location.X];
    }

    public static PanelDefinition Create(
        CircuitId id,
        int width,
        int height,
        IEnumerable<PanelCellDefinition> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (!StableData.IsStableId(id.Value))
        {
            throw new ArgumentException("Panel identifier is not stable data.", nameof(id));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Panel width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Panel height must be positive.");
        }

        var cellCount = (long)width * height;
        if (cellCount > MaximumCellCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Panel may contain at most {MaximumCellCount} cells.");
        }

        var rowMajor = new PanelCellDefinition?[(int)cellCount];
        var cellIds = new HashSet<string>(StringComparer.Ordinal);
        var inputPortIds = new HashSet<string>(StringComparer.Ordinal);
        var outputPortIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cell in cells)
        {
            ArgumentNullException.ThrowIfNull(cell);
            if ((uint)cell.Location.X >= (uint)width || (uint)cell.Location.Y >= (uint)height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cells),
                    $"Cell '{cell.Id}' is outside the panel.");
            }

            var index = cell.Location.Y * width + cell.Location.X;
            if (rowMajor[index] is not null)
            {
                throw new ArgumentException(
                    $"More than one cell uses coordinate ({cell.Location.X}, {cell.Location.Y}).",
                    nameof(cells));
            }

            if (!cellIds.Add(cell.Id.Value))
            {
                throw new ArgumentException($"Cell identifier '{cell.Id}' is duplicated.", nameof(cells));
            }

            if (cell.PortId is { } portId)
            {
                if (cell.Kind == CellKind.InputPort)
                {
                    inputPortIds.Add(portId.Value);
                }
                else if (cell.Kind == CellKind.OutputPort && !outputPortIds.Add(portId.Value))
                {
                    throw new ArgumentException($"Output port identifier '{portId}' is duplicated.", nameof(cells));
                }
            }

            rowMajor[index] = cell;
        }

        if (inputPortIds.Overlaps(outputPortIds))
        {
            throw new ArgumentException("An input and output port cannot share an identifier.", nameof(cells));
        }

        return new PanelDefinition(id, width, height, rowMajor.ToImmutableArray());
    }
}

internal readonly record struct CellPortSpec(
    string Name,
    CardinalDirection Direction,
    bool CanReceive,
    bool CanDrive,
    string Lane);

internal static class PanelCellPorts
{
    internal static ImmutableArray<CellPortSpec> For(
        PanelCellDefinition cell,
        ImmutableArray<CustomCellPort> customPorts = default)
    {
        var facing = cell.Orientation;
        var opposite = Opposite(facing);
        return cell.Kind switch
        {
            CellKind.Empty => [],
            CellKind.Wire => WirePorts(facing),
            CellKind.Junction => CardinalPorts("junction"),
            CellKind.Crossing => CrossingPorts(),
            CellKind.Constant or CellKind.InputPort or CellKind.Clock =>
                [new CellPortSpec("out", facing, false, true, "out")],
            CellKind.OutputPort or CellKind.Probe =>
                [new CellPortSpec("in", opposite, true, false, "in")],
            CellKind.Nand =>
            [
                new CellPortSpec("a", CounterClockwise(facing), true, false, "a"),
                new CellPortSpec("b", Clockwise(facing), true, false, "b"),
                new CellPortSpec("out", facing, false, true, "out")
            ],
            CellKind.DFlipFlop =>
            [
                new CellPortSpec("data", opposite, true, false, "data"),
                new CellPortSpec("clock", Clockwise(facing), true, false, "clock"),
                new CellPortSpec("out", facing, false, true, "out")
            ],
            CellKind.StabilityFilter =>
            [
                new CellPortSpec("in", opposite, true, false, "in"),
                new CellPortSpec("out", facing, false, true, "out")
            ],
            CellKind.Custom => CustomPorts(cell, customPorts),
            _ => throw new ArgumentOutOfRangeException(nameof(cell), cell.Kind, "Cell kind is not supported.")
        };
    }

    private static ImmutableArray<CellPortSpec> CustomPorts(
        PanelCellDefinition cell,
        ImmutableArray<CustomCellPort> ports)
    {
        if (ports.IsDefault)
        {
            throw new InvalidOperationException("Custom cell ports are not registered.");
        }

        var orientation = cell.Orientation;
        var facing = CardinalDirection.East;
        var result = ImmutableArray.CreateBuilder<CellPortSpec>(ports.Length);
        foreach (var port in ports)
        {
            facing = CardinalDirection.East;
            var direction = port.Direction;
            while (facing != orientation)
            {
                direction = Clockwise(direction);
                facing = Clockwise(facing);
            }

            result.Add(new CellPortSpec(port.Name, direction, port.CanReceive, port.CanDrive, port.Name));
        }

        return result.ToImmutable();
    }

    internal static CardinalDirection Opposite(CardinalDirection direction) =>
        (CardinalDirection)(((int)direction + 2) % 4);

    private static CardinalDirection Clockwise(CardinalDirection direction) =>
        (CardinalDirection)(((int)direction + 1) % 4);

    private static CardinalDirection CounterClockwise(CardinalDirection direction) =>
        (CardinalDirection)(((int)direction + 3) % 4);

    private static ImmutableArray<CellPortSpec> WirePorts(CardinalDirection orientation)
    {
        var first = orientation is CardinalDirection.East or CardinalDirection.West
            ? CardinalDirection.East
            : CardinalDirection.North;
        var second = Opposite(first);
        return
        [
            Bidirectional(DirectionName(first), first, "wire"),
            Bidirectional(DirectionName(second), second, "wire")
        ];
    }

    private static ImmutableArray<CellPortSpec> CardinalPorts(string lane) =>
    [
        Bidirectional("north", CardinalDirection.North, lane),
        Bidirectional("east", CardinalDirection.East, lane),
        Bidirectional("south", CardinalDirection.South, lane),
        Bidirectional("west", CardinalDirection.West, lane)
    ];

    private static ImmutableArray<CellPortSpec> CrossingPorts() =>
    [
        Bidirectional("north", CardinalDirection.North, "north-south"),
        Bidirectional("south", CardinalDirection.South, "north-south"),
        Bidirectional("east", CardinalDirection.East, "east-west"),
        Bidirectional("west", CardinalDirection.West, "east-west")
    ];

    private static CellPortSpec Bidirectional(string name, CardinalDirection direction, string lane) =>
        new(name, direction, true, true, lane);

    private static string DirectionName(CardinalDirection direction) => direction switch
    {
        CardinalDirection.North => "north",
        CardinalDirection.East => "east",
        CardinalDirection.South => "south",
        CardinalDirection.West => "west",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Direction must be cardinal.")
    };
}

internal readonly record struct CellRuntimeParameters(
    LogicValue ConstantValue,
    int DelayTicks,
    int HighTicks,
    int LowTicks,
    int SetupTicks,
    int ClockToOutputTicks,
    int ConsecutiveTicks)
{
    internal static CellRuntimeParameters Parse(
        CellKind kind,
        PortId? portId,
        ImmutableSortedDictionary<string, string> parameters)
    {
        var inputOrOutputPort = kind is CellKind.InputPort or CellKind.OutputPort;
        if (inputOrOutputPort != portId.HasValue)
        {
            throw new ArgumentException(
                inputOrOutputPort
                    ? "Panel input and output cells require a port identifier."
                    : "Only panel input and output cells may have a port identifier.",
                nameof(portId));
        }

        var allowed = kind switch
        {
            CellKind.Constant => new[] { "value" },
            CellKind.Nand => new[] { "delay" },
            CellKind.Clock => new[] { "high-ticks", "low-ticks" },
            CellKind.DFlipFlop => new[] { "setup-ticks", "hold-ticks", "clock-to-output-ticks" },
            CellKind.StabilityFilter => new[] { "consecutive-ticks" },
            _ => Array.Empty<string>()
        };

        foreach (var parameter in parameters)
        {
            if (!StableData.IsParameterName(parameter.Key))
            {
                throw new ArgumentException($"Parameter name '{parameter.Key}' is invalid.", nameof(parameters));
            }

            if (parameter.Value is null)
            {
                throw new ArgumentException($"Parameter '{parameter.Key}' has no value.", nameof(parameters));
            }

            if (kind != CellKind.Custom && !allowed.Contains(parameter.Key, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Parameter '{parameter.Key}' is not valid for cell kind '{kind}'.",
                    nameof(parameters));
            }
        }

        if (kind == CellKind.Custom)
        {
            return default;
        }

        var constantValue = LogicValue.Unknown;
        if (kind == CellKind.Constant)
        {
            if (!parameters.TryGetValue("value", out var value) || !TryParseLogicValue(value, out constantValue))
            {
                throw new ArgumentException("Constants require a four-state value parameter.", nameof(parameters));
            }
        }

        var delayTicks = ReadPositive(parameters, "delay", 1, required: false);
        var highTicks = ReadPositive(parameters, "high-ticks", 1, required: kind == CellKind.Clock);
        var lowTicks = ReadPositive(parameters, "low-ticks", 1, required: kind == CellKind.Clock);
        var setupTicks = ReadPositive(parameters, "setup-ticks", 1, required: false);
        var holdTicks = ReadNonNegative(parameters, "hold-ticks", 0);
        var clockToOutputTicks = ReadPositive(parameters, "clock-to-output-ticks", 1, required: false);
        var consecutiveTicks = ReadPositive(parameters, "consecutive-ticks", 1, required: false);

        if (kind == CellKind.DFlipFlop && holdTicks != 0)
        {
            throw new ArgumentException(
                "Nonzero D flip-flop hold time is not supported.",
                nameof(parameters));
        }

        return new CellRuntimeParameters(
            constantValue,
            delayTicks,
            highTicks,
            lowTicks,
            setupTicks,
            clockToOutputTicks,
            consecutiveTicks);
    }

    private static int ReadPositive(
        ImmutableSortedDictionary<string, string> parameters,
        string name,
        int defaultValue,
        bool required)
    {
        if (!parameters.TryGetValue(name, out var value))
        {
            if (required)
            {
                throw new ArgumentException($"Parameter '{name}' is required.", nameof(parameters));
            }

            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new ArgumentException($"Parameter '{name}' must be a positive integer.", nameof(parameters));
        }

        return parsed;
    }

    private static int ReadNonNegative(
        ImmutableSortedDictionary<string, string> parameters,
        string name,
        int defaultValue)
    {
        if (!parameters.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"Parameter '{name}' must be a nonnegative integer.", nameof(parameters));
        }

        return parsed;
    }

    private static bool TryParseLogicValue(string value, out LogicValue parsed)
    {
        parsed = value switch
        {
            "Low" => LogicValue.Low,
            "High" => LogicValue.High,
            "Unknown" => LogicValue.Unknown,
            "HighImpedance" => LogicValue.HighImpedance,
            _ => (LogicValue)(-1)
        };
        return Enum.IsDefined(parsed);
    }
}

internal static partial class StableData
{
    internal static bool IsStableId(string value) => StableIdPattern().IsMatch(value);

    internal static bool IsParameterName(string value) => ParameterNamePattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?(?::[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdPattern();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNamePattern();
}
