using System.Globalization;
using System.Text.Json;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Runner;
using SpatialCircuits.TestFixtures;
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

    [Fact]
    public void ExternalPatternDetectorFixturePassesItsExactTimedTrace()
    {
        var first = Run(FixturePath("pattern-detector.fixture.json", "stage-05"));
        var second = Run(FixturePath("pattern-detector.fixture.json", "stage-05"));

        Assert.Equal(RunnerApplication.Success, first.ExitCode);
        Assert.Equal(RunnerApplication.Success, second.ExitCode);
        Assert.Equal(first.Output, second.Output);
        var trace = ReadTrace(first.Output);
        Assert.Equal(5, trace.Length);
        Assert.Equal(
            ["HighImpedance", "Low", "Low", "High", "Low"],
            trace.Select(record => Probe(record, "pattern-probe")));
        Assert.All(trace, record => Assert.True(record.GetProperty("passed").GetBoolean()));
    }

    [Fact]
    public void MissingCustomRuleReturnsStableDiagnosticCode()
    {
        var (exitCode, output) = Run(FixturePath("missing-registration.fixture.json", "stage-05"));
        using var document = JsonDocument.Parse(output);

        Assert.Equal(RunnerApplication.InvalidInput, exitCode);
        Assert.Equal(
            "custom-cell.registration-missing",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void ExternalPatternRuleAssemblyHasNoGodotDependency()
    {
        Assert.DoesNotContain(
            typeof(RunnerApplication).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.StartsWith("Godot", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RuleWithTransitiveGodotDependencyIsRejected()
    {
        var ruleReferences = typeof(TransitiveGodotRule).Assembly.GetReferencedAssemblies();
        Assert.Contains(ruleReferences, assembly => assembly.Name == "CustomCell.GodotHelper");
        Assert.DoesNotContain(
            ruleReferences,
            assembly => assembly.Name?.StartsWith("Godot", StringComparison.Ordinal) == true);

        var exception = Assert.Throws<CustomCellRuleException>(() =>
            new CustomCellRuleRegistry(
                [new CustomCellRuleRegistration(
                    new BehaviorId("custom:godot-rule/v1"),
                    [new CustomCellPort("out", CardinalDirection.East, false, true)],
                    TransitiveGodotRule.ValidateParameters,
                    static () => new TransitiveGodotRule())]));

        Assert.Equal(CustomCellDiagnosticCodes.RegistrationInvalid, exception.Code);
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

    private static string FixturePath(string fixtureName, string stage = "stage-04") => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "examples", stage, fixtureName));
}
