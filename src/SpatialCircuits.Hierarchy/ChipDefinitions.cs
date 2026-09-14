using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.Hierarchy;

public enum ChipPortDirection
{
    Input,
    Output
}

public enum ChipPresentationMode
{
    ExpandedGrid,
    Collapsed
}

public sealed record ChipPortDefinition(string Name, PortId PanelPortId, ChipPortDirection Direction);

public sealed record ChipDefinitionDependency(DefinitionId DefinitionId, string ContentHash);

public sealed record ChipPortConnection(ChipPortEndpoint Source, ChipPortEndpoint Target);

public readonly record struct ChipPortEndpoint
{
    private ChipPortEndpoint(ComponentId? instanceId, string portName)
    {
        InstanceId = instanceId;
        PortName = portName;
    }

    /// <summary>Null identifies a port on the owning panel. Otherwise this identifies a child chip.</summary>
    public ComponentId? InstanceId { get; }

    /// <summary>A panel PortId for the owner, or a named chip port for a child.</summary>
    public string PortName { get; }

    public bool IsParentPanel => InstanceId is null;

    public static ChipPortEndpoint ParentPanel(PortId portId)
    {
        if (!ChipData.IsStableId(portId.Value))
        {
            throw new ArgumentException("Panel port identifier is not stable data.", nameof(portId));
        }

        return new ChipPortEndpoint(null, portId.Value);
    }

    public static ChipPortEndpoint ChildChip(ComponentId instanceId, string portName)
    {
        if (!ChipData.IsStableId(instanceId.Value))
        {
            throw new ArgumentException("Chip instance identifier is not stable data.", nameof(instanceId));
        }

        if (!ChipData.IsPortName(portName))
        {
            throw new ArgumentException("Chip port name is invalid.", nameof(portName));
        }

        return new ChipPortEndpoint(instanceId, portName);
    }
}

public sealed record ChipInstanceDefinition
{
    private ChipInstanceDefinition(ComponentId instanceId, DefinitionId definitionId, string contentHash)
    {
        InstanceId = instanceId;
        DefinitionId = definitionId;
        ContentHash = contentHash;
    }

    public ComponentId InstanceId { get; }

    public DefinitionId DefinitionId { get; }

    public string ContentHash { get; }

    public static ChipInstanceDefinition Create(
        ComponentId instanceId,
        DefinitionId definitionId,
        string contentHash)
    {
        if (!ChipData.IsStableId(instanceId.Value))
        {
            throw new ArgumentException("Chip instance identifier is not stable data.", nameof(instanceId));
        }

        if (!ChipData.IsStableId(definitionId.Value))
        {
            throw new ArgumentException("Chip definition identifier is not stable data.", nameof(definitionId));
        }

        if (!ChipData.IsContentHash(contentHash))
        {
            throw new ArgumentException("Chip content hash must be a SHA-256 hex value.", nameof(contentHash));
        }

        return new ChipInstanceDefinition(instanceId, definitionId, contentHash);
    }
}

/// <summary>A panel-owned child network. Its connections sit outside the owner's row-major cell grid.</summary>
public sealed class PanelOwnedChipNetworkDefinition
{
    private PanelOwnedChipNetworkDefinition(
        CircuitId ownerPanelId,
        ImmutableArray<ChipInstanceDefinition> instances,
        ImmutableArray<ChipPortConnection> connections)
    {
        OwnerPanelId = ownerPanelId;
        Instances = instances;
        Connections = connections;
    }

    public CircuitId OwnerPanelId { get; }

    public ImmutableArray<ChipInstanceDefinition> Instances { get; }

    public ImmutableArray<ChipPortConnection> Connections { get; }

    public static PanelOwnedChipNetworkDefinition Create(
        CircuitId ownerPanelId,
        IEnumerable<ChipInstanceDefinition> instances,
        IEnumerable<ChipPortConnection>? connections = null)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (!ChipData.IsStableId(ownerPanelId.Value))
        {
            throw new ArgumentException("Owner panel identifier is not stable data.", nameof(ownerPanelId));
        }

