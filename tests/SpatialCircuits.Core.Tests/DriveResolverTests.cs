using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Core.Tests;

public sealed class DriveResolverTests
{
    [Fact]
    public void NoActiveDriveResolvesToHighImpedance()
    {
        Assert.Equal(LogicValue.HighImpedance, DriveResolver.Resolve([]));
        Assert.Equal(LogicValue.HighImpedance, DriveResolver.Resolve([LogicValue.HighImpedance, LogicValue.HighImpedance]));
    }

    [Fact]
    public void UnanimousActiveDriveIsPreserved()
    {
        Assert.Equal(LogicValue.Low, DriveResolver.Resolve([LogicValue.Low, LogicValue.HighImpedance, LogicValue.Low]));
        Assert.Equal(LogicValue.High, DriveResolver.Resolve([LogicValue.High, LogicValue.High]));
    }

    [Fact]
    public void ConflictingAndUnknownDrivesResolveToUnknownRegardlessOfOrder()
    {
        Assert.Equal(LogicValue.Unknown, DriveResolver.Resolve([LogicValue.Low, LogicValue.High]));
        Assert.Equal(LogicValue.Unknown, DriveResolver.Resolve([LogicValue.High, LogicValue.Low]));
        Assert.Equal(LogicValue.Unknown, DriveResolver.Resolve([LogicValue.Unknown, LogicValue.Low]));
        Assert.Equal(LogicValue.Unknown, DriveResolver.Resolve([LogicValue.Low, LogicValue.Unknown]));
    }

    [Fact]
    public void EveryThreeDriveCombinationIsOrderIndependent()
    {
        var values = Enum.GetValues<LogicValue>();

        foreach (var first in values)
        {
            foreach (var second in values)
            {
                foreach (var third in values)
                {
                    var expected = DriveResolver.Resolve([first, second, third]);
                    Assert.Equal(expected, DriveResolver.Resolve([third, first, second]));
                    Assert.Equal(expected, DriveResolver.Resolve([second, third, first]));
                    Assert.Equal(expected, DriveResolver.Resolve([third, second, first]));
                }
            }
        }
    }
}
