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
    public void PublicNodeAndResourceAreConstructibleWithStableGlobalClassMetadataWithoutState()
    {
        var node = new SpatialCircuitNode();
        var resource = new SpatialCircuitResource();
        try
        {
            AssertThat(node.GetType()).IsEqual(typeof(SpatialCircuitNode));
            AssertThat(resource.GetType()).IsEqual(typeof(SpatialCircuitResource));
            AssertPublicShellType<SpatialCircuitNode, Node>("SpatialCircuitNode");
            AssertPublicShellType<SpatialCircuitResource, Resource>("SpatialCircuitResource");
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
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)).IsEmpty();
        AssertThat(publicType.GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)).IsEmpty();
    }
}
