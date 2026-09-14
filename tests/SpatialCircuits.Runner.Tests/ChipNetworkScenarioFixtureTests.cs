using System.Globalization;
using System.Text.Json;
using SpatialCircuits.Runner;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class ChipNetworkScenarioFixtureTests
{
    [Fact]
    public void PackagedXorRunsTwoIndependentlyDrivenPinnedInstancesWithExactTracesAndHashes()
    {
        var first = Run(FixturePath());
        var second = Run(FixturePath());

        Assert.Equal(RunnerApplication.Success, first.ExitCode);
        Assert.Equal(RunnerApplication.Success, second.ExitCode);
        Assert.Equal(first.Output, second.Output);

        var trace = ReadTrace(first.Output);
        Assert.Equal(84, trace.Length);
        Assert.All(trace, record =>
        {
            Assert.True(record.GetProperty("passed").GetBoolean());
            Assert.Equal(
                record.GetProperty("hash").GetString(),
                record.GetProperty("expectedHash").GetString());
            Assert.Equal(
                record.GetProperty("outputs").GetRawText(),
                record.GetProperty("expectedOutputs").GetRawText());
            Assert.Equal(
                "aa37541038ae4e787d354d88d9d2df605521852e6edf322251efbd46856b827a",
                record.GetProperty("contentHash").GetString());
        });

        var stableOutputs = trace
            .Where(record => record.GetProperty("microtick").GetInt32() == 33)
            .ToDictionary(
                record => record.GetProperty("chipInstanceId").GetString()!,
                record => record.GetProperty("outputs").GetProperty("y").GetString()!);
        Assert.Equal("Low", stableOutputs["left"]);
        Assert.Equal("High", stableOutputs["right"]);

        var updatedOutputs = trace
            .Where(record => record.GetProperty("microtick").GetInt32() == 35)
            .ToDictionary(
                record => record.GetProperty("chipInstanceId").GetString()!,
                record => record.GetProperty("outputs").GetProperty("y").GetString()!);
        Assert.Equal("High", updatedOutputs["left"]);
        Assert.Equal("Low", updatedOutputs["right"]);
    }

    [Fact]
    public void PackagedXorRejectsAnInstanceWithAChangedContentPin()
    {
        var fixtureJson = File.ReadAllText(FixturePath());
        var changedJson = fixtureJson.Replace(
            "aa37541038ae4e787d354d88d9d2df605521852e6edf322251efbd46856b827a",
            new string('0', 64),
            StringComparison.Ordinal);
        var read = FixtureCodec.Read(System.Text.Encoding.UTF8.GetBytes(changedJson));

        Assert.Empty(read.Diagnostics);
        Assert.Contains(
            FixtureValidator.Validate(read.Fixture!),
            diagnostic => diagnostic.Code == FixtureDiagnosticCodes.ChipScenarioInvalid);
    }

    [Theory]
    [InlineData("not a stable id", "signal")]
    [InlineData("left", "not a port")]
    public void MalformedChipInputEndpointsReturnFixtureDiagnostics(string instanceId, string portName)
    {
        var fixture = FixtureCodec.Read(File.ReadAllBytes(FixturePath())).Fixture!;
        var scenario = fixture.ChipNetworkScenario!;
        var invalidScenario = scenario with
        {
            Connections = scenario.Connections.Add(new ChipPortConnectionPlan(
                new ChipPortEndpointPlan("left", "y"),
                new ChipPortEndpointPlan("right", "a"))),
            Inputs = scenario.Inputs.Add(new ChipNetworkInputChange(0, instanceId, portName, "High"))
        };
        var invalidFixture = new RunnerFixture(
            fixture.FixtureSchema,
            fixture.TraceSchema,
            fixture.FixtureId,
            fixture.Action,
            fixture.Cases,
            chipNetworkScenario: invalidScenario);

        Assert.Contains(
            FixtureValidator.Validate(invalidFixture),
            diagnostic => diagnostic.Code == FixtureDiagnosticCodes.ChipScenarioInvalid);
    }

    private static (int ExitCode, string Output) Run(string path)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        return (RunnerApplication.Run([path], output), output.ToString());
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

    private static string FixturePath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "examples", "stage-06", "xor-chip.fixture.json"));
}
