using System.Globalization;
using System.Text.Json;
using SpatialCircuits.Runner;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class TamperDetectionFixtureTests
{
    [Fact]
    public void PublicTamperFixtureCoversTheRecordedWindowAndAlarmCases()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "examples", "stage-12", "tamper-detection.fixture.json"));
        var first = Run(path);
        var second = Run(path);

        Assert.Equal(RunnerApplication.Success, first.ExitCode);
        Assert.Equal(first.Output, second.Output);
        var records = first.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .ToArray();
        Assert.Equal(7_000, records.Length);
        Assert.All(records, record => Assert.True(record.GetProperty("passed").GetBoolean()));
        Assert.Contains(records, record =>
            record.GetProperty("caseId").GetString() == "intact" &&
            record.GetProperty("microtick").GetInt32() == 512 &&
            record.GetProperty("challenge").GetString() is not null);
        Assert.Contains(records, record =>
            record.GetProperty("caseId").GetString() == "disconnected-missing" &&
            record.GetProperty("decision").GetString() == "missing" &&
            record.GetProperty("microtick").GetInt32() == 97);
    }

    private static (int ExitCode, string Output) Run(string path)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = RunnerApplication.Run([path], output);
        return (exitCode, output.ToString());
    }
}
