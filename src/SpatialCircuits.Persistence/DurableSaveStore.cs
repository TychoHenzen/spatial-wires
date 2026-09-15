using System.Collections.Immutable;
using System.Security;
using SpatialCircuits.Cells;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Persistence;

public static class DurableSaveStore
{
    public static DurableSaveResult Save(
        string path,
        PanelWorkbenchSession session,
        DurableBehaviorManifest? carriedBehaviorManifest = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return SaveFailure(DurableSnapshotDiagnosticCodes.SavePathInvalid, "$", "Save path is empty.");
        }

        ArgumentNullException.ThrowIfNull(session);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SaveFailure(DurableSnapshotDiagnosticCodes.SavePathInvalid, path, exception.Message);
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            return SaveFailure(DurableSnapshotDiagnosticCodes.SavePathInvalid, fullPath, "Save path has no parent directory.");
        }

        string? candidatePath = null;
        try
        {
            var bytes = DurableSnapshotCodec.Serialize(session, carriedBehaviorManifest);
            var readback = DurableSnapshotCodec.Deserialize(bytes, session.CustomCellRules);
            if (!readback.Succeeded || readback.Session is null || readback.Session.SaveId != session.SaveId)
            {
                return new DurableSaveResult(
                    false,
                    session.SaveId,
                    null,
                    readback.Diagnostics.IsDefaultOrEmpty
                        ? [new DurableDiagnostic(
                            DurableSnapshotDiagnosticCodes.SnapshotInvalid,
                            "$",
                            "Saved candidate did not restore to the same session identity.")]
                        : readback.Diagnostics);
            }

            var document = System.Text.Json.JsonSerializer.Deserialize<DurableSaveDocument>(
                bytes,
                DurableSnapshotJson.Options)!;
            Directory.CreateDirectory(directory);
            candidatePath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                       candidatePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 81920,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            var candidateBytes = File.ReadAllBytes(candidatePath);
            if (!candidateBytes.AsSpan().SequenceEqual(bytes) ||
                !DurableSnapshotCodec.Deserialize(candidateBytes, session.CustomCellRules).Succeeded)
            {
                return SaveFailure(
                    DurableSnapshotDiagnosticCodes.SnapshotInvalid,
                    fullPath,
                    "Temporary save failed byte or validation readback.");
            }

            AtomicReplace(candidatePath, fullPath);
            candidatePath = null;
            return new DurableSaveResult(true, document.SaveId, document.ContentHash, []);
        }
        catch (DurableSnapshotException exception)
        {
            return SaveFailure(exception.Code, exception.Path, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          SecurityException or ArgumentException or InvalidOperationException or
                                          NotSupportedException or PathTooLongException)
        {
            return SaveFailure(
                DurableSnapshotDiagnosticCodes.SaveWriteFailed,
                fullPath,
                exception.Message);
        }
        finally
        {
            if (candidatePath is not null)
            {
                try
                {
                    File.Delete(candidatePath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    public static DurableLoadResult Load(string path, CustomCellRuleRegistry? customCellRules = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return LoadFailure(DurableSnapshotDiagnosticCodes.LoadFailed, "$", "Save path is empty.");
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            return DurableSnapshotCodec.Deserialize(bytes, customCellRules);
        }
        catch (FileNotFoundException exception)
        {
            return LoadFailure(DurableSnapshotDiagnosticCodes.LoadFileMissing, path, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          SecurityException or ArgumentException or NotSupportedException or
                                          PathTooLongException)
        {
            return LoadFailure(DurableSnapshotDiagnosticCodes.LoadFailed, path, exception.Message);
        }
    }

    public static DurableMigrationResult Migrate(
        string sourcePath,
        string destinationPath,
        Func<DurableSaveDocument, DurableSaveDocument> migration,
        CustomCellRuleRegistry? customCellRules = null)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
        {
            return MigrationFailure(DurableSnapshotDiagnosticCodes.SavePathInvalid, "$", "Migration paths are required.");
        }

        string sourceFullPath;
        string destinationFullPath;
        try
        {
            sourceFullPath = Path.GetFullPath(sourcePath);
            destinationFullPath = Path.GetFullPath(destinationPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return MigrationFailure(DurableSnapshotDiagnosticCodes.SavePathInvalid, "$", exception.Message);
        }

        if (string.Equals(sourceFullPath, destinationFullPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return MigrationFailure(
                DurableSnapshotDiagnosticCodes.MigrationFailed,
                "$",
                "Migration requires a distinct destination save path.");
        }

        DurableMigrationResult migrated;
        try
        {
            migrated = DurableTraceAndMigration.Migrate(
                File.ReadAllBytes(sourceFullPath),
                migration,
                customCellRules);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          SecurityException or ArgumentException or NotSupportedException or
                                          PathTooLongException)
        {
            return MigrationFailure(DurableSnapshotDiagnosticCodes.LoadFailed, sourceFullPath, exception.Message);
        }

        if (!migrated.Succeeded || migrated.Bytes is null)
        {
            return migrated;
        }

        string? candidatePath = null;
        try
        {
            var directory = Path.GetDirectoryName(destinationFullPath);
            if (string.IsNullOrEmpty(directory))
            {
                return MigrationFailure(DurableSnapshotDiagnosticCodes.SavePathInvalid, destinationFullPath,
                    "Migration destination has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            candidatePath = Path.Combine(
                directory,
                $".{Path.GetFileName(destinationFullPath)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                       candidatePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 81920,
                       FileOptions.WriteThrough))
            {
                stream.Write(migrated.Bytes);
                stream.Flush(flushToDisk: true);
            }

            var readback = File.ReadAllBytes(candidatePath);
            var validation = DurableSnapshotCodec.Deserialize(readback, customCellRules);
            if (!readback.AsSpan().SequenceEqual(migrated.Bytes) || !validation.Succeeded)
            {
                return MigrationFailure(
                    DurableSnapshotDiagnosticCodes.SnapshotInvalid,
                    destinationFullPath,
                    "Migrated candidate failed byte or validation readback.");
            }

            AtomicReplace(candidatePath, destinationFullPath);
            candidatePath = null;
            return migrated;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          SecurityException or ArgumentException or InvalidOperationException or
                                          NotSupportedException or PathTooLongException)
        {
            return MigrationFailure(DurableSnapshotDiagnosticCodes.SaveWriteFailed, destinationFullPath,
                exception.Message);
        }
        finally
        {
            if (candidatePath is not null)
            {
                try
                {
                    File.Delete(candidatePath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static DurableSaveResult SaveFailure(string code, string path, string message) =>
        new(false, null, null, [new DurableDiagnostic(code, path, message)]);

    private static void AtomicReplace(string candidatePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(candidatePath, destinationPath, null, ignoreMetadataErrors: false);
        }
        else
        {
            File.Move(candidatePath, destinationPath);
        }
    }

    private static DurableLoadResult LoadFailure(string code, string path, string message) =>
        new(false, null, null, null, null, [new DurableDiagnostic(code, path, message)]);

    private static DurableMigrationResult MigrationFailure(string code, string path, string message) =>
        new(false, null, null, [new DurableDiagnostic(code, path, message)]);
}
