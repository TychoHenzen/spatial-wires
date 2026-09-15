using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Persistence;

public static class DurableSnapshotCodec
{
    public const int CurrentSchemaVersion = 1;
    public const int CurrentManifestVersion = 1;

    public static byte[] Serialize(
        PanelWorkbenchSession session,
        DurableBehaviorManifest? carriedManifest = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var snapshot = session.CaptureSnapshot();
        var sessionDto = DurableRuntimeCodec.ToDto(snapshot);
        var definitions = BuildDefinitionManifest(sessionDto.CommittedDefinition);
        var behaviors = BuildBehaviorManifest(session, carriedManifest);
        var unsigned = new DurableSaveDocument(
            CurrentSchemaVersion,
            snapshot.SaveId,
            definitions,
            behaviors,
            sessionDto,
            string.Empty);
        var contentHash = DurableSnapshotJson.Hash(DurableSnapshotJson.Serialize(unsigned));
        return DurableSnapshotJson.Serialize(unsigned with { ContentHash = contentHash });
    }

    public static DurableLoadResult Deserialize(
        ReadOnlySpan<byte> bytes,
        CustomCellRuleRegistry? customCellRules = null)
    {
        if (bytes.IsEmpty)
        {
            return Failure(DurableSnapshotDiagnosticCodes.LoadFailed, "$", "Durable save is empty.");
        }

        if (bytes.Length > 64 * 1024 * 1024)
        {
            return Failure(DurableSnapshotDiagnosticCodes.LoadFailed, "$", "Durable save exceeds 64 MiB.");
        }

        try
        {
            var document = System.Text.Json.JsonSerializer.Deserialize<DurableSaveDocument>(
                               bytes,
                               DurableSnapshotJson.Options)
                           ?? throw new JsonException("Durable save document is null.");
            ValidateDocument(document);
            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                return Failure(
                    DurableSnapshotDiagnosticCodes.SchemaUnsupported,
                    "$.schemaVersion",
                    $"Schema version {document.SchemaVersion} is not supported by version {CurrentSchemaVersion}.");
            }

            var unsigned = document with { ContentHash = string.Empty };
            if (!string.Equals(
                    document.ContentHash,
                    DurableSnapshotJson.Hash(DurableSnapshotJson.Serialize(unsigned)),
                    StringComparison.Ordinal))
            {
                return Failure(
                    DurableSnapshotDiagnosticCodes.ContentHashMismatch,
                    "$.contentHash",
                    "Durable save content hash does not match its payload.");
            }

            if (document.Session.SaveId != document.SaveId)
            {
                return Failure(
                    DurableSnapshotDiagnosticCodes.SnapshotInvalid,
                    "$.session.saveId",
                    "Durable save and session identities do not match.");
            }

            var expectedDefinitions = BuildDefinitionManifest(document.Session.CommittedDefinition);
            if (!Equivalent(expectedDefinitions, document.Definitions))
            {
                return Failure(
                    DurableSnapshotDiagnosticCodes.DefinitionManifestMismatch,
                    "$.definitions",
                    "Definition manifest does not match the saved definitions.");
            }

            var session = DurableRuntimeCodec.Restore(document.Session, customCellRules);
            var expectedBehaviors = BuildBehaviorManifest(session, document.Behaviors);
            if (!Equivalent(expectedBehaviors, document.Behaviors))
            {
                return Failure(
                    DurableSnapshotDiagnosticCodes.BehaviorFingerprintMismatch,
                    "$.behaviors",
                    "Behavior manifest does not match the available runtime behavior.");
            }

            return new DurableLoadResult(
                true,
                session,
                document.Behaviors,
                document.Definitions,
                document.ContentHash,
                []);
        }
        catch (DurableSnapshotException exception)
        {
            return Failure(exception.Code, exception.Path, exception.Message);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or
                                          ArgumentException or InvalidOperationException or
                                          KeyNotFoundException or FormatException or OverflowException or
                                          NullReferenceException or IndexOutOfRangeException)
        {
            return Failure(
                DurableSnapshotDiagnosticCodes.SnapshotInvalid,
                "$",
                exception.Message);
        }
    }

    public static DurableDefinitionManifest BuildDefinitionManifest(DurableWorkbenchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var entries = ImmutableArray.CreateBuilder<DurableDefinitionFingerprint>();
        entries.Add(new DurableDefinitionFingerprint("panel", definition.Panel.Id, definition.Panel.ContentHash));
        foreach (var chip in definition.ChipDefinitions)
        {
            entries.Add(new DurableDefinitionFingerprint("chip", chip.Id, chip.ContentHash));
            entries.Add(new DurableDefinitionFingerprint(
                "chip-source-panel",
                $"{chip.Id}/{chip.SourcePanel.Id}",
                chip.SourcePanel.ContentHash));
        }

        if (definition.DeviceGraph is { } graph)
        {
            entries.Add(new DurableDefinitionFingerprint("device-graph", graph.Id, graph.ContentHash));
            foreach (var device in graph.Devices.Where(device => device.Backend.Panel is not null))
            {
                entries.Add(new DurableDefinitionFingerprint(
                    "device-panel",
                    $"{device.Id}/{device.Backend.Panel!.Id}",
                    device.Backend.Panel.ContentHash));
            }
        }

        var workbenchHash = DurableSnapshotJson.Hash(DurableSnapshotJson.Serialize(definition));
        return new DurableDefinitionManifest(
            CurrentManifestVersion,
            workbenchHash,
            entries.OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ThenBy(item => item.ContentHash, StringComparer.Ordinal)
                .ToImmutableArray());
    }

    private static DurableBehaviorManifest BuildBehaviorManifest(
        PanelWorkbenchSession session,
        DurableBehaviorManifest? carried)
    {
        var entries = new Dictionary<string, DurableBehaviorFingerprint>(StringComparer.Ordinal);
        if (carried is not null)
        {
            ValidateBehaviorManifest(carried);
            foreach (var entry in carried.Entries)
            {
                entries.Add(entry.BehaviorId, entry);
            }
        }

        var sources = session.BehaviorAssemblySources
            .Append(new WorkbenchBehaviorAssemblySource(
                "spatial-circuits/persistence",
                typeof(DurableSnapshotCodec).Assembly,
                null,
                false));
        foreach (var source in sources)
        {
            DurableBehaviorFingerprint current;
            if (source.Assembly is not null)
            {
                current = Fingerprint(source.BehaviorId, source.Assembly, source.OptionalForOfflineReplay);
                if (source.Sha256 is not null &&
                    !string.Equals(source.Sha256, current.Sha256, StringComparison.Ordinal))
                {
                    throw new DurableSnapshotException(
                        DurableSnapshotDiagnosticCodes.BehaviorFingerprintMismatch,
                        $"$.behaviors.{source.BehaviorId}",
                        "Current Node backend code does not match the saved behavior fingerprint.");
                }
            }
            else if (source.Sha256 is { } savedFingerprint && entries.TryGetValue(source.BehaviorId, out var saved))
            {
                if (!string.Equals(saved.Sha256, savedFingerprint, StringComparison.Ordinal))
                {
                    throw new DurableSnapshotException(
                        DurableSnapshotDiagnosticCodes.BehaviorFingerprintMismatch,
                        $"$.behaviors.{source.BehaviorId}",
                        "Saved Node backend fingerprint does not match its behavior manifest entry.");
                }

                current = saved;
            }
            else if (source.OptionalForOfflineReplay)
            {
                continue;
            }
            else
            {
                throw new DurableSnapshotException(
                    DurableSnapshotDiagnosticCodes.BehaviorManifestInvalid,
                    $"$.behaviors.{source.BehaviorId}",
                    "Required behavior assembly cannot be fingerprinted.");
            }

            if (entries.TryGetValue(source.BehaviorId, out var prior) &&
                !string.Equals(prior.Sha256, current.Sha256, StringComparison.Ordinal))
            {
                throw new DurableSnapshotException(
                    DurableSnapshotDiagnosticCodes.BehaviorFingerprintMismatch,
                    $"$.behaviors.{source.BehaviorId}",
                    "Behavior assembly changed without a matching behavior identity migration.");
            }

            entries[source.BehaviorId] = current;
        }

        return new DurableBehaviorManifest(
            CurrentManifestVersion,
            entries.Values.OrderBy(entry => entry.BehaviorId, StringComparer.Ordinal).ToImmutableArray());
    }

    private static DurableBehaviorFingerprint Fingerprint(
        string behaviorId,
        Assembly assembly,
        bool optionalForOfflineReplay)
    {
        var path = assembly.Location;
        var hash = !string.IsNullOrEmpty(path) && File.Exists(path)
            ? HashFile(path)
            : HashAssemblyIdentity(assembly);
        var name = assembly.GetName();
        return new DurableBehaviorFingerprint(
            behaviorId,
            name.Name ?? throw new DurableSnapshotException(
                DurableSnapshotDiagnosticCodes.BehaviorManifestInvalid,
                $"$.behaviors.{behaviorId}",
                "Behavior assembly has no stable name."),
            name.Version?.ToString() ?? "0.0.0.0",
            assembly.ManifestModule.ModuleVersionId.ToString("D"),
            hash,
            optionalForOfflineReplay);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashAssemblyIdentity(Assembly assembly)
    {
        var identity = string.Join(
            "\0",
            assembly.FullName ?? string.Empty,
            assembly.ManifestModule.ModuleVersionId.ToString("D"),
            assembly.GetName().Version?.ToString() ?? "0.0.0.0");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static void ValidateDocument(DurableSaveDocument document)
    {
        if (document.SchemaVersion <= 0 || document.SaveId == Guid.Empty ||
            document.Definitions is null || document.Behaviors is null || document.Session is null ||
            !IsHash(document.ContentHash))
        {
            throw new DurableSnapshotException(
                DurableSnapshotDiagnosticCodes.SnapshotInvalid,
                "$",
                "Durable save envelope is incomplete.");
        }

        ValidateBehaviorManifest(document.Behaviors);
    }

    private static void ValidateBehaviorManifest(DurableBehaviorManifest manifest)
    {
        if (manifest.Version != CurrentManifestVersion || manifest.Entries.IsDefault ||
            manifest.Entries.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.BehaviorId) ||
                                          string.IsNullOrWhiteSpace(entry.AssemblyName) ||
                                          string.IsNullOrWhiteSpace(entry.AssemblyVersion) ||
                                          !Guid.TryParse(entry.ModuleVersionId, out _) || !IsHash(entry.Sha256)) ||
            manifest.Entries.Select(entry => entry.BehaviorId).Distinct(StringComparer.Ordinal).Count() !=
            manifest.Entries.Length)
        {
            throw new DurableSnapshotException(
                DurableSnapshotDiagnosticCodes.BehaviorManifestInvalid,
                "$.behaviors",
                "Behavior manifest is incomplete or contains duplicate behavior identifiers.");
        }
    }

    private static bool Equivalent<T>(T expected, T actual) =>
        DurableSnapshotJson.Serialize(expected).AsSpan().SequenceEqual(DurableSnapshotJson.Serialize(actual));

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static DurableLoadResult Failure(string code, string path, string message) =>
        new(false, null, null, null, null, [new DurableDiagnostic(code, path, message)]);
}
