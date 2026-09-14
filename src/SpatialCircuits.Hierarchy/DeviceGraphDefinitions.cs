using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.Hierarchy;

public enum DevicePortDirection
{
    Input,
    Output
}

public enum DeviceValueContract
{
    BinaryLogic,
    FourStateLogic
}

public sealed record DevicePortDefinition
{
    private DevicePortDefinition(
        string name,
        DevicePortDirection direction,
        int width,
        DeviceValueContract valueContract)
    {
        Name = name;
        Direction = direction;
        Width = width;
        ValueContract = valueContract;
    }

    public string Name { get; }

    public DevicePortDirection Direction { get; }

    public int Width { get; }

    public DeviceValueContract ValueContract { get; }

    public static DevicePortDefinition Create(
        string name,
        DevicePortDirection direction,
        int width = 1,
        DeviceValueContract valueContract = DeviceValueContract.FourStateLogic)
    {
        if (!ChipData.IsPortName(name) || !Enum.IsDefined(direction) ||
            width <= 0 || !Enum.IsDefined(valueContract))
        {
            throw new ArgumentException("Device port contract is invalid.");
        }

        return new DevicePortDefinition(name, direction, width, valueContract);
    }

    internal void ValidateSignal(DeviceSignal signal, string parameterName)
    {
        if (signal.Bits.IsDefaultOrEmpty || signal.Bits.Length != Width ||
            (ValueContract == DeviceValueContract.BinaryLogic &&
             signal.Bits.Contains(LogicValue.Unknown)))
        {
            throw new ArgumentException(
                $"Signal does not satisfy port '{Name}' width or value contract.",
                parameterName);
        }
    }
}

public readonly struct DeviceSignal : IEquatable<DeviceSignal>
{
    private DeviceSignal(ImmutableArray<LogicValue> bits) => Bits = bits;

    public ImmutableArray<LogicValue> Bits { get; }

    public static DeviceSignal Create(IEnumerable<LogicValue> bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        var values = bits.ToImmutableArray();
        if (values.IsDefaultOrEmpty || values.Any(value => !Enum.IsDefined(value)))
        {
            throw new ArgumentException("Device signal must contain supported logic values.", nameof(bits));
        }

        return new DeviceSignal(values);
    }

    public static DeviceSignal Scalar(LogicValue value) => Create([value]);

    public bool Equals(DeviceSignal other) => Bits.AsSpan().SequenceEqual(other.Bits.AsSpan());

    public override bool Equals(object? obj) => obj is DeviceSignal other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var bit in Bits)
        {
            hash.Add(bit);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Concat(Bits.Select(value => value switch
    {
        LogicValue.Low => "0",
        LogicValue.High => "1",
        LogicValue.Unknown => "X",
        LogicValue.HighImpedance => "Z",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Logic value is unsupported.")
    }));

    public static bool TryParse(string? value, int width, out DeviceSignal signal)
    {
        if (width <= 0)
        {
            signal = default;
            return false;
        }

        var bits = ImmutableArray.CreateBuilder<LogicValue>(width);
        foreach (var character in value ?? string.Empty)
        {
            if (character is '0')
            {
                bits.Add(LogicValue.Low);
            }
            else if (character is '1')
            {
                bits.Add(LogicValue.High);
            }
            else if (character is 'X')
            {
                bits.Add(LogicValue.Unknown);
            }
            else if (character is 'Z')
            {
                bits.Add(LogicValue.HighImpedance);
            }
            else
            {
                signal = default;
                return false;
            }
        }

        if (bits.Count != width)
        {
            signal = default;
            return false;
        }

        signal = new DeviceSignal(bits.MoveToImmutable());
        return true;
    }
}

public abstract class DeviceBackendDefinition
{
    internal DeviceBackendDefinition(ImmutableArray<DevicePortDefinition> ports) => Ports = ports;

    public ImmutableArray<DevicePortDefinition> Ports { get; }
}

public sealed class PanelDeviceBackendDefinition : DeviceBackendDefinition
{
    private PanelDeviceBackendDefinition(PanelDefinition panel, ImmutableArray<DevicePortDefinition> ports)
        : base(ports) => Panel = panel;

    public PanelDefinition Panel { get; }

