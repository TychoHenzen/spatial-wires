using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Persistence;

internal static class DurableSnapshotJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = 128,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.Converters.Add(new DeviceSignalJsonConverter());
        options.Converters.Add(new SchedulerPortAddressJsonConverter());
        options.Converters.Add(new StableIdentifierJsonConverterFactory());
        return options;
    }
}

internal sealed class StableIdentifierJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(CircuitId) ||
        typeToConvert == typeof(DefinitionId) ||
        typeToConvert == typeof(ComponentId) ||
        typeToConvert == typeof(PortId) ||
        typeToConvert == typeof(BehaviorId) ||
        typeToConvert == typeof(OwnerId);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(CircuitId))
        {
            return new StableIdentifierJsonConverter<CircuitId>(value => new CircuitId(value), value => value.Value);
        }

        if (typeToConvert == typeof(DefinitionId))
        {
            return new StableIdentifierJsonConverter<DefinitionId>(value => new DefinitionId(value), value => value.Value);
        }

        if (typeToConvert == typeof(ComponentId))
        {
            return new StableIdentifierJsonConverter<ComponentId>(value => new ComponentId(value), value => value.Value);
        }

        if (typeToConvert == typeof(PortId))
        {
            return new StableIdentifierJsonConverter<PortId>(value => new PortId(value), value => value.Value);
        }

        if (typeToConvert == typeof(BehaviorId))
        {
            return new StableIdentifierJsonConverter<BehaviorId>(value => new BehaviorId(value), value => value.Value);
        }

        return new StableIdentifierJsonConverter<OwnerId>(value => new OwnerId(value), value => value.Value);
    }
}

internal sealed class StableIdentifierJsonConverter<T>(Func<string, T> parse, Func<T, string> format)
    : JsonConverter<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } value)
        {
            throw new JsonException("Stable identifiers must be JSON strings.");
        }

        return parse(value);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(format(value));
}

internal sealed class DeviceSignalJsonConverter : JsonConverter<DeviceSignal>
{
    public override DeviceSignal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!DeviceSignal.TryParse(value, value?.Length ?? 0, out var signal))
        {
            throw new JsonException("Device signal must contain one or more supported logic values.");
        }

        return signal;
    }

    public override void Write(Utf8JsonWriter writer, DeviceSignal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

internal sealed class SchedulerPortAddressJsonConverter : JsonConverter<SchedulerPortAddress>
{
    public override SchedulerPortAddress Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("targetStableId", out var target) ||
            !root.TryGetProperty("targetIncarnation", out var incarnation) ||
            !root.TryGetProperty("portOrLane", out var port))
        {
            throw new JsonException("Scheduler port address is incomplete.");
        }

        return new SchedulerPortAddress(
            target.GetString() ?? throw new JsonException("Scheduler target identifier is missing."),
            incarnation.GetInt64(),
            port.GetString() ?? throw new JsonException("Scheduler port identifier is missing."));
    }

    public override void Write(
        Utf8JsonWriter writer,
        SchedulerPortAddress value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("targetStableId", value.TargetStableId);
        writer.WriteNumber("targetIncarnation", value.TargetIncarnation);
        writer.WriteString("portOrLane", value.PortOrLane);
        writer.WriteEndObject();
    }

    public override SchedulerPortAddress ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(reader.GetString() ?? string.Empty);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 3)
        {
            throw new JsonException("Scheduler port address dictionary key is invalid.");
        }

        return new SchedulerPortAddress(
            root[0].GetString() ?? throw new JsonException("Scheduler target identifier is missing."),
            root[1].GetInt64(),
            root[2].GetString() ?? throw new JsonException("Scheduler port identifier is missing."));
    }

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        SchedulerPortAddress value,
        JsonSerializerOptions options)
    {
        var encoded = JsonSerializer.Serialize(
            new object?[] { value.TargetStableId, value.TargetIncarnation, value.PortOrLane });
        writer.WritePropertyName(encoded);
    }
}
