using System.Reflection;
using Godot;
using SpatialCircuits.GodotAdapter;
using Xunit;

namespace SpatialWires.Godot.Tests;

public sealed class PublicAddonTypeTests
{
    [Fact]
    public void PublicNodeAndResourceHaveStableGlobalClassMetadataWithoutState()
    {
        AssertPublicShellType<SpatialCircuitNode, Node>("SpatialCircuitNode");
        AssertPublicShellType<SpatialCircuitResource, Resource>("SpatialCircuitResource");
    }

    private static void AssertPublicShellType<TPublicType, TGodotBase>(string expectedName)
    {
        var publicType = typeof(TPublicType);

        Assert.True(publicType.IsPublic);
        Assert.False(publicType.IsAbstract);
        Assert.Equal(expectedName, publicType.Name);
        Assert.Equal(typeof(TGodotBase), publicType.BaseType);
        Assert.True(publicType.IsDefined(typeof(GlobalClassAttribute), inherit: false));
        Assert.NotNull(publicType.GetConstructor(Type.EmptyTypes));
        Assert.Empty(publicType.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        Assert.Empty(publicType.GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
    }
}
