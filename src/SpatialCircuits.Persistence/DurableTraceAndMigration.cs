using System.Collections.Immutable;
using System.Text.Json;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Persistence;

public sealed record DurableTraceEntry(
    string Source,
    long Tick,
    string Hash,
    ImmutableArray<DurableTraceValue> Values)
{
    public ImmutableArray<DurableTraceEvent> DeliveredEvents { get; init; } = [];

    public ImmutableArray<DurableTraceDiagnostic> Diagnostics { get; init; } = [];

    public ImmutableArray<DurableTracePresentationEvent> PresentationEvents { get; init; } = [];
}

public sealed record DurableTraceValue(string Path, string Value);

public sealed record DurableTraceEvent(
    long DueTick,
    string TargetStableId,
    long TargetIncarnation,
    string TargetPortOrLane,
    string SourceStableId,
    long SourceIncarnation,
    string SourcePort,
    string EventKind,
    long CausalOrdinal,
    string Value,
    string Payload,
    string TemporalRootId);

public sealed record DurableTraceDiagnostic(
    string Code,
    string Message,
    long Tick,
    SchedulerPhase Phase);

public sealed record DurableTracePresentationEvent(
    string DeviceId,
    long Tick,
    string EventId);

public sealed record DurableTraceExport(
    int SchemaVersion,
    string SaveContentHash,
    ImmutableArray<DurableTraceEntry> Entries,
    string ContentHash);

public sealed record DurableMigrationResult(
    bool Succeeded,
    Guid? SaveId,
    byte[]? Bytes,
    ImmutableArray<DurableDiagnostic> Diagnostics);

