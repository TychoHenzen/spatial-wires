using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Runner;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.GodotAdapter;

public sealed record WorkbenchPracticeResult(
    PanelWorkbenchSession Session,
    ChipDefinition PackagedChip,
    ImmutableArray<LogicValue> XorOutputTrace,
    ImmutableArray<LogicValue> PlacedChipOutputTrace,
    ImmutableArray<CableLaneHistoryEntry> CableHistory,
    bool Succeeded)
{
    public ImmutableArray<ProbeSample> XorWaveform => XorOutputTrace
        .Select((value, tick) => new ProbeSample(new ComponentId("xor-raw-probe"), tick, value))
        .ToImmutableArray();
}

public static class WorkbenchPractice
{
    public static WorkbenchPracticeResult Run(ReadOnlySpan<byte> fixtureBytes)
    {
        var read = FixtureCodec.Read(fixtureBytes);
        var plan = read.Fixture?.PanelScenario
            ?? throw new ArgumentException("XOR practice fixture is invalid.", nameof(fixtureBytes));
        var sourcePanel = plan.CreatePanelDefinition();
        var panel = PanelDefinition.Create(
            sourcePanel.Id,
            sourcePanel.Width,
            sourcePanel.Height,
            sourcePanel.Cells.OfType<PanelCellDefinition>()
                .Where(cell => cell.Id.Value != "xor-raw-probe")
                .Append(PanelCellDefinition.Create(
                    new ComponentId("xor-output"),
                    new GridCoordinate(15, 5),
                    CellKind.OutputPort,
                    CardinalDirection.East,
                    new PortId("xor"))));
        var session = new PanelWorkbenchSession(PanelDefinition.Create(panel.Id, panel.Width, panel.Height, []));
        foreach (var cell in panel.Cells.OfType<PanelCellDefinition>())
        {
            Require(session.TryPaint(cell, out var diagnostic), diagnostic, "Could not draw the XOR panel.");
        }

        var inputsByTick = plan.Inputs.GroupBy(input => input.Tick)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (inputsByTick.TryGetValue(0, out var initialInputs))
        {
            ApplyInputs(session, initialInputs);
        }

        Require(session.CommitStaged(out var commitDiagnostic), commitDiagnostic,
            "Could not commit the XOR panel.");
        session.SetPaused(false);
        var outputTrace = ImmutableArray.CreateBuilder<LogicValue>(plan.Microticks);
        var filteredTrace = ImmutableArray.CreateBuilder<LogicValue>(plan.Microticks);
        for (var tick = 0; tick < plan.Microticks; tick++)
        {
            if (tick > 0 && inputsByTick.TryGetValue(tick, out var inputs))
            {
                ApplyInputs(session, inputs);
            }

            var result = session.StepMicrotick();
            outputTrace.Add(Enum.Parse<LogicValue>(result.Outputs["xor"]));
            filteredTrace.Add(result.ProbeSamples
                .Single(sample => sample.ProbeId.Value == "xor-filtered-probe").Value);
        }

        var expectedRaw = plan.Expectations
            .Where(item => item.ProbeId == "xor-raw-probe")
            .Select(item => Enum.Parse<LogicValue>(item.Value))
            .ToArray();
        var expectedFiltered = plan.Expectations
            .Where(item => item.ProbeId == "xor-filtered-probe")
            .Select(item => Enum.Parse<LogicValue>(item.Value))
            .ToArray();
        var xorPassed = outputTrace.SequenceEqual(expectedRaw) && filteredTrace.SequenceEqual(expectedFiltered);

        session.SetPaused(true);
        Require(session.TryPackagePanelAsChip(
            new DefinitionId("chip/xor-practice"), "xor", out var chip, out var packageDiagnostic),
            packageDiagnostic,
            "Could not package the XOR panel.");
        var packagedChip = chip!;
        var chipInstanceId = new ComponentId("chip/xor-instance");
        Require(session.TryPlaceChip(packagedChip, chipInstanceId,
            FindFreeLocation(session.Definition), out var chipDiagnostic), chipDiagnostic,
            "Could not place the packaged XOR chip.");

        var panelDevice = DeviceDefinition.Create(
            new ComponentId("device/xor-panel"),
            PanelDeviceBackendDefinition.Create(packagedChip.SourcePanel));
        var delayedDevice = DeviceDefinition.Create(
            new ComponentId("device/delay"),
            TimedDeviceBackendDefinition.Create(
                [
                    DevicePortDefinition.Create("in", DevicePortDirection.Input),
                    DevicePortDefinition.Create("out", DevicePortDirection.Output)
                ],
                [new TimedOutputChange(1, "out", DeviceSignal.Scalar(LogicValue.Low))]));
        Require(session.TryPlaceDevice(panelDevice, FindFreeLocation(session.Definition), out var panelDeviceDiagnostic),
            panelDeviceDiagnostic,
            "Could not place the XOR panel device.");
        Require(session.TryPlaceDevice(delayedDevice, FindFreeLocation(session.Definition), out var delayDiagnostic),
            delayDiagnostic,
            "Could not place the delayed device.");

        var outputName = panelDevice.Ports.Single(port => port.Direction == DevicePortDirection.Output).Name;
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane/xor-delay"),
            DevicePortEndpoint.Create(panelDevice.Id, outputName),
            DevicePortEndpoint.Create(delayedDevice.Id, "in"),
            latency: 2);
        var bundle = CableBundleDefinition.Create(new ComponentId("bundle/xor-delay"), [lane.Id]);
        Require(session.TryAddCableBundle(bundle, [lane], out var cableDiagnostic), cableDiagnostic,
            "Could not connect the delayed cable bundle.");
        Require(session.CommitStaged(out commitDiagnostic), commitDiagnostic,
            "Could not commit the delayed cable bundle.");

