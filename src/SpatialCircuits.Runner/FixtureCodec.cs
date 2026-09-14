using System.Collections.Immutable;
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
        catch (ArgumentException)
        {
            return Failure(
                FixtureDiagnosticCodes.StructureInvalid,
                "$",
                "Fixture contains a duplicated or invalid field value.");
        }
    }

    private static RunnerFixture ReadFixture(JsonElement root)
    {
        RequireKind(root, JsonValueKind.Object, "$");
        var fixtureSchema = ReadFixtureVersion(RequireProperty(root, "fixtureSchema", "$.fixtureSchema"));
        var traceSchema = ReadTraceVersion(RequireProperty(root, "traceSchema", "$.traceSchema"));
        var fixtureId = ReadString(RequireProperty(root, "fixtureId", "$.fixtureId"), "$.fixtureId");
        var actionText = ReadString(RequireProperty(root, "action", "$.action"), "$.action");
        if (actionText == "scheduledDrive")
        {
            var scheduledDrive = ReadScheduledDrive(
                RequireProperty(root, "scheduledDrive", "$.scheduledDrive"));
            return new RunnerFixture(
                fixtureSchema,
                traceSchema,
                fixtureId,
                FixtureAction.ScheduledDrive,
                [],
                scheduledDrive);
        }

        if (actionText == "panelScenario")
        {
            var panelScenario = ReadPanelScenario(
                RequireProperty(root, "panelScenario", "$.panelScenario"));
            return new RunnerFixture(
                fixtureSchema,
                traceSchema,
                fixtureId,
                FixtureAction.PanelScenario,
                [],
                panelScenario: panelScenario);
        }

        if (actionText == "chipNetworkScenario")
        {
            var chipScenario = ReadChipNetworkScenario(
                RequireProperty(root, "chipNetworkScenario", "$.chipNetworkScenario"));
            return new RunnerFixture(
                fixtureSchema,
                traceSchema,
                fixtureId,
                FixtureAction.ChipNetworkScenario,
                [],
                chipNetworkScenario: chipScenario);
        }

        var casesElement = RequireProperty(root, "cases", "$.cases");
        RequireKind(casesElement, JsonValueKind.Array, "$.cases");

        var cases = casesElement.EnumerateArray().Select(ReadCase).ToArray();
        var action = actionText == "resolveDrives" ? FixtureAction.ResolveDrives : FixtureAction.Unsupported;
        return new RunnerFixture(fixtureSchema, traceSchema, fixtureId, action, cases);
    }

    private static ScheduledDrivePlan ReadScheduledDrive(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object, "$.scheduledDrive");
        return new ScheduledDrivePlan(
            ReadInt32(
                RequireProperty(element, "microticks", "$.scheduledDrive.microticks"),
                "$.scheduledDrive.microticks"),
            ReadInt32(
                RequireProperty(element, "snapshotAfter", "$.scheduledDrive.snapshotAfter"),
                "$.scheduledDrive.snapshotAfter"),
            ReadInt32(
                RequireProperty(element, "releaseAt", "$.scheduledDrive.releaseAt"),
                "$.scheduledDrive.releaseAt"),
            ReadString(
                RequireProperty(element, "sourceId", "$.scheduledDrive.sourceId"),
                "$.scheduledDrive.sourceId"),
            ReadString(
                RequireProperty(element, "sourcePort", "$.scheduledDrive.sourcePort"),
                "$.scheduledDrive.sourcePort"),
            ReadString(
                RequireProperty(element, "targetId", "$.scheduledDrive.targetId"),
                "$.scheduledDrive.targetId"),
            ReadString(
                RequireProperty(element, "targetPort", "$.scheduledDrive.targetPort"),
                "$.scheduledDrive.targetPort"),
            ReadString(
                RequireProperty(element, "drive", "$.scheduledDrive.drive"),
                "$.scheduledDrive.drive"));
    }

    private static PanelScenarioPlan ReadPanelScenario(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object, "$.panelScenario");
        var panelId = ReadString(
            RequireProperty(element, "panelId", "$.panelScenario.panelId"),
            "$.panelScenario.panelId");
        var width = ReadInt32(
            RequireProperty(element, "width", "$.panelScenario.width"),
            "$.panelScenario.width");
        var height = ReadInt32(
            RequireProperty(element, "height", "$.panelScenario.height"),
            "$.panelScenario.height");
        var microticks = ReadInt32(
            RequireProperty(element, "microticks", "$.panelScenario.microticks"),
            "$.panelScenario.microticks");

        var cellsElement = RequireProperty(element, "cells", "$.panelScenario.cells");
        RequireKind(cellsElement, JsonValueKind.Array, "$.panelScenario.cells");
        var cells = cellsElement.EnumerateArray()
            .Select((cell, index) => ReadPanelCell(cell, index, "$.panelScenario.cells"))
            .ToImmutableArray();

        var inputsElement = RequireProperty(element, "inputs", "$.panelScenario.inputs");
        RequireKind(inputsElement, JsonValueKind.Array, "$.panelScenario.inputs");
        var inputs = inputsElement.EnumerateArray()
            .Select((input, index) => ReadPanelInput(input, index))
            .ToImmutableArray();

        var expectationsElement = RequireProperty(element, "expectations", "$.panelScenario.expectations");
        RequireKind(expectationsElement, JsonValueKind.Array, "$.panelScenario.expectations");
        var expectations = expectationsElement.EnumerateArray()
            .Select((expectation, index) => ReadPanelExpectation(expectation, index))
            .ToImmutableArray();

        return new PanelScenarioPlan(
            panelId,
            width,
            height,
            microticks,
            cells,
            inputs,
            expectations);
    }

    private static ChipNetworkScenarioPlan ReadChipNetworkScenario(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object, "$.chipNetworkScenario");
        var path = "$.chipNetworkScenario";
        var parentPanel = ReadPanelDefinition(
            RequireProperty(element, "parentPanel", $"{path}.parentPanel"),
            $"{path}.parentPanel");
        var microticks = ReadInt32(
            RequireProperty(element, "microticks", $"{path}.microticks"),
            $"{path}.microticks");

        var definitionsElement = RequireProperty(element, "definitions", $"{path}.definitions");
        RequireKind(definitionsElement, JsonValueKind.Array, $"{path}.definitions");
        var definitions = definitionsElement.EnumerateArray()
            .Select((definition, index) => ReadChipDefinition(definition, index))
            .ToImmutableArray();

        var instancesElement = RequireProperty(element, "instances", $"{path}.instances");
        RequireKind(instancesElement, JsonValueKind.Array, $"{path}.instances");
        var instances = instancesElement.EnumerateArray()
            .Select((instance, index) => ReadChipInstance(instance, index))
            .ToImmutableArray();

        var connectionsElement = RequireProperty(element, "connections", $"{path}.connections");
        RequireKind(connectionsElement, JsonValueKind.Array, $"{path}.connections");
        var connections = connectionsElement.EnumerateArray()
            .Select((connection, index) => ReadChipConnection(connection, index))
            .ToImmutableArray();

        var inputsElement = RequireProperty(element, "inputs", $"{path}.inputs");
        RequireKind(inputsElement, JsonValueKind.Array, $"{path}.inputs");
        var inputs = inputsElement.EnumerateArray()
            .Select((input, index) => ReadChipInput(input, index))
            .ToImmutableArray();

        var expectationsElement = RequireProperty(element, "expectations", $"{path}.expectations");
        RequireKind(expectationsElement, JsonValueKind.Array, $"{path}.expectations");
        var expectations = expectationsElement.EnumerateArray()
            .Select((expectation, index) => ReadChipExpectation(expectation, index))
            .ToImmutableArray();

        return new ChipNetworkScenarioPlan(
            parentPanel,
            microticks,
            definitions,
            instances,
            connections,
            inputs,
            expectations);
    }

    private static ChipDefinitionPlan ReadChipDefinition(JsonElement element, int index)
    {
        var path = $"$.chipNetworkScenario.definitions[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        var panel = ReadPanelDefinition(
            RequireProperty(element, "panel", $"{path}.panel"),
            $"{path}.panel");
        var portsElement = RequireProperty(element, "ports", $"{path}.ports");
        RequireKind(portsElement, JsonValueKind.Array, $"{path}.ports");
        var ports = portsElement.EnumerateArray()
            .Select((port, portIndex) => ReadChipPort(port, index, portIndex))
            .ToImmutableArray();
        var parameters = ImmutableArray<KeyValuePair<string, string>>.Empty;
        if (element.TryGetProperty("parameters", out var parametersElement))
        {
            RequireKind(parametersElement, JsonValueKind.Object, $"{path}.parameters");
            parameters = parametersElement.EnumerateObject()
                .Select(parameter => new KeyValuePair<string, string>(
                    parameter.Name,
                    ReadParameterValue(parameter.Value, $"{path}.parameters.{parameter.Name}")))
                .ToImmutableArray();
        }

        return new ChipDefinitionPlan(
            ReadString(RequireProperty(element, "definitionId", $"{path}.definitionId"), $"{path}.definitionId"),
            panel,
            ports,
            ReadInt32(RequireProperty(element, "schemaVersion", $"{path}.schemaVersion"), $"{path}.schemaVersion"),
            ReadInt32(RequireProperty(element, "behaviorVersion", $"{path}.behaviorVersion"), $"{path}.behaviorVersion"),
            ReadString(RequireProperty(element, "symbol", $"{path}.symbol"), $"{path}.symbol"),
            parameters);
    }

    private static PanelDefinitionPlan ReadPanelDefinition(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        var cellsElement = RequireProperty(element, "cells", $"{path}.cells");
        RequireKind(cellsElement, JsonValueKind.Array, $"{path}.cells");
        return new PanelDefinitionPlan(
            ReadString(RequireProperty(element, "panelId", $"{path}.panelId"), $"{path}.panelId"),
            ReadInt32(RequireProperty(element, "width", $"{path}.width"), $"{path}.width"),
            ReadInt32(RequireProperty(element, "height", $"{path}.height"), $"{path}.height"),
            cellsElement.EnumerateArray()
                .Select((cell, index) => ReadPanelCell(cell, index, $"{path}.cells"))
                .ToImmutableArray());
    }

    private static ChipPortPlan ReadChipPort(JsonElement element, int definitionIndex, int index)
    {
        var path = $"$.chipNetworkScenario.definitions[{definitionIndex}].ports[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        return new ChipPortPlan(
            ReadString(RequireProperty(element, "name", $"{path}.name"), $"{path}.name"),
            ReadString(RequireProperty(element, "panelPortId", $"{path}.panelPortId"), $"{path}.panelPortId"),
            ReadString(RequireProperty(element, "direction", $"{path}.direction"), $"{path}.direction"));
    }

    private static ChipInstancePlan ReadChipInstance(JsonElement element, int index)
    {
        var path = $"$.chipNetworkScenario.instances[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        return new ChipInstancePlan(
            ReadString(RequireProperty(element, "instanceId", $"{path}.instanceId"), $"{path}.instanceId"),
            ReadString(RequireProperty(element, "definitionId", $"{path}.definitionId"), $"{path}.definitionId"),
            ReadString(RequireProperty(element, "contentHash", $"{path}.contentHash"), $"{path}.contentHash"));
    }

    private static ChipPortConnectionPlan ReadChipConnection(JsonElement element, int index)
    {
        var path = $"$.chipNetworkScenario.connections[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        return new ChipPortConnectionPlan(
            ReadChipEndpoint(RequireProperty(element, "source", $"{path}.source"), $"{path}.source"),
            ReadChipEndpoint(RequireProperty(element, "target", $"{path}.target"), $"{path}.target"));
    }

    private static ChipPortEndpointPlan ReadChipEndpoint(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        var hasParentPort = element.TryGetProperty("parentPanelPortId", out var parentPort);
        var hasInstance = element.TryGetProperty("instanceId", out var instanceId);
        if (hasParentPort == hasInstance)
        {
            throw new FixtureStructureException(path);
        }

        if (hasParentPort)
        {
            return new ChipPortEndpointPlan(
                null,
                ReadString(parentPort, $"{path}.parentPanelPortId"));
        }

        return new ChipPortEndpointPlan(
            ReadString(instanceId, $"{path}.instanceId"),
            ReadString(RequireProperty(element, "portName", $"{path}.portName"), $"{path}.portName"));
    }

    private static ChipNetworkInputChange ReadChipInput(JsonElement element, int index)
    {
        var path = $"$.chipNetworkScenario.inputs[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        return new ChipNetworkInputChange(
            ReadInt32(RequireProperty(element, "tick", $"{path}.tick"), $"{path}.tick"),
            ReadString(RequireProperty(element, "instanceId", $"{path}.instanceId"), $"{path}.instanceId"),
            ReadString(RequireProperty(element, "portName", $"{path}.portName"), $"{path}.portName"),
            ReadString(RequireProperty(element, "value", $"{path}.value"), $"{path}.value"));
    }

    private static ChipNetworkExpectation ReadChipExpectation(JsonElement element, int index)
    {
        var path = $"$.chipNetworkScenario.expectations[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        var outputsElement = RequireProperty(element, "outputs", $"{path}.outputs");
        RequireKind(outputsElement, JsonValueKind.Object, $"{path}.outputs");
        var outputs = outputsElement.EnumerateObject()
            .ToImmutableSortedDictionary(
                property => property.Name,
                property => ReadString(property.Value, $"{path}.outputs.{property.Name}"),
                StringComparer.Ordinal);
        return new ChipNetworkExpectation(
            ReadInt32(RequireProperty(element, "tick", $"{path}.tick"), $"{path}.tick"),
            ReadString(RequireProperty(element, "instanceId", $"{path}.instanceId"), $"{path}.instanceId"),
            outputs,
            ReadString(RequireProperty(element, "hash", $"{path}.hash"), $"{path}.hash"));
    }

    private static PanelCellPlan ReadPanelCell(JsonElement element, int index, string cellsPath)
    {
        var path = $"{cellsPath}[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        string? portId = null;
        string? behaviorId = null;
        if (element.TryGetProperty("portId", out var portElement))
        {
            portId = ReadString(portElement, $"{path}.portId");
        }

        if (element.TryGetProperty("behaviorId", out var behaviorElement))
        {
            behaviorId = ReadString(behaviorElement, $"{path}.behaviorId");
        }

        var parameters = ImmutableArray<KeyValuePair<string, string>>.Empty;
        if (element.TryGetProperty("parameters", out var parametersElement))
        {
            RequireKind(parametersElement, JsonValueKind.Object, $"{path}.parameters");
            parameters = parametersElement.EnumerateObject()
                .Select(parameter => new KeyValuePair<string, string>(
                    parameter.Name,
                    ReadParameterValue(parameter.Value, $"{path}.parameters.{parameter.Name}")))
                .ToImmutableArray();
        }

        return new PanelCellPlan(
            ReadString(RequireProperty(element, "cellId", $"{path}.cellId"), $"{path}.cellId"),
            ReadInt32(RequireProperty(element, "x", $"{path}.x"), $"{path}.x"),
            ReadInt32(RequireProperty(element, "y", $"{path}.y"), $"{path}.y"),
            ReadString(RequireProperty(element, "kind", $"{path}.kind"), $"{path}.kind"),
            ReadString(
                RequireProperty(element, "orientation", $"{path}.orientation"),
                $"{path}.orientation"),
            portId,
            parameters,
            behaviorId);
    }

    private static PanelInputChange ReadPanelInput(JsonElement element, int index)
    {
        var path = $"$.panelScenario.inputs[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        return new PanelInputChange(
            ReadInt32(RequireProperty(element, "tick", $"{path}.tick"), $"{path}.tick"),
            ReadString(RequireProperty(element, "portId", $"{path}.portId"), $"{path}.portId"),
            ReadString(RequireProperty(element, "value", $"{path}.value"), $"{path}.value"));
    }

    private static PanelProbeExpectation ReadPanelExpectation(JsonElement element, int index)
    {
        var path = $"$.panelScenario.expectations[{index}]";
        RequireKind(element, JsonValueKind.Object, path);
        return new PanelProbeExpectation(
            ReadInt32(RequireProperty(element, "tick", $"{path}.tick"), $"{path}.tick"),
            ReadString(RequireProperty(element, "probeId", $"{path}.probeId"), $"{path}.probeId"),
            ReadString(RequireProperty(element, "value", $"{path}.value"), $"{path}.value"));
    }

    private static string ReadParameterValue(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        _ => throw new FixtureStructureException(path)
    };

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
