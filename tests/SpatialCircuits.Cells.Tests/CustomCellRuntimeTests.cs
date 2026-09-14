using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Cells.Tests;

public sealed class CustomCellRuntimeTests
{
    private static readonly BehaviorId VersionOne = new("tests.custom:counter/v1");
    private static readonly BehaviorId VersionTwo = new("tests.custom:counter/v2");

    [Fact]
    public void CustomRuleStateAndRandomStateReplayWithMatchingTraceAndHash()
    {
        var definition = CreatePanel(VersionOne);
        var registry = new CustomCellRuleRegistry([CounterRegistration(VersionOne)]);
        var original = new PanelRuntimeInstance(definition, registry);
        original.SetInput(new PortId("signal"), LogicValue.High);
        _ = original.Step();
        var snapshot = original.CaptureSnapshot();

        original.SetInput(new PortId("signal"), LogicValue.Low);
        var expected = original.Step();

        var replay = new PanelRuntimeInstance(definition, registry);
        replay.RestoreSnapshot(snapshot);
        replay.SetInput(new PortId("signal"), LogicValue.Low);
        var actual = replay.Step();

        Assert.Equal(expected.Hash, actual.Hash);
        Assert.Equal(expected.ProbeSamples.ToArray(), actual.ProbeSamples.ToArray());
        Assert.Equal(
            original.CaptureSnapshot().Cells[1]!.CustomRandomState,
            replay.CaptureSnapshot().Cells[1]!.CustomRandomState);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void InvalidProposalsLeaveTickRuleStateAndRandomStateUnchanged(
        bool duplicateProposals,
        bool invalidOutputPort,
        bool nullOutputPort)
    {
        var definition = CreatePanel(VersionOne);
        var runtime = new PanelRuntimeInstance(
            definition,
            new CustomCellRuleRegistry(
                [CounterRegistration(
                    VersionOne,
                    duplicateProposals: duplicateProposals,
                    invalidOutputPort: invalidOutputPort,
                    nullOutputPort: nullOutputPort)]));
        runtime.SetInput(new PortId("signal"), LogicValue.High);
        var before = runtime.CaptureSnapshot();

        var exception = Assert.Throws<CustomCellRuleException>(() => runtime.Step());

        var after = runtime.CaptureSnapshot();
        Assert.Equal(CustomCellDiagnosticCodes.TransitionInvalid, exception.Code);
        Assert.Equal(before.Scheduler.CurrentTick, after.Scheduler.CurrentTick);
        Assert.Equal(before.Cells[1]!.CustomState.ToArray(), after.Cells[1]!.CustomState.ToArray());
        Assert.Equal(before.Cells[1]!.CustomRandomState, after.Cells[1]!.CustomRandomState);
        Assert.Equal(before.Cells[0]!.ExternalInput, after.Cells[0]!.ExternalInput);
        Assert.Equal(before.Cells[2]!.ObservedValue, after.Cells[2]!.ObservedValue);
    }

    [Fact]
    public void MissingRulesAndInvalidParametersHaveStableDiagnostics()
    {
        var missing = Assert.Throws<CustomCellRuleException>(() =>
            new PanelRuntimeInstance(CreatePanel(VersionOne)));
        Assert.Equal(CustomCellDiagnosticCodes.RegistrationMissing, missing.Code);

        var factoryCalls = 0;
        var invalid = Assert.Throws<CustomCellRuleException>(() =>
            new PanelRuntimeInstance(
                CreatePanelWithInvalidSecondCustomCell(),
                new CustomCellRuleRegistry(
                    [CounterRegistration(VersionOne, onCreate: () => factoryCalls++)])));
        Assert.Equal(CustomCellDiagnosticCodes.ParametersInvalid, invalid.Code);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void UnsupportedBehaviorIdentifierHasStableRegistrationDiagnostic()
    {
        var exception = Assert.Throws<CustomCellRuleException>(() =>
            new CustomCellRuleRegistry([CounterRegistration(new BehaviorId("CounterRule"))]));

        Assert.Equal(CustomCellDiagnosticCodes.RegistrationInvalid, exception.Code);
    }

    [Fact]
    public void CustomCellContractAssemblyHasNoGodotDependency()
    {
        Assert.DoesNotContain(
            typeof(ICustomCellRule).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.StartsWith("Godot", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void MigrationPublishesNewVersionWithoutChangingSourceSnapshot()
    {
        var oldRuntime = new PanelRuntimeInstance(
            CreatePanel(VersionOne),
            new CustomCellRuleRegistry([CounterRegistration(VersionOne)]));
        oldRuntime.SetInput(new PortId("signal"), LogicValue.High);
        _ = oldRuntime.Step();
        var source = oldRuntime.CaptureSnapshot();
        var sourceState = source.Cells[1]!.CustomState.ToArray();

        var current = new PanelRuntimeInstance(
            CreatePanel(VersionTwo),
            new CustomCellRuleRegistry([CounterRegistration(VersionTwo, migrateFromVersionOne: true)]));
        current.RestoreSnapshot(source);
        var migrated = current.CaptureSnapshot();

        Assert.Equal(VersionOne, source.Cells[1]!.BehaviorId);
        Assert.Equal(sourceState, source.Cells[1]!.CustomState.ToArray());
        Assert.Equal(VersionTwo, migrated.Cells[1]!.BehaviorId);
        Assert.Equal(new byte[] { 2, sourceState[0] }, migrated.Cells[1]!.CustomState.ToArray());

        var expected = current.Step();
        var replay = new PanelRuntimeInstance(
            CreatePanel(VersionTwo),
            new CustomCellRuleRegistry([CounterRegistration(VersionTwo, migrateFromVersionOne: true)]));
        replay.RestoreSnapshot(migrated);
        Assert.Equal(expected.Hash, replay.Step().Hash);
    }

    [Fact]
    public void UnavailableMigrationLeavesRuntimeAndSourceSnapshotReadable()
    {
        var oldRuntime = new PanelRuntimeInstance(
            CreatePanel(VersionOne),
            new CustomCellRuleRegistry([CounterRegistration(VersionOne)]));
        _ = oldRuntime.Step();
        var source = oldRuntime.CaptureSnapshot();

        var current = new PanelRuntimeInstance(
            CreatePanel(VersionTwo),
            new CustomCellRuleRegistry([CounterRegistration(VersionTwo)]));
        var before = current.CaptureSnapshot();
        var exception = Assert.Throws<CustomCellRuleException>(() => current.RestoreSnapshot(source));
        var after = current.CaptureSnapshot();

        Assert.Equal(CustomCellDiagnosticCodes.MigrationUnavailable, exception.Code);
        Assert.Equal(before.Scheduler.CurrentTick, after.Scheduler.CurrentTick);
        Assert.Equal(before.Cells[1]!.CustomState.ToArray(), after.Cells[1]!.CustomState.ToArray());
        Assert.Equal(VersionOne, source.Cells[1]!.BehaviorId);
        Assert.Equal(new byte[] { 1 }, source.Cells[1]!.CustomState.ToArray());
    }

    [Fact]
    public void FailedMigrationLeavesRuntimeAndSourceSnapshotReadable()
    {
        var oldRuntime = new PanelRuntimeInstance(
            CreatePanel(VersionOne),
            new CustomCellRuleRegistry([CounterRegistration(VersionOne)]));
        _ = oldRuntime.Step();
        var source = oldRuntime.CaptureSnapshot();

        var current = new PanelRuntimeInstance(
            CreatePanel(VersionTwo),
            new CustomCellRuleRegistry(
                [CounterRegistration(VersionTwo, migrateFromVersionOne: true, failMigration: true)]));
        var before = current.CaptureSnapshot();
        var exception = Assert.Throws<CustomCellRuleException>(() => current.RestoreSnapshot(source));
        var after = current.CaptureSnapshot();

        Assert.Equal(CustomCellDiagnosticCodes.MigrationFailed, exception.Code);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(before.Scheduler.CurrentTick, after.Scheduler.CurrentTick);
        Assert.Equal(before.Cells[1]!.CustomState.ToArray(), after.Cells[1]!.CustomState.ToArray());
        Assert.Equal(before.Cells[1]!.CustomRandomState, after.Cells[1]!.CustomRandomState);
        Assert.Equal(VersionOne, source.Cells[1]!.BehaviorId);
        Assert.Equal(new byte[] { 1 }, source.Cells[1]!.CustomState.ToArray());
    }

    [Fact]
    public void DeterministicRandomUsesExplicitRepeatableState()
    {
        ulong firstState = 42;
        ulong secondState = 42;

        Assert.Equal(CustomCellRandom.Next(ref firstState), CustomCellRandom.Next(ref secondState));
        Assert.Equal(firstState, secondState);
        Assert.NotEqual(42UL, firstState);
    }

    private static CustomCellRuleRegistration CounterRegistration(
        BehaviorId behaviorId,
        bool duplicateProposals = false,
        bool migrateFromVersionOne = false,
        bool invalidOutputPort = false,
        bool nullOutputPort = false,
        bool failMigration = false,
        Action? onCreate = null) =>
        new(
            behaviorId,
            CounterPorts,
            CounterRule.ValidateParameters,
            () =>
            {
                onCreate?.Invoke();
                return new CounterRule(
                    behaviorId == VersionTwo,
                    duplicateProposals,
                    migrateFromVersionOne,
                    invalidOutputPort,
                    nullOutputPort,
                    failMigration);
            });

    private static readonly ImmutableArray<CustomCellPort> CounterPorts =
    [
        new CustomCellPort("in", CardinalDirection.West, true, false),
        new CustomCellPort("out", CardinalDirection.East, false, true)
    ];

    private static PanelDefinition CreatePanelWithInvalidSecondCustomCell() =>
        PanelDefinition.Create(
            new CircuitId("custom-test/panel-two-rules"),
            4,
            1,
            [
                PanelCellDefinition.Create(
                    new ComponentId("source"),
                    new GridCoordinate(0, 0),
                    CellKind.InputPort,
                    portId: new PortId("signal")),
                PanelCellDefinition.Create(
                    new ComponentId("first-rule"),
                    new GridCoordinate(1, 0),
                    CellKind.Custom,
                    behaviorId: VersionOne),
                PanelCellDefinition.Create(
                    new ComponentId("second-rule"),
                    new GridCoordinate(2, 0),
                    CellKind.Custom,
                    parameters: [new KeyValuePair<string, string>("unexpected", "value")],
                    behaviorId: VersionOne),
                PanelCellDefinition.Create(
                    new ComponentId("probe"),
                    new GridCoordinate(3, 0),
                    CellKind.Probe)
            ]);

    private static PanelDefinition CreatePanel(
        BehaviorId behaviorId,
        IEnumerable<KeyValuePair<string, string>>? parameters = null) =>
        PanelDefinition.Create(
            new CircuitId("custom-test/panel"),
            3,
            1,
            [
                PanelCellDefinition.Create(
                    new ComponentId("source"),
                    new GridCoordinate(0, 0),
                    CellKind.InputPort,
                    portId: new PortId("signal")),
                PanelCellDefinition.Create(
                    new ComponentId("rule"),
                    new GridCoordinate(1, 0),
                    CellKind.Custom,
                    parameters: parameters,
                    behaviorId: behaviorId),
                PanelCellDefinition.Create(
                    new ComponentId("probe"),
                    new GridCoordinate(2, 0),
                    CellKind.Probe)
            ]);

    private sealed class CounterRule(
        bool isVersionTwo,
        bool duplicateProposals = false,
        bool migrateFromVersionOne = false,
        bool invalidOutputPort = false,
        bool nullOutputPort = false,
        bool failMigration = false) : ICustomCellRule
    {
        private bool IsVersionTwo { get; } = isVersionTwo;

        public static void ValidateParameters(ImmutableSortedDictionary<string, string> parameters)
        {
            if (parameters.Count != 0)
            {
                throw new ArgumentException("Counter rule accepts no parameters.", nameof(parameters));
            }
        }

        public ImmutableArray<byte> CreateInitialState(ImmutableSortedDictionary<string, string> parameters)
        {
            return IsVersionTwo ? [2, 0] : [0];
        }

        public void ValidateState(
            ImmutableSortedDictionary<string, string> parameters,
            ImmutableArray<byte> state)
        {
            var valid = IsVersionTwo
                ? !state.IsDefault && state.Length == 2 && state[0] == 2
                : !state.IsDefault && state.Length == 1;
            if (!valid)
            {
                throw new ArgumentException("Counter state is invalid.", nameof(state));
            }
        }

        public CustomCellTransition Evaluate(CustomCellEvaluationContext context)
        {
            var randomState = context.RandomState;
            var output = CustomCellRandom.Next(ref randomState) % 2 == 0
                ? LogicValue.Low
                : LogicValue.High;
            var count = checked((byte)(context.State[^1] + 1));
            var nextState = IsVersionTwo ? ImmutableArray.Create((byte)2, count) : ImmutableArray.Create(count);
            var proposal = nullOutputPort
                ? default
                : new CustomCellProposal(invalidOutputPort ? "unknown" : "out", output);
            var proposals = duplicateProposals
                ? ImmutableArray.Create(proposal, proposal)
                : ImmutableArray.Create(proposal);
            return new CustomCellTransition(proposals, nextState, randomState);
        }

        public bool TryMigrateState(
            BehaviorId sourceBehaviorId,
            ImmutableArray<byte> sourceState,
            out ImmutableArray<byte> migratedState)
        {
            if (IsVersionTwo && failMigration && sourceBehaviorId == VersionOne)
            {
                throw new InvalidOperationException("Counter migration failed.");
            }

            if (IsVersionTwo && migrateFromVersionOne &&
                sourceBehaviorId == VersionOne &&
                !sourceState.IsDefault && sourceState.Length == 1)
            {
                migratedState = [2, sourceState[0]];
                return true;
            }

            migratedState = default;
            return false;
        }

    }
}
