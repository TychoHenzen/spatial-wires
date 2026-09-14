using GdUnit4;
using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.GodotAdapter;
using static GdUnit4.Assertions;

namespace SpatialWires.IndependentConsumer.Tests;

[TestSuite]
public sealed class IndependentConsumerAddonTests
{
    // covers: spatial-circuits/package-baseline :: Portable second-consumer practice check :: Addon is moved to an independent consumer
    [TestCase]
    [RequireGodotRuntime]
    public void StagedAddonExposesItsPublicNodeAndResource()
    {
        var node = new SpatialCircuitNode();
        var resource = new SpatialCircuitResource();
        try
        {
            AssertThat(node).IsNotNull();
            AssertThat(resource).IsNotNull();
            AssertThat(node.GetType().FullName).IsEqual("SpatialCircuits.GodotAdapter.SpatialCircuitNode");
            AssertThat(resource.GetType().FullName).IsEqual("SpatialCircuits.GodotAdapter.SpatialCircuitResource");
        }
        finally
        {
            node.Free();
            resource.Dispose();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void StagedPanelAndCircuitResourcesRoundTripThroughGodot()
    {
        var panelPath = $"user://independent-panel-{Guid.NewGuid():N}.tres";
        var circuitPath = $"user://independent-circuit-{Guid.NewGuid():N}.tres";
        var panel = new SpatialCircuitPanelResource
        {
            PanelId = "panel/independent-roundtrip",
            Width = 2,
            Height = 1
        };
        var cell = new SpatialCircuitCellResource
        {
            CellId = "constant",
            X = 1,
            Kind = "constant",
            Parameters = new Godot.Collections.Dictionary<string, string> { ["value"] = "1" }
        };
        panel.Cells.Add(cell);
        var circuit = new SpatialCircuitResource
        {
            DefinitionId = "chip/independent-roundtrip",
            SourcePanel = panel,
            SchemaVersion = 3,
            BehaviorVersion = 2,
            Symbol = "roundtrip",
            Parameters = new Godot.Collections.Dictionary<string, string> { ["mode"] = "test" }
        };
        circuit.Ports.Add(new SpatialCircuitChipPortResource
        {
            Name = "result",
            PanelPortId = "result",
            Direction = "output"
        });
        SpatialCircuitPanelResource? loadedPanel = null;
        SpatialCircuitResource? loadedCircuit = null;

        try
        {
            AssertThat(ResourceSaver.Save(panel, panelPath)).IsEqual(Error.Ok);
            AssertThat(ResourceSaver.Save(circuit, circuitPath)).IsEqual(Error.Ok);

            loadedPanel = ResourceLoader.Load<SpatialCircuitPanelResource>(panelPath);
            var cachedPanel = ResourceLoader.Load<SpatialCircuitPanelResource>(panelPath);
            loadedCircuit = ResourceLoader.Load<SpatialCircuitResource>(circuitPath);
            AssertThat(loadedPanel).IsNotNull();
            AssertThat(cachedPanel).IsNotNull();
            AssertThat(loadedCircuit).IsNotNull();
            AssertThat(ReferenceEquals(loadedPanel, cachedPanel)).IsTrue();
            AssertThat(loadedPanel!.PanelId).IsEqual("panel/independent-roundtrip");
            AssertThat(loadedPanel.Cells.Single().Parameters["value"]).IsEqual("1");
            AssertThat(loadedCircuit!.DefinitionId).IsEqual("chip/independent-roundtrip");
            AssertThat(loadedCircuit.SchemaVersion).IsEqual(3);
            AssertThat(loadedCircuit.BehaviorVersion).IsEqual(2);
            AssertThat(loadedCircuit.Ports.Single().Direction).IsEqual("output");
            AssertThat(loadedCircuit.Parameters["mode"]).IsEqual("test");
        }
        finally
        {
            loadedPanel?.Dispose();
            loadedCircuit?.Dispose();
            circuit.Dispose();
            panel.Dispose();
            cell.Dispose();
            RemoveSavedResource(panelPath);
            RemoveSavedResource(circuitPath);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void OneCachedPanelResourceCreatesIndependentRuntimeInstances()
    {
        var path = $"user://independent-runtime-panel-{Guid.NewGuid():N}.tres";
        var panel = new SpatialCircuitPanelResource
        {
            PanelId = "panel/independent-runtime",
            Width = 1,
            Height = 1
        };
        var clock = new SpatialCircuitCellResource
        {
            CellId = "clock",
            Kind = "clock",
            Parameters = new Godot.Collections.Dictionary<string, string>
            {
                ["high-ticks"] = "1",
                ["low-ticks"] = "1"
            }
        };
        panel.Cells.Add(clock);
        SpatialCircuitPanelResource? loaded = null;

        try
        {
            AssertThat(ResourceSaver.Save(panel, path)).IsEqual(Error.Ok);
            loaded = ResourceLoader.Load<SpatialCircuitPanelResource>(path);
            var cachedAgain = ResourceLoader.Load<SpatialCircuitPanelResource>(path);
            AssertThat(loaded).IsNotNull();
            AssertThat(cachedAgain).IsNotNull();
            AssertThat(ReferenceEquals(loaded, cachedAgain)).IsTrue();

            var definition = SpatialCircuitResourceAdapter.ToPanelDefinition(loaded!);
            var firstRuntime = new PanelRuntimeInstance(definition);
            var secondRuntime = new PanelRuntimeInstance(definition);
            firstRuntime.Step();

            AssertThat(firstRuntime.CurrentTick).IsEqual(1L);
            AssertThat(secondRuntime.CurrentTick).IsEqual(0L);
            AssertThat(loaded!.PanelId).IsEqual("panel/independent-runtime");
            AssertThat(loaded.Cells.Single().Parameters["high-ticks"]).IsEqual("1");
        }
        finally
        {
            loaded?.Dispose();
            panel.Dispose();
            clock.Dispose();
            RemoveSavedResource(path);
        }
    }

    private static void RemoveSavedResource(string path)
    {
        var absolutePath = ProjectSettings.GlobalizePath(path);
        if (File.Exists(absolutePath))
        {
            DirAccess.RemoveAbsolute(absolutePath);
        }
    }
}