        var instanceList = instances.ToArray();
        if (instanceList.Any(instance => instance is null))
        {
            throw new ArgumentException("Chip network instances cannot contain null.", nameof(instances));
        }

        var copiedInstances = instanceList
            .OrderBy(instance => instance.InstanceId.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        if (copiedInstances.Select(instance => instance.InstanceId.Value).Distinct(StringComparer.Ordinal).Count() !=
            copiedInstances.Length)
        {
            throw new ArgumentException("Chip instance identifiers must be unique in a child network.", nameof(instances));
        }

        var connectionList = (connections ?? []).ToArray();
        if (connectionList.Any(connection => connection is null))
        {
            throw new ArgumentException("Chip network connections cannot contain null.", nameof(connections));
        }

        foreach (var connection in connectionList)
        {
            if (connection.Source.PortName is null || connection.Target.PortName is null ||
                (connection.Source.InstanceId is { } sourceId && !ChipData.IsStableId(sourceId.Value)) ||
                (connection.Target.InstanceId is { } targetId && !ChipData.IsStableId(targetId.Value)) ||
                (connection.Source.IsParentPanel
                    ? !ChipData.IsStableId(connection.Source.PortName)
                    : !ChipData.IsPortName(connection.Source.PortName)) ||
                (connection.Target.IsParentPanel
                    ? !ChipData.IsStableId(connection.Target.PortName)
                    : !ChipData.IsPortName(connection.Target.PortName)))
            {
                throw new ArgumentException("Chip network connection endpoint is invalid.", nameof(connections));
            }
        }

        var copiedConnections = connectionList
            .OrderBy(ChipData.ConnectionSortKey, StringComparer.Ordinal)
            .ToImmutableArray();

        return new PanelOwnedChipNetworkDefinition(ownerPanelId, copiedInstances, copiedConnections);
    }

    /// <summary>Validates named endpoints, direction, child pins, and single-driver targets.</summary>
    public ImmutableArray<Diagnostic> Validate(PanelDefinition ownerPanel, ChipDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(ownerPanel);
        ArgumentNullException.ThrowIfNull(catalog);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (ownerPanel.Id != OwnerPanelId)
        {
            diagnostics.Add(ChipData.Error(
                ChipDiagnosticCodes.OwnerPanelMismatch,
                "network.ownerPanelId",
                "Child network owner does not match the supplied panel."));
            return diagnostics.ToImmutable();
        }

        var instances = Instances.ToDictionary(instance => instance.InstanceId.Value, StringComparer.Ordinal);
        var drivenTargets = new HashSet<ChipPortEndpoint>();
        for (var index = 0; index < Connections.Length; index++)
        {
            var connection = Connections[index];
            var path = $"network.connections[{index}]";
            var sourceDirection = ResolveDirection(connection.Source, ownerPanel, instances, catalog);
            var targetDirection = ResolveDirection(connection.Target, ownerPanel, instances, catalog);
            if (sourceDirection is null)
            {
                diagnostics.Add(ChipData.Error(
                    ChipDiagnosticCodes.PortMissing,
                    $"{path}.source",
                    "Source endpoint does not name a defined panel or chip port."));
            }
            else if (sourceDirection != ChipPortDirection.Output)
            {
                diagnostics.Add(ChipData.Error(
                    ChipDiagnosticCodes.PortDirection,
                    $"{path}.source",
                    "Connection sources must be output ports."));
            }

            if (targetDirection is null)
            {
                diagnostics.Add(ChipData.Error(
                    ChipDiagnosticCodes.PortMissing,
                    $"{path}.target",
                    "Target endpoint does not name a defined panel or chip port."));
            }
            else if (targetDirection != ChipPortDirection.Input)
            {
                diagnostics.Add(ChipData.Error(
                    ChipDiagnosticCodes.PortDirection,
                    $"{path}.target",
                    "Connection targets must be input ports."));
            }

            if (!drivenTargets.Add(connection.Target))
            {
                diagnostics.Add(ChipData.Error(
                    ChipDiagnosticCodes.TargetAlreadyConnected,
                    $"{path}.target",
                    "A named input port may have only one child-network connection."));
            }
        }

        return diagnostics.ToImmutable();
    }

