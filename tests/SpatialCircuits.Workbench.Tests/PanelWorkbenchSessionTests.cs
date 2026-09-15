using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;
using Xunit;

namespace SpatialCircuits.Workbench.Tests;

public sealed class PanelWorkbenchSessionTests
{
    [Fact]
    public void WorkbenchModelDoesNotDependOnGodot()
    {
        Assert.DoesNotContain(
            typeof(PanelWorkbenchSession).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.StartsWith("Godot", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RunningEditsAreOrdinalLoggedAndCommitAtTheNextMicrotick()
    {
        var session = new PanelWorkbenchSession(EmptyPanel(2, 1));
        session.SetPaused(false);

        Assert.True(session.TryPaint(new GridCoordinate(0, 0), CellKind.Wire, CardinalDirection.East,
            out var diagnostic), diagnostic?.Message);

        var accepted = Assert.Single(session.CommandLog);
        Assert.Equal(1, accepted.AcceptedOrdinal);
        Assert.Equal(0, accepted.ApplyAtTick);
        Assert.Equal(WorkbenchCommandStatus.Pending, accepted.Status);
        Assert.Null(session.CommittedDefinition.Panel.GetCell(new GridCoordinate(0, 0)));
        Assert.Equal(CellKind.Wire, session.Definition.Panel.GetCell(new GridCoordinate(0, 0))!.Kind);

        Assert.Equal(0, session.StepMicrotick().Tick);

        Assert.Equal(WorkbenchCommandStatus.Committed, Assert.Single(session.CommandLog).Status);
        Assert.Equal(CellKind.Wire, session.CommittedDefinition.Panel.GetCell(new GridCoordinate(0, 0))!.Kind);
        Assert.Equal(1, session.CurrentTick);
    }

    [Fact]
    public void PausedEditsStayStagedUntilCommitOrStep()
    {
        var session = new PanelWorkbenchSession(EmptyPanel(2, 1));
        Assert.True(session.TryPaint(new GridCoordinate(1, 0), CellKind.Wire, CardinalDirection.East,
            out var diagnostic), diagnostic?.Message);

        Assert.Equal(0, session.CurrentTick);
        Assert.Null(session.CommittedDefinition.Panel.GetCell(new GridCoordinate(1, 0)));
        Assert.Equal(CellKind.Wire, session.Definition.Panel.GetCell(new GridCoordinate(1, 0))!.Kind);
        Assert.Empty(session.CommandLog);
        Assert.Equal(1, session.StagedEditCount);

        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        Assert.Equal(0, session.CurrentTick);
        Assert.Equal(CellKind.Wire, session.CommittedDefinition.Panel.GetCell(new GridCoordinate(1, 0))!.Kind);
        Assert.Equal((1L, 0L, WorkbenchCommandStatus.Committed),
            (Assert.Single(session.CommandLog).AcceptedOrdinal,
                session.CommandLog[0].ApplyAtTick,
            session.CommandLog[0].Status));
    }

    [Fact]
    public void AutomaticRunLeavesPausedEditsStagedUntilManualStepOrCommit()
    {
        var session = new PanelWorkbenchSession(EmptyPanel(2, 1));
        Assert.True(session.TryPaint(new GridCoordinate(0, 0), CellKind.Wire, CardinalDirection.East,
            out var diagnostic), diagnostic?.Message);
        var committed = session.CommittedDefinition;
        session.SetPaused(false);

        Assert.Equal(0, session.StepMicrotick(commitStaged: false).Tick);
        Assert.Equal(1, session.CurrentTick);
        Assert.Equal(1, session.StagedEditCount);
        Assert.Same(committed, session.CommittedDefinition);
        Assert.Null(session.CommittedDefinition.Panel.GetCell(new GridCoordinate(0, 0)));

        session.SetPaused(true);
        Assert.Equal(1, session.StepMicrotick().Tick);
        Assert.Equal(0, session.StagedEditCount);
        Assert.Equal(CellKind.Wire, session.CommittedDefinition.Panel.GetCell(new GridCoordinate(0, 0))!.Kind);
        Assert.Equal(WorkbenchCommandStatus.Committed, Assert.Single(session.CommandLog).Status);
    }

    [Fact]
    public void PausedInputEditsDoNotMutateCommittedRuntimeStateUntilCommit()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/workbench-paused-input"),
            1,
            1,
            [PortCell("input", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal")]);
        var session = new PanelWorkbenchSession(panel);

        Assert.True(session.TryDriveInput(new PortId("signal"), LogicValue.High, out var diagnostic), diagnostic?.Message);

        Assert.Equal(0, session.CurrentTick);
        Assert.Equal(LogicValue.High, session.GetInputValue(new PortId("signal")));
        Assert.Equal(LogicValue.HighImpedance, session.GetCommittedInputValue(new PortId("signal")));
        Assert.Empty(session.CommandLog);

        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);

        Assert.Equal(0, session.CurrentTick);
        Assert.Equal(LogicValue.High, session.GetCommittedInputValue(new PortId("signal")));
    }

    [Fact]
    public void RestoreRejectsPendingDeviceInputForAnUnknownDevice()
    {
        var panel = PanelDefinition.Create(new CircuitId("panel/workbench-pending-device"), 2, 1, []);
        var device = DeviceDefinition.Create(
            new ComponentId("device/pending"),
            TimedBackend(
                DevicePortDefinition.Create("in", DevicePortDirection.Input),
                DevicePortDefinition.Create("out", DevicePortDirection.Output)));
        var graph = DeviceGraphDefinition.Create(new DefinitionId("graph/pending"), [device], []);
        var definition = WorkbenchDefinition.Restore(
            panel,
            ChipDefinitionCatalog.Create([]),
            PanelOwnedChipNetworkDefinition.Create(panel.Id, []),
            [],
            graph,
            [new WorkbenchDevicePlacement(device.Id, new GridCoordinate(0, 0))]);
        var session = new PanelWorkbenchSession(definition);
        session.SetPaused(false);
        Assert.True(session.TryDriveDeviceInput(
            device.Id,
            "in",
            DeviceSignal.Scalar(LogicValue.High),
            out var diagnostic), diagnostic?.Message);

        var snapshot = session.CaptureSnapshot();
        var pending = snapshot.RunningMutations[0] with
        {
            DeviceInput = new WorkbenchDeviceInputDrive(
                new ComponentId("device/missing"),
                "in",
                DeviceSignal.Scalar(LogicValue.High))
        };
        var malformed = snapshot with { RunningMutations = [pending] };

        Assert.Throws<ArgumentException>(() => PanelWorkbenchSession.RestoreSnapshot(malformed));
    }

    [Fact]
    public void RemovingOneOfSeveralSharedInputCellsKeepsTheCommittedPortValue()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/workbench-shared-input"),
            2,
            1,
            [
                PortCell("input-left", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
                PortCell("input-right", 1, 0, CellKind.InputPort, CardinalDirection.West, "signal")
            ]);
        var session = new PanelWorkbenchSession(panel);
        Assert.True(session.TryDriveInput(new PortId("signal"), LogicValue.High, out var diagnostic), diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        Assert.True(session.TryErase(new GridCoordinate(0, 0), out diagnostic), diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);

        Assert.Equal(LogicValue.High, session.GetCommittedInputValue(new PortId("signal")));
        Assert.NotNull(session.CommittedDefinition.Panel.GetCell(new GridCoordinate(1, 0)));
    }

    [Fact]
    public void InvalidEditKeepsTheCurrentPortableDefinitionAndReportsStableCode()
    {
        var session = new PanelWorkbenchSession(EmptyPanel(2, 1));
        var outputPort = PortCell("out-a", 0, 0, CellKind.OutputPort, CardinalDirection.East, "result");
        Assert.True(session.TryPaint(outputPort, out var diagnostic), diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        var before = session.CommittedDefinition;

        Assert.False(session.TryPaint(
            PortCell("out-b", 1, 0, CellKind.OutputPort, CardinalDirection.East, "result"),
            out diagnostic));

        Assert.Equal("workbench.paint.invalid", diagnostic!.Code);
        Assert.Same(before, session.Definition);
        Assert.Same(before, session.CommittedDefinition);
    }

    [Fact]
    public void SelectionRotateAndEraseUseValidatedPortableEdits()
    {
        var session = new PanelWorkbenchSession(EmptyPanel(2, 1));
        var location = new GridCoordinate(0, 0);
        Assert.True(session.TryPaint(location, CellKind.Nand, CardinalDirection.East, out var diagnostic),
            diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        var beforeSelection = session.CommittedDefinition;
        var commandCount = session.CommandLog.Length;

        Assert.True(session.TrySelect(location, out diagnostic), diagnostic?.Message);
        Assert.Equal(location, session.Selection);
        Assert.Same(beforeSelection, session.CommittedDefinition);
        Assert.Equal(commandCount, session.CommandLog.Length);

        Assert.True(session.TryRotate(location, out diagnostic), diagnostic?.Message);
        Assert.Equal(CardinalDirection.South, session.Definition.Panel.GetCell(location)!.Orientation);
        Assert.Equal(CardinalDirection.East, session.CommittedDefinition.Panel.GetCell(location)!.Orientation);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        Assert.Equal(CardinalDirection.South, session.CommittedDefinition.Panel.GetCell(location)!.Orientation);

        Assert.True(session.TryErase(location, out diagnostic), diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        Assert.Null(session.CommittedDefinition.Panel.GetCell(location));
        Assert.Equal(0, session.CurrentTick);
    }

    [Fact]
    public void ReplacingARunningCellInvalidatesItsPendingGateWork()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/workbench-invalidation"),
            3,
            3,
            [
                PortCell("a-source", 1, 0, CellKind.InputPort, CardinalDirection.South, "a"),
                PortCell("b-source", 1, 2, CellKind.InputPort, CardinalDirection.North, "b"),
                Cell("nand", 1, 1, CellKind.Nand, CardinalDirection.East, ("delay", "2")),
                Cell("probe", 2, 1, CellKind.Probe, CardinalDirection.East)
            ]);
        var session = new PanelWorkbenchSession(panel);

        Assert.True(session.TryDriveInput(new PortId("a"), LogicValue.Low, out var diagnostic), diagnostic?.Message);
        Assert.True(session.TryDriveInput(new PortId("b"), LogicValue.Low, out diagnostic), diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        session.SetPaused(false);
        Assert.Equal(0, session.StepMicrotick().Tick);
        Assert.True(session.TryPaint(Cell("nand", 1, 1, CellKind.Wire), out diagnostic), diagnostic?.Message);
        Assert.Equal(3, session.CommandLog[^1].AcceptedOrdinal);

        session.StepMicrotick();
        session.StepMicrotick();

        Assert.Equal([0L, 1L, 2L], session.Waveform(new ComponentId("probe")).Select(sample => sample.Tick));
        Assert.All(session.Waveform(new ComponentId("probe")), sample =>
            Assert.Equal(LogicValue.HighImpedance, sample.Value));
        Assert.Equal(WorkbenchCommandStatus.Committed, session.CommandLog[^1].Status);
    }

    [Fact]
    public void CycleSteppingRunsOnlyConfiguredMicroticksAndKeepsExactProbeTicks()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/workbench-waveform"),
            3,
            1,
            [
                PortCell("source", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
                Cell("wire", 1, 0, CellKind.Wire, CardinalDirection.East),
                Cell("probe", 2, 0, CellKind.Probe, CardinalDirection.East)
            ]);
        var session = new PanelWorkbenchSession(panel, cycleTicks: 3);
        Assert.True(session.TryDriveInput(new PortId("signal"), LogicValue.Low, out var diagnostic), diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);

        Assert.Equal(0, session.StepMicrotick().Tick);
        Assert.Equal(1, session.CurrentTick);
        Assert.Equal(LogicValue.HighImpedance,
            session.Waveform(new ComponentId("probe")).Single().Value);
        session.SetPaused(false);
        var cycle = session.StepConfiguredCycles();

        Assert.Equal([1L, 2L, 3L], cycle.Select(result => result.Tick));
        Assert.Equal(4, session.CurrentTick);
        Assert.Equal([0L, 1L, 2L, 3L], session.Waveform(new ComponentId("probe")).Select(sample => sample.Tick));
        Assert.Equal(LogicValue.Low, session.Waveform(new ComponentId("probe"))[2].Value);
        session.SetPaused(true);
        Assert.Equal(4, session.CurrentTick);
    }

    [Fact]
    public void ChipDeviceAndCableActionsValidateBeforeChangingPortableDefinitions()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/workbench-assets"),
            6,
            1,
            [
                PortCell("in", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
                PortCell("out", 1, 0, CellKind.OutputPort, CardinalDirection.East, "result")
            ]);
        var session = new PanelWorkbenchSession(panel);
        Assert.True(session.TryPackagePanelAsChip(
            new DefinitionId("chip/workbench"), "workbench-chip", out var chip, out var diagnostic),
            diagnostic?.Message);
        Assert.True(session.TryPlaceChip(chip!, new ComponentId("chip/instance"), new GridCoordinate(2, 0),
            out diagnostic), diagnostic?.Message);

        var source = DeviceDefinition.Create(new ComponentId("device/source"), TimedBackend(
            DevicePortDefinition.Create("out", DevicePortDirection.Output)));
        var sink = DeviceDefinition.Create(new ComponentId("device/sink"), TimedBackend(
            DevicePortDefinition.Create("in", DevicePortDirection.Input),
            DevicePortDefinition.Create("out", DevicePortDirection.Output)));
        Assert.True(session.TryPlaceDevice(source, new GridCoordinate(3, 0), out diagnostic), diagnostic?.Message);
        Assert.True(session.TryPlaceDevice(sink, new GridCoordinate(4, 0), out diagnostic), diagnostic?.Message);

        var lane = CableLaneDefinition.Create(
            new ComponentId("lane/workbench"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            latency: 2);
        var bundle = CableBundleDefinition.Create(new ComponentId("bundle/workbench"), [lane.Id]);
        Assert.True(session.TryAddCableBundle(bundle, [lane], out diagnostic), diagnostic?.Message);
        var beforeInvalid = session.Definition;
        var badLane = CableLaneDefinition.Create(
            new ComponentId("lane/invalid"),
            DevicePortEndpoint.Create(source.Id, "missing"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            latency: 1);
        Assert.False(session.TryAddCableBundle(
            CableBundleDefinition.Create(new ComponentId("bundle/invalid"), [badLane.Id]),
            [badLane],
            out diagnostic));

        Assert.Equal("workbench.cable.invalid", diagnostic!.Code);
        Assert.Same(beforeInvalid, session.Definition);
        Assert.Single(session.Definition.ChipNetwork.Instances);
        Assert.Single(session.Definition.DeviceGraph!.Bundles);
        Assert.Equal(2, session.Definition.DeviceGraph.Lanes.Single().Latency);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        Assert.Equal(0, session.CurrentTick);
        Assert.Single(session.CommittedDefinition.DeviceGraph!.Bundles);
    }

    [Fact]
    public void PaintCannotOverwritePlacedChipsOrDevices()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/workbench-occupied-assets"),
            6,
            1,
            [
                PortCell("in", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
                PortCell("out", 1, 0, CellKind.OutputPort, CardinalDirection.East, "result")
            ]);
        var session = new PanelWorkbenchSession(panel);
        Assert.True(session.TryPackagePanelAsChip(
            new DefinitionId("chip/occupied-assets"), "asset", out var chip, out var diagnostic),
            diagnostic?.Message);
        Assert.True(session.TryPlaceChip(chip!, new ComponentId("chip/occupied"), new GridCoordinate(2, 0),
            out diagnostic), diagnostic?.Message);
        Assert.True(session.TryPlaceDevice(
            DeviceDefinition.Create(new ComponentId("device/occupied"), TimedBackend(
                DevicePortDefinition.Create("out", DevicePortDirection.Output))),
            new GridCoordinate(3, 0),
            out diagnostic), diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        var before = session.Definition;

        foreach (var location in new[] { new GridCoordinate(2, 0), new GridCoordinate(3, 0) })
        {
            Assert.False(session.TryPaint(location, CellKind.Wire, CardinalDirection.East, out diagnostic));
            Assert.Equal("workbench.paint.invalid", diagnostic!.Code);
            Assert.Same(before, session.Definition);
            Assert.Same(before, session.CommittedDefinition);
        }
    }

    [Fact]
    public void WorkbenchStepsConnectedDevicesAtThePanelTickAndPreservesThemWhenExtendingGraph()
    {
        var session = new PanelWorkbenchSession(EmptyPanel(3, 1));
        Assert.Equal(0, session.StepMicrotick().Tick);
        session.SetPaused(false);

        var source = DeviceDefinition.Create(
            new ComponentId("device/timed-source"),
            TimedDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("out", DevicePortDirection.Output)],
                [new TimedOutputChange(1, "out", DeviceSignal.Scalar(LogicValue.High))]));
        Assert.True(session.TryPlaceDevice(source, new GridCoordinate(0, 0), out var diagnostic),
            diagnostic?.Message);
        Assert.Equal(1, session.StepMicrotick().Tick);
        Assert.Equal(LogicValue.HighImpedance, session.GetDeviceOutput(source.Id, "out").Bits[0]);

        var sink = DeviceDefinition.Create(new ComponentId("device/timed-sink"), TimedBackend(
            DevicePortDefinition.Create("in", DevicePortDirection.Input),
            DevicePortDefinition.Create("out", DevicePortDirection.Output)));
        Assert.True(session.TryPlaceDevice(sink, new GridCoordinate(1, 0), out diagnostic), diagnostic?.Message);
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane/workbench-timed"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            latency: 2);
        Assert.True(session.TryAddCableBundle(
            CableBundleDefinition.Create(new ComponentId("bundle/workbench-timed"), [lane.Id]),
            [lane],
            out diagnostic), diagnostic?.Message);

        Assert.Equal(2, session.StepMicrotick().Tick);
        Assert.Equal(LogicValue.High, session.GetDeviceOutput(source.Id, "out").Bits[0]);
        for (var tick = 3; tick <= 5; tick++)
        {
            Assert.Equal(tick, session.StepMicrotick().Tick);
        }

        Assert.Equal(6, session.CurrentTick);
        Assert.Equal(
            [
                (4L, LogicValue.HighImpedance, 4L),
                (4L, LogicValue.High, 4L)
            ],
            session.GetCableLaneHistory(lane.Id)
                .Select(item => (item.ScheduledTick, item.Signal.Bits[0], item.DeliveredTick)));
    }

    [Fact]
    public void SessionSnapshotRestoresChipRuntimeInFlightCableAndPendingEdits()
    {
        var owner = EmptyPanel(8, 2);
        var chipPanel = PanelDefinition.Create(
            new CircuitId("panel/workbench-snapshot-chip"),
            3,
            1,
            [
                PortCell("chip-in", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
                Cell("chip-wire", 1, 0, CellKind.Wire),
                PortCell("chip-out", 2, 0, CellKind.OutputPort, CardinalDirection.East, "out")
            ]);
        var chip = ChipDefinition.Create(
            new DefinitionId("chip/workbench-snapshot"),
            chipPanel,
            [
                new ChipPortDefinition("signal", new PortId("signal"), ChipPortDirection.Input),
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)
            ],
            1,
            1,
            "snapshot-chip");
        var session = new PanelWorkbenchSession(owner);
        var chipInstance = new ComponentId("chip/workbench-snapshot-instance");
        Assert.True(session.TryPlaceChip(chip, chipInstance, new GridCoordinate(0, 1), out var diagnostic),
            diagnostic?.Message);

        var source = DeviceDefinition.Create(
            new ComponentId("device/snapshot-source"),
            TimedDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("out", DevicePortDirection.Output)],
                [new TimedOutputChange(5, "out", DeviceSignal.Scalar(LogicValue.High))]));
        var sink = DeviceDefinition.Create(
            new ComponentId("device/snapshot-sink"),
            TimedBackend(
                DevicePortDefinition.Create("in", DevicePortDirection.Input),
                DevicePortDefinition.Create("out", DevicePortDirection.Output)));
        Assert.True(session.TryPlaceDevice(source, new GridCoordinate(5, 1), out diagnostic), diagnostic?.Message);
        Assert.True(session.TryPlaceDevice(sink, new GridCoordinate(6, 1), out diagnostic), diagnostic?.Message);
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane/snapshot"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            latency: 2);
        Assert.True(session.TryAddCableBundle(
            CableBundleDefinition.Create(new ComponentId("bundle/snapshot"), [lane.Id]),
            [lane],
            out diagnostic), diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);

        Assert.True(session.TryDriveChipInput(chipInstance, "signal", LogicValue.High, out diagnostic),
            diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        session.SetPaused(false);
        for (var tick = 0; tick <= 5; tick++)
        {
            Assert.Equal(tick, session.StepMicrotick().Tick);
        }

        Assert.Contains(session.CaptureSnapshot().DeviceGraph!.Scheduler.PendingEvents,
            item => item.EventKind == "device-cable-transition" && item.Key.DueTick == 7);
        Assert.Equal(LogicValue.High, session.GetChipOutput(chipInstance, "out"));

        Assert.True(session.TryDriveChipInput(chipInstance, "signal", LogicValue.Low, out diagnostic),
            diagnostic?.Message);
        session.SetPaused(true);
        Assert.True(session.TryPaint(new GridCoordinate(7, 0), CellKind.Wire, CardinalDirection.East,
            out diagnostic), diagnostic?.Message);

        var snapshot = session.CaptureSnapshot();
        var restored = PanelWorkbenchSession.RestoreSnapshot(snapshot);
        Assert.Equal(session.SaveId, restored.SaveId);
        Assert.Equal(session.CurrentTick, restored.CurrentTick);
        Assert.Equal(session.CommandLog.ToArray(), restored.CommandLog.ToArray());
        Assert.Equal(session.PendingCommandCount, restored.PendingCommandCount);
        Assert.Equal(session.StagedEditCount, restored.StagedEditCount);
        Assert.Equal(session.GetChipOutput(chipInstance, "out"), restored.GetChipOutput(chipInstance, "out"));
        Assert.Equal(session.GetCableLaneHistory(lane.Id).ToArray(), restored.GetCableLaneHistory(lane.Id).ToArray());

        for (var tick = 6; tick <= 7; tick++)
        {
            var original = session.StepMicrotick();
            var copy = restored.StepMicrotick();
            Assert.Equal(tick, original.Tick);
            Assert.Equal(original.Hash, copy.Hash);
            Assert.Equal(original.Outputs.OrderBy(pair => pair.Key).ToArray(),
                copy.Outputs.OrderBy(pair => pair.Key).ToArray());
            Assert.Equal(session.CommandLog.ToArray(), restored.CommandLog.ToArray());
            Assert.Equal(session.GetCableLaneHistory(lane.Id).ToArray(), restored.GetCableLaneHistory(lane.Id).ToArray());
        }

        Assert.Contains(session.GetCableLaneHistory(lane.Id), item =>
            item.Signal.Bits[0] == LogicValue.High &&
            item.DeliveredTick == 7 &&
            item.Status == CableTransitionStatus.Delivered);
        Assert.Equal(session.Definition.Panel.GetCell(new GridCoordinate(7, 0))?.Kind,
            restored.Definition.Panel.GetCell(new GridCoordinate(7, 0))?.Kind);
    }

    private static PanelDefinition EmptyPanel(int width, int height) =>
        PanelDefinition.Create(new CircuitId("panel/workbench-empty"), width, height, []);

    private static PanelCellDefinition Cell(
        string id,
        int x,
        int y,
        CellKind kind,
        CardinalDirection orientation = CardinalDirection.East,
        params (string Name, string Value)[] parameters) => PanelCellDefinition.Create(
        new ComponentId(id),
        new GridCoordinate(x, y),
        kind,
        orientation,
        parameters: parameters.Select(parameter =>
            new KeyValuePair<string, string>(parameter.Name, parameter.Value)));

    private static PanelCellDefinition PortCell(
        string id,
        int x,
        int y,
        CellKind kind,
        CardinalDirection orientation,
        string portId) => PanelCellDefinition.Create(
        new ComponentId(id),
        new GridCoordinate(x, y),
        kind,
        orientation,
        new PortId(portId));

    private static TimedDeviceBackendDefinition TimedBackend(params DevicePortDefinition[] ports) =>
        TimedDeviceBackendDefinition.Create(ports, []);
}
