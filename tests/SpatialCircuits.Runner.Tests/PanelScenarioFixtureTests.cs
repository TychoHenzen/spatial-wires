using System.Globalization;
using System.Text.Json;
using SpatialCircuits.Runner;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class PanelScenarioFixtureTests
{
    [Theory]
    [InlineData("route-delay.fixture.json")]
    [InlineData("xor-glitch.fixture.json")]
    public void StageFourFixturesPassAndProduceRepeatableTraces(string fixtureName)
    {
        var path = FixturePath(fixtureName);
        var first = Run(path);
        var second = Run(path);

        Assert.Equal(RunnerApplication.Success, first.ExitCode);
        Assert.Equal(RunnerApplication.Success, second.ExitCode);
        Assert.Equal(first.Output, second.Output);
        Assert.All(ReadTrace(first.Output), record =>
            Assert.True(record.GetProperty("passed").GetBoolean()));
    }

    [Fact]
    public void XorFixtureShowsOneTickPulseThatTheTwoTickFilterRejects()
    {
        var (_, output) = Run(FixturePath("xor-glitch.fixture.json"));
        var records = ReadTrace(output).ToDictionary(
            record => record.GetProperty("microtick").GetInt32());

        Assert.Equal("Low", Probe(records[33], "xor-raw-probe"));
        Assert.Equal("High", Probe(records[34], "xor-raw-probe"));
        Assert.Equal("Low", Probe(records[35], "xor-raw-probe"));
        Assert.All(records.Values.Where(record => record.GetProperty("microtick").GetInt32() >= 26),
            record => Assert.Equal("Low", Probe(record, "xor-filtered-probe")));
    }

    private static (int ExitCode, string Output) Run(string path)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = RunnerApplication.Run([path], output);
        return (exitCode, output.ToString());
    }

    private static JsonElement[] ReadTrace(string output)
    {
        var records = new List<JsonElement>();
        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            records.Add(document.RootElement.Clone());
        }

        return records.ToArray();
    }

    private static string Probe(JsonElement record, string probeId) =>
        record.GetProperty("probes").GetProperty(probeId).GetString()!;

    private static string FixturePath(string fixtureName) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "examples", "stage-04", fixtureName));
}
