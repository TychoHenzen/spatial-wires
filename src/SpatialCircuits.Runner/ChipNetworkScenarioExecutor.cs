using System.Collections.Immutable;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Runner;

public sealed record ChipScenarioTraceRecord(
    TraceVersion TraceSchema,
    string FixtureId,
    int Sequence,
    int Microtick,
    string ChipInstanceId,
    string DefinitionId,
    string ContentHash,
    string Hash,
    ImmutableSortedDictionary<string, string> Outputs,
    ImmutableSortedDictionary<string, string> ExpectedOutputs,
    string? ExpectedHash,
    bool Passed);

public static class ChipNetworkScenarioExecutor
{
    public static IReadOnlyList<ChipScenarioTraceRecord> Execute(RunnerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var plan = fixture.ChipNetworkScenario
            ?? throw new ArgumentException("Fixture does not contain a chip-network scenario.", nameof(fixture));
        var catalog = plan.CreateCatalog();
        var network = new PanelOwnedChipNetworkInstance(
            plan.ParentPanel.CreatePanelDefinition(),
            plan.CreateNetworkDefinition(),
            catalog);
        var inputsByTick = plan.Inputs
            .GroupBy(input => input.Tick)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(input => input.InstanceId, StringComparer.Ordinal)
                .ThenBy(input => input.PortName, StringComparer.Ordinal)
                .ToArray());
        var expectedByTickAndInstance = plan.Expectations.ToDictionary(
            expectation => (expectation.Tick, expectation.InstanceId));
        var records = new List<ChipScenarioTraceRecord>();

        for (var tick = 0; tick < plan.Microticks; tick++)
        {
            if (inputsByTick.TryGetValue(tick, out var inputs))
            {
                foreach (var input in inputs)
                {
                    network.GetInstance(new ComponentId(input.InstanceId))
                        .SetInput(input.PortName, FixtureLogicValue.Parse(input.Value));
                }
            }

            var result = network.Step();
            foreach (var chip in result.Chips)
            {
                expectedByTickAndInstance.TryGetValue((tick, chip.InstanceId.Value), out var expected);
                var expectedOutputs = expected?.Outputs ?? ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);
                var passed = expected is null ||
                    (chip.Outputs.SequenceEqual(expected.Outputs) &&
                     string.Equals(chip.Hash, expected.Hash, StringComparison.Ordinal));
                records.Add(new ChipScenarioTraceRecord(
                    fixture.TraceSchema,
                    fixture.FixtureId,
                    records.Count,
                    tick,
                    chip.InstanceId.Value,
                    chip.DefinitionId.Value,
                    chip.ContentHash,
                    chip.Hash,
                    chip.Outputs,
                    expectedOutputs,
                    expected?.Hash,
                    passed));
            }
        }

        return records.AsReadOnly();
    }
}
