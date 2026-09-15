using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Persistence;
using SpatialCircuits.Workbench;
using Xunit;

namespace SpatialCircuits.Persistence.Tests;

public sealed class DurableSnapshotCodecTests
{
    [Fact]
    public void CodecPreservesNodeCellBindingIdentityAndVersion()
    {
        var panel = PanelDefinition.Create(new CircuitId("panel/node-binding-save"), 2, 1, []);
        var nodeDevice = DeviceDefinition.Create(
            new ComponentId("device/node-cell"),
            NodeDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
                "cell/monitor",
                3));
        var graph = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-binding-save"),
            [nodeDevice],
            []);
        var definition = WorkbenchDefinition.Restore(
            panel,
            ChipDefinitionCatalog.Create([]),
            PanelOwnedChipNetworkDefinition.Create(panel.Id, [], []),
            [],
            graph,
            [new WorkbenchDevicePlacement(nodeDevice.Id, new GridCoordinate(1, 0))]);
        var session = new PanelWorkbenchSession(definition);

        var loaded = DurableSnapshotCodec.Deserialize(DurableSnapshotCodec.Serialize(session));

        Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Diagnostics));
        var backend = Assert.IsType<NodeDeviceBackendDefinition>(
            loaded.Session!.CommittedDefinition.DeviceGraph!.Devices.Single().Backend);
        Assert.Equal("cell/monitor", backend.BindingId);
        Assert.Equal(3, backend.BindingVersion);
    }

    [Fact]
    public void DurableSnapshotRoundTripsCanonicallyAndResumesTheSameTrace()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/durable-snapshot"),
            3,
            1,
            [
                PortCell("source", 0, CellKind.InputPort, "signal"),
                Cell("wire", 1, CellKind.Wire),
                Cell("probe", 2, CellKind.Probe)
            ]);
        var session = new PanelWorkbenchSession(panel);
        Assert.True(session.TryDriveInput(new PortId("signal"), LogicValue.High, out var diagnostic),
            diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        session.StepMicrotick();
        session.StepMicrotick();

        var bytes = DurableSnapshotCodec.Serialize(session);
        var loaded = DurableSnapshotCodec.Deserialize(bytes);

        Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.NotNull(loaded.Session);
        Assert.Equal(session.SaveId, loaded.Session!.SaveId);
        Assert.Equal(session.CurrentTick, loaded.Session.CurrentTick);
        Assert.Equal(session.ProbeHistory.ToArray(), loaded.Session.ProbeHistory.ToArray());
        Assert.NotNull(loaded.DefinitionManifest);
        Assert.Contains(loaded.DefinitionManifest!.Definitions, entry => entry.Kind == "panel");
        Assert.NotNull(loaded.BehaviorManifest);
        Assert.NotEmpty(loaded.BehaviorManifest!.Entries);
        Assert.All(loaded.BehaviorManifest.Entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.AssemblyName));
            Assert.False(string.IsNullOrWhiteSpace(entry.AssemblyVersion));
            Assert.True(Guid.TryParse(entry.ModuleVersionId, out _));
            Assert.Matches("^[0-9a-f]{64}$", entry.Sha256);
        });
        Assert.Equal(bytes, DurableSnapshotCodec.Serialize(loaded.Session, loaded.BehaviorManifest));

        var originalTick = session.StepMicrotick();
        var restoredTick = loaded.Session.StepMicrotick();
        Assert.Equal(originalTick.Tick, restoredTick.Tick);
        Assert.Equal(originalTick.Hash, restoredTick.Hash);
        Assert.Equal(originalTick.Outputs.OrderBy(pair => pair.Key).ToArray(),
            restoredTick.Outputs.OrderBy(pair => pair.Key).ToArray());
    }

    [Fact]
    public void MissingCausalOrdinalIsRejectedAfterDocumentHashIsRecomputed()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/durable-missing-field"), 1, 1, []));
        var bytes = DurableSnapshotCodec.Serialize(session);
        var document = JsonNode.Parse(bytes)!.AsObject();
        var originalHash = document["contentHash"]!.GetValue<string>();
        document["contentHash"] = string.Empty;
        var canonicalWithoutHash = Encoding.UTF8.GetBytes(document.ToJsonString());
        Assert.Equal(originalHash, Convert.ToHexString(SHA256.HashData(canonicalWithoutHash)).ToLowerInvariant());

        var scheduler = document["session"]!["chipNetwork"]!["ownerPanel"]!["scheduler"]!.AsObject();
        scheduler["nextCausalOrdinal"] = 0;
        var unsigned = Encoding.UTF8.GetBytes(document.ToJsonString());
        document["contentHash"] = Convert.ToHexString(SHA256.HashData(unsigned)).ToLowerInvariant();
        var incomplete = Encoding.UTF8.GetBytes(document.ToJsonString());

        var loaded = DurableSnapshotCodec.Deserialize(incomplete);

        Assert.False(loaded.Succeeded);
        Assert.Null(loaded.Session);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Code == "durable.snapshot-invalid");
    }

    [Fact]
    public void SchedulerTargetOutsidePanelIsRejectedAfterDocumentHashIsRecomputed()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/durable-target-boundary"), 1, 1, []));
        var document = JsonNode.Parse(DurableSnapshotCodec.Serialize(session))!.AsObject();
        document["session"]!["chipNetwork"]!["ownerPanel"]!["scheduler"]!["targets"]!
            .AsArray().Add(JsonNode.Parse("{\"stableId\":\"missing-target\",\"incarnation\":1,\"active\":true}"));
        document["contentHash"] = string.Empty;
        document["contentHash"] = HashJson(document);

        var loaded = DurableSnapshotCodec.Deserialize(Encoding.UTF8.GetBytes(document.ToJsonString()));

        Assert.False(loaded.Succeeded);
        Assert.Null(loaded.Session);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Code == DurableSnapshotDiagnosticCodes.SnapshotInvalid);
    }

    [Fact]
    public void SchedulerTraceHashTamperingIsRejectedAfterDocumentHashIsRecomputed()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/durable-trace-boundary"), 1, 1, []));
        session.StepMicrotick();
        var document = JsonNode.Parse(DurableSnapshotCodec.Serialize(session))!.AsObject();
        document["session"]!["chipNetwork"]!["ownerPanel"]!["scheduler"]!["trace"]![0]!["hash"] = "bad";
        document["contentHash"] = string.Empty;
        document["contentHash"] = HashJson(document);

        var loaded = DurableSnapshotCodec.Deserialize(Encoding.UTF8.GetBytes(document.ToJsonString()));

        Assert.False(loaded.Succeeded);
        Assert.Null(loaded.Session);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Code == DurableSnapshotDiagnosticCodes.SnapshotInvalid);
    }

    [Fact]
    public void CodecRoundTripsPinnedChipAndInFlightDeviceGraph()
    {
        var rootPanel = PanelDefinition.Create(new CircuitId("panel/durable-world"), 8, 1, []);
        var chipPanel = PanelDefinition.Create(
            new CircuitId("panel/durable-chip"),
            3,
            1,
            [
                PortCell("chip-input", 0, CellKind.InputPort, "signal"),
                Cell("chip-wire", 1, CellKind.Wire),
                PortCell("chip-output", 2, CellKind.OutputPort, "out")
            ]);
        var chip = ChipDefinition.Create(
            new DefinitionId("chip/durable"),
            chipPanel,
            [
                new ChipPortDefinition("signal", new PortId("signal"), ChipPortDirection.Input),
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)
            ],
            1,
            1,
            "durable-chip");
        var catalog = ChipDefinitionCatalog.Create([chip]);
        var chipInstance = new ComponentId("chip/durable-instance");
        var network = PanelOwnedChipNetworkDefinition.Create(
            rootPanel.Id,
            [ChipInstanceDefinition.Create(chipInstance, chip.Id, chip.ContentHash)]);
        var source = DeviceDefinition.Create(
            new ComponentId("device/durable-source"),
            TimedDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("out", DevicePortDirection.Output)],
                [new TimedOutputChange(3, "out", DeviceSignal.Scalar(LogicValue.High))]));
        var sink = DeviceDefinition.Create(
            new ComponentId("device/durable-sink"),
            TimedDeviceBackendDefinition.Create(
                [
                    DevicePortDefinition.Create("in", DevicePortDirection.Input),
                    DevicePortDefinition.Create("out", DevicePortDirection.Output)
                ],
                []));
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane/durable"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            latency: 2);
        var graph = DeviceGraphDefinition.Create(
            new DefinitionId("graph/durable"),
            [source, sink],
            [lane],
            [CableBundleDefinition.Create(new ComponentId("bundle/durable"), [lane.Id])]);
        var definition = WorkbenchDefinition.Restore(
            rootPanel,
            catalog,
            network,
            [new WorkbenchChipPlacement(chipInstance, chip.Id, chip.ContentHash, new GridCoordinate(0, 0))],
            graph,
            [
                new WorkbenchDevicePlacement(source.Id, new GridCoordinate(5, 0)),
                new WorkbenchDevicePlacement(sink.Id, new GridCoordinate(6, 0))
            ]);
        var session = new PanelWorkbenchSession(definition);
        Assert.True(session.TryDriveChipInput(chipInstance, "signal", LogicValue.High, out var diagnostic),
            diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        session.SetPaused(false);
        for (var tick = 0; tick <= 3; tick++)
        {
            Assert.Equal(tick, session.StepMicrotick().Tick);
        }

        var bytes = DurableSnapshotCodec.Serialize(session);
        var loaded = DurableSnapshotCodec.Deserialize(bytes);
        Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Diagnostics));
        var restored = loaded.Session!;
        Assert.Equal(chip.ContentHash,
            restored.CommittedDefinition.ChipCatalog.Definitions.Single().ContentHash);
        Assert.Equal(lane, restored.CommittedDefinition.DeviceGraph!.Lanes.Single());
        Assert.Equal(session.GetCableLaneHistory(lane.Id).ToArray(),
            restored.GetCableLaneHistory(lane.Id).ToArray());
        Assert.Equal(session.GetChipOutput(chipInstance, "out"), restored.GetChipOutput(chipInstance, "out"));

        for (var tick = 4; tick <= 5; tick++)
        {
            var originalResult = session.StepMicrotick();
            var restoredResult = restored.StepMicrotick();
            Assert.Equal(tick, originalResult.Tick);
            Assert.Equal(originalResult.Hash, restoredResult.Hash);
            var originalSnapshot = session.CaptureSnapshot();
            var restoredSnapshot = restored.CaptureSnapshot();
            Assert.Equal(originalSnapshot.DeviceGraph!.Scheduler.Trace[^1].Hash,
                restoredSnapshot.DeviceGraph!.Scheduler.Trace[^1].Hash);
            Assert.Equal(session.GetCableLaneHistory(lane.Id).ToArray(),
                restored.GetCableLaneHistory(lane.Id).ToArray());
        }
    }

    [Fact]
    public void SnapshotDiffReportsFirstStablePathAndIgnoresDerivedHashes()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/durable-diff"), 1, 1, []));
        var first = DurableSnapshotCodec.Serialize(session);
        session.CycleTicks = 7;
        var second = DurableSnapshotCodec.Serialize(session);

        var difference = DurableSnapshotDiff.FirstDifference(first, second);

        Assert.NotNull(difference);
        Assert.Equal("$.session.cycleTicks", difference!.Path);
        Assert.Equal("4", difference.ExpectedValue);
        Assert.Equal("7", difference.ActualValue);
    }

    [Fact]
    public void TraceExportIncludesVersionedHashAndExactMicrotickEntries()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/durable-trace"), 1, 1, []));
        session.StepMicrotick();
        session.StepMicrotick();

        var export = DurableTraceAndMigration.Export(session);
        var savedHash = DurableSnapshotCodec.Deserialize(DurableSnapshotCodec.Serialize(session)).ContentHash;
        session.CycleTicks = 9;
        var presentationCadenceExport = DurableTraceAndMigration.Export(session);

        Assert.Equal(DurableSnapshotCodec.CurrentSchemaVersion, export.SchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(export.SaveContentHash));
        Assert.Equal(savedHash, export.SaveContentHash);
        Assert.Equal([0L, 1L], export.Entries.Select(entry => entry.Tick));
        Assert.All(export.Entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Hash)));
        Assert.All(export.Entries, entry =>
        {
            Assert.False(entry.DeliveredEvents.IsDefault);
            Assert.False(entry.Diagnostics.IsDefault);
        });
        Assert.False(string.IsNullOrWhiteSpace(export.ContentHash));
        var regeneratedPresentation = DurableTraceAndMigration.Export(session, presentationFramesPerTick: 7);
        Assert.Equal(export.Entries.ToArray(), regeneratedPresentation.Entries.ToArray());
        Assert.Equal(export.Entries.ToArray(), presentationCadenceExport.Entries.ToArray());
    }

    [Fact]
    public void UnresolvedNetworkOwnerIsRejectedBeforeSessionPublication()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/durable-reference"), 1, 1, []));
        var root = JsonNode.Parse(DurableSnapshotCodec.Serialize(session))!.AsObject();
        var definition = root["session"]!["committedDefinition"]!.AsObject();
        definition["chipNetwork"]!["ownerPanelId"] = "panel/missing";
        root["definitions"]!["workbenchHash"] = HashJson(definition);
        root["contentHash"] = string.Empty;
        root["contentHash"] = HashJson(root);

        var loaded = DurableSnapshotCodec.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));

        Assert.False(loaded.Succeeded);
        Assert.Null(loaded.Session);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Code == "durable.snapshot-invalid");
    }

    [Fact]
    public void CodecPreservesRunningAndStagedTopologyEditsAcrossReload()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/durable-edits"), 3, 1, []));
        session.SetPaused(false);
        Assert.True(session.TryPaint(new GridCoordinate(0, 0), CellKind.Wire, CardinalDirection.East,
            out var diagnostic), diagnostic?.Message);
        session.StepMicrotick();
        Assert.True(session.TryPaint(new GridCoordinate(1, 0), CellKind.Wire, CardinalDirection.East,
            out diagnostic), diagnostic?.Message);
        session.SetPaused(true);
        Assert.True(session.TryPaint(new GridCoordinate(2, 0), CellKind.Wire, CardinalDirection.East,
            out diagnostic), diagnostic?.Message);

        var loaded = DurableSnapshotCodec.Deserialize(DurableSnapshotCodec.Serialize(session));

        Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.Equal(session.PendingCommandCount, loaded.Session!.PendingCommandCount);
        Assert.Equal(session.StagedEditCount, loaded.Session.StagedEditCount);
        Assert.Equal(session.CommandLog.ToArray(), loaded.Session.CommandLog.ToArray());
        Assert.Equal(
            session.Definition.Panel.Cells.Select(cell => cell?.Kind).ToArray(),
            loaded.Session.Definition.Panel.Cells.Select(cell => cell?.Kind).ToArray());

        var original = session.StepMicrotick();
        var restored = loaded.Session.StepMicrotick();
        Assert.Equal(original.Hash, restored.Hash);
        Assert.Equal(session.StagedEditCount, loaded.Session.StagedEditCount);
    }

    private static string HashJson(JsonNode node) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString()))).ToLowerInvariant();

    private static PanelCellDefinition Cell(string id, int x, CellKind kind) =>
        PanelCellDefinition.Create(new ComponentId(id), new GridCoordinate(x, 0), kind);

    private static PanelCellDefinition PortCell(string id, int x, CellKind kind, string portId) =>
        PanelCellDefinition.Create(
            new ComponentId(id),
            new GridCoordinate(x, 0),
            kind,
            CardinalDirection.East,
            new PortId(portId));
}
