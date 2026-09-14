using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using Xunit;

namespace SpatialCircuits.Hierarchy.Tests;

public sealed class PanelOwnedChipNetworkTests
{
    [Fact]
    public void ChildNetworkConnectsNamedPortsOutsideTheParentGrid()
    {
        var chip = CreatePassThroughChip("chip/buffer", "panel/buffer", "buffer");
        var catalog = ChipDefinitionCatalog.Create([chip]);
        var owner = EmptyPanel("panel/owner");
        var networkDefinition = PanelOwnedChipNetworkDefinition.Create(
            owner.Id,
            [
                Pin("source", chip),
                Pin("sink", chip)
            ],
            [new ChipPortConnection(
                ChipPortEndpoint.ChildChip(new ComponentId("source"), "out"),
                ChipPortEndpoint.ChildChip(new ComponentId("sink"), "signal"))]);
        var network = new PanelOwnedChipNetworkInstance(owner, networkDefinition, catalog);

        network.GetInstance(new ComponentId("source")).SetInput("signal", LogicValue.High);
        Step(network, 20);

        Assert.Null(owner.Cells.Single());
        Assert.Equal(2, network.Definition.Instances.Length);
        Assert.Equal(LogicValue.High, network.GetInstance(new ComponentId("source")).GetOutput("out"));
        Assert.Equal(LogicValue.High, network.GetInstance(new ComponentId("sink")).GetOutput("out"));
    }

    [Fact]
    public void ChipDefinitionRunsItsOwnPanelOwnedChildNetwork()
    {
        var leaf = CreatePassThroughChip("chip/leaf", "panel/leaf", "buffer");
        var parentPanel = CreatePassThroughPanel("panel/parent-chip");
        var parent = ChipDefinition.Create(
            new DefinitionId("chip/parent"),
            parentPanel,
            [
                new ChipPortDefinition("signal", new PortId("signal"), ChipPortDirection.Input),
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)
            ],
            1,
            1,
            "parent",
            childNetwork: PanelOwnedChipNetworkDefinition.Create(
                parentPanel.Id,
                [Pin("inner", leaf)]));
        var catalog = ChipDefinitionCatalog.Create([leaf, parent]);
        var owner = EmptyPanel("panel/nested-owner");
        var runtime = new PanelOwnedChipNetworkInstance(
            owner,
            PanelOwnedChipNetworkDefinition.Create(owner.Id, [Pin("outer", parent)]),
            catalog);
        var outer = runtime.GetInstance(new ComponentId("outer"));
        outer.SetInput("signal", LogicValue.High);
        outer.Children.GetInstance(new ComponentId("inner")).SetInput("signal", LogicValue.High);

        ChipNetworkTickResult? result = null;
        for (var tick = 0; tick < 20; tick++)
        {
            result = runtime.Step();
        }

