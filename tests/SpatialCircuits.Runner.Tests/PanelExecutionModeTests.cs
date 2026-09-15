using System.Collections.Immutable;
using SpatialCircuits.Cells;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class PanelExecutionModeTests
{
    [Fact]
    public void OptimizedExecutionMatchesReferenceAcrossQuietAndTemporalTicks()
    {
        var plan = new PanelScenarioPlan(
            "benchmarks/temporal-frontier",
            12,
            2,
            32,
            [
                new PanelCellPlan("input", 0, 0, "input-port", "east", "drive", []),
                new PanelCellPlan("wire", 1, 0, "wire", "east", null, []),
                new PanelCellPlan("nand", 2, 0, "nand", "east", null,
                    [new KeyValuePair<string, string>("delay", "2")]),
                new PanelCellPlan("filter", 3, 0, "stability-filter", "east", null,
                    [new KeyValuePair<string, string>("consecutive-ticks", "3")]),
                new PanelCellPlan("clock", 8, 0, "clock", "east", null,
                    [
                        new KeyValuePair<string, string>("high-ticks", "2"),
                        new KeyValuePair<string, string>("low-ticks", "5")
                    ])
            ],
            [new PanelInputChange(0, "drive", "High"), new PanelInputChange(16, "drive", "Low")],
            ImmutableArray<PanelProbeExpectation>.Empty);

        var comparison = PanelExecutionComparisonRunner.Compare(plan);

        Assert.True(comparison.Equivalent, comparison.Divergence?.ToString());
        Assert.Equal(PanelExecutionMode.Reference, comparison.Reference.Mode);
        Assert.Equal(PanelExecutionMode.Optimized, comparison.Optimized.Mode);
        Assert.Equal(32, comparison.Reference.Samples.Length);
        Assert.Equal(
            comparison.Reference.Samples.Select(sample => (sample.Tick, sample.Hash)).ToArray(),
            comparison.Optimized.Samples.Select(sample => (sample.Tick, sample.Hash)).ToArray());
        Assert.All(comparison.Reference.Samples.Zip(comparison.Optimized.Samples), pair =>
            Assert.True(pair.First.Outputs.SequenceEqual(pair.Second.Outputs)));
    }
}