    public static PanelDeviceBackendDefinition Create(PanelDefinition panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var ports = panel.Cells
            .OfType<PanelCellDefinition>()
            .Where(cell => cell.Kind is CellKind.InputPort or CellKind.OutputPort)
            .GroupBy(cell => cell.PortId!.Value.Value, StringComparer.Ordinal)
            .Select(group =>
            {
                var directions = group.Select(cell => cell.Kind == CellKind.InputPort
                        ? DevicePortDirection.Input
                        : DevicePortDirection.Output)
                    .Distinct()
                    .ToArray();
                if (directions.Length != 1)
                {
                    throw new ArgumentException(
                        $"Panel port '{group.Key}' has more than one direction.",
                        nameof(panel));
                }

                return DevicePortDefinition.Create(group.Key, directions[0]);
            })
            .OrderBy(port => port.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        if (ports.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Panel device must expose at least one named port.", nameof(panel));
        }

        return new PanelDeviceBackendDefinition(panel, ports);
    }
}

public sealed record TimedOutputChange(long DelayTicks, string PortName, DeviceSignal Value);

public sealed class TimedDeviceBackendDefinition : DeviceBackendDefinition
{
    private TimedDeviceBackendDefinition(
        ImmutableArray<DevicePortDefinition> ports,
        ImmutableArray<TimedOutputChange> changes)
        : base(ports) => Changes = changes;

    public ImmutableArray<TimedOutputChange> Changes { get; }

    public static TimedDeviceBackendDefinition Create(
        IEnumerable<DevicePortDefinition> ports,
        IEnumerable<TimedOutputChange> changes)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(changes);
        var portList = ports.ToArray();
        if (portList.Length == 0 || portList.Any(port => port is null) ||
            portList.Select(port => port.Name).Distinct(StringComparer.Ordinal).Count() != portList.Length ||
            !portList.Any(port => port.Direction == DevicePortDirection.Output))
        {
            throw new ArgumentException(
                "Timed backend ports must be non-empty, uniquely named, and include an output port.",
                nameof(ports));
        }

        var orderedPorts = portList.OrderBy(port => port.Name, StringComparer.Ordinal).ToImmutableArray();
        var byName = orderedPorts.ToDictionary(port => port.Name, StringComparer.Ordinal);
        var changeList = changes.ToArray();
        if (changeList.Any(change => change is null))
        {
            throw new ArgumentException("Timed output changes cannot contain null.", nameof(changes));
        }

        foreach (var change in changeList)
        {
            if (change.DelayTicks <= 0 ||
                !ChipData.IsPortName(change.PortName) ||
                !byName.TryGetValue(change.PortName, out var port) ||
                port.Direction != DevicePortDirection.Output)
            {
                throw new ArgumentException("Timed output change has an invalid tick or port.", nameof(changes));
            }

            port.ValidateSignal(change.Value, nameof(changes));
        }

        var orderedChanges = changeList
            .OrderBy(change => change.DelayTicks)
            .ThenBy(change => change.PortName, StringComparer.Ordinal)
            .ToImmutableArray();
        if (orderedChanges.GroupBy(change => (change.DelayTicks, change.PortName)).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Timed output changes cannot duplicate a port and tick.", nameof(changes));
        }

        return new TimedDeviceBackendDefinition(orderedPorts, orderedChanges);
    }
}

public sealed class NodeDeviceBackendDefinition : DeviceBackendDefinition
{
    internal const string TargetStableIdPrefix = "node-backend:";

    private NodeDeviceBackendDefinition(ImmutableArray<DevicePortDefinition> ports) : base(ports)
    {
    }

    internal static string TargetStableId(ComponentId deviceId) => TargetStableIdPrefix + deviceId.Value;

    public static NodeDeviceBackendDefinition Create(IEnumerable<DevicePortDefinition> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var portList = ports.ToArray();
        if (portList.Length == 0 || portList.Any(port => port is null) ||
            portList.Select(port => port.Name).Distinct(StringComparer.Ordinal).Count() != portList.Length ||
            !portList.Any(port => port.Direction == DevicePortDirection.Output))
        {
            throw new ArgumentException(
                "Node backend ports must be non-empty, uniquely named, and include an output port.",
                nameof(ports));
        }

        return new NodeDeviceBackendDefinition(
            portList.OrderBy(port => port.Name, StringComparer.Ordinal).ToImmutableArray());
    }
}

public sealed class DeviceDefinition
{
    private DeviceDefinition(ComponentId id, DeviceBackendDefinition backend)
    {
        Id = id;
        Backend = backend;
    }

    public ComponentId Id { get; }

    public DeviceBackendDefinition Backend { get; }

