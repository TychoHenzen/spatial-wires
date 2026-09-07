using System.Text.RegularExpressions;

namespace SpatialCircuits.Core;

public static partial class CircuitValidator
{
    public static IReadOnlyList<Diagnostic> Validate(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var diagnostics = new List<Diagnostic>();
        ValidateCircuitId(circuit, diagnostics);
        ValidateDefinitions(circuit, diagnostics);
        ValidateComponents(circuit, diagnostics);
        ValidateOwnerships(circuit, diagnostics);
        return diagnostics.AsReadOnly();
    }

    private static void ValidateCircuitId(Circuit circuit, ICollection<Diagnostic> diagnostics)
    {
        if (!IsStableId(circuit.Id.Value))
        {
            Add(diagnostics, DiagnosticCodes.InvalidCircuitId, "$.circuitId", "Circuit identifier is not stable data.");
        }
    }

    private static void ValidateDefinitions(Circuit circuit, ICollection<Diagnostic> diagnostics)
    {
        var seenDefinitions = new HashSet<string>(StringComparer.Ordinal);

        for (var definitionIndex = 0; definitionIndex < circuit.Definitions.Length; definitionIndex++)
        {
            var definition = circuit.Definitions[definitionIndex];
            var path = $"$.definitions[{definitionIndex}]";

            if (!IsStableId(definition.Id.Value))
            {
                Add(diagnostics, DiagnosticCodes.InvalidDefinitionId, $"{path}.id", "Definition identifier is not stable data.");
            }
            else if (!seenDefinitions.Add(definition.Id.Value))
            {
                Add(diagnostics, DiagnosticCodes.DuplicateDefinition, $"{path}.id", "Definition identifier is duplicated.");
            }

            ValidateBehaviorId(definition.BehaviorId, $"{path}.behaviorId", diagnostics);
            ValidatePorts(definition, path, diagnostics);
            ValidateParameterDefinitions(definition, path, diagnostics);
        }
    }

    private static void ValidateBehaviorId(BehaviorId behaviorId, string path, ICollection<Diagnostic> diagnostics)
    {
        if (ClrTypeNamePattern().IsMatch(behaviorId.Value))
        {
            Add(diagnostics, DiagnosticCodes.ClrBehaviorType, path, "Behavior identity must not be a CLR type name.");
            return;
        }

        if (!BehaviorIdPattern().IsMatch(behaviorId.Value))
        {
            Add(diagnostics, DiagnosticCodes.InvalidBehaviorId, path, "Behavior identity must be namespaced and versioned.");
        }
    }

    private static void ValidatePorts(
        CircuitDefinition definition,
        string definitionPath,
        ICollection<Diagnostic> diagnostics)
    {
        var seenPorts = new HashSet<string>(StringComparer.Ordinal);

        for (var portIndex = 0; portIndex < definition.Ports.Length; portIndex++)
        {
            var port = definition.Ports[portIndex];
            var path = $"{definitionPath}.ports[{portIndex}].id";
            if (!IsStableId(port.Id.Value))
            {
                Add(diagnostics, DiagnosticCodes.InvalidPortId, path, "Port identifier is not stable data.");
            }
            else if (!seenPorts.Add(port.Id.Value))
            {
                Add(diagnostics, DiagnosticCodes.DuplicatePort, path, "Port identifier is duplicated in its definition.");
            }
        }
    }

    private static void ValidateParameterDefinitions(
        CircuitDefinition definition,
        string definitionPath,
        ICollection<Diagnostic> diagnostics)
    {
        for (var parameterIndex = 0; parameterIndex < definition.Parameters.Length; parameterIndex++)
        {
            var parameter = definition.Parameters[parameterIndex];
            if (!ParameterNamePattern().IsMatch(parameter.Name))
            {
                Add(
                    diagnostics,
                    DiagnosticCodes.InvalidParameter,
                    $"{definitionPath}.parameters[{parameterIndex}].name",
                    "Parameter name is invalid.");
            }
        }
    }

