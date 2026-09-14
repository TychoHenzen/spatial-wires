using System.Globalization;
using System.Text.Json;
using SpatialCircuits.Runner;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class DeviceExchangeScenarioFixtureTests
{
    [Fact]
    public void PublicControllerResponderFixturePrintsTheExpectedTwoLinkTicks()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "examples", "stage-07", "controller-responder.fixture.json"));
        var first = Run(path);
        var second = Run(path);

        Assert.Equal(RunnerApplication.Success, first.ExitCode);
        Assert.Equal(first.Output, second.Output);
        var trace = first.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .ToArray();
        Assert.Equal(10, trace.Length);
        Assert.All(trace, record => Assert.True(record.GetProperty("passed").GetBoolean()));
        Assert.Equal("High", trace[3].GetProperty("challenge").GetString());
        Assert.Equal("lane/challenge", trace[3].GetProperty("deliveries")[0].GetProperty("laneId").GetString());
        Assert.Equal("Low", trace[8].GetProperty("deliveries")[0].GetProperty("signal").GetString());
        Assert.Equal("Low", trace[9].GetProperty("response").GetString());
    }

    private static (int ExitCode, string Output) Run(string path)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = RunnerApplication.Run([path], output);
        return (exitCode, output.ToString());
    }
}
