using GdUnit4;
using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.GodotAdapter;
using SpatialCircuits.Hierarchy;
using static GdUnit4.Assertions;

namespace SpatialWires.IndependentConsumer.Tests;

[TestSuite]
public sealed class IndependentConsumerAddonTests
{
    // covers: spatial-circuits/package-baseline :: Portable second-consumer practice check :: Addon is moved to an independent consumer
    [TestCase]
    [RequireGodotRuntime]
    public void StagedAddonExposesItsPublicNodeAndResource()
    {
        var node = new SpatialCircuitNode();
        var resource = new SpatialCircuitResource();
        try
        {
            AssertThat(node).IsNotNull();
            AssertThat(resource).IsNotNull();
            AssertThat(node.GetType().FullName).IsEqual("SpatialCircuits.GodotAdapter.SpatialCircuitNode");
            AssertThat(resource.GetType().FullName).IsEqual("SpatialCircuits.GodotAdapter.SpatialCircuitResource");
        }
        finally
        {
            node.Free();
            resource.Dispose();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void StagedPanelAndCircuitResourcesRoundTripThroughGodot()
    {
        var panelPath = $"user://independent-panel-{Guid.NewGuid():N}.tres";
        var circuitPath = $"user://independent-circuit-{Guid.NewGuid():N}.tres";
        var panel = new SpatialCircuitPanelResource
        {
            PanelId = "panel/independent-roundtrip",
            Width = 2,
            Height = 1
        };
        var cell = new SpatialCircuitCellResource
        {
            CellId = "constant",
            X = 1,
            Kind = "constant",
            Parameters = new Godot.Collections.Dictionary<string, string> { ["value"] = "1" }
        };
        panel.Cells.Add(cell);
        var circuit = new SpatialCircuitResource
        {
            DefinitionId = "chip/independent-roundtrip",
            SourcePanel = panel,
            SchemaVersion = 3,
            BehaviorVersion = 2,
            Symbol = "roundtrip",
            Parameters = new Godot.Collections.Dictionary<string, string> { ["mode"] = "test" }
        };
        circuit.Ports.Add(new SpatialCircuitChipPortResource
        {
            Name = "result",
            PanelPortId = "result",
            Direction = "output"
        });
        SpatialCircuitPanelResource? loadedPanel = null;
        SpatialCircuitResource? loadedCircuit = null;

        try
        {
            AssertThat(ResourceSaver.Save(panel, panelPath)).IsEqual(Error.Ok);
            AssertThat(ResourceSaver.Save(circuit, circuitPath)).IsEqual(Error.Ok);

            loadedPanel = ResourceLoader.Load<SpatialCircuitPanelResource>(panelPath);
            var cachedPanel = ResourceLoader.Load<SpatialCircuitPanelResource>(panelPath);
            loadedCircuit = ResourceLoader.Load<SpatialCircuitResource>(circuitPath);
            AssertThat(loadedPanel).IsNotNull();
            AssertThat(cachedPanel).IsNotNull();
            AssertThat(loadedCircuit).IsNotNull();
            AssertThat(ReferenceEquals(loadedPanel, cachedPanel)).IsTrue();
            AssertThat(loadedPanel!.PanelId).IsEqual("panel/independent-roundtrip");
            AssertThat(loadedPanel.Cells.Single().Parameters["value"]).IsEqual("1");
            AssertThat(loadedCircuit!.DefinitionId).IsEqual("chip/independent-roundtrip");
            AssertThat(loadedCircuit.SchemaVersion).IsEqual(3);
            AssertThat(loadedCircuit.BehaviorVersion).IsEqual(2);
            AssertThat(loadedCircuit.Ports.Single().Direction).IsEqual("output");
            AssertThat(loadedCircuit.Parameters["mode"]).IsEqual("test");
        }
        finally
        {
            loadedPanel?.Dispose();
            loadedCircuit?.Dispose();
            circuit.Dispose();
            panel.Dispose();
            cell.Dispose();
            RemoveSavedResource(panelPath);
            RemoveSavedResource(circuitPath);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void OneCachedPanelResourceCreatesIndependentRuntimeInstances()
    {
        var path = $"user://independent-runtime-panel-{Guid.NewGuid():N}.tres";
        var panel = new SpatialCircuitPanelResource
        {
            PanelId = "panel/independent-runtime",
            Width = 1,
            Height = 1
        };
        var clock = new SpatialCircuitCellResource
        {
            CellId = "clock",
            Kind = "clock",
            Parameters = new Godot.Collections.Dictionary<string, string>
            {
                ["high-ticks"] = "1",
                ["low-ticks"] = "1"
            }
        };
        panel.Cells.Add(clock);
        SpatialCircuitPanelResource? loaded = null;

        try
        {
            AssertThat(ResourceSaver.Save(panel, path)).IsEqual(Error.Ok);
            loaded = ResourceLoader.Load<SpatialCircuitPanelResource>(path);
            var cachedAgain = ResourceLoader.Load<SpatialCircuitPanelResource>(path);
            AssertThat(loaded).IsNotNull();
            AssertThat(cachedAgain).IsNotNull();
            AssertThat(ReferenceEquals(loaded, cachedAgain)).IsTrue();

            var definition = SpatialCircuitResourceAdapter.ToPanelDefinition(loaded!);
            var firstRuntime = new PanelRuntimeInstance(definition);
            var secondRuntime = new PanelRuntimeInstance(definition);
            firstRuntime.Step();

            AssertThat(firstRuntime.CurrentTick).IsEqual(1L);
            AssertThat(secondRuntime.CurrentTick).IsEqual(0L);
            AssertThat(loaded!.PanelId).IsEqual("panel/independent-runtime");
            AssertThat(loaded.Cells.Single().Parameters["high-ticks"]).IsEqual("1");
        }
        finally
        {
            loaded?.Dispose();
            panel.Dispose();
            clock.Dispose();
            RemoveSavedResource(path);
        }
    }

    // covers: spatial-circuits/recorded-node-bridge :: Public record-disable-replay responder practice :: Node-disabled replay matches causal hashes
    [TestCase]
    [RequireGodotRuntime]
    public void ScriptedResponderRecordsAndReplaysWithoutExecutingItsNode()
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
            var controllerId = new ComponentId("controller");
            var responderId = new ComponentId("responder");
            var sinkId = new ComponentId("sink");
            var controller = DeviceDefinition.Create(controllerId, TimedDeviceBackendDefinition.Create(
                [
                    DevicePortDefinition.Create("challenge", DevicePortDirection.Output),
                    DevicePortDefinition.Create("enable", DevicePortDirection.Output)
                ],
                [
                    new TimedOutputChange(1, "challenge", DeviceSignal.Scalar(LogicValue.High)),
                    new TimedOutputChange(1, "enable", DeviceSignal.Scalar(LogicValue.High))
                ]));
            var responder = DeviceDefinition.Create(responderId, NodeDeviceBackendDefinition.Create(
                [
                    DevicePortDefinition.Create("challenge", DevicePortDirection.Input),
                    DevicePortDefinition.Create("enable", DevicePortDirection.Input),
                    DevicePortDefinition.Create("response", DevicePortDirection.Output)
                ]));
            var sink = DeviceDefinition.Create(sinkId, TimedDeviceBackendDefinition.Create(
                [
                    DevicePortDefinition.Create("response", DevicePortDirection.Input),
                    DevicePortDefinition.Create("readback", DevicePortDirection.Output)
                ], []));
            var definition = DeviceGraphDefinition.Create(
                new DefinitionId("graph/node-responder"),
                [controller, responder, sink],
                [
                    CableLaneDefinition.Create(new ComponentId("lane/challenge"),
                        DevicePortEndpoint.Create(controllerId, "challenge"),
                        DevicePortEndpoint.Create(responderId, "challenge"), 1),
                    CableLaneDefinition.Create(new ComponentId("lane/enable"),
                        DevicePortEndpoint.Create(controllerId, "enable"),
                        DevicePortEndpoint.Create(responderId, "enable"), 1),
                    CableLaneDefinition.Create(new ComponentId("lane/response"),
                        DevicePortEndpoint.Create(responderId, "response"),
                        DevicePortEndpoint.Create(sinkId, "response"), 1)
                ]);
            var live = new DeviceGraphInstance(definition);
            binding = new SpatialCircuitNodeBinding(live, responderId, node);
            node.DeviceStep += context =>
            {
                var hasChallenge = context.CommittedInputs.Any(input =>
                    input.PortName == "challenge" && input.Signal == "1");
                var isEnabled = context.CommittedInputs.Any(input =>
                    input.PortName == "enable" && input.Signal == "1");
                if (hasChallenge && isEnabled)
                {
                    AssertThat(context.Outputs.ScheduleTimer(context.Tick + 1, "respond")).IsTrue();
                }

                if (context.DueTimers.Any(timer => timer.TimerId == "respond"))
                {
                    AssertThat(context.Outputs.RequestOutput(context.Tick + 1, "response", "1")).IsTrue();
                }
            };

            var initial = live.CaptureSnapshot();
            var liveHashes = new List<string>();
            for (var tick = 0; tick < 6; tick++)
            {
                live.Step();
                liveHashes.Add(live.CaptureSnapshot().Scheduler.Trace[^1].Hash);
            }

            var recordedCommands = live.AcceptedCommands;
            var finalLive = live.CaptureSnapshot();
            AssertThat(recordedCommands.Length).IsEqual(2);
            AssertThat(finalLive.Devices.Single(device => device.DeviceId == responderId)
                .Outputs.Single(pair => pair.Key == "response").Value.Bits[0]).IsEqual(LogicValue.High);
            AssertThat(finalLive.Devices
                .Single(device => device.DeviceId == sinkId)
                .Inputs.Single(pair => pair.Key == "response").Value).IsEqual(DeviceSignal.Scalar(LogicValue.High));

            node.Free();
            var replay = new DeviceGraphInstance(definition);
            replay.RestoreSnapshot(initial);
            replay.ReplayNodeBackendCommands(recordedCommands);
            var replayHashes = new List<string>();
            for (var tick = 0; tick < 6; tick++)
            {
                replay.Step();
                replayHashes.Add(replay.CaptureSnapshot().Scheduler.Trace[^1].Hash);
            }

            var finalReplay = replay.CaptureSnapshot();
            AssertThat(liveHashes.SequenceEqual(replayHashes)).IsTrue();
            AssertThat(finalLive.Scheduler.AcceptedCommands.SequenceEqual(finalReplay.Scheduler.AcceptedCommands))
                .IsTrue();
            AssertThat(finalReplay.Devices
                .Single(device => device.DeviceId == sinkId)
                .Inputs.Single(pair => pair.Key == "response").Value).IsEqual(DeviceSignal.Scalar(LogicValue.High));
            GD.Print($"Node responder replay hashes match: {liveHashes[^1]} = {replayHashes[^1]}");
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

    private static void RemoveSavedResource(string path)
    {
        var absolutePath = ProjectSettings.GlobalizePath(path);
        if (File.Exists(absolutePath))
        {
            DirAccess.RemoveAbsolute(absolutePath);
        }
    }
}