    public ImmutableArray<DevicePortDefinition> Ports => Backend.Ports;

    public static DeviceDefinition Create(ComponentId id, DeviceBackendDefinition backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (!ChipData.IsStableId(id.Value))
        {
            throw new ArgumentException("Device identifier is not stable data.", nameof(id));
        }

        return new DeviceDefinition(id, backend);
    }

    internal DeviceDefinition WithBackend(DeviceBackendDefinition backend)
    {
        var replacement = Create(Id, backend);
        if (!Ports.SequenceEqual(replacement.Ports))
        {
            throw new ArgumentException("Replacement backend has an incompatible port contract.", nameof(backend));
        }

        return replacement;
    }
}

public readonly record struct DevicePortEndpoint(ComponentId DeviceId, string PortName)
{
    public static DevicePortEndpoint Create(ComponentId deviceId, string portName)
    {
        if (!ChipData.IsStableId(deviceId.Value) || !ChipData.IsPortName(portName))
        {
            throw new ArgumentException("Device port endpoint is invalid.");
        }

        return new DevicePortEndpoint(deviceId, portName);
    }
}

public sealed record CableLaneDefinition(
    ComponentId Id,
    DevicePortEndpoint Source,
    DevicePortEndpoint Target,
    long Latency)
{
    public static CableLaneDefinition Create(
        ComponentId id,
        DevicePortEndpoint source,
        DevicePortEndpoint target,
        long latency)
    {
        if (!ChipData.IsStableId(id.Value) || latency <= 0 ||
            !ChipData.IsStableId(source.DeviceId.Value) ||
            !ChipData.IsPortName(source.PortName) ||
            !ChipData.IsStableId(target.DeviceId.Value) ||
            !ChipData.IsPortName(target.PortName))
        {
            throw new ArgumentException("Cable lane identifier or latency is invalid.");
        }

        return new CableLaneDefinition(id, source, target, latency);
    }
}

public sealed record CableBundleDefinition
{
    private CableBundleDefinition(ComponentId id, ImmutableArray<ComponentId> laneIds)
    {
        Id = id;
        LaneIds = laneIds;
    }

    public ComponentId Id { get; }

    public ImmutableArray<ComponentId> LaneIds { get; }

    public static CableBundleDefinition Create(ComponentId id, IEnumerable<ComponentId> laneIds)
    {
        ArgumentNullException.ThrowIfNull(laneIds);
        var copied = laneIds.ToImmutableArray();
        if (!ChipData.IsStableId(id.Value) || copied.IsDefaultOrEmpty ||
            copied.Any(laneId => !ChipData.IsStableId(laneId.Value)) ||
            copied.Distinct().Count() != copied.Length)
        {
            throw new ArgumentException("Cable bundle identifier or lane list is invalid.", nameof(laneIds));
        }

        return new CableBundleDefinition(id, copied.OrderBy(laneId => laneId.Value, StringComparer.Ordinal).ToImmutableArray());
    }
}

public sealed class DeviceGraphDefinition
{
    private DeviceGraphDefinition(
        DefinitionId id,
        ImmutableArray<DeviceDefinition> devices,
        ImmutableArray<CableLaneDefinition> lanes,
        ImmutableArray<CableBundleDefinition> bundles)
    {
        Id = id;
        Devices = devices;
        Lanes = lanes;
        Bundles = bundles;
    }

    public DefinitionId Id { get; }

    public ImmutableArray<DeviceDefinition> Devices { get; }

    public ImmutableArray<CableLaneDefinition> Lanes { get; }

    public ImmutableArray<CableBundleDefinition> Bundles { get; }

