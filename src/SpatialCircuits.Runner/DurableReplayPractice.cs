using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Persistence;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Runner;

public sealed record DurableReplayScenarioPlan(int Microticks);

public sealed record DurableReplayTraceRecord(
    TraceVersion TraceSchema,
    string FixtureId,
    int Sequence,
    int Microtick,
    string LiveHash,
    string ReplayHash,
    int RecordedCommandCount,
    bool Saved,
    bool Reloaded,
    bool Passed);

public sealed record DurableReplayPracticeResult(
    bool Succeeded,
    Guid SaveId,
    bool Saved,
    bool Reloaded,
    ImmutableArray<string> LiveHashes,
    ImmutableArray<string> ReplayHashes,
    ImmutableArray<AcceptedSchedulerCommand> Commands,
    DurableTraceExport Trace,
    ImmutableArray<DurableDiagnostic> Diagnostics,
    int LiveNodeCalls);

public static class DurableReplayPractice
{
    public static DurableReplayPracticeResult Run(int microticks = 5)
    {
        if (microticks <= 0 || microticks > FixtureValidator.MaxScheduledDriveMicroticks)
        {
            throw new ArgumentOutOfRangeException(nameof(microticks), "Durable practice microticks are out of range.");
        }

        var deviceId = new ComponentId("controller");
        var panel = PanelDefinition.Create(new CircuitId("panel/durable-practice"), 1, 1, []);
        var device = DeviceDefinition.Create(
            deviceId,
            NodeDeviceBackendDefinition.Create(
            [
                DevicePortDefinition.Create("challenge", DevicePortDirection.Input),
                DevicePortDefinition.Create("response", DevicePortDirection.Output)
            ]));
        var graph = DeviceGraphDefinition.Create(
            new DefinitionId("graph/durable-practice"),
            [device],
            []);
        var definition = WorkbenchDefinition.Restore(
            panel,
            ChipDefinitionCatalog.Create([]),
            PanelOwnedChipNetworkDefinition.Create(panel.Id, []),
            [],
            graph,
            [new WorkbenchDevicePlacement(deviceId, new GridCoordinate(0, 0))]);
        var session = new PanelWorkbenchSession(definition);
        var nodeCalls = 0;
        session.AttachNodeBackend(deviceId, new DeviceNodeBackendBinding(context =>
        {
            nodeCalls++;
            if (context.Tick == 0 && context.CommittedInputs.Any(input =>
                    input.PortName == "challenge" && input.Signal.Bits[0] == LogicValue.High))
            {
                _ = context.Outputs.RequestOutput(context.Tick + 1, "response", DeviceSignal.Scalar(LogicValue.High));
                _ = context.Outputs.ScheduleTimer(context.Tick + 2, "release");
            }
            else if (context.Tick == 2 && context.DueTimers.Any(timer => timer.TimerId == "release"))
            {
                _ = context.Outputs.RequestOutput(context.Tick + 1, "response", DeviceSignal.Scalar(LogicValue.Low));
            }
        }));
        Require(session.TryDriveDeviceInput(
                deviceId,
                "challenge",
                DeviceSignal.Scalar(LogicValue.High),
                out var driveDiagnostic),
            driveDiagnostic);
        Require(session.CommitStaged(out var commitDiagnostic), commitDiagnostic);
        var savePath = Path.Combine(
            Path.GetTempPath(),
            $"spatial-wires-durable-practice-{Guid.NewGuid():N}.scsave");
        var finalSavePath = savePath + ".final";
        var save = DurableSaveStore.Save(savePath, session);
        if (!save.Succeeded)
        {
            return Failed(session.SaveId, nodeCalls, false, false, save.Diagnostics);
        }

        try
        {
            var initialLoad = DurableSaveStore.Load(savePath);
            if (!initialLoad.Succeeded || initialLoad.Session is null)
            {
                return Failed(session.SaveId, nodeCalls, true, false, initialLoad.Diagnostics);
            }

            session.SetPaused(false);
            var liveHashes = ImmutableArray.CreateBuilder<string>();
            for (var tick = 0; tick < microticks; tick++)
            {
                _ = session.StepMicrotick();
                liveHashes.Add(session.CaptureSnapshot().DeviceGraph!.Scheduler.Trace[^1].Hash);
            }

            var finalSave = DurableSaveStore.Save(finalSavePath, session);
            if (!finalSave.Succeeded)
            {
                return Failed(session.SaveId, nodeCalls, true, true, finalSave.Diagnostics);
            }

            var finalLoad = DurableSaveStore.Load(finalSavePath);
            if (!finalLoad.Succeeded || finalLoad.Session is null)
            {
                return Failed(session.SaveId, nodeCalls, true, false, finalLoad.Diagnostics);
            }

            var commands = finalLoad.Session.DeviceAcceptedCommands;
            var replay = initialLoad.Session;
            replay.ReplayNodeBackendCommands(commands);
            replay.SetPaused(false);
            var replayHashes = ImmutableArray.CreateBuilder<string>();
            for (var tick = 0; tick < microticks; tick++)
            {
                _ = replay.StepMicrotick();
                replayHashes.Add(replay.CaptureSnapshot().DeviceGraph!.Scheduler.Trace[^1].Hash);
            }

            var trace = DurableTraceAndMigration.Export(session);
            var passed = liveHashes.SequenceEqual(replayHashes) &&
                         commands.Length > 0 &&
                         nodeCalls == microticks &&
                         trace.Entries.Length == microticks * 2;
            return new DurableReplayPracticeResult(
                passed,
                session.SaveId,
                true,
                true,
                liveHashes.ToImmutable(),
                replayHashes.ToImmutable(),
                commands,
                trace,
                passed ? [] : [new DurableDiagnostic(
                    DurableSnapshotDiagnosticCodes.SnapshotInvalid,
                    "$.practice",
                    "Durable replay practice produced different causal hashes.")],
                nodeCalls);
        }
        finally
        {
            try
            {
                File.Delete(savePath);
                File.Delete(finalSavePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static DurableReplayPracticeResult Failed(
        Guid saveId,
        int nodeCalls,
        bool saved,
        bool reloaded,
        ImmutableArray<DurableDiagnostic> diagnostics) => new(
        false,
        saveId,
        saved,
        reloaded,
        [],
        [],
        [],
        new DurableTraceExport(0, string.Empty, [], string.Empty),
        diagnostics,
        nodeCalls);

    private static void Require(bool condition, DurableDiagnostic? diagnostic)
    {
        if (!condition)
        {
            throw new InvalidOperationException(diagnostic?.Message ?? "Durable practice action was rejected.");
        }
    }

    private static void Require(bool condition, WorkbenchDiagnostic? diagnostic)
    {
        if (!condition)
        {
            throw new InvalidOperationException(diagnostic?.Message ?? "Durable practice action was rejected.");
        }
    }
}
