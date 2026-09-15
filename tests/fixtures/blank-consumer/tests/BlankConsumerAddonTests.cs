using System.Text.Json;
using GdUnit4;
using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.GodotAdapter;
using SpatialCircuits.Hierarchy;
using static GdUnit4.Assertions;

namespace SpatialWires.BlankConsumer.Tests;

[TestSuite]
public sealed class BlankConsumerAddonTests
{
    private const string ProductPlugin = "res://addons/spatial_circuits/plugin.cfg";
    private const string ProductPluginScript = "res://addons/spatial_circuits/SpatialCircuitsEditorPlugin.cs";

    [TestCase]
    [RequireGodotRuntime]
    public void ImportedProjectDiscoversAndEnablesThePortableAddon()
    {
        AssertThat(ProjectSettings.HasSetting("editor_plugins/enabled")).IsTrue();

        var enabledPlugins = ProjectSettings.GetSetting("editor_plugins/enabled").AsStringArray();
        AssertThat(enabledPlugins.Contains(ProductPlugin)).IsTrue();

        var pluginConfiguration = new ConfigFile();
        AssertThat((long)pluginConfiguration.Load(ProductPlugin)).IsEqual((long)Error.Ok);
        AssertThat(pluginConfiguration.GetValue("plugin", "script").AsString())
            .IsEqual("SpatialCircuitsEditorPlugin.cs");
        AssertThat(ResourceLoader.Exists(ProductPluginScript)).IsTrue();
        AssertThat(ResourceLoader.Load<CSharpScript>(ProductPluginScript)).IsNotNull();

        var plugin = new SpatialCircuitsEditorPlugin();
        var node = new SpatialCircuitNode();
        var resource = new SpatialCircuitResource();
        try
        {
            AssertThat(plugin).IsNotNull();
            AssertThat(node).IsNotNull();
            AssertThat(resource).IsNotNull();
        }
        finally
        {
            plugin.Free();
            node.Free();
            resource.Dispose();
        }
    }

    // covers: spatial-circuits/package-baseline :: Clean editor plugin lifecycle :: Plugin is disabled cleanly
    // covers: spatial-circuits/package-baseline :: Clean editor plugin lifecycle :: Plugin is enabled again
    [TestCase]
    [RequireGodotRuntime]
    public void HeadlessEditorEnableDisableAndReenableHasOneCleanIntegration()
    {
        using var report = LoadEditorLifecycleReport();

        var enabled = report.RootElement.GetProperty("enabled");
        AssertThat(enabled.GetProperty("plugin_enabled").GetBoolean()).IsTrue();
        AssertThat(enabled.GetProperty("plugin_instance_count").GetInt32()).IsEqual(1);
        AssertThat(enabled.GetProperty("dock_count").GetInt32()).IsEqual(1);
        AssertThat(enabled.GetProperty("handler_count").GetInt32()).IsEqual(1);

        var disabled = report.RootElement.GetProperty("disabled");
        AssertThat(disabled.GetProperty("plugin_enabled").GetBoolean()).IsFalse();
        AssertThat(disabled.GetProperty("plugin_instance_count").GetInt32()).IsEqual(0);
        AssertThat(disabled.GetProperty("dock_count").GetInt32()).IsEqual(0);
        AssertThat(disabled.GetProperty("handler_count").GetInt32()).IsEqual(0);

        var reenabled = report.RootElement.GetProperty("reenabled");
        AssertThat(reenabled.GetProperty("plugin_enabled").GetBoolean()).IsTrue();
        AssertThat(reenabled.GetProperty("plugin_instance_count").GetInt32()).IsEqual(1);
        AssertThat(reenabled.GetProperty("dock_count").GetInt32()).IsEqual(1);
        AssertThat(reenabled.GetProperty("handler_count").GetInt32()).IsEqual(1);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BlankProjectRunsATwoDeviceTimedExchange()
    {
        var controllerPanel = PanelDefinition.Create(
            new CircuitId("panel/blank-controller"),
            3,
            2,
            [
                InputPort("challenge-in", 0, 0, "challenge-drive"),
                Wire("challenge-wire", 1, 0),
                OutputPort("challenge-out", 2, 0, "challenge"),
                InputPort("response-in", 0, 1, "response-in"),
                Wire("response-wire", 1, 1),
                OutputPort("response-out", 2, 1, "response")
            ]);
        var responderPanel = PanelDefinition.Create(
            new CircuitId("panel/blank-responder"),
            3,
            1,
            [
                InputPort("challenge-in", 0, 0, "challenge"),
                Wire("response-wire", 1, 0),
                OutputPort("response-out", 2, 0, "response")
            ]);
        var controller = DeviceDefinition.Create(
            new ComponentId("device/blank-controller"),
            PanelDeviceBackendDefinition.Create(controllerPanel));
        var responder = DeviceDefinition.Create(
            new ComponentId("device/blank-responder"),
            PanelDeviceBackendDefinition.Create(responderPanel));
        var graph = new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/blank-exchange"),
            [controller, responder],
            [
                CableLaneDefinition.Create(
                    new ComponentId("lane/challenge"),
                    DevicePortEndpoint.Create(controller.Id, "challenge"),
                    DevicePortEndpoint.Create(responder.Id, "challenge"),
                    2),
                CableLaneDefinition.Create(
                    new ComponentId("lane/response"),
                    DevicePortEndpoint.Create(responder.Id, "response"),
                    DevicePortEndpoint.Create(controller.Id, "response-in"),
                    2)
            ]));

        graph.SetInput(controller.Id, "challenge-drive", DeviceSignal.Scalar(LogicValue.High));
        for (var tick = 0; tick < 16; tick++)
        {
            graph.Step();
        }

        AssertThat(graph.GetOutput(controller.Id, "response")).IsEqual(
            DeviceSignal.Scalar(LogicValue.High));
    }

    private static PanelCellDefinition InputPort(string id, int x, int y, string port) =>
        PanelCellDefinition.Create(
            new ComponentId(id),
            new GridCoordinate(x, y),
            CellKind.InputPort,
            CardinalDirection.East,
            new PortId(port));

    private static PanelCellDefinition OutputPort(string id, int x, int y, string port) =>
        PanelCellDefinition.Create(
            new ComponentId(id),
            new GridCoordinate(x, y),
            CellKind.OutputPort,
            CardinalDirection.East,
            new PortId(port));

    private static PanelCellDefinition Wire(string id, int x, int y) =>
        PanelCellDefinition.Create(
            new ComponentId(id),
            new GridCoordinate(x, y),
            CellKind.Wire,
            CardinalDirection.East);

    private static JsonDocument LoadEditorLifecycleReport()
    {
        var reportPath = System.Environment.GetEnvironmentVariable("SPATIAL_WIRES_EDITOR_LIFECYCLE_REPORT");
        AssertThat(string.IsNullOrWhiteSpace(reportPath)).IsFalse();
        AssertThat(File.Exists(reportPath!))
            .OverrideFailureMessage($"Headless editor lifecycle probe report does not exist: '{reportPath}'.")
            .IsTrue();
        return JsonDocument.Parse(File.ReadAllText(reportPath!));
    }
}
