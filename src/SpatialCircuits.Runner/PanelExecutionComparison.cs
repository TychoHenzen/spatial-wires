using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.Runner;

public sealed record PanelExecutionSample(
    int Sequence,
    long Tick,
    string Hash,
    ImmutableSortedDictionary<string, string> Outputs);

public sealed record PanelExecutionTrace(
    PanelExecutionMode Mode,
    ImmutableArray<PanelExecutionSample> Samples);

public sealed record PanelExecutionDivergence(
    int Sequence,
    string Path,
    string ReferenceValue,
    string OptimizedValue);

public sealed record PanelExecutionComparison(
    PanelExecutionTrace Reference,
    PanelExecutionTrace Optimized,
    PanelExecutionDivergence? Divergence)
{
    public bool Equivalent => Divergence is null;
}

public static class PanelExecutionComparisonRunner
{
    public static PanelExecutionTrace Execute(
        PanelScenarioPlan plan,
        PanelExecutionMode mode,
        CustomCellRuleRegistry? customCellRules = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var runtime = new PanelRuntimeInstance(
            plan.CreatePanelDefinition(),
            customCellRules,
            executionMode: mode);
        var inputsByTick = plan.Inputs
            .GroupBy(input => input.Tick)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var samples = ImmutableArray.CreateBuilder<PanelExecutionSample>(plan.Microticks);
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
            samples.Add(new PanelExecutionSample(samples.Count, result.Tick, result.Hash, result.Outputs));
        }

        return new PanelExecutionTrace(mode, samples.ToImmutable());
    }

    public static PanelExecutionComparison Compare(
        PanelScenarioPlan plan,
        CustomCellRuleRegistry? customCellRules = null)
    {
        var reference = Execute(plan, PanelExecutionMode.Reference, customCellRules);
        var optimized = Execute(plan, PanelExecutionMode.Optimized, customCellRules);
        return new PanelExecutionComparison(reference, optimized, FindDivergence(reference, optimized));
    }

    private static PanelExecutionDivergence? FindDivergence(
        PanelExecutionTrace reference,
        PanelExecutionTrace optimized)
    {
        if (reference.Samples.Length != optimized.Samples.Length)
        {
            return new PanelExecutionDivergence(
                Math.Min(reference.Samples.Length, optimized.Samples.Length),
                "samples.length",
                reference.Samples.Length.ToString(),
                optimized.Samples.Length.ToString());
        }

        for (var index = 0; index < reference.Samples.Length; index++)
        {
            var expected = reference.Samples[index];
            var actual = optimized.Samples[index];
            if (expected.Tick != actual.Tick)
            {
                return Divergence(index, "tick", expected.Tick.ToString(), actual.Tick.ToString());
            }

            if (!string.Equals(expected.Hash, actual.Hash, StringComparison.Ordinal))
            {
                return Divergence(index, "hash", expected.Hash, actual.Hash);
            }

            var keys = expected.Outputs.Keys
                .Concat(actual.Outputs.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal);
            foreach (var key in keys)
            {
                expected.Outputs.TryGetValue(key, out var expectedValue);
                actual.Outputs.TryGetValue(key, out var actualValue);
                if (!string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
                {
                    return Divergence(index, $"outputs.{key}", expectedValue ?? "<missing>", actualValue ?? "<missing>");
                }
            }
        }

        return null;
    }

    private static PanelExecutionDivergence Divergence(
        int sequence,
        string path,
        string referenceValue,
        string optimizedValue) =>
        new(sequence, $"samples[{sequence}].{path}", referenceValue, optimizedValue);
}
