using System.Collections.ObjectModel;

namespace SpatialCircuits.Core;

public sealed record RuntimeCreationResult(
    CircuitRuntimeInstance? Instance,
    IReadOnlyList<Diagnostic> Diagnostics);

public sealed class CircuitRuntimeInstance
{
    private readonly IReadOnlyDictionary<ComponentId, ComponentRuntimeState> _components;

    internal CircuitRuntimeInstance(IEnumerable<ComponentDefinition> components)
    {
        var stateByComponent = components.ToDictionary(
            component => component.Id,
            component => new ComponentRuntimeState(component.Parameters));
        _components = new ReadOnlyDictionary<ComponentId, ComponentRuntimeState>(stateByComponent);
    }

    public IReadOnlyDictionary<ComponentId, ComponentRuntimeState> Components => _components;

    public ComponentRuntimeState GetComponent(ComponentId componentId) => _components[componentId];
}

public sealed class ComponentRuntimeState
{
    private readonly Dictionary<string, string> _parameters;
    private readonly IReadOnlyDictionary<string, string> _readOnlyParameters;

    internal ComponentRuntimeState(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        _parameters = parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _readOnlyParameters = new ReadOnlyDictionary<string, string>(_parameters);
    }

    public IReadOnlyDictionary<string, string> Parameters => _readOnlyParameters;

    public void SetParameter(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        if (!_parameters.ContainsKey(name))
        {
            throw new KeyNotFoundException($"Runtime parameter '{name}' is not defined.");
        }

        _parameters[name] = value;
    }
}

public class CircuitRuntimeFactory
{
    public RuntimeCreationResult Create(CircuitDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = CircuitDocumentValidator.Validate(document);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new RuntimeCreationResult(null, diagnostics);
        }

        return new RuntimeCreationResult(CreateInstance(document), diagnostics);
    }

    protected virtual CircuitRuntimeInstance CreateInstance(CircuitDocument document) => new(document.Components);
}