        var graphStartTick = session.CurrentTick;
        var placedChipTrace = ImmutableArray.CreateBuilder<LogicValue>(plan.Microticks);
        var sourceTrace = ImmutableArray.CreateBuilder<LogicValue>(plan.Microticks);
        session.SetPaused(false);
        for (var tick = 0; tick < plan.Microticks + lane.Latency; tick++)
        {
            if (inputsByTick.TryGetValue(tick, out var inputs))
            {
                ApplyChipInputs(session, chipInstanceId, inputs);
                ApplyInputs(session, panelDevice.Id, inputs);
            }

            _ = session.StepMicrotick();
            if (tick < plan.Microticks)
            {
                placedChipTrace.Add(session.GetChipOutput(chipInstanceId, outputName));
                sourceTrace.Add(session.GetDeviceOutput(panelDevice.Id, outputName).Bits[0]);
            }
        }

        var expectedTransitions = new List<(long Tick, LogicValue Value)>
        {
            (graphStartTick + lane.Latency, LogicValue.HighImpedance)
        };
        var prior = LogicValue.HighImpedance;
        for (var tick = 0; tick < sourceTrace.Count; tick++)
        {
            if (sourceTrace[tick] != prior)
            {
                expectedTransitions.Add((graphStartTick + tick + lane.Latency, sourceTrace[tick]));
                prior = sourceTrace[tick];
            }
        }

        var history = session.GetCableLaneHistory(lane.Id);
        var delayPassed = history.Select(item => (item.ScheduledTick, item.Signal.Bits[0]))
                                 .SequenceEqual(expectedTransitions) &&
                         history.All(item => item.ScheduledTick == item.DeliveredTick &&
                                             item.Status == CableTransitionStatus.Delivered);
        session.SetPaused(true);
        return new WorkbenchPracticeResult(
            session,
            packagedChip,
            outputTrace.ToImmutable(),
            placedChipTrace.ToImmutable(),
            history,
            xorPassed && delayPassed &&
            placedChipTrace.SequenceEqual(expectedRaw) && placedChipTrace.SequenceEqual(sourceTrace));
    }

    private static void ApplyInputs(PanelWorkbenchSession session, IEnumerable<PanelInputChange> inputs)
    {
        foreach (var input in inputs)
        {
            Require(session.TryDriveInput(new PortId(input.PortId), Enum.Parse<LogicValue>(input.Value),
                out var diagnostic), diagnostic, "Could not drive an XOR input.");
        }
    }

    private static void ApplyChipInputs(
        PanelWorkbenchSession session,
        ComponentId instanceId,
        IEnumerable<PanelInputChange> inputs)
    {
        foreach (var input in inputs)
        {
            Require(session.TryDriveChipInput(
                    instanceId,
                    input.PortId,
                    Enum.Parse<LogicValue>(input.Value),
                    out var diagnostic),
                diagnostic,
                "Could not drive a placed XOR chip input.");
        }
    }

    private static void ApplyInputs(
        PanelWorkbenchSession session,
        ComponentId deviceId,
        IEnumerable<PanelInputChange> inputs)
    {
        foreach (var input in inputs)
        {
            Require(session.TryDriveDeviceInput(
                    deviceId,
                    input.PortId,
                    DeviceSignal.Scalar(Enum.Parse<LogicValue>(input.Value)),
                    out var diagnostic),
                diagnostic,
                "Could not drive an XOR device input.");
        }
    }

    private static GridCoordinate FindFreeLocation(WorkbenchDefinition definition)
    {
        for (var y = 0; y < definition.Panel.Height; y++)
        {
            for (var x = 0; x < definition.Panel.Width; x++)
            {
                var location = new GridCoordinate(x, y);
                if (definition.Panel.GetCell(location) is null &&
                    !definition.ChipPlacements.Any(item => item.Location == location) &&
                    !definition.DevicePlacements.Any(item => item.Location == location))
                {
                    return location;
                }
            }
        }

        throw new InvalidOperationException("The XOR practice panel has no free placement location.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Require(bool condition, WorkbenchDiagnostic? diagnostic, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(diagnostic is null
                ? message
                : $"{diagnostic.Code}: {diagnostic.Message}");
        }
    }
}
