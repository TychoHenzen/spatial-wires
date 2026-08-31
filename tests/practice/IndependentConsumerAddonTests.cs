using GdUnit4;
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
}
