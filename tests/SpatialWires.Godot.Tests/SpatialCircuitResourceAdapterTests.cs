using GdUnit4;
using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.GodotAdapter;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Runner;
using SpatialCircuits.Workbench;
using static GdUnit4.Assertions;

namespace SpatialWires.Godot.Tests;

[TestSuite]
public sealed class SpatialCircuitResourceAdapterTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void ResourceAndCanonicalJsonProduceTheSameImmutableDefinitionAndHash()
    {
        var plan = ReadChipDefinitionPlan();
        var expected = plan.CreateDefinition();
        var resource = CreateResource(plan);
        var actual = SpatialCircuitResourceAdapter.ToDefinition(resource);

        AssertThat(actual.Id.Value).IsEqual(expected.Id.Value);
        AssertThat(actual.SchemaVersion).IsEqual(expected.SchemaVersion);
        AssertThat(actual.BehaviorVersion).IsEqual(expected.BehaviorVersion);
        AssertThat(actual.ContentHash).IsEqual(expected.ContentHash);
        AssertThat(actual.SourcePanel.Cells.Length).IsEqual(expected.SourcePanel.Cells.Length);
        AssertThat(actual.Ports.Length).IsEqual(expected.Ports.Length);

        var immutableDelay = actual.SourcePanel.GetCell(new GridCoordinate(2, 5))!.Parameters["delay"];
        resource.SourcePanel.Cells.First(cell => cell.CellId == "nand-one").Parameters["delay"] = "9";
        AssertThat(immutableDelay).IsEqual("1");
        AssertThat(actual.SourcePanel.GetCell(new GridCoordinate(2, 5))!.Parameters["delay"]).IsEqual("1");
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ValidEditPublishesANewRevisionAndInvalidEditKeepsTheLastValidOne()
    {
        var resource = CreateResource(ReadChipDefinitionPlan());
        var publisher = new SpatialCircuitDefinitionPublisher();

        AssertThat(publisher.TryPublish(resource, out var firstDiagnostic)).IsTrue();
        AssertThat(firstDiagnostic).IsEqual(string.Empty);
        var first = publisher.CurrentDefinition!;

        resource.Symbol = "xor-v2";
        resource.SchemaVersion = first.SchemaVersion + 1;
        AssertThat(publisher.TryPublish(resource, out var secondDiagnostic)).IsTrue();
        AssertThat(secondDiagnostic).IsEqual(string.Empty);
        var second = publisher.CurrentDefinition!;
        AssertThat(ReferenceEquals(first, second)).IsFalse();
        AssertThat(first.Symbol).IsEqual("xor");
        AssertThat(second.Symbol).IsEqual("xor-v2");
        AssertThat(second.SchemaVersion).IsEqual(first.SchemaVersion + 1);
        AssertThat(second.ContentHash).IsNotEqual(first.ContentHash);

        resource.Symbol = "Invalid Symbol";
        AssertThat(publisher.TryPublish(resource, out var invalidDiagnostic)).IsFalse();
        AssertThat(invalidDiagnostic.Length > 0).IsTrue();
        AssertThat(ReferenceEquals(publisher.CurrentDefinition, second)).IsTrue();
        AssertThat(publisher.CurrentDefinition!.ContentHash).IsEqual(second.ContentHash);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ChipResourceRoundTripPreservesChildNetworkAndCanonicalHash()
    {
        var panel = PanelDefinition.Create(
            new CircuitId("panel/editor-child"),
            2,
            1,
            [
                PanelCellDefinition.Create(
                    new ComponentId("in"),
                    new GridCoordinate(0, 0),
                    CellKind.InputPort,
                    portId: new PortId("in")),
                PanelCellDefinition.Create(
                    new ComponentId("out"),
                    new GridCoordinate(1, 0),
                    CellKind.OutputPort,
                    portId: new PortId("out"))
            ]);
        var child = ChipInstanceDefinition.Create(
            new ComponentId("child"),
            new DefinitionId("chip/child"),
            new string('a', 64));
        var network = PanelOwnedChipNetworkDefinition.Create(
            panel.Id,
            [child],
            [
                new ChipPortConnection(
                    ChipPortEndpoint.ChildChip(child.InstanceId, "out"),
                    ChipPortEndpoint.ParentPanel(new PortId("out")))
            ]);
        var definition = ChipDefinition.Create(
            new DefinitionId("chip/editor-child"),
            panel,
            [
                new ChipPortDefinition("in", new PortId("in"), ChipPortDirection.Input),
                new ChipPortDefinition("out", new PortId("out"), ChipPortDirection.Output)
            ],
            1,
            1,
            "editor-child",
            childNetwork: network);

        var resource = SpatialCircuitResourceAdapter.ToResource(definition);
        var roundTrip = SpatialCircuitResourceAdapter.ToDefinition(resource);

        AssertThat(roundTrip.ContentHash).IsEqual(definition.ContentHash);
        AssertThat(roundTrip.ChildNetwork.Instances).IsEqual(definition.ChildNetwork.Instances);
        AssertThat(roundTrip.ChildNetwork.Connections).IsEqual(definition.ChildNetwork.Connections);
        resource.Dispose();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void EditorAuthoringSavesAPortablePanelResource()
    {
        var path = $"user://editor-panel-{Guid.NewGuid():N}.tres";
        var editor = new SpatialCircuitEditorAuthoringSession(new PanelWorkbenchSession(
            PanelDefinition.Create(new CircuitId("panel/editor-save"), 1, 1, [])));
        try
        {
            AssertThat(editor.TrySavePanel(path, out var reference, out var diagnostic)).IsTrue();
            AssertThat(diagnostic).IsEqual(string.Empty);
            AssertThat(reference).IsNotNull();
            AssertThat(global::Godot.FileAccess.FileExists(path)).IsTrue();
            AssertThat(editor.LastSavedPath).IsEqual(path);
        }
        finally
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
    }

    private static ChipDefinitionPlan ReadChipDefinitionPlan()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "xor-chip.fixture.json");
        var result = FixtureCodec.Read(File.ReadAllBytes(path));
        if (result.Fixture?.ChipNetworkScenario is not { } scenario || scenario.Definitions.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("Canonical chip fixture has no chip definition.");
        }

        return scenario.Definitions[0];
    }

    private static SpatialCircuitResource CreateResource(ChipDefinitionPlan plan)
    {
        var panel = new SpatialCircuitPanelResource
        {
            PanelId = plan.SourcePanel.PanelId,
            Width = plan.SourcePanel.Width,
            Height = plan.SourcePanel.Height
        };
        foreach (var cell in plan.SourcePanel.Cells)
        {
            var cellResource = new SpatialCircuitCellResource
            {
                CellId = cell.CellId,
                X = cell.X,
                Y = cell.Y,
                Kind = cell.Kind,
                Orientation = cell.Orientation,
                PortId = cell.PortId ?? string.Empty,
                BehaviorId = cell.BehaviorId ?? string.Empty
            };
            foreach (var parameter in cell.Parameters)
            {
                cellResource.Parameters.Add(parameter.Key, parameter.Value);
            }

            panel.Cells.Add(cellResource);
        }

        var resource = new SpatialCircuitResource
        {
            DefinitionId = plan.DefinitionId,
            SourcePanel = panel,
            SchemaVersion = plan.SchemaVersion,
            BehaviorVersion = plan.BehaviorVersion,
            Symbol = plan.Symbol
        };
        foreach (var port in plan.Ports)
        {
            resource.Ports.Add(new SpatialCircuitChipPortResource
            {
                Name = port.Name,
                PanelPortId = port.PanelPortId,
                Direction = port.Direction
            });
        }

        foreach (var parameter in plan.Parameters)
        {
            resource.Parameters.Add(parameter.Key, parameter.Value);
        }

        return resource;
    }

}
