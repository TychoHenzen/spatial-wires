using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Core.Tests;

public sealed class CircuitTests
{
    [Fact]
    public void ClrTypeNameIsRejectedAsBehaviorIdentity()
    {
        var definition = CircuitDefinition.Create(
            new DefinitionId("def-string"),
            new BehaviorId("System.String, System.Private.CoreLib"),
            []);
        var circuit = Circuit.Create(new CircuitId("circuit"), [definition]);

        var diagnostic = Assert.Single(CircuitValidator.Validate(circuit));

        Assert.Equal(DiagnosticCodes.ClrBehaviorType, diagnostic.Code);
        Assert.Equal("$.definitions[0].behaviorId", diagnostic.Path);
    }

    [Fact]
    public void CircuitCollectionsDoNotExposeMutableSourceState()
    {
        var definitions = new[] { TestCircuits.AndDefinition() };
        var components = new[]
        {
            ComponentDefinition.Create(
                new ComponentId("component-a"),
                new DefinitionId("def-and"),
                new GridCoordinate(0, 0))
        };
        var circuit = Circuit.Create(new CircuitId("circuit"), definitions, components);

        definitions[0] = CircuitDefinition.Create(
            new DefinitionId("replacement"),
            new BehaviorId("spatial:replacement/v1"),
            []);
        components[0] = ComponentDefinition.Create(
            new ComponentId("replacement"),
            new DefinitionId("replacement"),
            new GridCoordinate(9, 9));

        Assert.Equal("def-and", Assert.Single(circuit.Definitions).Id.Value);
        Assert.Equal("component-a", Assert.Single(circuit.Components).Id.Value);
    }

    [Fact]
    public void InvalidCircuitReportsStableOrderedStructuredDiagnostics()
    {
        var invalidDefinition = CircuitDefinition.Create(
            new DefinitionId("Bad Definition"),
            new BehaviorId("spatial:bad/v1"),
            [new PortDefinition(new PortId("Bad Port"), new GridCoordinate(0, 0))],
            [new ParameterDefinition("Bad Parameter", "0")]);
        var invalidComponent = ComponentDefinition.Create(
            new ComponentId("Bad Component"),
            new DefinitionId("Bad Definition"),
            new GridCoordinate(0, 0));
        var missingReference = ComponentDefinition.Create(
            new ComponentId("component-missing"),
            new DefinitionId("not-present"),
            new GridCoordinate(1, 0));
        var unknownParameter = ComponentDefinition.Create(
            new ComponentId("component-parameters"),
            new DefinitionId("Bad Definition"),
            new GridCoordinate(2, 0),
            [new KeyValuePair<string, string>("unknown", "1")]);
        var circuit = Circuit.Create(
            new CircuitId("Bad Circuit"),
            [invalidDefinition],
            [invalidComponent, missingReference, unknownParameter],
            [
                new Ownership(new OwnerId("owner-a"), new ComponentId("component-parameters")),
                new Ownership(new OwnerId("owner-b"), new ComponentId("component-parameters"))
            ]);

        var first = CircuitValidator.Validate(circuit);
        var second = CircuitValidator.Validate(circuit);

        Assert.Equal(first, second);
        Assert.Equal(
            [
                (DiagnosticCodes.InvalidCircuitId, "$.circuitId"),
                (DiagnosticCodes.InvalidDefinitionId, "$.definitions[0].id"),
                (DiagnosticCodes.InvalidPortId, "$.definitions[0].ports[0].id"),
                (DiagnosticCodes.InvalidParameter, "$.definitions[0].parameters[0].name"),
                (DiagnosticCodes.InvalidComponentId, "$.components[0].id"),
                (DiagnosticCodes.MissingDefinitionReference, "$.components[1].definitionId"),
                (DiagnosticCodes.UnknownParameter, "$.components[2].parameters[0].name"),
                (DiagnosticCodes.OwnershipConflict, "$.ownerships[1].componentId")
            ],
            first.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.All(first, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        });
    }
}