    private static void ValidateComponents(Circuit circuit, ICollection<Diagnostic> diagnostics)
    {
        var definitionById = circuit.Definitions
            .GroupBy(definition => definition.Id.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var seenComponents = new HashSet<string>(StringComparer.Ordinal);

        for (var componentIndex = 0; componentIndex < circuit.Components.Length; componentIndex++)
        {
            var component = circuit.Components[componentIndex];
            var path = $"$.components[{componentIndex}]";

            if (!IsStableId(component.Id.Value))
            {
                Add(diagnostics, DiagnosticCodes.InvalidComponentId, $"{path}.id", "Component identifier is not stable data.");
            }
            else if (!seenComponents.Add(component.Id.Value))
            {
                Add(diagnostics, DiagnosticCodes.DuplicateComponent, $"{path}.id", "Component identifier is duplicated.");
            }

            if (!definitionById.TryGetValue(component.DefinitionId.Value, out var definition))
            {
                Add(
                    diagnostics,
                    DiagnosticCodes.MissingDefinitionReference,
                    $"{path}.definitionId",
                    "Component references a definition that is not present.");
                continue;
            }

            ValidateComponentParameters(component, definition, path, diagnostics);
        }
    }

    private static void ValidateComponentParameters(
        ComponentDefinition component,
        CircuitDefinition definition,
        string componentPath,
        ICollection<Diagnostic> diagnostics)
    {
        var allowedParameters = definition.Parameters
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);
        var parameterIndex = 0;

        foreach (var parameter in component.Parameters)
        {
            var path = $"{componentPath}.parameters[{parameterIndex}].name";
            if (!ParameterNamePattern().IsMatch(parameter.Key))
            {
                Add(diagnostics, DiagnosticCodes.InvalidParameter, path, "Parameter name is invalid.");
            }
            else if (!allowedParameters.Contains(parameter.Key))
            {
                Add(diagnostics, DiagnosticCodes.UnknownParameter, path, "Parameter is not declared by the referenced definition.");
            }

            parameterIndex++;
        }
    }

    private static void ValidateOwnerships(Circuit circuit, ICollection<Diagnostic> diagnostics)
    {
        var componentIds = circuit.Components
            .Select(component => component.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        var ownedComponents = new HashSet<string>(StringComparer.Ordinal);

        for (var ownershipIndex = 0; ownershipIndex < circuit.Ownerships.Length; ownershipIndex++)
        {
            var ownership = circuit.Ownerships[ownershipIndex];
            var path = $"$.ownerships[{ownershipIndex}]";

            if (!IsStableId(ownership.OwnerId.Value))
            {
                Add(diagnostics, DiagnosticCodes.InvalidOwnerId, $"{path}.ownerId", "Owner identifier is not stable data.");
            }

            if (!componentIds.Contains(ownership.ComponentId.Value))
            {
                Add(
                    diagnostics,
                    DiagnosticCodes.MissingComponentReference,
                    $"{path}.componentId",
                    "Ownership references a component that is not present.");
            }

            if (!ownedComponents.Add(ownership.ComponentId.Value))
            {
                Add(
                    diagnostics,
                    DiagnosticCodes.OwnershipConflict,
                    $"{path}.componentId",
                    "Component is assigned to more than one owner.");
            }
        }
    }

    private static bool IsStableId(string value) => StableIdPattern().IsMatch(value);

    private static void Add(
        ICollection<Diagnostic> diagnostics,
        string code,
        string path,
        string message) => diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, path, message));

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?(?::[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdPattern();

    [GeneratedRegex("^[a-z][a-z0-9.-]*:[a-z0-9][a-z0-9._/-]*/v[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex BehaviorIdPattern();

    [GeneratedRegex("^(?:[A-Z_][A-Za-z0-9_]*\\.)+[A-Z_][A-Za-z0-9_]*(?:,\\s*[A-Za-z0-9_.-]+(?:,.*)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ClrTypeNamePattern();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNamePattern();
}