    internal static ChipPortDirection? ResolveDirection(
        ChipPortEndpoint endpoint,
        PanelDefinition ownerPanel,
        IReadOnlyDictionary<string, ChipInstanceDefinition> instances,
        ChipDefinitionCatalog catalog)
    {
        if (string.IsNullOrEmpty(endpoint.PortName))
        {
            return null;
        }

        if (endpoint.InstanceId is not { } instanceId)
        {
            var matches = ownerPanel.Cells
                .OfType<PanelCellDefinition>()
                .Where(cell => cell.PortId?.Value == endpoint.PortName)
                .Select(cell => cell.Kind switch
                {
                    CellKind.InputPort => ChipPortDirection.Input,
                    CellKind.OutputPort => ChipPortDirection.Output,
                    _ => (ChipPortDirection?)null
                })
                .Where(direction => direction.HasValue)
                .Select(direction => direction!.Value)
                .Distinct()
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        if (!instances.TryGetValue(instanceId.Value, out var instance) ||
            !catalog.TryResolve(instance.DefinitionId, instance.ContentHash, out var definition) ||
            definition is null)
        {
            return null;
        }

        return definition.Ports
            .FirstOrDefault(port => string.Equals(port.Name, endpoint.PortName, StringComparison.Ordinal))?
            .Direction;
    }
}

public sealed class ChipDefinition
{
    private ChipDefinition(
        DefinitionId id,
        PanelDefinition sourcePanel,
        ImmutableArray<ChipPortDefinition> ports,
        int schemaVersion,
        int behaviorVersion,
        string symbol,
        ImmutableSortedDictionary<string, string> parameters,
        PanelOwnedChipNetworkDefinition childNetwork,
        string contentHash)
    {
        Id = id;
        SourcePanel = sourcePanel;
        Ports = ports;
        SchemaVersion = schemaVersion;
        BehaviorVersion = behaviorVersion;
        Symbol = symbol;
        Parameters = parameters;
        ChildNetwork = childNetwork;
        Dependencies = childNetwork.Instances
            .Select(instance => new ChipDefinitionDependency(instance.DefinitionId, instance.ContentHash))
            .Distinct()
            .OrderBy(dependency => dependency.DefinitionId.Value, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.ContentHash, StringComparer.Ordinal)
            .ToImmutableArray();
        ContentHash = contentHash;
    }

    public DefinitionId Id { get; }

    public PanelDefinition SourcePanel { get; }

    public ImmutableArray<ChipPortDefinition> Ports { get; }

    public int SchemaVersion { get; }

    public int BehaviorVersion { get; }

    public string Symbol { get; }

    public ImmutableSortedDictionary<string, string> Parameters { get; }

    public PanelOwnedChipNetworkDefinition ChildNetwork { get; }

    public ImmutableArray<ChipDefinitionDependency> Dependencies { get; }

    public string ContentHash { get; }

    public static ChipDefinition Create(
        DefinitionId id,
        PanelDefinition sourcePanel,
        IEnumerable<ChipPortDefinition> ports,
        int schemaVersion,
        int behaviorVersion,
        string symbol,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        PanelOwnedChipNetworkDefinition? childNetwork = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePanel);
        ArgumentNullException.ThrowIfNull(ports);
        if (!ChipData.IsStableId(id.Value))
        {
            throw new ArgumentException("Chip definition identifier is not stable data.", nameof(id));
        }

        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema version must be positive.");
        }

