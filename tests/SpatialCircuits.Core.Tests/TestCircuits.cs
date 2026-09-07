using SpatialCircuits.Core;

namespace SpatialCircuits.Core.Tests;

internal static class TestCircuits
{
    internal static CircuitDefinition AndDefinition() => CircuitDefinition.Create(
        new DefinitionId("def-and"),
        new BehaviorId("spatial:and/v1"),
        [
            new PortDefinition(new PortId("input-b"), new GridCoordinate(4, 3)),
            new PortDefinition(new PortId("input-a"), new GridCoordinate(2, 3))
        ],
        [new ParameterDefinition("delay", "1")]);

    internal static Circuit ValidCircuit(
        IEnumerable<KeyValuePair<string, string>>? parameters = null)
    {
        var component = ComponentDefinition.Create(
            new ComponentId("component-a"),
            new DefinitionId("def-and"),
            new GridCoordinate(8, 5),
            parameters ?? [new KeyValuePair<string, string>("delay", "1")]);

        return Circuit.Create(
            new CircuitId("circuit-1"),
            [AndDefinition()],
            [component],
            [new Ownership(new OwnerId("panel-main"), component.Id)]);
    }
}
