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
    public void DefinitionsRejectDeviceAndLaneIdsThatCollideWithNodeBackendTargets()
    {
        const string targetId = "node-backend/responder";

        var responderId = new ComponentId("responder");
        var responder = DeviceDefinition.Create(responderId,
            NodeBackend(DevicePortDefinition.Create("output", DevicePortDirection.Output)));
        var collidingDevice = DeviceDefinition.Create(new ComponentId(targetId),
            TimedBackend(DevicePortDefinition.Create("output", DevicePortDirection.Output)));

        Assert.Throws<ArgumentException>(() => DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-device-target-collision"), [responder, collidingDevice], []));

        var sinkId = new ComponentId("sink");
        var sink = DeviceDefinition.Create(sinkId, TimedBackend(
            DevicePortDefinition.Create("input", DevicePortDirection.Input),
            DevicePortDefinition.Create("output", DevicePortDirection.Output)));
        var lane = CableLaneDefinition.Create(
            new ComponentId(targetId),
            DevicePortEndpoint.Create(responderId, "output"),
            DevicePortEndpoint.Create(sinkId, "input"),
            1);

        Assert.Throws<ArgumentException>(() => DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-lane-target-collision"), [responder, sink], [lane]));

        var namespacedNode = DeviceDefinition.Create(new ComponentId("responder:script"),
            NodeBackend(DevicePortDefinition.Create("output", DevicePortDirection.Output)));
        var namespacedGraph = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-namespaced-id"), [namespacedNode], []);
        Assert.Equal(0L, new DeviceGraphInstance(namespacedGraph).Step().Tick);
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
            [rightLane, leftLane],
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
        Assert.Equal([1L, 3L], runtime.GetLaneHistory(leftLane.Id).Select(item => item.CausalOrdinal));
        Assert.Equal([2L, 4L], runtime.GetLaneHistory(rightLane.Id).Select(item => item.CausalOrdinal));
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
            DevicePortDefinition.Create("second", DevicePortDirection.Output),
            new TimedOutputChange(5, "out", DeviceSignal.Scalar(LogicValue.High)),
            new TimedOutputChange(5, "second", DeviceSignal.Scalar(LogicValue.High))));
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/timer"), [device], []));

        for (var tick = 0; tick < 5; tick++)
        {
            Assert.Equal(tick, runtime.Step().Tick);
            Assert.Equal(LogicValue.HighImpedance, runtime.GetOutput(device.Id, "out").Bits[0]);
            Assert.Equal(LogicValue.HighImpedance, runtime.GetOutput(device.Id, "second").Bits[0]);
        }

        Assert.Equal(5, runtime.Step().Tick);
        Assert.Equal(LogicValue.High, runtime.GetOutput(device.Id, "out").Bits[0]);
        Assert.Equal(LogicValue.High, runtime.GetOutput(device.Id, "second").Bits[0]);
    }

    [Fact]
    public void NodeBackendReceivesCommittedInputsAndTimersAndAcceptsOnlyFutureOutputs()
    {
        var deviceId = new ComponentId("node");
        var device = DeviceDefinition.Create(deviceId, NodeBackend(
            DevicePortDefinition.Create("input", DevicePortDirection.Input),
            DevicePortDefinition.Create("output", DevicePortDirection.Output)));
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-step"), [device], []));
        var contexts = new List<DeviceNodeStepContext>();
        DeviceNodeOutputCapability? expiredCapability = null;
        var rejectedCurrentTick = false;
        var rejectedPastTick = false;
        var acceptedFirstOutput = false;
        var acceptedTimer = false;
        var acceptedSecondOutput = false;

        runtime.AttachNodeBackend(deviceId, new DeviceNodeBackendBinding(context =>
        {
            contexts.Add(context);
            if (context.Tick == 0)
            {
                expiredCapability = context.Outputs;
                rejectedCurrentTick = !context.Outputs.RequestOutput(
                    context.Tick, "output", DeviceSignal.Scalar(LogicValue.High));
                rejectedPastTick = !context.Outputs.RequestOutput(
                    context.Tick - 1, "output", DeviceSignal.Scalar(LogicValue.High));
                acceptedFirstOutput = context.Outputs.RequestOutput(
                    context.Tick + 1, "output", DeviceSignal.Scalar(LogicValue.High));
                acceptedTimer = context.Outputs.ScheduleTimer(context.Tick + 2, "wake");
            }
            else if (context.Tick == 2)
            {
                acceptedSecondOutput = context.Outputs.RequestOutput(
                    context.Tick + 1, "output", DeviceSignal.Scalar(LogicValue.Low));
            }
        }));
        runtime.SetInput(deviceId, "input", DeviceSignal.Scalar(LogicValue.High));

        Assert.Equal(0, runtime.Step().Tick);
        Assert.True(rejectedCurrentTick);
        Assert.True(rejectedPastTick);
        Assert.True(acceptedFirstOutput);
        Assert.True(acceptedTimer);
        Assert.Equal(LogicValue.HighImpedance, runtime.GetOutput(deviceId, "output").Bits[0]);
        Assert.False(expiredCapability!.RequestOutput(
            5, "output", DeviceSignal.Scalar(LogicValue.Low)));

        Assert.Equal(1, runtime.Step().Tick);
        Assert.Equal(LogicValue.High, runtime.GetOutput(deviceId, "output").Bits[0]);
        Assert.Equal(2, runtime.Step().Tick);
        Assert.True(acceptedSecondOutput);
        Assert.Contains(contexts[0].CommittedInputs, transition =>
            transition.PortName == "input" && transition.Signal.Equals(DeviceSignal.Scalar(LogicValue.High)));
        Assert.Single(contexts[0].CommittedInputs);
        Assert.Contains(contexts[2].DueTimers, timer => timer.TimerId == "wake" && timer.DueTick == 2);
        Assert.Equal(LogicValue.High, runtime.GetOutput(deviceId, "output").Bits[0]);

        Assert.Equal(3, runtime.Step().Tick);
        Assert.Equal(LogicValue.Low, runtime.GetOutput(deviceId, "output").Bits[0]);
        Assert.Equal([1L, 2L, 3L], runtime.AcceptedCommands.Select(item => item.AcceptedOrdinal));
        Assert.Equal([1L, 2L, 3L], runtime.AcceptedCommands.Select(item => item.Command.DueTick));
    }

    [Fact]
    public void NodeCallbacksRejectReentryAndDiscardAllRequestsWhenOneCallbackFails()
    {
        var firstId = new ComponentId("node-a");
        var secondId = new ComponentId("node-b");
        var first = DeviceDefinition.Create(firstId,
            NodeBackend(DevicePortDefinition.Create("output", DevicePortDirection.Output)));
        var second = DeviceDefinition.Create(secondId,
            NodeBackend(DevicePortDefinition.Create("output", DevicePortDirection.Output)));
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-failure"), [first, second], []));
        var nestedStepRejected = false;
        var firstCallbackRan = false;

        runtime.AttachNodeBackend(firstId, new DeviceNodeBackendBinding(context =>
        {
            firstCallbackRan = true;
            try
            {
                runtime.Step();
            }
            catch (InvalidOperationException)
            {
                nestedStepRejected = true;
            }

            Assert.True(context.Outputs.RequestOutput(
                context.Tick + 1, "output", DeviceSignal.Scalar(LogicValue.High)));
        }));
        runtime.AttachNodeBackend(secondId, new DeviceNodeBackendBinding(context =>
        {
            Assert.True(context.Outputs.RequestOutput(
                context.Tick + 1, "output", DeviceSignal.Scalar(LogicValue.High)));
            throw new InvalidOperationException("responder failed");
        }));

        var result = runtime.Step();

        Assert.Equal(0, result.Tick);
        Assert.Equal(1, runtime.CurrentTick);
        Assert.Single(runtime.CaptureSnapshot().Scheduler.Trace);
        Assert.True(nestedStepRejected);
        Assert.True(firstCallbackRan);
        Assert.Empty(runtime.AcceptedCommands);
        Assert.Equal(LogicValue.HighImpedance, runtime.GetOutput(firstId, "output").Bits[0]);
        Assert.Equal(LogicValue.HighImpedance, runtime.GetOutput(secondId, "output").Bits[0]);
        Assert.Collection(result.NodeBackendFailures,
            failure =>
            {
                Assert.Equal(secondId, failure.DeviceId);
                Assert.Equal("responder failed", failure.Message);
            });
    }

    [Fact]
    public void RemovedNodeBackendInvalidatesStaleCommandsAndReleasesThroughCableLatency()
    {
        var nodeId = new ComponentId("node");
        var sinkId = new ComponentId("sink");
        var node = DeviceDefinition.Create(nodeId,
            NodeBackend(DevicePortDefinition.Create("output", DevicePortDirection.Output)));
        var sink = DeviceDefinition.Create(sinkId, TimedBackend(
            [
                DevicePortDefinition.Create("input", DevicePortDirection.Input),
                DevicePortDefinition.Create("output", DevicePortDirection.Output)
            ], []));
        var laneId = new ComponentId("lane/node-sink");
        var lane = CableLaneDefinition.Create(
            laneId,
            DevicePortEndpoint.Create(nodeId, "output"),
            DevicePortEndpoint.Create(sinkId, "input"),
            latency: 2);
        var runtime = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-delete"), [node, sink], [lane]));
        DeviceNodeOutputCapability? expiredCapability = null;
        var calls = 0;
        var binding = new DeviceNodeBackendBinding(context =>
        {
            calls++;
            if (context.Tick == 0)
            {
                expiredCapability = context.Outputs;
                Assert.True(context.Outputs.RequestOutput(
                    context.Tick + 1, "output", DeviceSignal.Scalar(LogicValue.High)));
            }
            else if (context.Tick == 1)
            {
                Assert.True(context.Outputs.RequestOutput(
                    context.Tick + 3, "output", DeviceSignal.Scalar(LogicValue.Low)));
            }
        });
        runtime.AttachNodeBackend(nodeId, binding);

        runtime.Step();
        runtime.Step();
        runtime.Step();
        runtime.Step();
        Assert.Equal(LogicValue.High, runtime.CaptureSnapshot().Devices
            .Single(device => device.DeviceId == sinkId).Inputs.Single(pair => pair.Key == "input").Value.Bits[0]);
        Assert.Equal(LogicValue.High, runtime.GetOutput(nodeId, "output").Bits[0]);

        binding.Invalidate();
        runtime.Step();
        Assert.True(runtime.IsConnected(laneId));
        Assert.Equal(LogicValue.HighImpedance, runtime.GetOutput(nodeId, "output").Bits[0]);
        Assert.Equal(LogicValue.High, runtime.CaptureSnapshot().Devices
            .Single(device => device.DeviceId == sinkId).Inputs.Single(pair => pair.Key == "input").Value.Bits[0]);
        Assert.False(expiredCapability!.RequestOutput(
            10, "output", DeviceSignal.Scalar(LogicValue.Low)));

        runtime.Step();
        Assert.Equal(LogicValue.High, runtime.CaptureSnapshot().Devices
            .Single(device => device.DeviceId == sinkId).Inputs.Single(pair => pair.Key == "input").Value.Bits[0]);
        runtime.Step();
        Assert.Equal(LogicValue.HighImpedance, runtime.CaptureSnapshot().Devices
            .Single(device => device.DeviceId == sinkId).Inputs.Single(pair => pair.Key == "input").Value.Bits[0]);
        Assert.Equal(4, calls);
        Assert.Contains(runtime.GetLaneHistory(laneId), item =>
            item.Status == CableTransitionStatus.Delivered && item.Signal.Equals(
                DeviceSignal.Scalar(LogicValue.HighImpedance)) && item.DeliveredTick == 6);
        Assert.DoesNotContain(runtime.GetLaneHistory(laneId), item =>
            item.Signal.Equals(DeviceSignal.Scalar(LogicValue.Low)) &&
            item.Status == CableTransitionStatus.Delivered);
    }

    [Fact]
    public void NodeBackendCommandReplayMatchesPerTickHashesWithoutInvokingTheNode()
    {
        var deviceId = new ComponentId("node");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-replay"),
            [DeviceDefinition.Create(deviceId, NodeBackend(
                DevicePortDefinition.Create("input", DevicePortDirection.Input),
                DevicePortDefinition.Create("output", DevicePortDirection.Output)))],
            []);
        var live = new DeviceGraphInstance(definition);
        var liveCalls = 0;
        live.AttachNodeBackend(deviceId, new DeviceNodeBackendBinding(context =>
        {
            liveCalls++;
            if (context.Tick == 0)
            {
                Assert.True(context.Outputs.RequestOutput(
                    context.Tick + 1, "output", DeviceSignal.Scalar(LogicValue.High)));
                Assert.True(context.Outputs.ScheduleTimer(context.Tick + 2, "wake"));
            }
            else if (context.Tick == 2)
            {
                Assert.Contains(context.DueTimers, timer => timer.TimerId == "wake");
                Assert.True(context.Outputs.RequestOutput(
                    context.Tick + 1, "output", DeviceSignal.Scalar(LogicValue.Low)));
            }
        }));
        live.SetInput(deviceId, "input", DeviceSignal.Scalar(LogicValue.High));
        var initial = live.CaptureSnapshot();
        var liveHashes = new List<string>();
        for (var tick = 0; tick < 5; tick++)
        {
            live.Step();
            liveHashes.Add(live.CaptureSnapshot().Scheduler.Trace[^1].Hash);
        }

        var acceptedCommands = live.AcceptedCommands;
        var finalLive = live.CaptureSnapshot();
        var replay = new DeviceGraphInstance(definition);
        replay.RestoreSnapshot(initial);
        replay.ReplayNodeBackendCommands(acceptedCommands);
        var replayHashes = new List<string>();
        for (var tick = 0; tick < 5; tick++)
        {
            replay.Step();
            replayHashes.Add(replay.CaptureSnapshot().Scheduler.Trace[^1].Hash);
        }

        var finalReplay = replay.CaptureSnapshot();
        Assert.Equal(5, liveCalls);
        Assert.Equal(liveHashes, replayHashes);
        Assert.True(finalLive.Scheduler.AcceptedCommands.SequenceEqual(finalReplay.Scheduler.AcceptedCommands));
        Assert.True(finalLive.Devices.Single().Outputs.SequenceEqual(finalReplay.Devices.Single().Outputs));
        Assert.True(finalLive.Lanes.SequenceEqual(finalReplay.Lanes));
    }

    [Fact]
    public void RuntimeSnapshotRestoresOfflineNodeReplayCursor()
    {
        var deviceId = new ComponentId("node");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-replay-snapshot"),
            [DeviceDefinition.Create(deviceId, NodeBackend(
                DevicePortDefinition.Create("output", DevicePortDirection.Output)))],
            []);
        var live = new DeviceGraphInstance(definition);
        live.AttachNodeBackend(deviceId, new DeviceNodeBackendBinding(context =>
        {
            if (context.Tick == 0)
            {
                Assert.True(context.Outputs.RequestOutput(
                    context.Tick + 1, "output", DeviceSignal.Scalar(LogicValue.High)));
            }
            else if (context.Tick == 2)
            {
                Assert.True(context.Outputs.RequestOutput(
                    context.Tick + 1, "output", DeviceSignal.Scalar(LogicValue.Low)));
            }
        }));
        for (var tick = 0; tick < 5; tick++)
        {
            Assert.Equal(tick, live.Step().Tick);
        }

        var commands = live.AcceptedCommands;
        var replay = new DeviceGraphInstance(definition);
        replay.ReplayNodeBackendCommands(commands);
        Assert.Equal(0, replay.Step().Tick);
        Assert.Equal(1, replay.Step().Tick);
        var checkpoint = replay.CaptureSnapshot();
        Assert.Equal(commands.ToArray(), checkpoint.ReplayCommands.ToArray());
        Assert.InRange(checkpoint.NextReplayCommandIndex, 1, commands.Length - 1);

        var resumed = new DeviceGraphInstance(definition);
        resumed.RestoreSnapshot(checkpoint);
        for (var tick = 2; tick < 5; tick++)
        {
            var replayResult = replay.Step();
            var resumedResult = resumed.Step();
            Assert.Equal(tick, replayResult.Tick);
            Assert.Equal(replayResult.Tick, resumedResult.Tick);
            Assert.Equal(replay.CaptureSnapshot().Scheduler.Trace[^1].Hash,
                resumed.CaptureSnapshot().Scheduler.Trace[^1].Hash);
            Assert.True(replay.AcceptedCommands.SequenceEqual(resumed.AcceptedCommands));
        }
    }

    [Fact]
    public void RestoreRejectsMalformedPendingNodeOutputEvents()
    {
        var deviceId = new ComponentId("node");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-snapshot"),
            [DeviceDefinition.Create(deviceId, NodeBackend(
                DevicePortDefinition.Create("output", DevicePortDirection.Output)))],
            []);
        var snapshot = new DeviceGraphInstance(definition).CaptureSnapshot();
        var targetStableId = "node-backend/" + deviceId.Value;
        var target = snapshot.Scheduler.Targets.Single(item => item.StableId == targetStableId);
        var malformedEvent = new ScheduledEvent(
            new ScheduledEventKey(
                1,
                SchedulerPhase.Deliver,
                targetStableId,
                target.Incarnation,
                "output",
                targetStableId,
                "output",
                "device-node-output",
                1),
            LogicValue.High,
            "not-a-signal",
            SourceIncarnation: target.Incarnation);
        var malformedSnapshot = snapshot with
        {
            Scheduler = snapshot.Scheduler with
            {
                NextCausalOrdinal = 2,
                PendingEvents = [malformedEvent]
            }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            new DeviceGraphInstance(definition).RestoreSnapshot(malformedSnapshot));

        Assert.Contains("Node output event", exception.Message);
    }

    [Fact]
    public void RestoreRejectsPendingNodeEventsWithoutRecordedCommands()
    {
        var deviceId = new ComponentId("node");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-unrecorded-event"),
            [DeviceDefinition.Create(deviceId, NodeBackend(
                DevicePortDefinition.Create("output", DevicePortDirection.Output)))],
            []);
        var snapshot = new DeviceGraphInstance(definition).CaptureSnapshot();
        var targetStableId = "node-backend/" + deviceId.Value;
        var target = snapshot.Scheduler.Targets.Single(item => item.StableId == targetStableId);
        var unrecordedEvent = new ScheduledEvent(
            new ScheduledEventKey(
                1,
                SchedulerPhase.Deliver,
                targetStableId,
                target.Incarnation,
                "output",
                targetStableId,
                "output",
                "device-node-output",
                1),
            LogicValue.High,
            "1",
            SourceIncarnation: target.Incarnation);
        var malformedSnapshot = snapshot with
        {
            Scheduler = snapshot.Scheduler with
            {
                NextCausalOrdinal = 2,
                PendingEvents = [unrecordedEvent]
            }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            new DeviceGraphInstance(definition).RestoreSnapshot(malformedSnapshot));

        Assert.Contains("no applied accepted command", exception.Message);
    }

    [Fact]
    public void RestoreRejectsUnmatchedNodeDetachAndMalformedPendingNodeCommands()
    {
        var deviceId = new ComponentId("node");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-command-snapshot"),
            [DeviceDefinition.Create(deviceId, NodeBackend(
                DevicePortDefinition.Create("output", DevicePortDirection.Output)))],
            []);
        var snapshot = new DeviceGraphInstance(definition).CaptureSnapshot();
        var targetStableId = "node-backend/" + deviceId.Value;
        var target = snapshot.Scheduler.Targets.Single(item => item.StableId == targetStableId);
        var pendingRemoval = SchedulerCommand.RemoveTarget(targetStableId, snapshot.Scheduler.CurrentTick);
        var unmatchedDetach = snapshot with
        {
            Scheduler = snapshot.Scheduler with
            {
                NextAcceptedOrdinal = 2,
                AcceptedCommands = [new AcceptedSchedulerCommand(1, pendingRemoval)]
            }
        };

        var detachException = Assert.Throws<ArgumentException>(() =>
            new DeviceGraphInstance(definition).RestoreSnapshot(unmatchedDetach));

        Assert.Contains("detach", detachException.Message);

        var detachingDevice = snapshot.Devices.Single() with
        {
            NodeDetachApplyAtTick = snapshot.Scheduler.CurrentTick
        };
        var duplicateDetaches = snapshot with
        {
            Devices = [detachingDevice],
            Scheduler = snapshot.Scheduler with
            {
                NextAcceptedOrdinal = 3,
                AcceptedCommands =
                [
                    new AcceptedSchedulerCommand(1, pendingRemoval),
                    new AcceptedSchedulerCommand(2, pendingRemoval)
                ]
            }
        };

        var duplicateDetachException = Assert.Throws<ArgumentException>(() =>
            new DeviceGraphInstance(definition).RestoreSnapshot(duplicateDetaches));

        Assert.Contains("detach commands", duplicateDetachException.Message);

        var malformedOutput = new ScheduledEvent(
            new ScheduledEventKey(
                1,
                SchedulerPhase.Deliver,
                targetStableId,
                target.Incarnation,
                "output",
                targetStableId,
                "output",
                "device-node-output",
                CausalOrdinal: 0),
            LogicValue.High,
            "not-a-signal",
            SourceIncarnation: target.Incarnation);
        var malformedCommand = SchedulerCommand.ScheduleEvent(malformedOutput, snapshot.Scheduler.CurrentTick);
        var invalidCommandSnapshot = snapshot with
        {
            Scheduler = snapshot.Scheduler with
            {
                NextAcceptedOrdinal = 2,
                AcceptedCommands = [new AcceptedSchedulerCommand(1, malformedCommand)]
            }
        };

        var commandException = Assert.Throws<ArgumentException>(() =>
            new DeviceGraphInstance(definition).RestoreSnapshot(invalidCommandSnapshot));

        Assert.Contains("invalid signal", commandException.Message);
    }

    [Fact]
    public void RestoreAppliesUnappliedNodeOutputCommandAtItsOriginalBoundary()
    {
        var deviceId = new ComponentId("node");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-pending-output-command"),
            [DeviceDefinition.Create(deviceId, NodeBackend(
                DevicePortDefinition.Create("output", DevicePortDirection.Output)))],
            []);
        var live = new DeviceGraphInstance(definition);
        live.AttachNodeBackend(deviceId, new DeviceNodeBackendBinding(context =>
        {
            if (context.Tick == 0)
            {
                Assert.True(context.Outputs.RequestOutput(
                    context.Tick + 3, "output", DeviceSignal.Scalar(LogicValue.High)));
            }
        }));
        live.Step();
        var snapshot = live.CaptureSnapshot();

        var restored = new DeviceGraphInstance(definition);
        restored.RestoreSnapshot(snapshot);
        Assert.Equal(1L, restored.Step().Tick);
        var pendingEventSnapshot = restored.CaptureSnapshot();
        var resumed = new DeviceGraphInstance(definition);
        resumed.RestoreSnapshot(pendingEventSnapshot);
        Assert.Equal(2L, resumed.Step().Tick);
        Assert.Equal(3L, resumed.Step().Tick);

        Assert.Equal(LogicValue.High, resumed.GetOutput(deviceId, "output").Bits[0]);
    }

    [Fact]
    public void RestoreAppliesPendingNodeDetachAndReRegistersItsTarget()
    {
        var deviceId = new ComponentId("node");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-pending-detach"),
            [DeviceDefinition.Create(deviceId, NodeBackend(
                DevicePortDefinition.Create("output", DevicePortDirection.Output)))],
            []);
        var live = new DeviceGraphInstance(definition);
        DeviceNodeBackendBinding? binding = null;
        binding = new DeviceNodeBackendBinding(_ => binding!.Invalidate());
        live.AttachNodeBackend(deviceId, binding);
        live.Step();
        var snapshot = live.CaptureSnapshot();

        var restored = new DeviceGraphInstance(definition);
        restored.RestoreSnapshot(snapshot);
        Assert.Equal(1L, restored.Step().Tick);

        var restoredSnapshot = restored.CaptureSnapshot();
        var target = restoredSnapshot.Scheduler.Targets
            .Single(item => item.StableId == "node-backend/" + deviceId.Value);
        Assert.Equal(2L, target.Incarnation);
        Assert.True(target.Active);
        Assert.Null(restoredSnapshot.Devices.Single().NodeDetachApplyAtTick);
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

    private static NodeDeviceBackendDefinition NodeBackend(params DevicePortDefinition[] ports) =>
        NodeDeviceBackendDefinition.Create(ports);

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
