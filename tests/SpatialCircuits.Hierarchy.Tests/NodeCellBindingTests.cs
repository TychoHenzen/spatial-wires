using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Hierarchy.Tests;

public sealed class NodeCellBindingTests
{
    [Fact]
    public void RegistryResolvesByOwnerAndLeavesMissingBindingsAsPlaceholders()
    {
        var owner = new CircuitId("panel/owner");
        var binding = NodeCellBindingDefinition.Create(
            owner,
            "cell/monitor",
            2,
            new ComponentId("device/monitor"),
            [DevicePortDefinition.Create("output", DevicePortDirection.Output)]);
        var registry = new NodeCellBindingRegistry();
        registry.Register(binding);

        var resolved = registry.Resolve(owner, binding.BindingId, binding.Version);
        var wrongOwner = registry.Resolve(new CircuitId("panel/other"), binding.BindingId, binding.Version);
        var missing = registry.Resolve(owner, "cell/missing", 1);

        Assert.False(resolved.IsPlaceholder);
        Assert.Equal(binding, resolved.Definition);
        Assert.True(wrongOwner.IsPlaceholder);
        Assert.Equal(NodeCellDiagnosticCodes.BindingOwnerMismatch, wrongOwner.Placeholder!.DiagnosticCode);
        Assert.True(missing.IsPlaceholder);
        Assert.Equal(NodeCellDiagnosticCodes.BindingMissing, missing.Placeholder!.DiagnosticCode);
    }

