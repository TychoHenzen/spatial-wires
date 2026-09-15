using System.Text.Json;

namespace SpatialCircuits.Persistence;

public static class DurableSnapshotDiff
{
    public static DurableSnapshotDifference? FirstDifference(
        ReadOnlyMemory<byte> expected,
        ReadOnlyMemory<byte> actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        return Compare(expectedDocument.RootElement, actualDocument.RootElement, "$");
    }

    public static DurableSnapshotDifference? FirstDifference(
        DurableSaveDocument expected,
        DurableSaveDocument actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return FirstDifference(
            DurableSnapshotJson.Serialize(expected),
            DurableSnapshotJson.Serialize(actual));
    }

    private static DurableSnapshotDifference? Compare(JsonElement expected, JsonElement actual, string path)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return Difference(path, expected, actual);
        }

        if (expected.ValueKind == JsonValueKind.Object)
        {
            var expectedProperties = expected.EnumerateObject()
                .Where(property => !IsDerivedHash(property.Name))
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            var actualProperties = actual.EnumerateObject()
                .Where(property => !IsDerivedHash(property.Name))
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            foreach (var name in expectedProperties.Keys.Concat(actualProperties.Keys)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                var childPath = PropertyPath(path, name);
                var hasExpected = expectedProperties.TryGetValue(name, out var expectedValue);
                var hasActual = actualProperties.TryGetValue(name, out var actualValue);
                if (!hasExpected || !hasActual)
                {
                    return new DurableSnapshotDifference(
                        childPath,
                        hasExpected ? expectedValue.GetRawText() : "<missing>",
                        hasActual ? actualValue.GetRawText() : "<missing>");
                }

                var difference = Compare(expectedValue, actualValue, childPath);
                if (difference is not null)
                {
                    return difference;
                }
            }

            return null;
        }

        if (expected.ValueKind == JsonValueKind.Array)
        {
            var expectedItems = expected.EnumerateArray().ToArray();
            var actualItems = actual.EnumerateArray().ToArray();
            var count = Math.Min(expectedItems.Length, actualItems.Length);
            for (var index = 0; index < count; index++)
            {
                var difference = Compare(expectedItems[index], actualItems[index], $"{path}[{index}]");
                if (difference is not null)
                {
                    return difference;
                }
            }

            if (expectedItems.Length != actualItems.Length)
            {
                var index = count;
                return new DurableSnapshotDifference(
                    $"{path}[{index}]",
                    index < expectedItems.Length ? expectedItems[index].GetRawText() : "<missing>",
                    index < actualItems.Length ? actualItems[index].GetRawText() : "<missing>");
            }

            return null;
        }

        return ScalarsEqual(expected, actual) ? null : Difference(path, expected, actual);
    }

    private static bool IsDerivedHash(string propertyName) =>
        propertyName is "contentHash" or "workbenchHash";

    private static bool ScalarsEqual(JsonElement expected, JsonElement actual) => expected.ValueKind switch
    {
        JsonValueKind.String => string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal),
        JsonValueKind.Number => string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal),
        JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null =>
            string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal),
        _ => false
    };

    private static DurableSnapshotDifference Difference(
        string path,
        JsonElement expected,
        JsonElement actual) =>
        new(path, expected.GetRawText(), actual.GetRawText());

    private static string PropertyPath(string path, string propertyName) =>
        propertyName.Length > 0 && propertyName.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? $"{path}.{propertyName}"
            : $"{path}[{JsonSerializer.Serialize(propertyName)}]";
}
