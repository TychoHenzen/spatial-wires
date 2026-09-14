using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.Runner;

public static class PanelScenarioExecutor
{
    private static readonly CustomCellRuleRegistry CustomCellRules =
        new([PatternDetectorRule.Registration]);

    public static IReadOnlyList<PanelTraceRecord> Execute(RunnerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var plan = fixture.PanelScenario
            ?? throw new ArgumentException("Fixture does not contain a panel scenario.", nameof(fixture));
        var runtime = new PanelRuntimeInstance(plan.CreatePanelDefinition(), CustomCellRules);
        var inputsByTick = plan.Inputs
            .GroupBy(input => input.Tick)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var expectedByTick = plan.Expectations
            .GroupBy(expectation => expectation.Tick)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var records = new List<PanelTraceRecord>(plan.Microticks);

        for (var tick = 0; tick < plan.Microticks; tick++)
        {
            if (inputsByTick.TryGetValue(tick, out var inputs))
            {
                foreach (var input in inputs)
                {
                    runtime.SetInput(new PortId(input.PortId), FixtureLogicValue.Parse(input.Value));
                }
            }

            var result = runtime.Step();
            var probes = result.ProbeSamples.ToImmutableSortedDictionary(
                sample => sample.ProbeId.Value,
                sample => sample.Value.ToString(),
                StringComparer.Ordinal);
            var expected = expectedByTick[tick].ToImmutableSortedDictionary(
                sample => sample.ProbeId,
                sample => sample.Value,
                StringComparer.Ordinal);
            var passed = probes.Count == expected.Count && probes.SequenceEqual(expected);
            records.Add(new PanelTraceRecord(
                fixture.TraceSchema,
                fixture.FixtureId,
                tick,
                checked((int)result.Tick),
                result.Hash,
                probes,
                expected,
                result.Outputs,
                passed));
        }

        return records.AsReadOnly();
    }
}