public static class DurableTraceAndMigration
{
    public static DurableTraceExport Export(
        PanelWorkbenchSession session,
        int presentationFramesPerTick = 1)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (presentationFramesPerTick <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationFramesPerTick),
                "Presentation frame cadence must be positive.");
        }

        var snapshot = session.CaptureSnapshot();
        var saveBytes = DurableSnapshotCodec.Serialize(session);
        var save = JsonSerializer.Deserialize<DurableSaveDocument>(
                        saveBytes,
                        DurableSnapshotJson.Options)
                    ?? throw new InvalidDataException("Durable save could not be decoded for trace export.");
        var entries = EnumerateTraces(snapshot)
            .OrderBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Trace.Tick)
            .Select(item => new DurableTraceEntry(
                item.Source,
                item.Trace.Tick,
                item.Trace.Hash,
                item.Trace.ResolvedInputs
                    .OrderBy(pair => pair.Key.TargetStableId, StringComparer.Ordinal)
                    .ThenBy(pair => pair.Key.TargetIncarnation)
                    .ThenBy(pair => pair.Key.PortOrLane, StringComparer.Ordinal)
                    .Select(pair => new DurableTraceValue(
                        $"scheduler.inputs/{pair.Key.TargetStableId}/{pair.Key.PortOrLane}",
                        pair.Value.ToString()))
                    .ToImmutableArray())
            {
                DeliveredEvents = item.Trace.DeliveredEvents
                    .OrderBy(scheduledEvent => scheduledEvent.Key)
                    .Select(ToDurableEvent)
                    .ToImmutableArray(),
                Diagnostics = item.Trace.Diagnostics
                    .Select(diagnostic => new DurableTraceDiagnostic(
                        diagnostic.Code,
                        diagnostic.Message,
                        diagnostic.Tick,
                        diagnostic.Phase))
                    .ToImmutableArray(),
                PresentationEvents = item.Source.StartsWith("device-graph/", StringComparison.Ordinal) &&
                                      snapshot.DeviceGraph is { } graph
                    ? graph.Devices
                        .Where(device => device.PresentationEvents.Any(presentation =>
                            presentation.Tick == item.Trace.Tick))
                        .SelectMany(device => device.PresentationEvents
                            .Where(presentation => presentation.Tick == item.Trace.Tick)
                            .Select(presentation => new DurableTracePresentationEvent(
                                device.DeviceId.Value,
                                presentation.Tick,
                                presentation.EventId)))
                        .OrderBy(presentation => presentation.DeviceId, StringComparer.Ordinal)
                        .ThenBy(presentation => presentation.EventId, StringComparer.Ordinal)
                        .ToImmutableArray()
                    : []
            })
            .ToImmutableArray();
        var unsigned = new DurableTraceExport(
            DurableSnapshotCodec.CurrentSchemaVersion,
            save.ContentHash,
            entries,
            string.Empty);
        var contentHash = DurableSnapshotJson.Hash(DurableSnapshotJson.Serialize(unsigned));
        return unsigned with { ContentHash = contentHash };
    }

    public static DurableMigrationResult Migrate(
        ReadOnlySpan<byte> sourceBytes,
        Func<DurableSaveDocument, DurableSaveDocument> migration,
        CustomCellRuleRegistry? customCellRules = null)
    {
        ArgumentNullException.ThrowIfNull(migration);
        var loaded = DurableSnapshotCodec.Deserialize(sourceBytes, customCellRules);
        if (!loaded.Succeeded)
        {
            return new DurableMigrationResult(false, null, null, loaded.Diagnostics);
        }

        DurableSaveDocument source;
        try
        {
            source = JsonSerializer.Deserialize<DurableSaveDocument>(
                         sourceBytes,
                         DurableSnapshotJson.Options)
                     ?? throw new JsonException("Durable source document is null.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new DurableMigrationResult(false, null, null, [new DurableDiagnostic(
                DurableSnapshotDiagnosticCodes.MigrationFailed,
                "$",
                exception.Message)]);
        }

        try
        {
            var candidate = migration(source);
            if (candidate is null || candidate.SaveId == source.SaveId)
            {
                return new DurableMigrationResult(false, null, null, [new DurableDiagnostic(
                    DurableSnapshotDiagnosticCodes.MigrationFailed,
                    "$.saveId",
                    "Migration must create a new save identity.")]);
            }

            if (!StableIdentityEqual(source, candidate))
            {
                return new DurableMigrationResult(false, null, null, [new DurableDiagnostic(
                    DurableSnapshotDiagnosticCodes.MigrationFailed,
                    "$.identity",
                    "Migration must preserve stable circuit, component, port, behavior, and replay identifiers.")]);
            }

            candidate = candidate with
            {
                Session = candidate.Session with { SaveId = candidate.SaveId },
                ContentHash = string.Empty
            };
            var candidateWithoutHash = candidate;
            var bytes = DurableSnapshotJson.Serialize(candidateWithoutHash);
            var candidateBytes = DurableSnapshotJson.Serialize(candidateWithoutHash with
            {
                ContentHash = DurableSnapshotJson.Hash(bytes)
            });
            var candidateLoad = DurableSnapshotCodec.Deserialize(candidateBytes, customCellRules);
            if (!candidateLoad.Succeeded)
            {
                return new DurableMigrationResult(false, null, null, candidateLoad.Diagnostics);
            }

            return new DurableMigrationResult(true, candidate.SaveId, candidateBytes, []);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new DurableMigrationResult(false, null, null, [new DurableDiagnostic(
                DurableSnapshotDiagnosticCodes.MigrationFailed,
                "$",
                exception.Message)]);
        }
    }

    private static DurableTraceEvent ToDurableEvent(ScheduledEvent scheduledEvent) => new(
        scheduledEvent.Key.DueTick,
        scheduledEvent.Key.TargetStableId,
        scheduledEvent.Key.TargetIncarnation,
        scheduledEvent.Key.TargetPortOrLane,
        scheduledEvent.Key.SourceStableId,
        scheduledEvent.SourceIncarnation,
        scheduledEvent.Key.SourcePort,
        scheduledEvent.Key.EventKind,
        scheduledEvent.Key.CausalOrdinal,
        scheduledEvent.Value.ToString(),
        scheduledEvent.Payload,
        scheduledEvent.TemporalRootId);

    private static bool StableIdentityEqual(
        DurableSaveDocument source,
        DurableSaveDocument candidate)
    {
        var sourceIds = CollectStableIdentity(source);
        var candidateIds = CollectStableIdentity(candidate);
        return sourceIds.SequenceEqual(candidateIds);
    }

    private static ImmutableArray<string> CollectStableIdentity(DurableSaveDocument document)
    {
        var values = new List<string>();
        CollectStableIdentity(document, "$", values);
        values.Sort(StringComparer.Ordinal);
        return values.ToImmutableArray();
    }

    private static void CollectStableIdentity(object? value, string path, ICollection<string> values)
    {
        if (value is null || value is Guid || value is Enum || value is System.Reflection.MemberInfo ||
            value.GetType().IsPrimitive)
        {
            return;
        }

        if (value is string text)
        {
            values.Add(path + "=" + text);
            return;
        }

        if (value.GetType().Name.EndsWith("Id", StringComparison.Ordinal) &&
            value.GetType().GetProperty("Value")?.GetValue(value) is string stableValue)
        {
            values.Add(path + ".Value=" + stableValue);
            return;
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                CollectStableIdentity(item, path + "[]", values);
            }

            return;
        }

        foreach (var property in value.GetType().GetProperties(System.Reflection.BindingFlags.Instance |
                                                               System.Reflection.BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0 ||
                string.Equals(property.Name, "SaveId", StringComparison.Ordinal) ||
                property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Fingerprint", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var propertyValue = property.GetValue(value);
            if (propertyValue is string)
            {
                if (IsStableIdentityProperty(property.Name))
                {
                    CollectStableIdentity(propertyValue, path + "." + property.Name, values);
                }

                continue;
            }

            CollectStableIdentity(propertyValue, path + "." + property.Name, values);
        }
    }

    private static bool IsStableIdentityProperty(string name) =>
        name.Equals("Id", StringComparison.Ordinal) ||
        name.EndsWith("Id", StringComparison.Ordinal) ||
        name.EndsWith("Ids", StringComparison.Ordinal) ||
        name.Equals("Name", StringComparison.Ordinal) ||
        name.EndsWith("PortName", StringComparison.Ordinal) ||
        name.Equals("StableId", StringComparison.Ordinal) ||
        name.Equals("RootId", StringComparison.Ordinal) ||
        name.Equals("InputPort", StringComparison.Ordinal) ||
        name.Equals("PortOrLane", StringComparison.Ordinal) ||
        name.Equals("SourcePort", StringComparison.Ordinal) ||
        name.Equals("TargetPortOrLane", StringComparison.Ordinal);

    private static IEnumerable<(string Source, SchedulerTickTrace Trace)> EnumerateTraces(
        PanelWorkbenchSessionSnapshot snapshot)
    {
        foreach (var trace in snapshot.ChipNetwork.OwnerPanel.Scheduler.Trace)
        {
            yield return ("panel/" + snapshot.ChipNetwork.OwnerPanelId.Value, trace);
        }

        foreach (var chip in snapshot.ChipNetwork.Chips)
        {
            foreach (var item in EnumerateChipTraces(chip, "chip/" + chip.InstanceId.Value))
            {
                yield return item;
            }
        }

        if (snapshot.DeviceGraph is not null)
        {
            foreach (var trace in snapshot.DeviceGraph.Scheduler.Trace)
            {
                yield return ("device-graph/" + snapshot.DeviceGraph.Definition.Id.Value, trace);
            }
        }
    }

    private static IEnumerable<(string Source, SchedulerTickTrace Trace)> EnumerateChipTraces(
        ChipInstanceRuntimeSnapshot chip,
        string source)
    {
        foreach (var trace in chip.Panel.Scheduler.Trace)
        {
            yield return (source, trace);
        }

        foreach (var child in chip.Children)
        {
            foreach (var item in EnumerateChipTraces(child, source + "/" + child.InstanceId.Value))
            {
                yield return item;
            }
        }
    }
}