        if (behaviorVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(behaviorVersion), "Behavior version must be positive.");
        }

        if (!ChipData.IsPortName(symbol))
        {
            throw new ArgumentException("Chip symbol is invalid.", nameof(symbol));
        }

        var portList = ports.ToArray();
        if (portList.Any(port => port is null))
        {
            throw new ArgumentException("Chip ports cannot contain null.", nameof(ports));
        }

        var copiedPorts = portList
            .OrderBy(port => port.Name, StringComparer.Ordinal)
            .ToImmutableArray();

        if (copiedPorts.Length == 0 ||
            copiedPorts.Select(port => port.Name).Distinct(StringComparer.Ordinal).Count() != copiedPorts.Length ||
            copiedPorts.Select(port => port.PanelPortId.Value).Distinct(StringComparer.Ordinal).Count() != copiedPorts.Length)
        {
            throw new ArgumentException("Chip ports must have unique names and panel identifiers.", nameof(ports));
        }

        var panelPorts = sourcePanel.Cells
            .OfType<PanelCellDefinition>()
            .Where(cell => cell.Kind is CellKind.InputPort or CellKind.OutputPort)
            .GroupBy(cell => cell.PortId!.Value.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Kind == CellKind.InputPort
                    ? ChipPortDirection.Input
                    : ChipPortDirection.Output,
                StringComparer.Ordinal);
        foreach (var port in copiedPorts)
        {
            if (!ChipData.IsPortName(port.Name) || !Enum.IsDefined(port.Direction) ||
                !ChipData.IsStableId(port.PanelPortId.Value) ||
                !panelPorts.TryGetValue(port.PanelPortId.Value, out var panelDirection) ||
                panelDirection != port.Direction)
            {
                throw new ArgumentException(
                    $"Chip port '{port.Name}' must map to a source panel port with the same direction.",
                    nameof(ports));
            }
        }

        var parameterList = (parameters ?? []).ToArray();
        if (parameterList.Any(parameter => parameter.Key is null || parameter.Value is null))
        {
            throw new ArgumentException("Chip parameters must have non-null names and values.", nameof(parameters));
        }

        var copiedParameters = parameterList
            .ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var parameter in copiedParameters)
        {
            if (!ChipData.IsParameterName(parameter.Key) || parameter.Value is null)
            {
                throw new ArgumentException("Chip parameters must have valid names and non-null values.", nameof(parameters));
            }
        }

        var copiedNetwork = childNetwork ?? PanelOwnedChipNetworkDefinition.Create(sourcePanel.Id, []);
        if (copiedNetwork.OwnerPanelId != sourcePanel.Id)
        {
            throw new ArgumentException("Chip child network must be owned by its source panel.", nameof(childNetwork));
        }

        var publicInputPortIds = copiedPorts
            .Where(port => port.Direction == ChipPortDirection.Input)
            .Select(port => port.PanelPortId.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (copiedNetwork.Connections.Any(connection =>
                connection.Target.IsParentPanel && publicInputPortIds.Contains(connection.Target.PortName)))
        {
            throw new ArgumentException("A chip child network cannot drive a public input port.", nameof(childNetwork));
        }

        var hash = ChipData.HashDefinition(
            id,
            sourcePanel,
            copiedPorts,
            schemaVersion,
            behaviorVersion,
            symbol,
            copiedParameters,
            copiedNetwork);
        return new ChipDefinition(
            id,
            sourcePanel,
            copiedPorts,
            schemaVersion,
            behaviorVersion,
            symbol,
            copiedParameters,
            copiedNetwork,
            hash);
    }
}

public sealed class ChipDefinitionCatalog
{
    private readonly ImmutableDictionary<(string Id, string Hash), ChipDefinition> _byPin;
    private readonly ImmutableDictionary<string, ImmutableArray<ChipDefinition>> _byId;

