using System.Globalization;
using System.Text.Json;
using SpatialCircuits.Runner;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class DurableReplayFixtureTests
{
    [Fact]
    public void PublicDurableReplayFixturePrintsMatchingLiveAndOfflineHashes()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "examples", "stage-11", "durable-replay.fixture.json"));
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = RunnerApplication.Run([path], output);
        var trace = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();

        Assert.Equal(RunnerApplication.Success, exitCode);
        Assert.Equal(5, trace.Length);
        Assert.All(trace, record =>
        {
            Assert.True(record.GetProperty("passed").GetBoolean());
            Assert.Equal(
                record.GetProperty("liveHash").GetString(),
                record.GetProperty("replayHash").GetString());
            Assert.True(record.GetProperty("saved").GetBoolean());
            Assert.True(record.GetProperty("reloaded").GetBoolean());
        });
        Assert.True(trace[0].GetProperty("recordedCommandCount").GetInt32() > 0);
    }
}
