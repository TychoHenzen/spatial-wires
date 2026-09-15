using System.Collections.Immutable;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Persistence;

public sealed record DurableDiagnostic(string Code, string Path, string Message);

public sealed record DurableSaveResult(
    bool Succeeded,
    Guid? SaveId,
    string? ContentHash,
    ImmutableArray<DurableDiagnostic> Diagnostics);

public sealed record DurableLoadResult(
    bool Succeeded,
    PanelWorkbenchSession? Session,
    DurableBehaviorManifest? BehaviorManifest,
    DurableDefinitionManifest? DefinitionManifest,
    string? ContentHash,
    ImmutableArray<DurableDiagnostic> Diagnostics);

public sealed record DurableSnapshotDifference(string Path, string ExpectedValue, string ActualValue);

public sealed class DurableSnapshotException : InvalidOperationException
{
    public DurableSnapshotException(string code, string path, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Path = path;
    }

    public string Code { get; }

    public string Path { get; }
}

public static class DurableSnapshotDiagnosticCodes
{
    public const string SavePathInvalid = "durable.save.path-invalid";
    public const string SaveWriteFailed = "durable.save.write-failed";
    public const string LoadFileMissing = "durable.load.file-missing";
    public const string LoadFailed = "durable.load.failed";
    public const string SchemaUnsupported = "durable.schema.unsupported";
    public const string ContentHashMismatch = "durable.content-hash-mismatch";
    public const string DefinitionManifestMismatch = "durable.definition-manifest-mismatch";
    public const string BehaviorManifestInvalid = "durable.behavior-manifest-invalid";
    public const string BehaviorFingerprintMismatch = "durable.behavior-fingerprint-mismatch";
    public const string ReferenceUnresolved = "durable.reference-unresolved";
    public const string SnapshotInvalid = "durable.snapshot-invalid";
    public const string MigrationUnavailable = "durable.migration-unavailable";
    public const string MigrationFailed = "durable.migration-failed";
}
