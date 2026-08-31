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
}
