using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace SpatialCircuits.Runner;

public static class TraceOutput
{
    public static void WriteTrace(TextWriter writer, TraceRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);

        WriteLine(writer, json =>
        {
            WriteVersion(json, record.TraceSchema);
            json.WriteString("fixtureId", record.FixtureId);
            json.WriteNumber("sequence", record.Sequence);
            json.WriteString("caseId", record.CaseId);
            json.WriteString("observed", record.Observed);
            json.WriteString("expected", record.Expected);
            json.WriteBoolean("passed", record.Passed);
        });
    }

    public static void WriteScheduledDriveTrace(TextWriter writer, ScheduledDriveTraceRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);

        WriteLine(writer, json =>
        {
            WriteVersion(json, record.TraceSchema);
            json.WriteString("fixtureId", record.FixtureId);
            json.WriteNumber("sequence", record.Sequence);
            json.WriteNumber("microtick", record.Microtick);
            json.WriteString("observed", record.Observed);
            if (record.RestoredObserved is null)
            {
                json.WriteNull("restoredObserved");
            }
            else
            {
                json.WriteString("restoredObserved", record.RestoredObserved);
            }

            json.WriteString("deliveredEvents", record.DeliveredEvents);
            if (record.RestoredDeliveredEvents is null)
            {
                json.WriteNull("restoredDeliveredEvents");
            }
            else
            {
                json.WriteString("restoredDeliveredEvents", record.RestoredDeliveredEvents);
            }

            json.WriteString("diagnostics", record.Diagnostics);
            if (record.RestoredDiagnostics is null)
            {
                json.WriteNull("restoredDiagnostics");
            }
            else
            {
                json.WriteString("restoredDiagnostics", record.RestoredDiagnostics);
            }

            json.WriteString("originalHash", record.OriginalHash);
            json.WriteString("replayedHash", record.ReplayedHash);
            if (record.RestoredHash is null)
            {
                json.WriteNull("restoredHash");
            }
            else
            {
                json.WriteString("restoredHash", record.RestoredHash);
            }

            json.WriteBoolean("passed", record.Passed);
        });
    }

    public static void WritePanelScenarioTrace(TextWriter writer, PanelTraceRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);

        WriteLine(writer, json =>
        {
            WriteVersion(json, record.TraceSchema);
            json.WriteString("fixtureId", record.FixtureId);
            json.WriteNumber("sequence", record.Sequence);
            json.WriteNumber("microtick", record.Microtick);
            json.WriteString("schedulerHash", record.SchedulerHash);
            WriteValues(json, "probes", record.Probes);
            WriteValues(json, "expectedProbes", record.ExpectedProbes);
            WriteValues(json, "outputs", record.Outputs);
            json.WriteBoolean("passed", record.Passed);
        });
    }

    public static void WriteChipScenarioTrace(TextWriter writer, ChipScenarioTraceRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);

        WriteLine(writer, json =>
        {
            WriteVersion(json, record.TraceSchema);
            json.WriteString("fixtureId", record.FixtureId);
            json.WriteNumber("sequence", record.Sequence);
            json.WriteNumber("microtick", record.Microtick);
            json.WriteString("chipInstanceId", record.ChipInstanceId);
            json.WriteString("definitionId", record.DefinitionId);
            json.WriteString("contentHash", record.ContentHash);
            json.WriteString("hash", record.Hash);
            WriteValues(json, "outputs", record.Outputs);
            WriteValues(json, "expectedOutputs", record.ExpectedOutputs);
            if (record.ExpectedHash is null)
            {
                json.WriteNull("expectedHash");
            }
            else
            {
                json.WriteString("expectedHash", record.ExpectedHash);
            }

            json.WriteBoolean("passed", record.Passed);
        });
    }

    public static void WriteDeviceExchangeTrace(TextWriter writer, DeviceExchangeTraceRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);

        WriteLine(writer, json =>
        {
            WriteVersion(json, record.TraceSchema);
            json.WriteString("fixtureId", record.FixtureId);
            json.WriteNumber("sequence", record.Sequence);
            json.WriteNumber("microtick", record.Microtick);
            json.WriteString("challenge", record.Challenge);
            json.WriteString("expectedChallenge", record.ExpectedChallenge);
            json.WriteString("response", record.Response);
            json.WriteString("expectedResponse", record.ExpectedResponse);
            WriteDeliveries(json, "deliveries", record.Deliveries);
            json.WriteStartArray("expectedDeliveries");
            foreach (var delivery in record.ExpectedDeliveries)
            {
                json.WriteStartObject();
                json.WriteString("laneId", delivery.LaneId);
                json.WriteNumber("tick", delivery.Tick);
                json.WriteString("signal", delivery.Signal);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteBoolean("passed", record.Passed);
        });
    }

    public static void WriteDiagnostic(TextWriter writer, FixtureDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(diagnostic);

        WriteLine(writer, json =>
        {
            WriteVersion(json, TraceVersion.Current);
            json.WriteString("kind", "diagnostic");
            json.WriteString("code", diagnostic.Code);
            json.WriteString("severity", "Error");
            json.WriteString("path", diagnostic.Path);
            json.WriteString("message", diagnostic.Message);
        });
    }

    private static void WriteVersion(Utf8JsonWriter writer, TraceVersion version)
    {
        writer.WriteStartObject("traceSchema");
        writer.WriteNumber("major", version.Major);
        writer.WriteNumber("minor", version.Minor);
        writer.WriteEndObject();
    }

    private static void WriteValues(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyDictionary<string, string> values)
    {
        writer.WriteStartObject(propertyName);
        foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteDeliveries(
        Utf8JsonWriter writer,
        string propertyName,
        ImmutableArray<DeviceExchangeDeliveryTrace> deliveries)
    {
        writer.WriteStartArray(propertyName);
        foreach (var delivery in deliveries)
        {
            writer.WriteStartObject();
            writer.WriteString("laneId", delivery.LaneId);
            writer.WriteNumber("tick", delivery.Tick);
            writer.WriteString("signal", delivery.Signal);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLine(TextWriter writer, Action<Utf8JsonWriter> writeProperties)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            writeProperties(json);
            json.WriteEndObject();
        }

        writer.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
