using System.Collections.Immutable;

namespace SpatialCircuits.Core;

public readonly record struct GridCoordinate(int X, int Y);

public sealed record PortDefinition(PortId Id, GridCoordinate Location);

public sealed record ParameterDefinition(string Name, string DefaultValue);

public sealed class CircuitDefinition
{
    private CircuitDefinition(
        DefinitionId id,
        BehaviorId behaviorId,
        ImmutableArray<PortDefinition> ports,
        ImmutableArray<ParameterDefinition> parameters)
    {
        Id = id;
        BehaviorId = behaviorId;
        Ports = ports;
        Parameters = parameters;
    }

    public DefinitionId Id { get; }

    public BehaviorId BehaviorId { get; }

    public ImmutableArray<PortDefinition> Ports { get; }

    public ImmutableArray<ParameterDefinition> Parameters { get; }

    public static CircuitDefinition Create(
        DefinitionId id,
        BehaviorId behaviorId,
        IEnumerable<PortDefinition> ports,
        IEnumerable<ParameterDefinition>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(ports);

        return new CircuitDefinition(
            id,
            behaviorId,
            ports.OrderBy(port => port.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            (parameters ?? []).OrderBy(parameter => parameter.Name, StringComparer.Ordinal).ToImmutableArray());
    }
}

public sealed class ComponentDefinition
{
    private ComponentDefinition(
        ComponentId id,
        DefinitionId definitionId,
        GridCoordinate location,
        ImmutableSortedDictionary<string, string> parameters)
    {
        Id = id;
        DefinitionId = definitionId;
        Location = location;
        Parameters = parameters;
    }

    public ComponentId Id { get; }

    public DefinitionId DefinitionId { get; }

    public GridCoordinate Location { get; }

    public ImmutableSortedDictionary<string, string> Parameters { get; }

    public static ComponentDefinition Create(
        ComponentId id,
        DefinitionId definitionId,
        GridCoordinate location,
        IEnumerable<KeyValuePair<string, string>>? parameters = null) => new(
            id,
            definitionId,
            location,
            (parameters ?? []).ToImmutableSortedDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal));
}

public sealed record Ownership(OwnerId OwnerId, ComponentId ComponentId);

public sealed class Circuit
{
    private Circuit(
        CircuitId id,
        ImmutableArray<CircuitDefinition> definitions,
        ImmutableArray<ComponentDefinition> components,
        ImmutableArray<Ownership> ownerships)
    {
        Id = id;
        Definitions = definitions;
        Components = components;
        Ownerships = ownerships;
    }

    public CircuitId Id { get; }

    public ImmutableArray<CircuitDefinition> Definitions { get; }

    public ImmutableArray<ComponentDefinition> Components { get; }

    public ImmutableArray<Ownership> Ownerships { get; }

    public static Circuit Create(
        CircuitId id,
        IEnumerable<CircuitDefinition> definitions,
        IEnumerable<ComponentDefinition>? components = null,
        IEnumerable<Ownership>? ownerships = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        return new Circuit(
            id,
            definitions.OrderBy(definition => definition.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            (components ?? []).OrderBy(component => component.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            (ownerships ?? [])
                .OrderBy(ownership => ownership.ComponentId.Value, StringComparer.Ordinal)
                .ThenBy(ownership => ownership.OwnerId.Value, StringComparer.Ordinal)
                .ToImmutableArray());
    }
}
