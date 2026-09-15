using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using Xunit;

namespace SpatialCircuits.Workbench.Tests;

public sealed class WorkbenchExecutionModeTests
{
    [Fact]
    public void WorkerAdmissionStopsBeforeAnActiveNodeBackendCanRun()
    {
        var owner = new CircuitId("panel/worker-admission");
        var binding = NodeCellBindingDefinition.Create(
            owner,
            "cell/worker-admission",
            1,
            new ComponentId("device/worker-admission"),
            [DevicePortDefinition.Create("out", DevicePortDirection.Output)]);
        var session = new PanelWorkbenchSession(PanelDefinition.Create(owner, 2, 1, []));
        Assert.True(session.TryPlaceNodeCell(binding, new GridCoordinate(0, 0), out var diagnostic), diagnostic?.Message);
        Assert.True(session.CommitStaged(out diagnostic), diagnostic?.Message);
        session.AttachNodeBackend(binding.Device.Id, new DeviceNodeBackendBinding(_ => { }));

        Assert.False(session.TrySetExecutionMode(WorkbenchExecutionMode.Worker, out diagnostic));
        Assert.Equal(NodeCellDiagnosticCodes.WorkerRequiresMainThread, diagnostic!.Code);
        Assert.Equal(WorkbenchExecutionMode.Reference, session.ExecutionMode);
    }

    [Fact]
    public void WorkerAdmissionBridgesToThePureRuntimeWhenNoNodeBackendIsActive()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/worker-safe"), 1, 1, []));

        Assert.True(session.TrySetExecutionMode(WorkbenchExecutionMode.Worker, out var diagnostic), diagnostic?.Message);
        Assert.Null(diagnostic);
        Assert.Equal(WorkbenchExecutionMode.Worker, session.ExecutionMode);
        session.StepMicrotick();
    }
}
