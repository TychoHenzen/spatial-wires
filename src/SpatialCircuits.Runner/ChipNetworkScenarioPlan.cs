using System.Collections.Immutable;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Runner;

public sealed record ChipDefinitionPlan(
    string DefinitionId,
    PanelDefinitionPlan SourcePanel,
    ImmutableArray<ChipPortPlan> Ports,
    int SchemaVersion,
    int BehaviorVersion,
    string Symbol,
    ImmutableArray<KeyValuePair<string, string>> Parameters)
{
    public ChipDefinition CreateDefinition() => ChipDefinition.Create(
        new DefinitionId(DefinitionId),
        SourcePanel.CreatePanelDefinition(),
        Ports.Select(port => new ChipPortDefinition(
            port.Name,
            new PortId(port.PanelPortId),
            port.Direction switch
            {
                "input" => ChipPortDirection.Input,
                "output" => ChipPortDirection.Output,
                _ => (ChipPortDirection)(-1)
            })),
        SchemaVersion,
        BehaviorVersion,
        Symbol,
        Parameters);
}

public sealed record ChipPortPlan(string Name, string PanelPortId, string Direction);

public sealed record ChipInstancePlan(string InstanceId, string DefinitionId, string ContentHash)
{
    public ChipInstanceDefinition CreateInstanceDefinition() => ChipInstanceDefinition.Create(
        new ComponentId(InstanceId),
        new DefinitionId(DefinitionId),
        ContentHash);
}

public sealed record ChipNetworkInputChange(int Tick, string InstanceId, string PortName, string Value);

public sealed record ChipPortEndpointPlan(string? InstanceId, string PortName)
{
    public ChipPortEndpoint CreateEndpoint() => InstanceId is null
        ? ChipPortEndpoint.ParentPanel(new PortId(PortName))
        : ChipPortEndpoint.ChildChip(new ComponentId(InstanceId), PortName);
}

public sealed record ChipPortConnectionPlan(ChipPortEndpointPlan Source, ChipPortEndpointPlan Target)
{
    public ChipPortConnection CreateConnection() =>
        new(Source.CreateEndpoint(), Target.CreateEndpoint());
}

public sealed record ChipNetworkExpectation(
    int Tick,
    string InstanceId,
    ImmutableSortedDictionary<string, string> Outputs,
    string Hash);

public sealed record ChipNetworkScenarioPlan(
    PanelDefinitionPlan ParentPanel,
    int Microticks,
    ImmutableArray<ChipDefinitionPlan> Definitions,
    ImmutableArray<ChipInstancePlan> Instances,
    ImmutableArray<ChipPortConnectionPlan> Connections,
    ImmutableArray<ChipNetworkInputChange> Inputs,
    ImmutableArray<ChipNetworkExpectation> Expectations)
{
    public ChipDefinitionCatalog CreateCatalog() =>
        ChipDefinitionCatalog.Create(Definitions.Select(definition => definition.CreateDefinition()));

    public PanelOwnedChipNetworkDefinition CreateNetworkDefinition() =>
        PanelOwnedChipNetworkDefinition.Create(
            new CircuitId(ParentPanel.PanelId),
            Instances.Select(instance => instance.CreateInstanceDefinition()),
            Connections.Select(connection => connection.CreateConnection()));
}
