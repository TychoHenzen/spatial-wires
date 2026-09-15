using System.Reflection;
using GdUnit4;
using Godot;
using SpatialCircuits.GodotAdapter;
using static GdUnit4.Assertions;

namespace SpatialWires.Godot.Tests;

[TestSuite]
public sealed class PublicAddonTypeTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void PublicNodeAndResourceAreConstructibleWithStableGlobalClassMetadata()
    {
        var node = new SpatialCircuitNode();
        var resource = new SpatialCircuitResource();
        try
        {
            AssertThat(node.GetType()).IsEqual(typeof(SpatialCircuitNode));
            AssertThat(resource.GetType()).IsEqual(typeof(SpatialCircuitResource));
            AssertPublicShellType<SpatialCircuitNode, Node>("SpatialCircuitNode");
            var deviceStep = typeof(SpatialCircuitNode).GetEvent(
                "DeviceStep", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            AssertThat(deviceStep).IsNotNull();
            AssertThat(deviceStep!.EventHandlerType).IsEqual(typeof(Action<SpatialCircuitNodeStepContext>));
            AssertExportedResourceType<SpatialCircuitResource>("SpatialCircuitResource",
                "BehaviorVersion", "ChildNetwork", "DefinitionId", "Parameters", "Ports", "SchemaVersion",
                "SourcePanel", "Symbol");
            AssertExportedResourceType<SpatialCircuitPanelResource>("SpatialCircuitPanelResource",
                "Cells", "Height", "PanelId", "Width");
            AssertExportedResourceType<SpatialCircuitCellResource>("SpatialCircuitCellResource",
                "BehaviorId", "CellId", "Kind", "Orientation", "Parameters", "PortId", "X", "Y");
            AssertExportedResourceType<SpatialCircuitChipPortResource>("SpatialCircuitChipPortResource",
                "Direction", "Name", "PanelPortId");
            AssertExportedResourceType<SpatialCircuitChildNetworkResource>("SpatialCircuitChildNetworkResource",
                "Connections", "Instances");
            AssertExportedResourceType<SpatialCircuitChildInstanceResource>("SpatialCircuitChildInstanceResource",
                "ContentHash", "DefinitionId", "InstanceId");
            AssertExportedResourceType<SpatialCircuitChildConnectionResource>(
                "SpatialCircuitChildConnectionResource",
                "SourceInstanceId", "SourcePortName", "TargetInstanceId", "TargetPortName");
        }
        finally
        {
            node.Free();
            resource.Dispose();
        }
    }

    private static void AssertPublicShellType<TPublicType, TGodotBase>(string expectedName)
    {
        var publicType = typeof(TPublicType);

        AssertThat(publicType.IsPublic).IsTrue();
        AssertThat(publicType.IsAbstract).IsFalse();
        AssertThat(publicType.Name).IsEqual(expectedName);
        AssertThat(publicType.BaseType).IsEqual(typeof(TGodotBase));
        AssertThat(publicType.IsDefined(typeof(GlobalClassAttribute), inherit: false)).IsTrue();
        AssertThat(publicType.GetConstructor(Type.EmptyTypes)).IsNotNull();
        AssertThat(publicType.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)).IsEmpty();
        AssertThat(publicType.GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)).IsEmpty();
    }

    private static void AssertExportedResourceType<TPublicType>(string expectedName, params string[] expectedPropertyNames)
    {
        var publicType = typeof(TPublicType);
        var properties = publicType.GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        AssertThat(publicType.IsPublic).IsTrue();
        AssertThat(publicType.IsAbstract).IsFalse();
        AssertThat(publicType.Name).IsEqual(expectedName);
        AssertThat(publicType.BaseType).IsEqual(typeof(Resource));
        AssertThat(publicType.IsDefined(typeof(GlobalClassAttribute), inherit: false)).IsTrue();
        AssertThat(publicType.GetConstructor(Type.EmptyTypes)).IsNotNull();
        var actualPropertyNames = properties.Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        AssertThat(actualPropertyNames.SequenceEqual(expectedPropertyNames.OrderBy(name => name, StringComparer.Ordinal)))
            .IsTrue();
        foreach (var property in properties)
        {
            AssertThat(property.GetCustomAttribute<ExportAttribute>()).IsNotNull();
        }
    }
}
