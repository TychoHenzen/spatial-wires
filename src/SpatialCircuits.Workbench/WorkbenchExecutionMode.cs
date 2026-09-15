using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Workbench;

public enum WorkbenchExecutionMode
{
    Reference,
    Optimized,
    Worker
}

public static class WorkbenchExecutionAdmission
{
    public static bool TryAdmit(
        PanelWorkbenchSession session,
        WorkbenchExecutionMode mode,
        out WorkbenchDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Enum.IsDefined(mode))
        {
            diagnostic = new WorkbenchDiagnostic(
                "workbench.execution.invalid",
                "Execution mode is invalid.");
            return false;
        }

        if (mode == WorkbenchExecutionMode.Worker && !session.WorkerExecutionAllowed)
        {
            diagnostic = new WorkbenchDiagnostic(
                NodeCellDiagnosticCodes.WorkerRequiresMainThread,
                "Worker execution requires the main-thread barrier while a Node backend is active.");
            return false;
        }

        diagnostic = null;
        return true;
    }
}
