using System.Collections.Immutable;
using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.GodotAdapter;

public sealed record SpatialCircuitNodeMonitorPracticeResult(
    bool Succeeded,
    long CallbackTick,
    long GraphTickAfterStep,
    ImmutableArray<SpatialCircuitNodePresentationEvent> PresentationEvents,
    PanelWorkbenchSession Session)
{
    public bool InvalidPresentationRejected { get; init; }
}

public static class SpatialCircuitNodeMonitorPractice
{
    public static SpatialCircuitNodeMonitorPracticeResult Run(SceneTree sceneTree)
    {
        ArgumentNullException.ThrowIfNull(sceneTree);
        var ownerPanelId = new CircuitId("panel/node-monitor");
        var binding = NodeCellBindingDefinition.Create(
            ownerPanelId,
            "node-monitor/alarm",
            1,
            new ComponentId("node-monitor"),
            [
                DevicePortDefinition.Create("alarm", DevicePortDirection.Input),
                DevicePortDefinition.Create("output", DevicePortDirection.Output)
            ]);
        var session = new PanelWorkbenchSession(PanelDefinition.Create(ownerPanelId, 2, 2, []));
        if (!session.TryPlaceNodeCell(binding, new GridCoordinate(0, 0), out var diagnostic) ||
            !session.CommitStaged(out diagnostic))
        {
            throw new InvalidOperationException(
                diagnostic?.Message ?? "Node monitor could not be placed through the public editor API.");
        }

        var parent = new Node { Name = "NodeMonitorPractice" };
        sceneTree.Root.AddChild(parent);
        var node = new SpatialCircuitNode();
        parent.AddChild(node);
        SpatialCircuitNodeBinding? liveBinding = null;
        try
        {
            var graph = session.DeviceGraph ?? throw new InvalidOperationException("Node monitor graph is missing.");
            graph.SetInput(binding.Device.Id, "alarm", DeviceSignal.Scalar(LogicValue.High));
            long callbackTick = -1;
            var invalidPresentationRejected = false;
            node.DeviceStep += context =>
            {
                if (callbackTick < 0)
                {
                    callbackTick = context.Tick;
                }
                invalidPresentationRejected =
                    !context.Outputs.RequestPresentation(context.Tick, "same-tick") &&
                    !context.Outputs.RequestPresentation(context.Tick - 1, "past");
                if (context.CommittedInputs.Any(input =>
                        input.PortName == "alarm" && input.Signal == "1"))
                {
                    _ = context.Outputs.RequestPresentation(context.Tick + 1, "alarm");
                }
            };
            liveBinding = new SpatialCircuitNodeBinding(session, binding, node);
            var result = session.StepMicrotick();
            session.StepMicrotick();
            var events = liveBinding.PresentationEvents;
            liveBinding.RestoreSnapshot(liveBinding.CaptureSnapshot());
            var hasAlarm = events.Any(item => item.Tick > callbackTick && item.EventId == "alarm") &&
                           graph.GetNodePresentationEvents(binding.Device.Id).Any(item =>
                               item.Tick > callbackTick && item.EventId == "alarm");
            var succeeded = callbackTick == result.Tick && graph.CurrentTick == result.Tick + 2 && hasAlarm;
            return new SpatialCircuitNodeMonitorPracticeResult(
                succeeded,
                callbackTick,
                graph.CurrentTick,
                events,
                session)
            {
                InvalidPresentationRejected = invalidPresentationRejected
            };
        }
        finally
        {
            liveBinding?.Dispose();
            if (GodotObject.IsInstanceValid(parent))
            {
                parent.Free();
            }
        }
    }
}