    public static DeviceGraphDefinition Create(
        DefinitionId id,
        IEnumerable<DeviceDefinition> devices,
        IEnumerable<CableLaneDefinition> lanes,
        IEnumerable<CableBundleDefinition>? bundles = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(lanes);
        if (!ChipData.IsStableId(id.Value))
        {
            throw new ArgumentException("Device graph identifier is not stable data.", nameof(id));
        }

        var deviceList = devices.ToArray();
        var laneList = lanes.ToArray();
        var bundleList = (bundles ?? []).ToArray();
        if (deviceList.Length == 0 || deviceList.Any(device => device is null) ||
            laneList.Any(lane => lane is null) || bundleList.Any(bundle => bundle is null))
        {
            throw new ArgumentException("Device graph definitions must have unique, non-null members.");
        }

        var copiedDevices = deviceList.OrderBy(device => device.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        var copiedLanes = laneList.OrderBy(lane => lane.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        var copiedBundles = bundleList
            .OrderBy(bundle => bundle.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        if (copiedDevices.IsDefaultOrEmpty ||
            copiedDevices.Select(device => device.Id).Distinct().Count() != copiedDevices.Length ||
            copiedLanes.Select(lane => lane.Id).Distinct().Count() != copiedLanes.Length ||
            copiedBundles.Select(bundle => bundle.Id).Distinct().Count() != copiedBundles.Length ||
            copiedLanes.Any(lane => !ChipData.IsStableId(lane.Id.Value) || lane.Latency <= 0) ||
            copiedDevices.Select(device => device.Id.Value)
                .Concat(copiedLanes.Select(lane => lane.Id.Value))
                .Distinct(StringComparer.Ordinal)
                .Count() != copiedDevices.Length + copiedLanes.Length ||
            copiedLanes.Select(lane => lane.Target).Distinct().Count() != copiedLanes.Length)
        {
            throw new ArgumentException(
                "Device graph definitions must have unique identifiers, valid lanes, and one lane per input.");
        }

        var graphTargetIds = copiedDevices.Select(device => device.Id.Value)
            .Concat(copiedLanes.Select(lane => lane.Id.Value))
            .ToHashSet(StringComparer.Ordinal);
        if (copiedDevices.Where(device => device.Backend is NodeDeviceBackendDefinition)
            .Select(device => NodeDeviceBackendDefinition.TargetStableId(device.Id))
            .Any(graphTargetIds.Contains))
        {
            throw new ArgumentException("Device or lane identifiers cannot collide with Node backend targets.");
        }

        var deviceById = copiedDevices.ToDictionary(device => device.Id);
        var laneById = copiedLanes.ToDictionary(lane => lane.Id);
        foreach (var lane in copiedLanes)
        {
            var sourcePort = FindPort(deviceById, lane.Source, DevicePortDirection.Output, nameof(lanes));
            var targetPort = FindPort(deviceById, lane.Target, DevicePortDirection.Input, nameof(lanes));
            if (sourcePort.Width != targetPort.Width || sourcePort.ValueContract != targetPort.ValueContract)
            {
                throw new ArgumentException(
                    $"Cable lane '{lane.Id}' connects incompatible port contracts.",
                    nameof(lanes));
            }
        }

        foreach (var bundle in copiedBundles)
        {
            if (bundle.LaneIds.Any(laneId => !laneById.ContainsKey(laneId)))
            {
                throw new ArgumentException(
                    $"Cable bundle '{bundle.Id}' references a missing lane.",
                    nameof(bundles));
            }
        }

        return new DeviceGraphDefinition(id, copiedDevices, copiedLanes, copiedBundles);
    }

    internal DeviceGraphDefinition WithBackend(ComponentId deviceId, DeviceBackendDefinition backend)
    {
        var found = false;
        var devices = Devices.Select(device =>
        {
            if (device.Id != deviceId)
            {
                return device;
            }

            found = true;
            return device.WithBackend(backend);
        }).ToArray();
        if (!found)
        {
            throw new KeyNotFoundException($"Device '{deviceId}' is not in this graph.");
        }

        return Create(Id, devices, Lanes, Bundles);
    }

    internal DeviceDefinition GetDevice(ComponentId id) =>
        Devices.FirstOrDefault(device => device.Id == id) ??
        throw new KeyNotFoundException($"Device '{id}' is not in this graph.");

    private static DevicePortDefinition FindPort(
        IReadOnlyDictionary<ComponentId, DeviceDefinition> devices,
        DevicePortEndpoint endpoint,
        DevicePortDirection direction,
        string parameterName)
    {
        if (!ChipData.IsStableId(endpoint.DeviceId.Value) || !ChipData.IsPortName(endpoint.PortName))
        {
            throw new ArgumentException("Cable endpoint is invalid.", parameterName);
        }

        if (!devices.TryGetValue(endpoint.DeviceId, out var device))
        {
            throw new ArgumentException(
                $"Cable endpoint device '{endpoint.DeviceId}' does not exist.",
                parameterName);
        }

        var port = device.Ports.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, endpoint.PortName, StringComparison.Ordinal));
        if (port is null || port.Direction != direction)
        {
            throw new ArgumentException(
                $"Cable endpoint '{endpoint.DeviceId}.{endpoint.PortName}' has the wrong direction or is missing.",
                parameterName);
        }

        return port;
    }
}
