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
