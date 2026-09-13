using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Cells.Tests;

public sealed class PanelRuntimeTests
{
    [Fact]
    public void OrthogonalHopsDelayBothEdgesAndProbesRecordTicks()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/route"),
            5,
            3,
            [
                PortCell("source", 0, 1, CellKind.InputPort, CardinalDirection.East, "signal"),
                Cell("wire-one", 1, 1, CellKind.Wire, CardinalDirection.East),
                Cell("junction", 2, 1, CellKind.Junction),
                Cell("wire-two", 3, 1, CellKind.Wire, CardinalDirection.East),
                PortCell("sink", 4, 1, CellKind.OutputPort, CardinalDirection.East, "result"),
                Cell("probe", 2, 2, CellKind.Probe, CardinalDirection.South)
            ]);
        var runtime = new PanelRuntimeInstance(panel);

        runtime.SetInput(new PortId("signal"), LogicValue.Low);
        for (var tick = 0; tick < 5; tick++)
        {
            runtime.Step();
            var expected = tick < 4 ? LogicValue.HighImpedance : LogicValue.Low;
            Assert.True(
                runtime.GetOutput(new PortId("result")) == expected,
                $"Tick {tick}: {string.Join(',', runtime.ProbeHistory.Select(sample => $"{sample.Tick}={sample.Value}"))}");
        }

        Assert.Contains(runtime.ProbeHistory, sample =>
            sample.ProbeId.Value == "probe" && sample.Tick == 3 && sample.Value == LogicValue.Low);

        runtime.SetInput(new PortId("signal"), LogicValue.High);
        for (var tick = 5; tick <= 9; tick++)
        {
            runtime.Step();
            Assert.Equal(tick < 9 ? LogicValue.Low : LogicValue.High,
                runtime.GetOutput(new PortId("result")));
        }

        runtime.SetInput(new PortId("signal"), LogicValue.Low);
        for (var tick = 10; tick <= 14; tick++)
        {
            runtime.Step();
            Assert.Equal(tick < 14 ? LogicValue.High : LogicValue.Low,
                runtime.GetOutput(new PortId("result")));
        }
    }

    [Fact]
    public void CrossingKeepsOrthogonalLanesSeparate()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/crossing"),
            3,
            3,
            [
                PortCell("west-source", 0, 1, CellKind.InputPort, CardinalDirection.East, "horizontal"),
                PortCell("north-source", 1, 0, CellKind.InputPort, CardinalDirection.South, "vertical"),
                Cell("crossing", 1, 1, CellKind.Crossing),
                Cell("east-probe", 2, 1, CellKind.Probe, CardinalDirection.East),
                Cell("south-probe", 1, 2, CellKind.Probe, CardinalDirection.South)
            ]);
        var runtime = new PanelRuntimeInstance(panel);

        runtime.SetInput(new PortId("horizontal"), LogicValue.High);
        runtime.SetInput(new PortId("vertical"), LogicValue.Low);
        PanelTickResult result = default!;
        for (var tick = 0; tick < 3; tick++)
        {
            result = runtime.Step();
        }

        Assert.Equal(
            new[]
            {
                new ProbeSample(new ComponentId("east-probe"), 2, LogicValue.High),
                new ProbeSample(new ComponentId("south-probe"), 2, LogicValue.Low)
            },
            result.ProbeSamples);
    }

    [Theory]
    [InlineData(LogicValue.Low)]
    [InlineData(LogicValue.High)]
    [InlineData(LogicValue.Unknown)]
    [InlineData(LogicValue.HighImpedance)]
    public void ConstantDrivesEachFourStateValue(LogicValue value)
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/constant"),
            2,
            1,
            [
                Cell("constant", 0, 0, CellKind.Constant, ("value", value.ToString())),
                Cell("probe", 1, 0, CellKind.Probe, CardinalDirection.East)
            ]);
        var runtime = new PanelRuntimeInstance(panel);

        for (var tick = 0; tick < 4; tick++)
        {
            runtime.Step();
        }

        Assert.Equal(value, runtime.ProbeHistory[^1].Value);
    }

    [Fact]
    public void TwoRuntimeInstancesDoNotShareMutableState()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/instances"),
            2,
            1,
            [
                PortCell("source", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
                PortCell("sink", 1, 0, CellKind.OutputPort, CardinalDirection.East, "result")
            ]);
        var first = new PanelRuntimeInstance(panel);
        var second = new PanelRuntimeInstance(panel);

        first.SetInput(new PortId("signal"), LogicValue.High);
        first.Step();
        second.Step();
        first.Step();

        Assert.Equal(LogicValue.High, first.GetOutput(new PortId("result")));
        Assert.Equal(LogicValue.HighImpedance, second.GetOutput(new PortId("result")));
        Assert.Empty(second.ProbeHistory);
    }

    [Theory]
    [InlineData(LogicValue.Low, LogicValue.HighImpedance, LogicValue.High)]
    [InlineData(LogicValue.High, LogicValue.High, LogicValue.Low)]
    [InlineData(LogicValue.Unknown, LogicValue.High, LogicValue.Unknown)]
    [InlineData(LogicValue.HighImpedance, LogicValue.High, LogicValue.Unknown)]
    public void NandUsesTheFourStateTruthTable(
        LogicValue first,
        LogicValue second,
        LogicValue expected)
    {
        var runtime = CreateNandRuntime(delay: 1);
        runtime.SetInput(new PortId("a"), first);
        runtime.SetInput(new PortId("b"), second);

        for (var tick = 0; tick < 7; tick++)
        {
            runtime.Step();
        }

        Assert.Equal(expected, runtime.ProbeHistory.Last(sample => sample.ProbeId.Value == "nand-probe").Value);
    }

    [Fact]
    public void NandCommitsBeforeInputsChangingAtItsDeadline()
    {
        var runtime = CreateNandRuntime(delay: 1);
        runtime.SetInput(new PortId("a"), LogicValue.Low);
        runtime.SetInput(new PortId("b"), LogicValue.Low);
        StepThrough(runtime, 6);

        var pulseStart = runtime.CurrentTick;
        runtime.SetInput(new PortId("a"), LogicValue.High);
        runtime.SetInput(new PortId("b"), LogicValue.High);
        runtime.Step();
        runtime.SetInput(new PortId("a"), LogicValue.Low);
        runtime.SetInput(new PortId("b"), LogicValue.Low);
        StepThrough(runtime, 4);

        var lowSamples = runtime.ProbeHistory
            .Where(sample => sample.Tick >= pulseStart && sample.Value == LogicValue.Low)
            .ToArray();
        Assert.Single(lowSamples);
    }

    [Fact]
    public void NandCancelsAndRestartsItsInertialDeadline()
    {
        var runtime = CreateNandRuntime(delay: 2);
        runtime.SetInput(new PortId("a"), LogicValue.High);
        runtime.SetInput(new PortId("b"), LogicValue.High);
        var early = new List<ProbeSample>();
        for (var tick = 0; tick < 5; tick++)
        {
            early.AddRange(runtime.Step().ProbeSamples);
        }

        Assert.Equal(LogicValue.Low, early[^1].Value);

        runtime.SetInput(new PortId("a"), LogicValue.Low);
        runtime.Step();
        runtime.SetInput(new PortId("a"), LogicValue.High);
        var later = new List<PanelTickResult>();
        for (var tick = 0; tick < 6; tick++)
        {
            later.Add(runtime.Step());
        }

        Assert.All(later.SelectMany(result => result.ProbeSamples), sample =>
            Assert.Equal(LogicValue.Low, sample.Value));
    }

    [Fact]
    public void ClockStartsLowThenAlternatesByItsConfiguredDurations()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/clock"),
            2,
            1,
            [
                Cell("clock", 0, 0, CellKind.Clock, CardinalDirection.East,
                    ("high-ticks", "2"), ("low-ticks", "1")),
                Cell("probe", 1, 0, CellKind.Probe, CardinalDirection.East)
            ]);
        var runtime = new PanelRuntimeInstance(panel);

        var observed = Enumerable.Range(0, 5)
            .Select(_ => runtime.Step().ProbeSamples.Single().Value)
            .ToArray();

        Assert.Equal(
            new[]
            {
                LogicValue.HighImpedance,
                LogicValue.Low,
                LogicValue.High,
                LogicValue.High,
                LogicValue.Low
            },
            observed);
    }

    [Fact]
    public void StabilityFilterRejectsShortPulsesAndAcceptsAtItsThreshold()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/filter"),
            3,
            1,
            [
                PortCell("source", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
                Cell("filter", 1, 0, CellKind.StabilityFilter, CardinalDirection.East,
                    ("consecutive-ticks", "2")),
                Cell("probe", 2, 0, CellKind.Probe, CardinalDirection.East)
            ]);
        var runtime = new PanelRuntimeInstance(panel);

        runtime.SetInput(new PortId("signal"), LogicValue.Low);
        StepThrough(runtime, 5);
        Assert.Equal(LogicValue.Low, LastProbe(runtime));

        runtime.SetInput(new PortId("signal"), LogicValue.High);
        runtime.Step();
        runtime.SetInput(new PortId("signal"), LogicValue.Low);
        StepThrough(runtime, 4);
        Assert.Equal(LogicValue.Low, LastProbe(runtime));

        runtime.SetInput(new PortId("signal"), LogicValue.High);
        runtime.Step();
        runtime.Step();
        runtime.SetInput(new PortId("signal"), LogicValue.Low);
        StepThrough(runtime, 3);
        Assert.Contains(runtime.ProbeHistory, sample => sample.Value == LogicValue.High);

        AssertAccepted(LogicValue.Unknown, LogicValue.Unknown, runtime);
        AssertAccepted(LogicValue.HighImpedance, LogicValue.HighImpedance, runtime);
        AssertAccepted(LogicValue.High, LogicValue.High, runtime);
    }

    [Fact]
    public void DFlipFlopReportsUnknownWhenDataAndRisingClockArriveTogether()
    {
        var runtime = CreateFlipFlopRuntime();
        runtime.SetInput(new PortId("data"), LogicValue.Low);
        runtime.SetInput(new PortId("clock"), LogicValue.Low);
        StepThrough(runtime, 3);

        runtime.SetInput(new PortId("data"), LogicValue.High);
        runtime.SetInput(new PortId("clock"), LogicValue.High);
        StepThrough(runtime, 4);

        Assert.Equal(LogicValue.Unknown, LastProbe(runtime));
    }

    [Fact]
    public void DFlipFlopUsesItsDefaultSetupAndClockToOutputTiming()
    {
        var runtime = CreateFlipFlopRuntime();
        runtime.SetInput(new PortId("data"), LogicValue.Low);
        runtime.SetInput(new PortId("clock"), LogicValue.Low);
        StepThrough(runtime, 3);

        runtime.SetInput(new PortId("data"), LogicValue.High);
        runtime.Step();
        runtime.SetInput(new PortId("clock"), LogicValue.High);
        StepThrough(runtime, 5);

        Assert.Equal(LogicValue.High, LastProbe(runtime));
    }

    [Fact]
    public void ReplacementInvalidatesOldCellEventsAndReleasesItsDrive()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/replacement"),
            2,
            1,
            [
                PortCell("source", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
                Cell("probe", 1, 0, CellKind.Probe, CardinalDirection.East)
            ]);
        var runtime = new PanelRuntimeInstance(panel);
        runtime.SetInput(new PortId("signal"), LogicValue.High);
        runtime.Step();
        runtime.ReplaceCell(PortCell(
            "source", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"));
        var staleStep = runtime.Step();

        Assert.Equal(LogicValue.HighImpedance, staleStep.ProbeSamples.Single().Value);
        Assert.Contains(staleStep.Diagnostics, diagnostic =>
            diagnostic.Code == SchedulerDiagnosticCodes.StaleEvent);

        runtime.SetInput(new PortId("signal"), LogicValue.High);
        runtime.Step();
        runtime.Step();
        Assert.Equal(LogicValue.High, LastProbe(runtime));
        runtime.ReplaceCell(PortCell(
            "source", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"));
        var releaseStep = runtime.Step();

        Assert.Equal(LogicValue.HighImpedance, releaseStep.ProbeSamples.Single().Value);
    }

    [Fact]
    public void RemovingACellStopsFutureStepsFromDrivingItsOldNeighbors()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/removal"),
            2,
            1,
            [
                PortCell("source", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
                Cell("probe", 1, 0, CellKind.Probe, CardinalDirection.East)
            ]);
        var runtime = new PanelRuntimeInstance(panel);
        runtime.SetInput(new PortId("signal"), LogicValue.High);
        StepThrough(runtime, 2);

        Assert.True(runtime.RemoveCell(new ComponentId("source")));
        var result = runtime.Step();

        Assert.Equal(LogicValue.HighImpedance, result.ProbeSamples.Single().Value);
        Assert.False(runtime.RemoveCell(new ComponentId("source")));
    }

    private static PanelRuntimeInstance CreateNandRuntime(int delay)
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/nand"),
            3,
            3,
            [
                PortCell("a-source", 1, 0, CellKind.InputPort, CardinalDirection.South, "a"),
                PortCell("b-source", 1, 2, CellKind.InputPort, CardinalDirection.North, "b"),
                Cell("nand", 1, 1, CellKind.Nand, CardinalDirection.East, ("delay", delay.ToString())),
                Cell("nand-probe", 2, 1, CellKind.Probe, CardinalDirection.East)
            ]);
        return new PanelRuntimeInstance(panel);
    }

    private static PanelRuntimeInstance CreateFlipFlopRuntime()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/dff"),
            4,
            4,
            [
                PortCell("data-source", 1, 2, CellKind.InputPort, CardinalDirection.East, "data"),
                PortCell("clock-source", 2, 3, CellKind.InputPort, CardinalDirection.North, "clock"),
                Cell("dff", 2, 2, CellKind.DFlipFlop, CardinalDirection.East),
                Cell("dff-probe", 3, 2, CellKind.Probe, CardinalDirection.East)
            ]);
        return new PanelRuntimeInstance(panel);
    }

    private static void AssertAccepted(
        LogicValue input,
        LogicValue expected,
        PanelRuntimeInstance runtime)
    {
        var current = runtime.CurrentTick;
        runtime.SetInput(new PortId("signal"), input);
        StepThrough(runtime, 5);

        var samples = runtime.ProbeHistory.Where(sample => sample.Tick >= current).ToArray();
        Assert.Contains(samples, sample => sample.Value == expected);
        Assert.Equal(expected, LastProbe(runtime));
    }

    private static LogicValue LastProbe(PanelRuntimeInstance runtime) =>
        runtime.ProbeHistory.Last(sample => sample.ProbeId.Value is "probe" or "nand-probe" or "dff-probe").Value;

    private static void StepThrough(PanelRuntimeInstance runtime, int count)
    {
        for (var tick = 0; tick < count; tick++)
        {
            runtime.Step();
        }
    }

    private static PanelCellDefinition PortCell(
        string id,
        int x,
        int y,
        CellKind kind,
        CardinalDirection orientation,
        string portId) =>
        PanelCellDefinition.Create(
            new ComponentId(id),
            new GridCoordinate(x, y),
            kind,
            orientation,
            new PortId(portId));

    private static PanelCellDefinition Cell(
        string id,
        int x,
        int y,
        CellKind kind,
        params (string Name, string Value)[] parameters) =>
        Cell(id, x, y, kind, CardinalDirection.East, parameters);

    private static PanelCellDefinition Cell(
        string id,
        int x,
        int y,
        CellKind kind,
        CardinalDirection orientation,
        params (string Name, string Value)[] parameters) =>
        PanelCellDefinition.Create(
            new ComponentId(id),
            new GridCoordinate(x, y),
            kind,
            orientation,
            parameters: parameters.Select(parameter =>
                new KeyValuePair<string, string>(parameter.Name, parameter.Value)));
}
