using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Runner;
using SpatialCircuits.Workbench;
using Xunit;

namespace SpatialCircuits.Workbench.Tests;

public sealed class XorDelayPracticeTests
{
    [Fact]
    public void PublicActionsBuildTheXorObserveItsTracePackageItAndDeliverItAcrossADelayedBundle()
    {
        var fixture = ReadXorFixture();
        var plan = fixture.PanelScenario!;
        var panel = AddOutputPort(plan.CreatePanelDefinition());
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            panel.Id,
            panel.Width,
            panel.Height,
            []));

        foreach (var cell in panel.Cells.OfType<PanelCellDefinition>())
        {
            Assert.True(session.TryPaint(cell, out var diagnostic), diagnostic?.Message);
        }

        ApplyInputs(session, plan.Inputs.Where(input => input.Tick == 0));
        Assert.True(session.CommitStaged(out var commitDiagnostic), commitDiagnostic?.Message);
        session.SetPaused(false);

        var panelOutputs = new List<string>();
        var filteredProbe = new List<string>();
        var inputsByTick = plan.Inputs.GroupBy(input => input.Tick)
            .ToDictionary(group => group.Key, group => group.ToArray());
        for (var tick = 0; tick < plan.Microticks; tick++)
        {
            if (inputsByTick.TryGetValue(tick, out var inputs) && tick != 0)
            {
                ApplyInputs(session, inputs);
            }

            var result = session.StepMicrotick();
            panelOutputs.Add(result.Outputs["xor"]);
            filteredProbe.Add(result.ProbeSamples.Single(sample => sample.ProbeId.Value == "xor-filtered-probe")
                .Value.ToString());
        }

        var expectedRaw = plan.Expectations
            .Where(expectation => expectation.ProbeId == "xor-raw-probe")
            .Select(expectation => expectation.Value)
            .ToArray();
        var expectedFiltered = plan.Expectations
            .Where(expectation => expectation.ProbeId == "xor-filtered-probe")
            .Select(expectation => expectation.Value)
            .ToArray();
        Assert.Equal(expectedRaw, panelOutputs);
        Assert.Equal(expectedFiltered, filteredProbe);
        Assert.Contains("High", panelOutputs);
        Assert.Contains("Low", panelOutputs);

        session.SetPaused(true);
        Assert.True(session.TryPackagePanelAsChip(
            new DefinitionId("chip/xor-practice"), "xor", out var chip, out var packageDiagnostic),
            packageDiagnostic?.Message);
        Assert.NotNull(chip);
        var chipLocation = FindEmpty(session.Definition);
        var chipInstanceId = new ComponentId("chip/xor-instance");
        Assert.True(session.TryPlaceChip(
            chip!, chipInstanceId, chipLocation, out var chipDiagnostic),
            chipDiagnostic?.Message);

        var panelDevice = DeviceDefinition.Create(
            new ComponentId("device/xor-panel"),
            PanelDeviceBackendDefinition.Create(chip!.SourcePanel));
        var delayedDevice = DeviceDefinition.Create(
            new ComponentId("device/delay"),
            TimedDeviceBackendDefinition.Create(
                [
                    DevicePortDefinition.Create("in", DevicePortDirection.Input),
                    DevicePortDefinition.Create("out", DevicePortDirection.Output)
                ],
                [new TimedOutputChange(1, "out", DeviceSignal.Scalar(LogicValue.Low))]));
        Assert.True(session.TryPlaceDevice(panelDevice, FindEmpty(session.Definition), out var panelDeviceDiagnostic),
            panelDeviceDiagnostic?.Message);
        Assert.True(session.TryPlaceDevice(delayedDevice, FindEmpty(session.Definition), out var delayDiagnostic),
            delayDiagnostic?.Message);

        var outputName = panelDevice.Ports.Single(port => port.Direction == DevicePortDirection.Output).Name;
        var lane = CableLaneDefinition.Create(
            new ComponentId("lane/xor-delay"),
            DevicePortEndpoint.Create(panelDevice.Id, outputName),
            DevicePortEndpoint.Create(delayedDevice.Id, "in"),
            latency: 2);
        var bundle = CableBundleDefinition.Create(new ComponentId("bundle/xor-delay"), [lane.Id]);
        Assert.True(session.TryAddCableBundle(bundle, [lane], out var cableDiagnostic), cableDiagnostic?.Message);
        Assert.True(session.CommitStaged(out commitDiagnostic), commitDiagnostic?.Message);

        var graphStartTick = session.CurrentTick;
        var sourceTrace = new List<LogicValue>();
        var chipTrace = new List<LogicValue>();
        var secondChipId = new ComponentId("chip/xor-second-instance");
        session.SetPaused(false);
        for (var tick = 0; tick < plan.Microticks + lane.Latency; tick++)
        {
            if (tick == 10)
            {
                Assert.True(session.TryPlaceChip(
                    chip!,
                    secondChipId,
                    FindEmpty(session.Definition),
                    out var placementDiagnostic),
                    placementDiagnostic?.Message);
            }

            if (inputsByTick.TryGetValue(tick, out var inputs))
            {
                foreach (var input in inputs)
                {
                    Assert.True(session.TryDriveChipInput(
                        chipInstanceId,
                        input.PortId,
                        ParseLogic(input.Value),
                        out var chipInputDiagnostic),
                        chipInputDiagnostic?.Message);
                    Assert.True(session.TryDriveDeviceInput(
                        panelDevice.Id,
                        input.PortId,
                        DeviceSignal.Scalar(ParseLogic(input.Value)),
                        out var inputDiagnostic),
                        inputDiagnostic?.Message);
                }
            }

            session.StepMicrotick();
            if (tick < plan.Microticks)
            {
                chipTrace.Add(session.GetChipOutput(chipInstanceId, outputName));
                sourceTrace.Add(session.GetDeviceOutput(panelDevice.Id, outputName).Bits[0]);
            }
        }

        Assert.Equal(expectedRaw, sourceTrace.Select(value => value.ToString()));
        Assert.Equal(expectedRaw, chipTrace.Select(value => value.ToString()));
        Assert.Equal(chipTrace, sourceTrace);
        Assert.Equal(2, session.CommittedDefinition.ChipNetwork.Instances.Length);
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
        Assert.Equal(expectedTransitions,
            history.Select(item => (item.ScheduledTick, item.Signal.Bits[0])));
        Assert.All(history, item =>
        {
            Assert.Equal(item.ScheduledTick, item.DeliveredTick);
            Assert.Equal(CableTransitionStatus.Delivered, item.Status);
        });
    }

    private static RunnerFixture ReadXorFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "xor-glitch.fixture.json");
        var read = FixtureCodec.Read(File.ReadAllBytes(path));
        return read.Fixture ?? throw new InvalidOperationException("XOR practice fixture is invalid.");
    }

    private static PanelDefinition AddOutputPort(PanelDefinition source)
    {
        var cells = source.Cells.OfType<PanelCellDefinition>()
            .Where(cell => cell.Id.Value != "xor-raw-probe")
            .Append(PanelCellDefinition.Create(
                new ComponentId("xor-output"),
                new GridCoordinate(15, 5),
                CellKind.OutputPort,
                CardinalDirection.East,
                new PortId("xor")));
        return PanelDefinition.Create(source.Id, source.Width, source.Height, cells);
    }

    private static void ApplyInputs(PanelWorkbenchSession session, IEnumerable<PanelInputChange> inputs)
    {
        foreach (var input in inputs)
        {
            Assert.True(session.TryDriveInput(
                new PortId(input.PortId), ParseLogic(input.Value), out var diagnostic),
                diagnostic?.Message);
        }
    }

    private static GridCoordinate FindEmpty(WorkbenchDefinition definition)
    {
        for (var y = 0; y < definition.Panel.Height; y++)
        {
            for (var x = 0; x < definition.Panel.Width; x++)
            {
                var location = new GridCoordinate(x, y);
                var occupied = definition.Panel.GetCell(location) is not null ||
                               definition.ChipPlacements.Any(item => item.Location == location) ||
                               definition.DevicePlacements.Any(item => item.Location == location);
                if (!occupied)
                {
                    return location;
                }
            }
        }

        throw new InvalidOperationException("XOR practice panel has no placement space.");
    }

    private static LogicValue ParseLogic(string value) => value switch
    {
        "Low" => LogicValue.Low,
        "High" => LogicValue.High,
        "Unknown" => LogicValue.Unknown,
        "HighImpedance" => LogicValue.HighImpedance,
        _ => throw new ArgumentException($"Unsupported fixture logic value '{value}'.", nameof(value))
    };
}
