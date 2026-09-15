using SpatialCircuits.Runner;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class DurableReplayPracticeTests
{
    [Fact]
    public void StageElevenPracticeSavesReloadsAndReplaysNodeExchange()
    {
        var result = DurableReplayPractice.Run();

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotEqual(Guid.Empty, result.SaveId);
        Assert.True(result.Saved);
        Assert.True(result.Reloaded);
        Assert.Equal(5, result.LiveHashes.Length);
        Assert.Equal(result.LiveHashes.ToArray(), result.ReplayHashes.ToArray());
        Assert.NotEmpty(result.Commands);
        Assert.Equal(5, result.LiveNodeCalls);
        Assert.Equal(10, result.Trace.Entries.Length);
        Assert.NotEqual(string.Empty, result.Trace.SaveContentHash);
    }
}
