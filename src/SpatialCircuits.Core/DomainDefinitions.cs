using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpatialCircuits.Core;

public readonly record struct StableId
{
    public StableId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(':'))
        {
            throw new ArgumentException("Stable identifiers must be non-empty and must not contain ':'", nameof(value));
        }
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct SchemaVersion(int Major, int Minor)
{
    public static SchemaVersion Current => new(1, 0);
}

public sealed record Ownership(StableId OwnerId, StableId DefinitionId);

public sealed record CircuitDocument(
    SchemaVersion Schema,
    StableId DocumentId,
    IReadOnlyList<StableId> Definitions,
    IReadOnlyList<Ownership> Ownerships,
    IReadOnlyList<StableId> Behaviors)
{
    public IReadOnlyList<string> Validate() => Behaviors
        .Where(x => x.Value.Contains('.', StringComparison.Ordinal))
        .Select(_ => "SCHEMA_BEHAVIOR_ID:behaviors")
        .ToArray();

    public static CircuitDocument Create(
        StableId documentId,
        IEnumerable<StableId> definitions,
        IEnumerable<Ownership> ownerships,
        IEnumerable<StableId> behaviors) => new(
            SchemaVersion.Current,
            documentId,
            definitions.OrderBy(x => x.Value, StringComparer.Ordinal).ToArray(),
            ownerships.OrderBy(x => x.OwnerId.Value, StringComparer.Ordinal).ThenBy(x => x.DefinitionId.Value, StringComparer.Ordinal).ToArray(),
            behaviors.OrderBy(x => x.Value, StringComparer.Ordinal).ToArray());
}

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
        var payload = new
        {
            schema = new { major = document.Schema.Major, minor = document.Schema.Minor },
            documentId = document.DocumentId.Value,
            definitions = document.Definitions.Select(x => x.Value).ToArray(),
            ownerships = document.Ownerships.Select(x => new { ownerId = x.OwnerId.Value, definitionId = x.DefinitionId.Value }).ToArray(),
            behaviors = document.Behaviors.Select(x => x.Value).ToArray()
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, Options);
    }

    public static CircuitDocument Read(ReadOnlySpan<byte> bytes)
    {
        using var json = JsonDocument.Parse(bytes.ToArray());
        var root = json.RootElement;
        var schema = root.GetProperty("schema");
        var definitions = root.GetProperty("definitions").EnumerateArray().Select(x => new StableId(x.GetString()!));
        var ownerships = root.GetProperty("ownerships").EnumerateArray().Select(x => new Ownership(new StableId(x.GetProperty("ownerId").GetString()!), new StableId(x.GetProperty("definitionId").GetString()!)));
        var behaviors = root.GetProperty("behaviors").EnumerateArray().Select(x => new StableId(x.GetString()!));
        return CircuitDocument.Create(new StableId(root.GetProperty("documentId").GetString()!), definitions, ownerships, behaviors) with
        {
            Schema = new SchemaVersion(schema.GetProperty("major").GetInt32(), schema.GetProperty("minor").GetInt32())
        };
    }
}
