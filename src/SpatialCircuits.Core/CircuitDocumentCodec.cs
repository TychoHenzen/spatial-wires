using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpatialCircuits.Core;

public static class CircuitDocumentCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static byte[] Write(CircuitDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var payload = new
        {
            schema = new { major = document.Schema.Major, minor = document.Schema.Minor },
            documentId = document.DocumentId.Value,
            definitions = document.Definitions.Select(definition => new
            {
                id = definition.Id.Value,
                behaviorId = definition.BehaviorId.Value,
                ports = definition.Ports.Select(port => new
                {
                    id = port.Id.Value,
                    location = new { x = port.Location.X, y = port.Location.Y }
                }).ToArray(),
                parameters = definition.Parameters.Select(parameter => new
                {
                    name = parameter.Name,
                    defaultValue = parameter.DefaultValue
                }).ToArray()
            }).ToArray(),
            components = document.Components.Select(component => new
            {
                id = component.Id.Value,
                definitionId = component.DefinitionId.Value,
                location = new { x = component.Location.X, y = component.Location.Y },
                parameters = component.Parameters.Select(parameter => new
                {
                    name = parameter.Key,
                    value = parameter.Value
                }).ToArray()
            }).ToArray(),
            ownerships = document.Ownerships.Select(ownership => new
            {
                ownerId = ownership.OwnerId.Value,
                componentId = ownership.ComponentId.Value
            }).ToArray()
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload, Options);
    }

    public static CircuitDocument Read(ReadOnlySpan<byte> bytes)
    {
        using var json = JsonDocument.Parse(bytes.ToArray());
        var root = json.RootElement;
        var schemaElement = root.GetProperty("schema");
        var definitions = root.GetProperty("definitions").EnumerateArray().Select(ReadDefinition);
        var components = root.GetProperty("components").EnumerateArray().Select(ReadComponent);
        var ownerships = root.GetProperty("ownerships").EnumerateArray().Select(ownership => new Ownership(
            new OwnerId(ownership.GetProperty("ownerId").GetString() ?? string.Empty),
            new ComponentId(ownership.GetProperty("componentId").GetString() ?? string.Empty)));

        return CircuitDocument.Create(
            new DocumentId(root.GetProperty("documentId").GetString() ?? string.Empty),
            definitions,
            components,
            ownerships,
            new SchemaVersion(
                schemaElement.GetProperty("major").GetInt32(),
                schemaElement.GetProperty("minor").GetInt32()));
    }

    public static byte[] ComputeContentHash(CircuitDocument document) => SHA256.HashData(Write(document));

    public static string ComputeContentHashHex(CircuitDocument document) =>
        Convert.ToHexString(ComputeContentHash(document)).ToLowerInvariant();

    private static CircuitDefinition ReadDefinition(JsonElement definition)
    {
        var ports = definition.GetProperty("ports").EnumerateArray().Select(port =>
        {
            var location = port.GetProperty("location");
            return new PortDefinition(
                new PortId(port.GetProperty("id").GetString() ?? string.Empty),
                new GridCoordinate(location.GetProperty("x").GetInt32(), location.GetProperty("y").GetInt32()));
        });
        var parameters = definition.GetProperty("parameters").EnumerateArray().Select(parameter =>
            new ParameterDefinition(
                parameter.GetProperty("name").GetString() ?? string.Empty,
                parameter.GetProperty("defaultValue").GetString() ?? string.Empty));

        return CircuitDefinition.Create(
            new DefinitionId(definition.GetProperty("id").GetString() ?? string.Empty),
            new BehaviorId(definition.GetProperty("behaviorId").GetString() ?? string.Empty),
            ports,
            parameters);
    }

    private static ComponentDefinition ReadComponent(JsonElement component)
    {
        var location = component.GetProperty("location");
        var parameters = component.GetProperty("parameters").EnumerateArray().Select(parameter =>
            new KeyValuePair<string, string>(
                parameter.GetProperty("name").GetString() ?? string.Empty,
                parameter.GetProperty("value").GetString() ?? string.Empty));

        return ComponentDefinition.Create(
            new ComponentId(component.GetProperty("id").GetString() ?? string.Empty),
            new DefinitionId(component.GetProperty("definitionId").GetString() ?? string.Empty),
            new GridCoordinate(location.GetProperty("x").GetInt32(), location.GetProperty("y").GetInt32()),
            parameters);
    }
}
