using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using Xunit;

namespace SpatialCircuits.Hierarchy.Tests;

public sealed class ChipDefinitionTests
{
    [Fact]
    public void CanonicalDefinitionHashIgnoresInputEnumerationOrder()
    {
        var panel = CreatePassThroughPanel("panel/canonical");
        var first = ChipDefinition.Create(
            new DefinitionId("chip/canonical"),
            panel,
            [
                new ChipPortDefinition("signal", new PortId("signal"), ChipPortDirection.Input),
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)
            ],
            1,
            1,
            "buffer",
            [new KeyValuePair<string, string>("mode", "exact"), new("width", "1")]);
        var second = ChipDefinition.Create(
            new DefinitionId("chip/canonical"),
            panel,
            [
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output),
                new ChipPortDefinition("signal", new PortId("signal"), ChipPortDirection.Input)
            ],
            1,
            1,
            "buffer",
            [new KeyValuePair<string, string>("width", "1"), new("mode", "exact")]);

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(64, first.ContentHash.Length);
        Assert.Equal(first.ContentHash, first.ContentHash.ToLowerInvariant());
    }

    [Fact]
    public void DefinitionExposesItsHashPinnedChildDependencies()
    {
        var childPanel = CreatePassThroughPanel("panel/dependency-child");
        var child = CreateDefinition(new DefinitionId("chip/dependency-child"), childPanel);
        var editedChild = CreateDefinition(
            child.Id,
            CreatePassThroughPanel("panel/dependency-child-v2"));
        var parentPanel = CreatePassThroughPanel("panel/dependency-parent");
        var parent = CreateDefinition(
            new DefinitionId("chip/dependency-parent"),
            parentPanel,
            PanelOwnedChipNetworkDefinition.Create(
                parentPanel.Id,
                [PinnedInstance("child", child.Id, child.ContentHash)]));

        var catalog = ChipDefinitionCatalog.Create([child, editedChild, parent]);

        var dependency = Assert.Single(parent.Dependencies);
        Assert.Equal(child.Id, dependency.DefinitionId);
        Assert.Equal(child.ContentHash, dependency.ContentHash);
        Assert.True(catalog.TryResolve(child.Id, child.ContentHash, out _));
        Assert.True(catalog.TryResolve(editedChild.Id, editedChild.ContentHash, out _));
    }

    [Fact]
    public void DefinitionRejectsChildNetworkDrivingItsPublicInput()
    {
        var driver = CreateDefinition(
            new DefinitionId("chip/public-input-driver"),
            CreatePassThroughPanel("panel/public-input-driver"));
        var panel = CreatePassThroughPanel("panel/public-input-owner");
        var childNetwork = PanelOwnedChipNetworkDefinition.Create(
            panel.Id,
            [PinnedInstance("driver", driver.Id, driver.ContentHash)],
            [new ChipPortConnection(
                ChipPortEndpoint.ChildChip(new ComponentId("driver"), "out"),
                ChipPortEndpoint.ParentPanel(new PortId("signal")))]);

        Assert.Throws<ArgumentException>(() => CreateDefinition(
            new DefinitionId("chip/public-input-owner"),
            panel,
            childNetwork));
    }

    [Fact]
    public void CatalogRejectsDirectAndIndirectDependencyCyclesWithPaths()
    {
        var directPanel = CreatePassThroughPanel("panel/direct-cycle");
        var directId = new DefinitionId("chip/direct");
        var direct = CreateDefinition(
            directId,
            directPanel,
            PanelOwnedChipNetworkDefinition.Create(
                directPanel.Id,
                [PinnedInstance("self", directId, UnknownHash)]));

        var directError = Assert.Throws<ChipDefinitionException>(() => ChipDefinitionCatalog.Create([direct]));
        var directDiagnostic = Assert.Single(directError.Diagnostics, item =>
            item.Code == ChipDiagnosticCodes.DependencyCycle);
        Assert.Contains("chip/direct@", directDiagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(2, directDiagnostic.Message.Split("chip/direct@", StringSplitOptions.None).Length - 1);

        var firstPanel = CreatePassThroughPanel("panel/indirect-a");
        var secondPanel = CreatePassThroughPanel("panel/indirect-b");
        var firstId = new DefinitionId("chip/indirect-a");
        var secondId = new DefinitionId("chip/indirect-b");
        var first = CreateDefinition(
            firstId,
            firstPanel,
            PanelOwnedChipNetworkDefinition.Create(
                firstPanel.Id,
                [PinnedInstance("to-b", secondId, UnknownHash)]));
        var second = CreateDefinition(
            secondId,
            secondPanel,
            PanelOwnedChipNetworkDefinition.Create(
                secondPanel.Id,
                [PinnedInstance("to-a", firstId, UnknownHash)]));

        var indirectError = Assert.Throws<ChipDefinitionException>(() => ChipDefinitionCatalog.Create([first, second]));
        var indirectDiagnostic = Assert.Single(indirectError.Diagnostics, item =>
            item.Code == ChipDiagnosticCodes.DependencyCycle);
        Assert.Contains("chip/indirect-a@", indirectDiagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("chip/indirect-b@", indirectDiagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(2, indirectDiagnostic.Message.Split("chip/indirect-a@", StringSplitOptions.None).Length - 1);
    }

    private static ChipDefinition CreateDefinition(
        DefinitionId id,
        PanelDefinition panel,
        PanelOwnedChipNetworkDefinition? childNetwork = null) =>
        ChipDefinition.Create(
            id,
            panel,
            [
                new ChipPortDefinition("signal", new PortId("signal"), ChipPortDirection.Input),
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)
            ],
            1,
            1,
            "buffer",
            childNetwork: childNetwork);

    private static ChipInstanceDefinition PinnedInstance(string id, DefinitionId definitionId, string hash) =>
        ChipInstanceDefinition.Create(new ComponentId(id), definitionId, hash);

    private static PanelDefinition CreatePassThroughPanel(string id) => PanelDefinition.Create(
        new CircuitId(id),
        3,
        1,
        [
            PanelCellDefinition.Create(
                new ComponentId("source"),
                new GridCoordinate(0, 0),
                CellKind.InputPort,
                CardinalDirection.East,
                new PortId("signal")),
            PanelCellDefinition.Create(
                new ComponentId("wire"),
                new GridCoordinate(1, 0),
                CellKind.Wire),
            PanelCellDefinition.Create(
                new ComponentId("output"),
                new GridCoordinate(2, 0),
                CellKind.OutputPort,
                CardinalDirection.East,
                new PortId("out"))
        ]);

    private const string UnknownHash = "0000000000000000000000000000000000000000000000000000000000000000";
}
