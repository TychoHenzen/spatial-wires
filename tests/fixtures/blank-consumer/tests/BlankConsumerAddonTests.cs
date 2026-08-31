using System.Text.Json;
using GdUnit4;
using Godot;
using SpatialCircuits.GodotAdapter;
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