        Assert.Equal(LogicValue.High, outer.GetOutput("out"));
        Assert.Equal(
            LogicValue.High,
            outer.Children.GetInstance(new ComponentId("inner")).GetOutput("out"));
        Assert.Equal(new ComponentId("inner"), Assert.Single(Assert.Single(result!.Chips).Children).InstanceId);
    }

    [Fact]
    public void NestedChildNetworkConnectsItsOutputToTheOwningChipPanelInput()
    {
        var leaf = CreatePassThroughChip("chip/connected-leaf", "panel/connected-leaf", "buffer");
        var parentPanel = PanelDefinition.Create(
            new CircuitId("panel/connected-parent"),
            3,
            1,
            [
                PortCell("internal", 0, 0, CellKind.InputPort, CardinalDirection.East, "internal-signal"),
                Cell("wire", 1, 0, CellKind.Wire),
                PortCell("output", 2, 0, CellKind.OutputPort, CardinalDirection.East, "out")
            ]);
        var parent = ChipDefinition.Create(
            new DefinitionId("chip/connected-parent"),
            parentPanel,
            [new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)],
            1,
            1,
            "connected-parent",
            childNetwork: PanelOwnedChipNetworkDefinition.Create(
                parentPanel.Id,
                [Pin("inner", leaf)],
                [new ChipPortConnection(
                    ChipPortEndpoint.ChildChip(new ComponentId("inner"), "out"),
                    ChipPortEndpoint.ParentPanel(new PortId("internal-signal")))]));
        var catalog = ChipDefinitionCatalog.Create([leaf, parent]);
        var owner = EmptyPanel("panel/connected-owner");
        var network = new PanelOwnedChipNetworkInstance(
            owner,
            PanelOwnedChipNetworkDefinition.Create(owner.Id, [Pin("outer", parent)]),
            catalog);
        var inner = network.GetInstance(new ComponentId("outer"))
            .Children.GetInstance(new ComponentId("inner"));
        inner.SetInput("signal", LogicValue.High);

        Step(network, 20);

        Assert.Equal(LogicValue.High, inner.GetOutput("out"));
        Assert.Equal(LogicValue.High, network.GetInstance(new ComponentId("outer")).GetOutput("out"));
    }

    [Fact]
    public void NestedChildNetworkCannotAdvanceItsOwnerRuntimeSeparately()
    {
        var leaf = CreatePassThroughChip("chip/step-leaf", "panel/step-leaf", "buffer");
        var parentPanel = CreatePassThroughPanel("panel/step-parent");
        var parent = ChipDefinition.Create(
            new DefinitionId("chip/step-parent"),
            parentPanel,
            [
                new ChipPortDefinition("signal", new PortId("signal"), ChipPortDirection.Input),
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)
            ],
            1,
            1,
            "step-parent",
            childNetwork: PanelOwnedChipNetworkDefinition.Create(parentPanel.Id, [Pin("inner", leaf)]));
        var catalog = ChipDefinitionCatalog.Create([leaf, parent]);
        var owner = EmptyPanel("panel/step-owner");
        var network = new PanelOwnedChipNetworkInstance(
            owner,
            PanelOwnedChipNetworkDefinition.Create(owner.Id, [Pin("outer", parent)]),
            catalog);

        Assert.Throws<InvalidOperationException>(() =>
            network.GetInstance(new ComponentId("outer")).Children.Step());
    }

    [Fact]
    public void NestedChipInputsCannotChangeDuringAnAncestorStep()
    {
        var leaf = CreatePassThroughChip("chip/guard-leaf", "panel/guard-leaf", "buffer");
        var parentPanel = CreatePassThroughPanel("panel/guard-parent");
        var parent = ChipDefinition.Create(
            new DefinitionId("chip/guard-parent"),
            parentPanel,
            [
                new ChipPortDefinition("signal", new PortId("signal"), ChipPortDirection.Input),
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)
            ],
            1,
            1,
            "guard-parent",
            childNetwork: PanelOwnedChipNetworkDefinition.Create(parentPanel.Id, [Pin("inner", leaf)]));
        var catalog = ChipDefinitionCatalog.Create([leaf, parent]);
        var behaviorId = new BehaviorId("tests.hierarchy:mutation-guard/v1");
        PanelOwnedChipNetworkInstance.ChipInstance? target = null;
        var mutationWasRejected = false;
        var rules = new CustomCellRuleRegistry(
        [
            new CustomCellRuleRegistration(
                behaviorId,
                [new CustomCellPort("out", CardinalDirection.East, false, true)],
                static parameters =>
                {
                    if (parameters.Count != 0)
                    {
                        throw new ArgumentException("Mutation guard rule has no parameters.", nameof(parameters));
                    }
                },
                () => new CallbackRule(() =>
                {
                    try
                    {
                        target!.SetInput("signal", LogicValue.High);
                    }
                    catch (InvalidOperationException)
                    {
                        mutationWasRejected = true;
                    }
                }))
        ]);
        var owner = PanelDefinition.Create(
            new CircuitId("panel/guard-owner"),
            1,
            1,
            [PanelCellDefinition.Create(
                new ComponentId("callback"),
                new GridCoordinate(0, 0),
                CellKind.Custom,
                behaviorId: behaviorId)]);
        var network = new PanelOwnedChipNetworkInstance(
            owner,
            PanelOwnedChipNetworkDefinition.Create(owner.Id, [Pin("outer", parent)]),
            catalog,
            rules);
        target = network.GetInstance(new ComponentId("outer"))
            .Children.GetInstance(new ComponentId("inner"));

        network.Step();

        Assert.True(mutationWasRejected);
    }

    [Fact]
    public void StatefulInstancesAndPresentationModesRemainIndependent()
    {
        var chip = CreateFlipFlopChip();
        var catalog = ChipDefinitionCatalog.Create([chip]);
        var owner = EmptyPanel("panel/stateful-owner");
        var networkDefinition = PanelOwnedChipNetworkDefinition.Create(
            owner.Id,
            [Pin("first", chip), Pin("second", chip)]);
        var expanded = new PanelOwnedChipNetworkInstance(owner, networkDefinition, catalog);
        var collapsed = new PanelOwnedChipNetworkInstance(owner, networkDefinition, catalog);
        collapsed.GetInstance(new ComponentId("first")).Collapse();

        foreach (var network in new[] { expanded, collapsed })
        {
            network.GetInstance(new ComponentId("first")).SetInput("data", LogicValue.Low);
            network.GetInstance(new ComponentId("first")).SetInput("clock", LogicValue.Low);
            network.GetInstance(new ComponentId("second")).SetInput("data", LogicValue.Low);
            network.GetInstance(new ComponentId("second")).SetInput("clock", LogicValue.Low);
            Step(network, 3);
        }

        expanded.GetInstance(new ComponentId("first")).SetInput("data", LogicValue.High);
        collapsed.GetInstance(new ComponentId("first")).SetInput("data", LogicValue.High);
        AssertSameTick(expanded, collapsed);
        expanded.GetInstance(new ComponentId("first")).SetInput("clock", LogicValue.High);
        collapsed.GetInstance(new ComponentId("first")).SetInput("clock", LogicValue.High);
        for (var tick = 0; tick < 5; tick++)
        {
            AssertSameTick(expanded, collapsed);
        }

        Assert.Equal(LogicValue.High, expanded.GetInstance(new ComponentId("first")).GetOutput("q"));
        Assert.NotEqual(LogicValue.High, expanded.GetInstance(new ComponentId("second")).GetOutput("q"));
        Assert.Equal(
            ChipPresentationMode.ExpandedGrid,
            expanded.GetInstance(new ComponentId("first")).ResolvePresentation(authoringEnabled: true));
        Assert.Equal(
            ChipPresentationMode.Collapsed,
            collapsed.GetInstance(new ComponentId("first")).ResolvePresentation(authoringEnabled: true));
        Assert.Equal(
            ChipPresentationMode.ExpandedGrid,
            collapsed.GetInstance(new ComponentId("first")).ResolvePresentation(authoringEnabled: false));
    }

    [Fact]
    public void CompatibleUpgradeChangesThePinOnlyAfterStateRestores()
    {
        var sourcePanel = CreatePassThroughPanel("panel/upgrade");
        var original = CreatePassThroughChip("chip/upgrade", sourcePanel, "buffer");
        var edited = CreatePassThroughChip("chip/upgrade", sourcePanel, "renamed-buffer");
        var oldCatalog = ChipDefinitionCatalog.Create([original, edited]);
        var owner = EmptyPanel("panel/upgrade-owner");
        var originalNetwork = PanelOwnedChipNetworkDefinition.Create(owner.Id, [Pin("buffer", original)]);
        var runtime = new PanelOwnedChipNetworkInstance(owner, originalNetwork, oldCatalog);
        var instance = runtime.GetInstance(new ComponentId("buffer"));
        instance.SetInput("signal", LogicValue.High);
        Step(runtime, 4);

        var newCatalog = ChipDefinitionCatalog.Create([edited]);
        Assert.Equal(original.ContentHash, instance.Definition.ContentHash);
        Assert.True(instance.TryUpgrade(edited, newCatalog, out var diagnostic));

        Assert.Null(diagnostic);
        Assert.Equal(edited.ContentHash, instance.Definition.ContentHash);
        Assert.Equal(edited.ContentHash, runtime.Definition.Instances.Single().ContentHash);
        Assert.Equal(LogicValue.High, instance.GetOutput("out"));
    }

    [Fact]
    public void FailedUpgradeLeavesTheOldPinAndRuntimeStateUsable()
    {
        var original = CreatePassThroughChip("chip/failed-upgrade", "panel/failed-upgrade", "buffer");
        var catalog = ChipDefinitionCatalog.Create([original]);
        var owner = EmptyPanel("panel/failed-upgrade-owner");
        var network = new PanelOwnedChipNetworkInstance(
            owner,
            PanelOwnedChipNetworkDefinition.Create(owner.Id, [Pin("buffer", original)]),
            catalog);
        var instance = network.GetInstance(new ComponentId("buffer"));
        instance.SetInput("signal", LogicValue.High);
        Step(network, 4);

        var incompatible = CreatePassThroughChip(
            "chip/failed-upgrade",
            CreatePassThroughPanel("panel/replaced-grid"),
            "buffer-v2");
        var candidateCatalog = ChipDefinitionCatalog.Create([incompatible]);
        var oldHash = instance.Definition.ContentHash;
        var oldOutput = instance.GetOutput("out");

        Assert.False(instance.TryUpgrade(incompatible, candidateCatalog, out var diagnostic));

        Assert.Equal(ChipDiagnosticCodes.UpgradeIncompatible, diagnostic?.Code);
        Assert.Equal(oldHash, instance.Definition.ContentHash);
        Assert.Equal(oldHash, network.Definition.Instances.Single().ContentHash);
        Assert.Equal(oldOutput, instance.GetOutput("out"));
    }

    private static ChipDefinition CreatePassThroughChip(string id, string panelId, string symbol) =>
        CreatePassThroughChip(id, CreatePassThroughPanel(panelId), symbol);

    private static ChipDefinition CreatePassThroughChip(string id, PanelDefinition panel, string symbol) =>
        ChipDefinition.Create(
            new DefinitionId(id),
            panel,
            [
                new ChipPortDefinition("signal", new PortId("signal"), ChipPortDirection.Input),
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)
            ],
            1,
            1,
            symbol);

    private static ChipDefinition CreateFlipFlopChip()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/stateful-chip"),
            4,
            4,
            [
                PortCell("data-source", 1, 2, CellKind.InputPort, CardinalDirection.East, "data"),
                PortCell("clock-source", 2, 3, CellKind.InputPort, CardinalDirection.North, "clock"),
                Cell("dff", 2, 2, CellKind.DFlipFlop, CardinalDirection.East),
                PortCell("q-output", 3, 2, CellKind.OutputPort, CardinalDirection.East, "q")
            ]);
        return ChipDefinition.Create(
            new DefinitionId("chip/stateful"),
            panel,
            [
                new ChipPortDefinition("data", new PortId("data"), ChipPortDirection.Input),
                new ChipPortDefinition("clock", new PortId("clock"), ChipPortDirection.Input),
                new ChipPortDefinition("q", new PortId("q"), ChipPortDirection.Output)
            ],
            1,
            1,
            "d-flip-flop");
    }

    private static ChipInstanceDefinition Pin(string id, ChipDefinition definition) =>
        ChipInstanceDefinition.Create(new ComponentId(id), definition.Id, definition.ContentHash);

    private static PanelDefinition EmptyPanel(string id) => PanelDefinition.Create(new CircuitId(id), 1, 1, []);

    private static PanelDefinition CreatePassThroughPanel(string id) => PanelDefinition.Create(
        new CircuitId(id),
        3,
        1,
        [
            PortCell("source", 0, 0, CellKind.InputPort, CardinalDirection.East, "signal"),
            Cell("wire", 1, 0, CellKind.Wire),
            PortCell("output", 2, 0, CellKind.OutputPort, CardinalDirection.East, "out")
        ]);

    private static PanelCellDefinition Cell(
        string id,
        int x,
        int y,
        CellKind kind,
        CardinalDirection orientation = CardinalDirection.East) =>
        PanelCellDefinition.Create(new ComponentId(id), new GridCoordinate(x, y), kind, orientation);

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

    private static void Step(PanelOwnedChipNetworkInstance network, int count)
    {
        for (var tick = 0; tick < count; tick++)
        {
            network.Step();
        }
    }

    private static void AssertSameTick(
        PanelOwnedChipNetworkInstance expanded,
        PanelOwnedChipNetworkInstance collapsed)
    {
        var expandedResult = expanded.Step();
        var collapsedResult = collapsed.Step();
        Assert.Equal(expandedResult.Hash, collapsedResult.Hash);
        Assert.Equal(expandedResult.Chips.Length, collapsedResult.Chips.Length);
        for (var index = 0; index < expandedResult.Chips.Length; index++)
        {
            Assert.Equal(expandedResult.Chips[index].Hash, collapsedResult.Chips[index].Hash);
            Assert.Equal(
                expandedResult.Chips[index].Outputs.ToArray(),
                collapsedResult.Chips[index].Outputs.ToArray());
        }
    }

    private sealed class CallbackRule(Action onEvaluate) : ICustomCellRule
    {
        public ImmutableArray<byte> CreateInitialState(ImmutableSortedDictionary<string, string> parameters) => [0];

        public void ValidateState(
            ImmutableSortedDictionary<string, string> parameters,
            ImmutableArray<byte> state)
        {
            if (state.IsDefault || state.Length != 1 || state[0] != 0)
            {
                throw new ArgumentException("Callback rule state is invalid.", nameof(state));
            }
        }

        public CustomCellTransition Evaluate(CustomCellEvaluationContext context)
        {
            onEvaluate();
            return new CustomCellTransition(
                [new CustomCellProposal("out", LogicValue.Low)],
                context.State,
                context.RandomState);
        }

        public bool TryMigrateState(
            BehaviorId sourceBehaviorId,
            ImmutableArray<byte> sourceState,
            out ImmutableArray<byte> migratedState)
        {
            migratedState = ImmutableArray<byte>.Empty;
            return false;
        }
    }
}
