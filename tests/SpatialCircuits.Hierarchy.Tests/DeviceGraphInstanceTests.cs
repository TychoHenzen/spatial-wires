using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using Xunit;

namespace SpatialCircuits.Hierarchy.Tests;

public sealed class DeviceGraphInstanceTests
{
    [Fact]
    public void DefinitionsRejectInvalidLaneContractsAndBinaryUnknown()
    {
        var source = DeviceDefinition.Create(new ComponentId("source"), TimedBackend(
            DevicePortDefinition.Create("out", DevicePortDirection.Output, width: 2)));
        var sink = DeviceDefinition.Create(new ComponentId("sink"), PanelBackend("panel/sink", "in", "out"));

        Assert.Throws<ArgumentException>(() => DeviceGraphDefinition.Create(
            new DefinitionId("graph/width-mismatch"),
            [source, sink],
            [CableLaneDefinition.Create(
                new ComponentId("lane"),
                DevicePortEndpoint.Create(source.Id, "out"),
                DevicePortEndpoint.Create(sink.Id, "in"),
                1)]));
        var wrongDirection = DeviceDefinition.Create(
            new ComponentId("input-only"), PanelDeviceBackendDefinition.Create(
                PanelDefinition.Create(new CircuitId("panel/input-only"), 1, 1,
                    [PortCell("input", 0, CellKind.InputPort, "in")])));
        Assert.Throws<ArgumentException>(() => DeviceGraphDefinition.Create(
            new DefinitionId("graph/direction-mismatch"),
            [wrongDirection, sink],
            [CableLaneDefinition.Create(
                new ComponentId("wrong-lane"),
                DevicePortEndpoint.Create(wrongDirection.Id, "in"),
                DevicePortEndpoint.Create(sink.Id, "in"),
                1)]));
        var binary = DevicePortDefinition.Create(
            "binary", DevicePortDirection.Output, valueContract: DeviceValueContract.BinaryLogic);
        var binarySource = DeviceDefinition.Create(new ComponentId("binary-source"), TimedBackend(binary));
        Assert.Throws<ArgumentException>(() => DeviceGraphDefinition.Create(
            new DefinitionId("graph/value-contract-mismatch"),
            [binarySource, sink],
            [CableLaneDefinition.Create(
                new ComponentId("value-contract-lane"),
                DevicePortEndpoint.Create(binarySource.Id, "binary"),
                DevicePortEndpoint.Create(sink.Id, "in"),
                1)]));
        Assert.Throws<ArgumentException>(() => TimedBackend(
            binary,
            new TimedOutputChange(1, "binary", DeviceSignal.Scalar(LogicValue.Unknown))));
        _ = TimedBackend(binary,
            new TimedOutputChange(1, "binary", DeviceSignal.Scalar(LogicValue.HighImpedance)));
    }

    [Fact]
    public void BundledLanesKeepIndependentLatencyAndHistory()
    {
        var sourceBackend = TimedBackend(
            DevicePortDefinition.Create("left", DevicePortDirection.Output),
            DevicePortDefinition.Create("right", DevicePortDirection.Output),
            new TimedOutputChange(2, "left", DeviceSignal.Scalar(LogicValue.High)),
            new TimedOutputChange(2, "right", DeviceSignal.Scalar(LogicValue.High)));
        var source = DeviceDefinition.Create(new ComponentId("source"), sourceBackend);
        var left = DeviceDefinition.Create(new ComponentId("left-sink"), PanelBackend("panel/left", "in", "out"));
        var right = DeviceDefinition.Create(new ComponentId("right-sink"), PanelBackend("panel/right", "in", "out"));
        var leftLane = CableLaneDefinition.Create(
            new ComponentId("lane/left"),
            DevicePortEndpoint.Create(source.Id, "left"),
            DevicePortEndpoint.Create(left.Id, "in"),
            1);
        var rightLane = CableLaneDefinition.Create(
            new ComponentId("lane/right"),
            DevicePortEndpoint.Create(source.Id, "right"),
            DevicePortEndpoint.Create(right.Id, "in"),
            2);
        var graph = DeviceGraphDefinition.Create(
            new DefinitionId("graph/bundle"),
            [source, left, right],
            [leftLane, rightLane],
            [CableBundleDefinition.Create(new ComponentId("bundle"), [leftLane.Id, rightLane.Id])]);
        var runtime = new DeviceGraphInstance(graph);
        var deliveries = new List<DeviceCableDelivery>();

        for (var tick = 0; tick < 5; tick++)
        {
            deliveries.AddRange(runtime.Step().CableDeliveries);
        }

        runtime.Step();
        runtime.Step();

        Assert.Equal(
            [(1L, LogicValue.HighImpedance), (3L, LogicValue.High)],
            deliveries.Where(item => item.LaneId == leftLane.Id)
                .Select(item => (item.DeliveredTick, item.Signal.Bits[0])));
        Assert.Equal(
            [(2L, LogicValue.HighImpedance), (4L, LogicValue.High)],
            deliveries.Where(item => item.LaneId == rightLane.Id)
                .Select(item => (item.DeliveredTick, item.Signal.Bits[0])));
        Assert.Equal([1L, 3L], runtime.GetLaneHistory(leftLane.Id).Select(item => item.ScheduledTick));
        Assert.Equal([2L, 4L], runtime.GetLaneHistory(rightLane.Id).Select(item => item.ScheduledTick));
        Assert.Equal(LogicValue.High, runtime.GetOutput(left.Id, "out").Bits[0]);
        Assert.Equal(LogicValue.High, runtime.GetOutput(right.Id, "out").Bits[0]);
    }