    [Fact]
    public void ActiveNodeCellBindingBlocksWorkerExecutionByStableBindingId()
    {
        var deviceId = new ComponentId("device/monitor");
        var graph = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-cell-worker"),
            [DeviceDefinition.Create(deviceId, NodeDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
                "cell/monitor",
                1))],
            []));
        var binding = new DeviceNodeBackendBinding(_ => { });
        graph.AttachNodeBackend(deviceId, binding);

        Assert.False(graph.WorkerExecutionAllowed);
        var exception = Assert.Throws<InvalidOperationException>(graph.EnsureWorkerExecutionAllowed);
        Assert.Contains("cell/monitor", exception.Message);

        binding.Invalidate();
        graph.EnsureWorkerExecutionAllowed();
        Assert.True(graph.WorkerExecutionAllowed);
    }

    [Fact]
    public void ReplacingBindingIdentityInvalidatesThePreviousCallback()
    {
        var deviceId = new ComponentId("device/replaced-node");
        var graph = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-cell-replace"),
            [DeviceDefinition.Create(deviceId, NodeDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
                "cell/old",
                1))],
            []));
        var oldBinding = new DeviceNodeBackendBinding(context =>
        {
            Assert.True(context.Outputs.RequestOutput(
                context.Tick + 3,
                "output",
                DeviceSignal.Scalar(LogicValue.High)));
        });
        graph.AttachNodeBackend(deviceId, oldBinding);
        graph.Step();

        graph.ReplaceBackend(deviceId, NodeDeviceBackendDefinition.Create(
            [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
            "cell/new",
            2));

        Assert.False(oldBinding.IsActive);
        Assert.True(graph.WorkerExecutionAllowed);
        graph.Step();
        graph.Step();
        Assert.Equal(LogicValue.HighImpedance, graph.GetOutput(deviceId, "output").Bits[0]);
    }

    [Fact]
    public void SnapshotRejectsAChangedNodeBindingIdentity()
    {
        var deviceId = new ComponentId("device/snapshot-node");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/node-cell-snapshot"),
            [DeviceDefinition.Create(deviceId, NodeDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
                "cell/snapshot-old",
                1))],
            []);
        var graph = new DeviceGraphInstance(definition);
        var snapshot = graph.CaptureSnapshot();
        var changedDefinition = DeviceGraphDefinition.Create(
            definition.Id,
            [DeviceDefinition.Create(deviceId, NodeDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
                "cell/snapshot-new",
                2))],
            []);

        Assert.Throws<ArgumentException>(() => graph.RestoreSnapshot(snapshot with
        {
            Definition = changedDefinition
        }));
    }

    [Fact]
    public void PresentationEventsReplayFromTheRecordedSchedulerCommands()
    {
        var deviceId = new ComponentId("device/presentation");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/presentation-replay"),
            [DeviceDefinition.Create(deviceId, NodeDeviceBackendDefinition.Create(
                [
                    DevicePortDefinition.Create("input", DevicePortDirection.Input),
                    DevicePortDefinition.Create("output", DevicePortDirection.Output)
                ],
                "cell/presentation",
                1))],
            []);
        var live = new DeviceGraphInstance(definition);
        var binding = new DeviceNodeBackendBinding(context =>
        {
            if (context.CommittedInputs.Any(input => input.PortName == "input" &&
                                                     input.Signal.Equals(DeviceSignal.Scalar(LogicValue.High))))
            {
                Assert.True(context.Outputs.RequestPresentation(context.Tick + 1, "alarm"));
            }
        });
        live.AttachNodeBackend(deviceId, binding);
        live.SetInput(deviceId, "input", DeviceSignal.Scalar(LogicValue.High));
        var initial = live.CaptureSnapshot();
        live.Step();
        live.Step();
        var finalLive = live.CaptureSnapshot();

        var replay = new DeviceGraphInstance(definition);
        replay.RestoreSnapshot(initial);
        replay.ReplayNodeBackendCommands(finalLive.ReplayCommands);
        replay.Step();
        replay.Step();
        var finalReplay = replay.CaptureSnapshot();

        Assert.Equal(finalLive.StateIntegrityHash, finalReplay.StateIntegrityHash);
        Assert.Equal(finalLive.Devices.Single().PresentationEvents.ToArray(),
            finalReplay.Devices.Single().PresentationEvents.ToArray());
    }

    [Fact]
    public void ReplacementReplayKeepsTheNewNodeTargetActive()
    {
        var deviceId = new ComponentId("device/replay-replacement");
        var oldBackend = NodeDeviceBackendDefinition.Create(
            [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
            "cell/old",
            1);
        var newBackend = NodeDeviceBackendDefinition.Create(
            [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
            "cell/new",
            2);
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/replay-replacement"),
            [DeviceDefinition.Create(deviceId, oldBackend)],
            []);
        var live = new DeviceGraphInstance(definition);
        live.AttachNodeBackend(deviceId, new DeviceNodeBackendBinding(context =>
            Assert.True(context.Outputs.RequestOutput(
                context.Tick + 3,
                "output",
                DeviceSignal.Scalar(LogicValue.High)))));
        live.Step();
        live.ReplaceBackend(deviceId, newBackend);
        var snapshot = live.CaptureSnapshot();

        var replay = new DeviceGraphInstance(snapshot.Definition);
        replay.ReplayNodeBackendCommands(snapshot.Scheduler.AcceptedCommands);
        replay.Step();

        Assert.True(replay.CaptureSnapshot().Scheduler.Targets.Single(target =>
            target.StableId == "node-backend/" + deviceId.Value).Active);
    }

    [Fact]
    public void ReplacingAfterDisposalDoesNotLeaveTheReplacementInactive()
    {
        var deviceId = new ComponentId("device/disposal-replacement");
        var definition = DeviceGraphDefinition.Create(
            new DefinitionId("graph/disposal-replacement"),
            [DeviceDefinition.Create(deviceId, NodeDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
                "cell/old",
                1))],
            []);
        var graph = new DeviceGraphInstance(definition);
        var binding = new DeviceNodeBackendBinding(_ => { });
        graph.AttachNodeBackend(deviceId, binding);
        var initial = graph.CaptureSnapshot();
        binding.Invalidate();
        graph.InvalidateNodeBackend(deviceId, binding);
        graph.ReplaceBackend(deviceId, NodeDeviceBackendDefinition.Create(
            [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
            "cell/new",
            2));

        graph.Step();

        var finalLive = graph.CaptureSnapshot();
        var replay = new DeviceGraphInstance(definition);
        replay.RestoreSnapshot(initial);
        replay.ReplayNodeBackendCommands(finalLive.Scheduler.AcceptedCommands);
        replay.Step();
        var finalReplay = replay.CaptureSnapshot();

        Assert.True(finalLive.Scheduler.Targets.Single(target =>
            target.StableId == "node-backend/" + deviceId.Value).Active);
        Assert.Equal(finalLive.StateIntegrityHash, finalReplay.StateIntegrityHash);
    }

    [Fact]
    public void SameIdentityReplacementPreservesPresentationEvents()
    {
        var deviceId = new ComponentId("device/presentation-replacement");
        var backend = NodeDeviceBackendDefinition.Create(
            [DevicePortDefinition.Create("output", DevicePortDirection.Output)],
            "cell/same",
            1);
        var graph = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/presentation-replacement"),
            [DeviceDefinition.Create(deviceId, backend)],
            []));
        var binding = new DeviceNodeBackendBinding(context =>
            Assert.True(context.Outputs.RequestPresentation(context.Tick + 1, "alarm")));
        graph.AttachNodeBackend(deviceId, binding);
        graph.Step();
        graph.Step();
        var before = graph.GetNodePresentationEvents(deviceId);

        graph.ReplaceBackend(deviceId, backend);

        Assert.Equal(before.ToArray(), graph.GetNodePresentationEvents(deviceId).ToArray());
    }

}
