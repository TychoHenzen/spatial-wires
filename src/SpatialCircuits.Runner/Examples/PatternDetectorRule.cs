using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.Runner;

internal sealed class PatternDetectorRule : ICustomCellRule
{
    private static readonly BehaviorId PreviousBehaviorId =
        new("spatial-wires.examples:pattern-detector/v1");

    private static readonly BehaviorId CurrentBehaviorId =
        new("spatial-wires.examples:pattern-detector/v2");

    private static readonly ImmutableArray<CustomCellPort> RulePorts =
    [
        new CustomCellPort("in", CardinalDirection.West, true, false),
        new CustomCellPort("out", CardinalDirection.East, false, true)
    ];

    internal static CustomCellRuleRegistration Registration { get; } = new(
        CurrentBehaviorId,
        RulePorts,
        ValidateParameters,
        static () => new PatternDetectorRule());

    private static void ValidateParameters(ImmutableSortedDictionary<string, string> parameters)
    {
        if (parameters.Count != 1 ||
            !parameters.TryGetValue("pattern", out var pattern) ||
            pattern.Length != 2 ||
            pattern.Any(bit => bit is not ('0' or '1')))
        {
            throw new ArgumentException("Pattern detector requires a two-bit pattern.", nameof(parameters));
        }
    }

    ImmutableArray<byte> ICustomCellRule.CreateInitialState(ImmutableSortedDictionary<string, string> parameters) =>
        [2, 0];

    void ICustomCellRule.ValidateState(
        ImmutableSortedDictionary<string, string> parameters,
        ImmutableArray<byte> state)
    {
        ValidateParameters(parameters);
        if (state.IsDefault || state.Length != 2 || state[0] != 2 || state[1] > 1)
        {
            throw new ArgumentException("Pattern detector state is invalid.", nameof(state));
        }
    }

    CustomCellTransition ICustomCellRule.Evaluate(CustomCellEvaluationContext context)
    {
        var pattern = context.Parameters["pattern"];
        var matched = context.State[1];
        var output = LogicValue.Low;
        var input = context.Inputs["in"];
        if (input is LogicValue.Low or LogicValue.High)
        {
            var bit = input == LogicValue.High ? '1' : '0';
            if (matched == 0)
            {
                matched = bit == pattern[0] ? (byte)1 : (byte)0;
            }
            else if (bit == pattern[1])
            {
                output = LogicValue.High;
                matched = pattern[0] == pattern[1] ? (byte)1 : (byte)0;
            }
            else
            {
                matched = bit == pattern[0] ? (byte)1 : (byte)0;
            }
        }
        else
        {
            matched = 0;
        }

        return new CustomCellTransition(
            [new CustomCellProposal("out", output)],
            [2, matched],
            context.RandomState);
    }

    bool ICustomCellRule.TryMigrateState(
        BehaviorId sourceBehaviorId,
        ImmutableArray<byte> sourceState,
        out ImmutableArray<byte> migratedState)
    {
        if (sourceBehaviorId == PreviousBehaviorId &&
            !sourceState.IsDefault &&
            sourceState.Length == 1 &&
            sourceState[0] <= 1)
        {
            migratedState = [2, sourceState[0]];
            return true;
        }

        migratedState = default;
        return false;
    }
}
