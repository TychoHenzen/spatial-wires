using GdUnit4;
using Godot;
using SpatialCircuits.Core;
using SpatialCircuits.GodotAdapter;
using SpatialCircuits.Hierarchy;
using static GdUnit4.Assertions;

namespace SpatialWires.Godot.Tests;

[TestSuite]
public sealed class SpatialCircuitNodeBindingTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void TreeExitInvalidatesNodeAndReleasesItsOutputAtLaneLatency()
    {
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        AssertThat(sceneTree).IsNotNull();
        var parent = new Node();
        sceneTree!.Root.AddChild(parent);
        var node = new SpatialCircuitNode();
        parent.AddChild(node);
        SpatialCircuitNodeBinding? binding = null;

        try
        {
            var nodeId = new ComponentId("node");
            var sinkId = new ComponentId("sink");
            var laneId = new ComponentId("lane/node-sink");
            var graph = new DeviceGraphInstance(DeviceGraphDefinition.Create(
                new DefinitionId("graph/node-godot"),
                [
                    DeviceDefinition.Create(nodeId, NodeDeviceBackendDefinition.Create(
                        [
                            DevicePortDefinition.Create("enable", DevicePortDirection.Input),
                            DevicePortDefinition.Create("output", DevicePortDirection.Output)
                        ])),
                    DeviceDefinition.Create(sinkId, TimedDeviceBackendDefinition.Create(
                        [
                            DevicePortDefinition.Create("input", DevicePortDirection.Input),
                            DevicePortDefinition.Create("output", DevicePortDirection.Output)
                        ], []))
                ],
                [CableLaneDefinition.Create(
                    laneId,
                    DevicePortEndpoint.Create(nodeId, "output"),
                    DevicePortEndpoint.Create(sinkId, "input"),
                    latency: 2)]));

            var calls = 0;
            var csharpEventReentryRejected = false;
            var signalReentryRejected = false;
            node.ChildEnteredTree += _ =>
            {
                try
                {
                    graph.Step();
                }
                catch (InvalidOperationException)
                {
                    signalReentryRejected = true;
                }
            };
            node.DeviceStep += context =>
            {
                calls++;
                if (context.Tick == 0)
                {
                    try
                    {
                        graph.Step();
                    }
                    catch (InvalidOperationException)
                    {
                        csharpEventReentryRejected = true;
                    }

                    node.AddChild(new Node());
                    AssertThat(context.CommittedInputs.Any(input =>
                        input.PortName == "enable" && input.Signal == "1")).IsTrue();
                    AssertThat(context.Outputs.RequestOutput(1, "output", "1")).IsTrue();
                }
            };
            graph.SetInput(nodeId, "enable", DeviceSignal.Scalar(LogicValue.High));
            binding = new SpatialCircuitNodeBinding(graph, nodeId, node);

            for (var tick = 0; tick < 4; tick++)
            {
                AssertThat(graph.Step().Tick).IsEqual((long)tick);
            }

            AssertThat(graph.GetOutput(nodeId, "output").Bits[0]).IsEqual(LogicValue.High);
            AssertThat(graph.CaptureSnapshot().Devices.Single(device => device.DeviceId == sinkId)
                .Inputs.Single(pair => pair.Key == "input").Value.Bits[0]).IsEqual(LogicValue.High);

            node.Free();
            AssertThat(graph.Step().Tick).IsEqual(4L);
            AssertThat(graph.GetOutput(nodeId, "output").Bits[0]).IsEqual(LogicValue.HighImpedance);
            AssertThat(graph.CaptureSnapshot().Devices.Single(device => device.DeviceId == sinkId)
                .Inputs.Single(pair => pair.Key == "input").Value.Bits[0]).IsEqual(LogicValue.High);
            AssertThat(graph.Step().Tick).IsEqual(5L);
            AssertThat(graph.CaptureSnapshot().Devices.Single(device => device.DeviceId == sinkId)
                .Inputs.Single(pair => pair.Key == "input").Value.Bits[0]).IsEqual(LogicValue.High);
            AssertThat(graph.Step().Tick).IsEqual(6L);
            AssertThat(graph.CaptureSnapshot().Devices.Single(device => device.DeviceId == sinkId)
                .Inputs.Single(pair => pair.Key == "input").Value.Bits[0]).IsEqual(LogicValue.HighImpedance);
            AssertThat(calls).IsEqual(4);
            AssertThat(csharpEventReentryRejected).IsTrue();
            AssertThat(signalReentryRejected).IsTrue();
            AssertThat(graph.IsConnected(laneId)).IsTrue();
        }
        finally
        {
            binding?.Dispose();
            if (GodotObject.IsInstanceValid(parent))
            {
                parent.Free();
            }
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TreeExitCallbackCannotReenterAndInvalidatesItsOutputCapability()
    {
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        AssertThat(sceneTree).IsNotNull();
        var parent = new Node();
        sceneTree!.Root.AddChild(parent);
        var node = new SpatialCircuitNode();
        parent.AddChild(node);
        SpatialCircuitNodeBinding? binding = null;

        try
        {
            var deviceId = new ComponentId("node");
            var graph = new DeviceGraphInstance(DeviceGraphDefinition.Create(
                new DefinitionId("graph/node-exit"),
                [DeviceDefinition.Create(deviceId, NodeDeviceBackendDefinition.Create(
                    [DevicePortDefinition.Create("output", DevicePortDirection.Output)]))],
                []));
            var exitReentryRejected = false;
            var staleOutputRejected = false;
            node.TreeExiting += () =>
            {
                try
                {
                    graph.Step();
                }
                catch (InvalidOperationException)
                {
                    exitReentryRejected = true;
                }
            };
            binding = new SpatialCircuitNodeBinding(graph, deviceId, node);
            node.DeviceStep += context =>
            {
                node.Free();
                staleOutputRejected = !context.Outputs.RequestOutput(
                    context.Tick + 1, "output", "1");
            };

            var result = graph.Step();

            AssertThat(result.Tick).IsEqual(0L);
            AssertThat(graph.CurrentTick).IsEqual(1L);
            AssertThat(exitReentryRejected).IsTrue();
            AssertThat(staleOutputRejected).IsTrue();
            AssertThat(graph.AcceptedCommands.Count(command =>
                command.Command.Kind == SchedulerCommandKind.ScheduleEvent)).IsEqual(0);
            AssertThat(graph.GetOutput(deviceId, "output").Bits[0]).IsEqual(LogicValue.HighImpedance);
            AssertThat(graph.Step().Tick).IsEqual(1L);
            AssertThat(graph.CurrentTick).IsEqual(2L);
        }
        finally
        {
            binding?.Dispose();
            if (GodotObject.IsInstanceValid(parent))
            {
                parent.Free();
            }
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void WrongThreadDisposeCanBeRetriedOnTheBoundThread()
    {
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        AssertThat(sceneTree).IsNotNull();
        var parent = new Node();
        sceneTree!.Root.AddChild(parent);
        var node = new SpatialCircuitNode();
        parent.AddChild(node);
        SpatialCircuitNodeBinding? binding = null;

        try
        {
            var deviceId = new ComponentId("node");
            var graph = new DeviceGraphInstance(DeviceGraphDefinition.Create(
                new DefinitionId("graph/node-dispose-thread"),
                [DeviceDefinition.Create(deviceId, NodeDeviceBackendDefinition.Create(
                    [DevicePortDefinition.Create("output", DevicePortDirection.Output)]))],
                []));
            var calls = 0;
            node.DeviceStep += _ => calls++;
            var activeBinding = new SpatialCircuitNodeBinding(graph, deviceId, node);
            binding = activeBinding;

            var failedOffThread = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    activeBinding.Dispose();
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }).GetAwaiter().GetResult();

            AssertThat(failedOffThread).IsTrue();
            AssertThat(graph.Step().Tick).IsEqual(0L);
            AssertThat(calls).IsEqual(1);
            activeBinding.Dispose();
            AssertThat(graph.Step().Tick).IsEqual(1L);
            AssertThat(calls).IsEqual(1);
        }
        finally
        {
            binding?.Dispose();
            if (GodotObject.IsInstanceValid(parent))
            {
                parent.Free();
            }
        }
    }
}