    private ChipDefinitionCatalog(
        ImmutableDictionary<(string Id, string Hash), ChipDefinition> byPin,
        ImmutableDictionary<string, ImmutableArray<ChipDefinition>> byId,
        ImmutableArray<ChipDefinition> definitions)
    {
        _byPin = byPin;
        _byId = byId;
        Definitions = definitions;
    }

    public ImmutableArray<ChipDefinition> Definitions { get; }

    public static ChipDefinitionCatalog Create(IEnumerable<ChipDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var supplied = definitions.ToArray();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (supplied.Any(definition => definition is null))
        {
            throw new ChipDefinitionException([
                ChipData.Error(
                    ChipDiagnosticCodes.DefinitionMissing,
                    "definitions",
                    "Chip definition collection cannot contain null.")
            ]);
        }

        var ordered = supplied
            .OrderBy(definition => definition.Id.Value, StringComparer.Ordinal)
            .ThenBy(definition => definition.ContentHash, StringComparer.Ordinal)
            .ToImmutableArray();
        var byPinBuilder = ImmutableDictionary.CreateBuilder<(string Id, string Hash), ChipDefinition>();
        foreach (var definition in ordered)
        {
            if (!byPinBuilder.TryAdd((definition.Id.Value, definition.ContentHash), definition))
            {
                diagnostics.Add(ChipData.Error(
                    ChipDiagnosticCodes.DefinitionDuplicate,
                    $"definitions/{definition.Id.Value}",
                    "The same chip definition pin appears more than once."));
            }
        }

        if (diagnostics.Count > 0)
        {
            throw new ChipDefinitionException(diagnostics.ToImmutable());
        }

        var byPin = byPinBuilder.ToImmutable();
        var byId = ordered
            .GroupBy(definition => definition.Id.Value, StringComparer.Ordinal)
            .ToImmutableDictionary(
                group => group.Key,
                group => group.ToImmutableArray(),
                StringComparer.Ordinal);
        var catalog = new ChipDefinitionCatalog(byPin, byId, ordered);
        catalog.ValidateDependencies(diagnostics);
        foreach (var definition in ordered)
        {
            diagnostics.AddRange(definition.ChildNetwork.Validate(definition.SourcePanel, catalog));
        }

        if (diagnostics.Count > 0)
        {
            throw new ChipDefinitionException(diagnostics.ToImmutable());
        }

        return catalog;
    }

    public bool TryResolve(DefinitionId id, string contentHash, out ChipDefinition? definition)
    {
        if (_byPin.TryGetValue((id.Value, contentHash), out var candidate))
        {
            definition = candidate;
            return true;
        }

        definition = null;
        return false;
    }

    public ChipDefinition Resolve(DefinitionId id, string contentHash)
    {
        if (TryResolve(id, contentHash, out var definition))
        {
            return definition!;
        }

        var code = _byId.ContainsKey(id.Value)
            ? ChipDiagnosticCodes.DefinitionPinMismatch
            : ChipDiagnosticCodes.DefinitionMissing;
        throw new ChipDefinitionException([
            ChipData.Error(code, $"definitions/{id.Value}", "Pinned chip definition is unavailable.")
        ]);
    }

