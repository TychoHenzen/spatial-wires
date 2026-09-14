using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.TestFixtures;

public sealed class TransitiveGodotRule : ICustomCellRule
{
    public static void ValidateParameters(ImmutableSortedDictionary<string, string> parameters) =>
        _ = GodotReferenceHelper.GodotTypeName;

    public ImmutableArray<byte> CreateInitialState(ImmutableSortedDictionary<string, string> parameters) => [];

    public void ValidateState(
        ImmutableSortedDictionary<string, string> parameters,
        ImmutableArray<byte> state)
    {
    }

    public CustomCellTransition Evaluate(CustomCellEvaluationContext context) =>
        new([], context.State, context.RandomState);

    public bool TryMigrateState(
        BehaviorId sourceBehaviorId,
        ImmutableArray<byte> sourceState,
        out ImmutableArray<byte> migratedState)
    {
        migratedState = sourceState;
        return true;
    }
}
