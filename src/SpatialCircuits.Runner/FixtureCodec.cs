using System.Text.Json;

namespace SpatialCircuits.Runner;

public static class FixtureCodec
{
    public static FixtureReadResult Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes.ToArray());
            var fixture = ReadFixture(json.RootElement);
            return new FixtureReadResult(fixture, []);
        }
        catch (JsonException)
        {
            return Failure(FixtureDiagnosticCodes.JsonInvalid, "$", "Fixture is not valid JSON.");
        }
        catch (FixtureStructureException exception)
        {
            return Failure(
                FixtureDiagnosticCodes.StructureInvalid,
                exception.Path,
                "Fixture field is missing or has the wrong JSON type.");
        }
    }

    private static RunnerFixture ReadFixture(JsonElement root)
    {
        RequireKind(root, JsonValueKind.Object, "$");
        var fixtureSchema = ReadFixtureVersion(RequireProperty(root, "fixtureSchema", "$.fixtureSchema"));
        var traceSchema = ReadTraceVersion(RequireProperty(root, "traceSchema", "$.traceSchema"));
        var fixtureId = ReadString(RequireProperty(root, "fixtureId", "$.fixtureId"), "$.fixtureId");
        var actionText = ReadString(RequireProperty(root, "action", "$.action"), "$.action");
        var casesElement = RequireProperty(root, "cases", "$.cases");
        RequireKind(casesElement, JsonValueKind.Array, "$.cases");

        var cases = casesElement.EnumerateArray().Select(ReadCase).ToArray();
        var action = actionText == "resolveDrives" ? FixtureAction.ResolveDrives : FixtureAction.Unsupported;
        return new RunnerFixture(fixtureSchema, traceSchema, fixtureId, action, cases);
    }

    private static ResolutionCase ReadCase(JsonElement element, int index)
    {
        var path = $"$.cases[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        var caseId = ReadString(RequireProperty(element, "caseId", $"{path}.caseId"), $"{path}.caseId");
        var drivesElement = RequireProperty(element, "drives", $"{path}.drives");
        RequireKind(drivesElement, JsonValueKind.Array, $"{path}.drives");
        var drives = drivesElement.EnumerateArray().Select((drive, driveIndex) =>
            ReadString(drive, $"{path}.drives[{driveIndex}]")).ToArray();
        var expected = ReadString(
            RequireProperty(element, "expected", $"{path}.expected"),
            $"{path}.expected");
        return new ResolutionCase(caseId, drives, expected);
    }

    private static FixtureVersion ReadFixtureVersion(JsonElement element)
    {
        var version = ReadVersion(element, "$.fixtureSchema");
        return new FixtureVersion(version.Major, version.Minor);
    }

    private static TraceVersion ReadTraceVersion(JsonElement element)
    {
        var version = ReadVersion(element, "$.traceSchema");
        return new TraceVersion(version.Major, version.Minor);
    }

    private static (int Major, int Minor) ReadVersion(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        var major = ReadInt32(RequireProperty(element, "major", $"{path}.major"), $"{path}.major");
        var minor = ReadInt32(RequireProperty(element, "minor", $"{path}.minor"), $"{path}.minor");
        return (major, minor);
    }

    private static JsonElement RequireProperty(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            throw new FixtureStructureException(path);
        }

        return value;
    }

    private static string ReadString(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new FixtureStructureException(path);
        }

        return element.GetString() ?? string.Empty;
    }

    private static int ReadInt32(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new FixtureStructureException(path);
        }

        return value;
    }

    private static void RequireKind(JsonElement element, JsonValueKind expected, string path)
    {
        if (element.ValueKind != expected)
        {
            throw new FixtureStructureException(path);
        }
    }

    private static FixtureReadResult Failure(string code, string path, string message) =>
        new(null, [new FixtureDiagnostic(code, path, message)]);

    private sealed class FixtureStructureException(string path) : Exception
    {
        internal string Path { get; } = path;
    }
}
