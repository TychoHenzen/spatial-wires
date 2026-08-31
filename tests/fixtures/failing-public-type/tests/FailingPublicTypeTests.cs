using GdUnit4;
using SpatialCircuits.GodotAdapter;
using static GdUnit4.Assertions;

namespace SpatialWires.FailingPublicType.Tests;

[TestSuite]
public sealed class FailingPublicTypeTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void DeliberatelyWrongPublicNodeTypeAssertionFails()
    {
        var node = new SpatialCircuitNode();
        try
        {
            AssertThat(node.GetType().Name)
                .OverrideFailureMessage("EXPECTED_NEGATIVE_PUBLIC_TYPE_ASSERTION")
                .IsEqual("DeliberatelyWrongSpatialCircuitNode");
        }
        finally
        {
            node.Free();
        }
    }
}