    private void ValidateDependencies(ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var state = new Dictionary<string, byte>(StringComparer.Ordinal);
        var path = new List<ChipDefinition>();
        var cycles = new HashSet<string>(StringComparer.Ordinal);

        void Visit(ChipDefinition definition)
        {
            var currentKey = DefinitionKey(definition);
            state[currentKey] = 1;
            path.Add(definition);
            foreach (var child in definition.ChildNetwork.Instances)
            {
                var dependencyKey = $"{child.DefinitionId.Value}\u001f{child.ContentHash}";
                ChipDefinition? dependency = null;
                if (_byPin.TryGetValue((child.DefinitionId.Value, child.ContentHash), out var exact))
                {
                    dependency = exact;
                }
                else if (_byId.TryGetValue(child.DefinitionId.Value, out var versions) && versions.Length == 1)
                {
                    // A unique unpinned target still participates in cycle diagnosis before its pin error.
                    dependency = versions[0];
                }

                if (dependency is null)
                {
                    continue;
                }

                dependencyKey = DefinitionKey(dependency);
                if (!state.TryGetValue(dependencyKey, out var childState))
                {
                    Visit(dependency);
                }
                else if (childState == 1)
                {
                    var cycleStart = path.FindIndex(item => DefinitionKey(item) == dependencyKey);
                    var cycle = path.Skip(cycleStart).Append(dependency)
                        .Select(FormatCycleNode)
                        .ToArray();
                    var cycleKey = string.Join(" -> ", cycle);
                    if (cycles.Add(cycleKey))
                    {
                        diagnostics.Add(ChipData.Error(
                            ChipDiagnosticCodes.DependencyCycle,
                            $"definitions/{definition.Id.Value}/dependencies",
                            $"Recursive chip dependency cycle: {cycleKey}."));
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[currentKey] = 2;
        }

        foreach (var definition in Definitions)
        {
            if (!state.ContainsKey(DefinitionKey(definition)))
            {
                Visit(definition);
            }
        }

        foreach (var definition in Definitions)
        {
            foreach (var child in definition.ChildNetwork.Instances)
            {
                if (!_byId.TryGetValue(child.DefinitionId.Value, out var versions))
                {
                    diagnostics.Add(ChipData.Error(
                        ChipDiagnosticCodes.DefinitionMissing,
                        $"definitions/{definition.Id.Value}/children/{child.InstanceId.Value}",
                        $"Child definition '{child.DefinitionId}' is unavailable."));
                }
                else if (!_byPin.ContainsKey((child.DefinitionId.Value, child.ContentHash)))
                {
                    diagnostics.Add(ChipData.Error(
                        ChipDiagnosticCodes.DefinitionPinMismatch,
                        $"definitions/{definition.Id.Value}/children/{child.InstanceId.Value}",
                        $"Child definition '{child.DefinitionId}' does not match its pinned content hash."));
                }
            }
        }
    }

    private static string DefinitionKey(ChipDefinition definition) =>
        $"{definition.Id.Value}\u001f{definition.ContentHash}";

    private static string FormatCycleNode(ChipDefinition definition) =>
        $"{definition.Id.Value}@{definition.ContentHash[..8]}";
}

public sealed class ChipDefinitionException : InvalidOperationException
{
    public ChipDefinitionException(ImmutableArray<Diagnostic> diagnostics)
        : base(string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message))) =>
        Diagnostics = diagnostics;

    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public static class ChipDiagnosticCodes
{
    public const string OwnerPanelMismatch = "chip.network-owner-mismatch";
    public const string PortMissing = "chip.port-missing";
    public const string PortDirection = "chip.port-direction";
    public const string TargetAlreadyConnected = "chip.port-already-connected";
    public const string DefinitionMissing = "chip.definition-missing";
    public const string DefinitionDuplicate = "chip.definition-duplicate";
    public const string DefinitionPinMismatch = "chip.definition-pin-mismatch";
    public const string DependencyCycle = "chip.dependency-cycle";
    public const string UpgradeIncompatible = "chip.upgrade-incompatible";
    public const string UpgradeCandidateInvalid = "chip.upgrade-candidate-invalid";
}

internal static partial class ChipData
{
    internal static bool IsStableId(string? value) => value is not null && StableIdPattern().IsMatch(value);

    internal static bool IsPortName(string? value) => value is not null && PortNamePattern().IsMatch(value);

    internal static bool IsParameterName(string? value) => value is not null && ParameterNamePattern().IsMatch(value);

    internal static bool IsContentHash(string? value) => value is not null && ContentHashPattern().IsMatch(value);

    internal static Diagnostic Error(string code, string path, string message) =>
        new(code, DiagnosticSeverity.Error, path, message);

    internal static string ConnectionSortKey(ChipPortConnection connection) =>
        $"{EndpointSortKey(connection.Source)}\u001f{EndpointSortKey(connection.Target)}";

    internal static string EndpointSortKey(ChipPortEndpoint endpoint) =>
        $"{endpoint.InstanceId?.Value ?? string.Empty}\u001f{endpoint.PortName}";

    internal static string HashDefinition(
        DefinitionId id,
        PanelDefinition sourcePanel,
        ImmutableArray<ChipPortDefinition> ports,
        int schemaVersion,
        int behaviorVersion,
        string symbol,
        ImmutableSortedDictionary<string, string> parameters,
        PanelOwnedChipNetworkDefinition childNetwork)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.Write("spatial-circuits-chip-definition-v1");
            writer.Write(id.Value);
            writer.Write(schemaVersion);
            writer.Write(behaviorVersion);
            writer.Write(symbol);
            writer.Write(parameters.Count);
            foreach (var parameter in parameters)
            {
                writer.Write(parameter.Key);
                writer.Write(parameter.Value);
            }

            WritePanel(writer, sourcePanel);
            writer.Write(ports.Length);
            foreach (var port in ports)
            {
                writer.Write(port.Name);
                writer.Write(port.PanelPortId.Value);
                writer.Write((int)port.Direction);
            }

            WriteNetwork(writer, childNetwork);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    internal static string HashRuntime(string domain, string ownHash, IEnumerable<(string Id, string Hash)> children)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.Write(domain);
            writer.Write(ownHash);
            var ordered = children.OrderBy(child => child.Id, StringComparer.Ordinal).ToArray();
            writer.Write(ordered.Length);
            foreach (var child in ordered)
            {
                writer.Write(child.Id);
                writer.Write(child.Hash);
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WritePanel(BinaryWriter writer, PanelDefinition panel)
    {
        writer.Write(panel.Id.Value);
        writer.Write(panel.Width);
        writer.Write(panel.Height);
        writer.Write(panel.Cells.Length);
        foreach (var cell in panel.Cells)
        {
            writer.Write(cell is not null);
            if (cell is null)
            {
                continue;
            }

            writer.Write(cell.Id.Value);
            writer.Write(cell.Location.X);
            writer.Write(cell.Location.Y);
            writer.Write((int)cell.Kind);
            writer.Write((int)cell.Orientation);
            WriteNullable(writer, cell.PortId?.Value);
            WriteNullable(writer, cell.BehaviorId?.Value);
            writer.Write(cell.Parameters.Count);
            foreach (var parameter in cell.Parameters)
            {
                writer.Write(parameter.Key);
                writer.Write(parameter.Value);
            }
        }
    }

    private static void WriteNetwork(BinaryWriter writer, PanelOwnedChipNetworkDefinition network)
    {
        writer.Write(network.OwnerPanelId.Value);
        writer.Write(network.Instances.Length);
        foreach (var instance in network.Instances)
        {
            writer.Write(instance.InstanceId.Value);
            writer.Write(instance.DefinitionId.Value);
            writer.Write(instance.ContentHash);
        }

        writer.Write(network.Connections.Length);
        foreach (var connection in network.Connections)
        {
            WriteEndpoint(writer, connection.Source);
            WriteEndpoint(writer, connection.Target);
        }
    }

    private static void WriteEndpoint(BinaryWriter writer, ChipPortEndpoint endpoint)
    {
        writer.Write(endpoint.InstanceId.HasValue);
        if (endpoint.InstanceId is { } instanceId)
        {
            writer.Write(instanceId.Value);
        }

        writer.Write(endpoint.PortName);
    }

    private static void WriteNullable(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            writer.Write(value);
        }
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?(?::[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdPattern();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PortNamePattern();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNamePattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContentHashPattern();
}