    [Fact]
    public void TimedDeviceReceivesVectorSignalsAndSnapshotsItsInputState()
    {
        var sourcePort = DevicePortDefinition.Create("out", DevicePortDirection.Output, width: 2);
        var source = DeviceDefinition.Create(new ComponentId("source"),
            TimedDeviceBackendDefinition.Create(
                [sourcePort],
                [new TimedOutputChange(2, "out", DeviceSignal.Create([LogicValue.Low, LogicValue.High]))]));
        var sinkInput = DevicePortDefinition.Create("in", DevicePortDirection.Input, width: 2);
        var sinkOutput = DevicePortDefinition.Create("out", DevicePortDirection.Output, width: 2);
        var sink = DeviceDefinition.Create(new ComponentId("sink"),
            TimedDeviceBackendDefinition.Create([sinkInput, sinkOutput], []));
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            2);
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/vector"), [source, sink], [lane]));

        for (var tick = 0; tick < 5; tick++)
        {
            runtime.Step();
        }

        var input = Assert.Single(runtime.CaptureSnapshot().Devices.Single(item => item.DeviceId == sink.Id).Inputs);
        Assert.Equal("in", input.Key);
        Assert.Equal(DeviceSignal.Create([LogicValue.Low, LogicValue.High]), input.Value);
        Assert.Contains(runtime.GetLaneHistory(lane.Id), item =>
            item.Status == CableTransitionStatus.Delivered && item.ScheduledTick == 4 &&
            item.Signal.Equals(input.Value));
    }

    [Fact]
    public void DisconnectDrainsQueuedTransitionsThenReleasesAndBlocksLaterChanges()
    {
        var source = DeviceDefinition.Create(new ComponentId("source"), TimedBackend(
            DevicePortDefinition.Create("out", DevicePortDirection.Output),
            new TimedOutputChange(1, "out", DeviceSignal.Scalar(LogicValue.High)),
            new TimedOutputChange(3, "out", DeviceSignal.Scalar(LogicValue.Low))));
        var sink = DeviceDefinition.Create(new ComponentId("sink"), PanelBackend("panel/sink", "in", "out"));
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            2);
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/disconnect"), [source, sink], [lane]));
        runtime.Step();
        runtime.Step();

        runtime.Disconnect(lane.Id);
        for (var tick = 0; tick < 3; tick++)
        {
            runtime.Step();
        }

        runtime.Step();

        var history = runtime.GetLaneHistory(lane.Id);
        Assert.False(runtime.IsConnected(lane.Id));
        Assert.Equal([2L, 3L, 4L], history.Select(item => item.ScheduledTick));
        Assert.All(history, item => Assert.Equal(CableTransitionStatus.Delivered, item.Status));
        Assert.Equal(LogicValue.HighImpedance, history[^1].Signal.Bits[0]);
        Assert.Equal(LogicValue.HighImpedance, runtime.GetOutput(sink.Id, "out").Bits[0]);
    }

    [Fact]
    public void DisconnectReleaseFollowsAnAlreadyQueuedTransitionAtTheSameTick()
    {
        var source = DeviceDefinition.Create(new ComponentId("source"), TimedBackend(
            DevicePortDefinition.Create("out", DevicePortDirection.Output)));
        var sink = DeviceDefinition.Create(new ComponentId("sink"), PanelBackend("panel/sink", "in", "out"));
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            1);
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/disconnect-order"), [source, sink], [lane]));
        runtime.Disconnect(lane.Id);
        runtime.Step();
        var result = runtime.Step();

        Assert.Equal([false, true], result.CableDeliveries.Select(item => item.IsRelease));
    }

    [Fact]
    public void ReconnectInvalidatesOldEpochAndSchedulesCurrentValueAtNormalLatency()
    {
        var source = DeviceDefinition.Create(new ComponentId("source"), TimedBackend(
            DevicePortDefinition.Create("out", DevicePortDirection.Output),
            new TimedOutputChange(1, "out", DeviceSignal.Scalar(LogicValue.High))));
        var sink = DeviceDefinition.Create(new ComponentId("sink"), PanelBackend("panel/sink", "in", "out"));
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            3);
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/reconnect"), [source, sink], [lane]));
        runtime.Step();
        runtime.Step();

        runtime.Disconnect(lane.Id);
        runtime.Reconnect(lane.Id);
        var invalidated = runtime.GetLaneHistory(lane.Id);
        Assert.Equal(3, invalidated.Count(item => item.Status == CableTransitionStatus.Invalidated));
        Assert.Equal(5, Assert.Single(invalidated.Where(item => item.Status == CableTransitionStatus.Pending)).ScheduledTick);
        var snapshot = runtime.CaptureSnapshot();
        runtime.RestoreSnapshot(snapshot);

        var deliveries = new List<DeviceCableDelivery>();
        for (var tick = 0; tick < 4; tick++)
        {
            deliveries.AddRange(runtime.Step().CableDeliveries);
        }

        runtime.Step();

        var reconnectDrive = Assert.Single(deliveries);
        Assert.Equal(5, reconnectDrive.DeliveredTick);
        Assert.Equal(LogicValue.High, reconnectDrive.Signal.Bits[0]);
        Assert.Equal(LogicValue.High, runtime.GetOutput(sink.Id, "out").Bits[0]);
    }

    [Fact]
    public void SnapshotReplaysInFlightTransitionsOnceAtTheirOriginalTicks()
    {
        var source = DeviceDefinition.Create(new ComponentId("source"), TimedBackend(
            DevicePortDefinition.Create("out", DevicePortDirection.Output),
            new TimedOutputChange(1, "out", DeviceSignal.Scalar(LogicValue.High))));
        var sink = DeviceDefinition.Create(new ComponentId("sink"), PanelBackend("panel/sink", "in", "out"));
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            3);
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/snapshot"), [source, sink], [lane]));
        var snapshot = runtime.CaptureSnapshot();
        Assert.Single(snapshot.Devices.Single(item => item.DeviceId == source.Id).Timers);

        var firstReplay = RunUntilTick(runtime, 5);
        runtime.RestoreSnapshot(snapshot);
        var secondReplay = RunUntilTick(runtime, 5);

        Assert.Equal(firstReplay.Select(item => item.Tick), secondReplay.Select(item => item.Tick));
        Assert.Equal(firstReplay.SelectMany(item => item.CableDeliveries),
            secondReplay.SelectMany(item => item.CableDeliveries));
        Assert.Equal([3L, 4L], secondReplay.SelectMany(item => item.CableDeliveries)
            .Select(item => item.DeliveredTick));
        Assert.All(runtime.GetLaneHistory(lane.Id), item =>
            Assert.Equal(CableTransitionStatus.Delivered, item.Status));
    }

    [Fact]
    public void IncompatibleBackendReplacementLeavesCausalStateUntouchedAndCompatibleReplacementKeepsLanes()
    {
        var source = DeviceDefinition.Create(new ComponentId("source"), TimedBackend(
            DevicePortDefinition.Create("out", DevicePortDirection.Output),
            new TimedOutputChange(1, "out", DeviceSignal.Scalar(LogicValue.High))));
        var sink = DeviceDefinition.Create(new ComponentId("sink"), PanelBackend("panel/sink", "in", "out"));
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane"),
            DevicePortEndpoint.Create(source.Id, "out"),
            DevicePortEndpoint.Create(sink.Id, "in"),
            2);
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/replace"), [source, sink], [lane]));
        var before = runtime.CaptureSnapshot();
        var incompatible = TimedBackend(
            DevicePortDefinition.Create("out", DevicePortDirection.Output, width: 2));

        Assert.Throws<ArgumentException>(() => runtime.ReplaceBackend(source.Id, incompatible));
        var afterFailure = runtime.CaptureSnapshot();
        Assert.Equal(before.Scheduler.NextCausalOrdinal, afterFailure.Scheduler.NextCausalOrdinal);
        Assert.Equal(before.Scheduler.PendingEvents.ToArray(), afterFailure.Scheduler.PendingEvents.ToArray());
        Assert.Equal(before.Definition.Lanes.ToArray(), afterFailure.Definition.Lanes.ToArray());

        runtime.ReplaceBackend(source.Id, PanelDeviceBackendDefinition.Create(OutputOnlyPanel("panel/replacement", "out")));
        Assert.Equal(before.Definition.Lanes.ToArray(), runtime.Definition.Lanes.ToArray());
        Assert.Equal(0, runtime.CurrentTick);
        runtime.Step();
        runtime.Step();
        Assert.Equal(LogicValue.HighImpedance, runtime.GetOutput(source.Id, "out").Bits[0]);
    }

    [Fact]
    public void TimedOutputsFireAtTheirExactTickAfterAQuietPeriod()
    {
        var device = DeviceDefinition.Create(new ComponentId("timer"), TimedBackend(
            DevicePortDefinition.Create("out", DevicePortDirection.Output),
            new TimedOutputChange(5, "out", DeviceSignal.Scalar(LogicValue.High))));
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/timer"), [device], []));

        for (var tick = 0; tick < 5; tick++)
        {
            Assert.Equal(tick, runtime.Step().Tick);
            Assert.Equal(LogicValue.HighImpedance, runtime.GetOutput(device.Id, "out").Bits[0]);
        }

        Assert.Equal(5, runtime.Step().Tick);
        Assert.Equal(LogicValue.High, runtime.GetOutput(device.Id, "out").Bits[0]);
    }

    [Fact]
    public void ControllerAndResponderExchangeShowsTicksAcrossTwoLanes()
    {
        var runtime = CreateExchangeRuntime();
        runtime.SetInput(new ComponentId("controller"), "challenge-drive",
            DeviceSignal.Scalar(LogicValue.High));
        runtime.SetInput(new ComponentId("responder"), "enable",
            DeviceSignal.Scalar(LogicValue.High));
        var challengeTrace = new List<LogicValue>();
        var responseTrace = new List<LogicValue>();
        var deliveries = new List<DeviceCableDelivery>();
        for (var tick = 0; tick < 14; tick++)
        {
            var result = runtime.Step();
            challengeTrace.Add(runtime.GetOutput(new ComponentId("controller"), "challenge").Bits[0]);
            responseTrace.Add(runtime.GetOutput(new ComponentId("controller"), "response").Bits[0]);
            deliveries.AddRange(result.CableDeliveries);
        }

        Assert.Equal([LogicValue.HighImpedance, .. Enumerable.Repeat(LogicValue.High, 13)], challengeTrace);
        Assert.Equal(
            [.. Enumerable.Repeat(LogicValue.HighImpedance, 5),
                .. Enumerable.Repeat(LogicValue.Unknown, 4),
                .. Enumerable.Repeat(LogicValue.Low, 5)],
            responseTrace);
        Assert.Equal(
            [
                new DeviceCableDelivery(new ComponentId("lane/challenge"), 1, 2, 2,
                    DeviceSignal.Scalar(LogicValue.HighImpedance), false),
                new DeviceCableDelivery(new ComponentId("lane/response"), 1, 2, 2,
                    DeviceSignal.Scalar(LogicValue.HighImpedance), false),
                new DeviceCableDelivery(new ComponentId("lane/challenge"), 1, 3, 3,
                    DeviceSignal.Scalar(LogicValue.High), false),
                new DeviceCableDelivery(new ComponentId("lane/response"), 1, 4, 4,
                    DeviceSignal.Scalar(LogicValue.Unknown), false),
                new DeviceCableDelivery(new ComponentId("lane/response"), 1, 8, 8,
                    DeviceSignal.Scalar(LogicValue.Low), false)
            ],
            deliveries);
    }

    private static IReadOnlyList<DeviceGraphTickResult> RunUntilTick(DeviceGraphInstance runtime, int tickExclusive)
    {
        var results = new List<DeviceGraphTickResult>();
        while (runtime.CurrentTick < tickExclusive)
        {
            results.Add(runtime.Step());
        }

        return results;
    }

    private static TimedDeviceBackendDefinition TimedBackend(
        DevicePortDefinition port,
        params TimedOutputChange[] changes) => TimedBackend([port], changes);

    private static TimedDeviceBackendDefinition TimedBackend(
        DevicePortDefinition first,
        DevicePortDefinition second,
        params TimedOutputChange[] changes) => TimedBackend([first, second], changes);

    private static TimedDeviceBackendDefinition TimedBackend(
        IEnumerable<DevicePortDefinition> ports,
        IEnumerable<TimedOutputChange> changes) => TimedDeviceBackendDefinition.Create(ports, changes);

    private static PanelDeviceBackendDefinition PanelBackend(string panelId, string input, string output)
    {
        var panel = PanelDefinition.Create(
            new CircuitId(panelId),
            2,
            1,
            [
                PortCell("source", 0, CellKind.InputPort, input),
                PortCell("sink", 1, CellKind.OutputPort, output)
            ]);
        return PanelDeviceBackendDefinition.Create(panel);
    }

    private static PanelDefinition OutputOnlyPanel(string panelId, string output) => PanelDefinition.Create(
        new CircuitId(panelId),
        1,
        1,
        [PortCell("sink", 0, CellKind.OutputPort, output)]);

    private static DeviceGraphInstance CreateExchangeRuntime()
    {
        var controllerPanel = PanelDefinition.Create(
            new CircuitId("panel/controller"),
            2,
            3,
            [
                PortCell("challenge-source", 0, 0, CellKind.InputPort, CardinalDirection.East, "challenge-drive"),
                PortCell("challenge-output", 1, 0, CellKind.OutputPort, CardinalDirection.East, "challenge"),
                PortCell("response-source", 0, 2, CellKind.InputPort, CardinalDirection.East, "response-in"),
                PortCell("response-output", 1, 2, CellKind.OutputPort, CardinalDirection.East, "response")
            ]);
        var responderPanel = PanelDefinition.Create(
            new CircuitId("panel/responder"),
            3,
            3,
            [
                PortCell("challenge-source", 1, 0, CellKind.InputPort, CardinalDirection.South, "challenge"),
                PortCell("enable-source", 1, 2, CellKind.InputPort, CardinalDirection.North, "enable"),
                PanelCellDefinition.Create(new ComponentId("nand"), new GridCoordinate(1, 1),
                    CellKind.Nand, CardinalDirection.East,
                    parameters: [new KeyValuePair<string, string>("delay", "1")]),
                PortCell("response-output", 2, 1, CellKind.OutputPort, CardinalDirection.East, "response")
            ]);
        var controller = DeviceDefinition.Create(new ComponentId("controller"),
            PanelDeviceBackendDefinition.Create(controllerPanel));
        var responder = DeviceDefinition.Create(new ComponentId("responder"),
            PanelDeviceBackendDefinition.Create(responderPanel));
        var lanes = new[]
        {
            CableLaneDefinition.Create(new ComponentId("lane/challenge"),
                DevicePortEndpoint.Create(controller.Id, "challenge"),
                DevicePortEndpoint.Create(responder.Id, "challenge"), 2),
            CableLaneDefinition.Create(new ComponentId("lane/response"),
                DevicePortEndpoint.Create(responder.Id, "response"),
                DevicePortEndpoint.Create(controller.Id, "response-in"), 2)
        };
        return new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/exchange"), [controller, responder], lanes));
    }

    private static PanelCellDefinition PortCell(
        string id,
        int x,
        int y,
        CellKind kind,
        CardinalDirection direction,
        string port) => PanelCellDefinition.Create(
        new ComponentId(id),
        new GridCoordinate(x, y),
        kind,
        direction,
        new PortId(port));

    private static PanelCellDefinition PortCell(string id, int x, CellKind kind, string port) =>
        PanelCellDefinition.Create(
            new ComponentId(id),
            new GridCoordinate(x, 0),
            kind,
            CardinalDirection.East,
            new PortId(port));
}
