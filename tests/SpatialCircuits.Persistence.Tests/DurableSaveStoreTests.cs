using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Persistence;
using SpatialCircuits.Workbench;
using Xunit;

namespace SpatialCircuits.Persistence.Tests;

public sealed class DurableSaveStoreTests
{
    [Fact]
    public void SaveWritesReloadableSessionAndLeavesNoTemporaryFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "world.scsave");
            var session = CreateSession();
            var result = DurableSaveStore.Save(path, session);

            Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(session.SaveId, result.SaveId);
            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            var loaded = DurableSaveStore.Load(path);
            Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Diagnostics));
            Assert.NotNull(loaded.Session);
            Assert.Equal(session.SaveId, loaded.Session!.SaveId);
            Assert.Equal(session.CurrentTick, loaded.Session.CurrentTick);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveReplacesAnExistingValidFileAfterCandidateValidation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "world.scsave");
            var session = CreateSession();
            Assert.True(DurableSaveStore.Save(path, session).Succeeded);
            session.CycleTicks = 11;

            var replaced = DurableSaveStore.Save(path, session);

            Assert.True(replaced.Succeeded, string.Join(Environment.NewLine, replaced.Diagnostics));
            var loaded = DurableSaveStore.Load(path);
            Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Diagnostics));
            Assert.Equal(11, loaded.Session!.CycleTicks);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ValidationFailurePreservesExistingSaveByteForByte()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "world.scsave");
            var session = CreateSession();
            var initial = DurableSaveStore.Save(path, session);
            Assert.True(initial.Succeeded);
            var before = File.ReadAllBytes(path);
            var loaded = DurableSaveStore.Load(path);
            Assert.True(loaded.Succeeded);

            var manifest = loaded.BehaviorManifest!;
            var altered = manifest.Entries[0] with { Sha256 = new string('0', 64) };
            var invalidManifest = manifest with
            {
                Entries = manifest.Entries.SetItem(0, altered)
            };
            var failed = DurableSaveStore.Save(path, session, invalidManifest);

            Assert.False(failed.Succeeded);
            Assert.Contains(failed.Diagnostics, diagnostic =>
                diagnostic.Code == DurableSnapshotDiagnosticCodes.BehaviorFingerprintMismatch);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReplaceFailurePreservesExistingSaveByteForByte()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "world.scsave");
            var session = CreateSession();
            var initial = DurableSaveStore.Save(path, session);
            Assert.True(initial.Succeeded);
            var before = File.ReadAllBytes(path);
            var priorManifest = DurableSaveStore.Load(path).BehaviorManifest!;
            DurableSaveResult failed;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                failed = DurableSaveStore.Save(path, session, priorManifest);
            }

            Assert.False(failed.Succeeded);
            Assert.Contains(failed.Diagnostics, diagnostic =>
                diagnostic.Code == DurableSnapshotDiagnosticCodes.SaveWriteFailed);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MigrationCreatesNewSaveIdentityAndLeavesSourceBytesUntouched()
    {
        var session = CreateSession();
        var source = DurableSnapshotCodec.Serialize(session);
        var sourceCopy = source.ToArray();
        var decoded = DurableSnapshotCodec.Deserialize(source);
        Assert.True(decoded.Succeeded);

        var migrated = DurableTraceAndMigration.Migrate(
            source,
            document => document with
            {
                SaveId = Guid.NewGuid(),
                Session = document.Session with { CycleTicks = 8 }
            });

        Assert.True(migrated.Succeeded, string.Join(Environment.NewLine, migrated.Diagnostics));
        Assert.NotEqual(session.SaveId, migrated.SaveId);
        Assert.NotNull(migrated.Bytes);
        var migratedLoad = DurableSnapshotCodec.Deserialize(migrated.Bytes!);
        Assert.True(migratedLoad.Succeeded, string.Join(Environment.NewLine, migratedLoad.Diagnostics));
        Assert.Equal(migrated.SaveId, migratedLoad.Session!.SaveId);
        Assert.Equal(8, migratedLoad.Session.CycleTicks);
        Assert.Equal(sourceCopy, source);
    }

    [Fact]
    public void MigrationRejectsStableIdentityChanges()
    {
        var source = DurableSnapshotCodec.Serialize(CreateSession());
        var migrated = DurableTraceAndMigration.Migrate(
            source,
            document => document with
            {
                SaveId = Guid.NewGuid(),
                Definitions = document.Definitions with
                {
                    Definitions = document.Definitions.Definitions.SetItem(
                        0,
                        document.Definitions.Definitions[0] with { Id = "changed/id" })
                }
            });

        Assert.False(migrated.Succeeded);
        Assert.Contains(migrated.Diagnostics, diagnostic =>
            diagnostic.Code == DurableSnapshotDiagnosticCodes.MigrationFailed &&
            diagnostic.Path == "$.identity");
    }

    [Fact]
    public void FileMigrationWritesAValidatedCopyAndPreservesSourceAndPriorDestinationOnFailure()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "source.scsave");
            var destinationPath = Path.Combine(directory, "migrated.scsave");
            var session = CreateSession();
            Assert.True(DurableSaveStore.Save(sourcePath, session).Succeeded);
            var sourceBefore = File.ReadAllBytes(sourcePath);
            var migrated = DurableSaveStore.Migrate(
                sourcePath,
                destinationPath,
                document => document with
                {
                    SaveId = Guid.NewGuid(),
                    Session = document.Session with { CycleTicks = 8 }
                });

            Assert.True(migrated.Succeeded, string.Join(Environment.NewLine, migrated.Diagnostics));
            Assert.Equal(sourceBefore, File.ReadAllBytes(sourcePath));
            var migratedLoad = DurableSaveStore.Load(destinationPath);
            Assert.True(migratedLoad.Succeeded, string.Join(Environment.NewLine, migratedLoad.Diagnostics));
            Assert.Equal(8, migratedLoad.Session!.CycleTicks);
            Assert.NotEqual(session.SaveId, migratedLoad.Session.SaveId);

            var destinationBefore = File.ReadAllBytes(destinationPath);
            var failed = DurableSaveStore.Migrate(
                sourcePath,
                destinationPath,
                document => document with
                {
                    SaveId = Guid.NewGuid(),
                    Session = document.Session with { CycleTicks = 0 }
                });

            Assert.False(failed.Succeeded);
            Assert.Equal(sourceBefore, File.ReadAllBytes(sourcePath));
            Assert.Equal(destinationBefore, File.ReadAllBytes(destinationPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PanelWorkbenchSession CreateSession()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/durable-store"),
            3,
            1,
            [
                PanelCellDefinition.Create(
                    new ComponentId("source"),
                    new GridCoordinate(0, 0),
                    CellKind.InputPort,
                    CardinalDirection.East,
                    new PortId("signal")),
                PanelCellDefinition.Create(
                    new ComponentId("wire"),
                    new GridCoordinate(1, 0),
                    CellKind.Wire),
                PanelCellDefinition.Create(
                    new ComponentId("probe"),
                    new GridCoordinate(2, 0),
                    CellKind.Probe)
            ]));
        Assert.True(session.TryDriveInput(new PortId("signal"), LogicValue.High, out var diagnostic),
            diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        session.StepMicrotick();
        return session;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"spatial-wires-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
