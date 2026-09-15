using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SpatialCircuits.Cells;
using SpatialCircuits.Runner;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: SpatialCircuits.Benchmarks <fixture.json> [output.json]");
    return 2;
}

var fixturePath = Path.GetFullPath(args[0]);
if (!File.Exists(fixturePath))
{
    Console.Error.WriteLine($"Benchmark fixture does not exist: '{fixturePath}'.");
    return 2;
}

var fixtureBytes = File.ReadAllBytes(fixturePath);
var read = FixtureCodec.Read(fixtureBytes);
if (read.Diagnostics.Count > 0 || read.Fixture?.PanelScenario is not { } plan)
{
    Console.Error.WriteLine("Benchmark fixture must contain a readable panel scenario.");
    return 2;
}

var comparison = PanelExecutionComparisonRunner.Compare(plan);
if (!comparison.Equivalent)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(comparison.Divergence));
    return 3;
}

var panel = plan.CreatePanelDefinition();
var report = new BenchmarkReport(
    1,
    read.Fixture.FixtureId,
    Convert.ToHexString(SHA256.HashData(fixtureBytes)).ToLowerInvariant(),
    checked(panel.Width * panel.Height),
    panel.Cells.OfType<PanelCellDefinition>().Count(cell => cell.Kind != CellKind.Empty),
    Environment.GetEnvironmentVariable("CONFIGURATION") ?? "Release",
    new BenchmarkMachine(
        DateTimeOffset.UtcNow,
        RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        RuntimeInformation.FrameworkDescription,
        Environment.ProcessorCount,
        GCSettings.IsServerGC),
    [Measure(plan, PanelExecutionMode.Reference), Measure(plan, PanelExecutionMode.Optimized)],
    comparison);

var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (args.Length == 2)
{
    var outputPath = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, json + Environment.NewLine);
}

Console.WriteLine(json);
return 0;

static BenchmarkMeasurement Measure(PanelScenarioPlan plan, PanelExecutionMode mode)
{
    const int warmupRuns = 1;
    const int measuredRuns = 5;
    for (var index = 0; index < warmupRuns; index++)
    {
        _ = PanelExecutionComparisonRunner.Execute(plan, mode);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var startAllocated = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();
    for (var index = 0; index < measuredRuns; index++)
    {
        _ = PanelExecutionComparisonRunner.Execute(plan, mode);
    }

    stopwatch.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - startAllocated;
    var microticks = (double)plan.Microticks * measuredRuns;
    var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, double.Epsilon);
    return new BenchmarkMeasurement(
        mode.ToString(),
        microticks / seconds,
        allocated / microticks,
        stopwatch.Elapsed.TotalMilliseconds / microticks);
}

public sealed record BenchmarkReport(
    int SchemaVersion,
    string FixtureId,
    string FixtureHash,
    int CellCount,
    int ActiveFrontier,
    string BuildConfiguration,
    BenchmarkMachine Machine,
    ImmutableArray<BenchmarkMeasurement> Measurements,
    PanelExecutionComparison Comparison);

public sealed record BenchmarkMachine(
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string ProcessArchitecture,
    string Runtime,
    int ProcessorCount,
    bool ServerGc);

public sealed record BenchmarkMeasurement(
    string ExecutionMode,
    double MicroticksPerSecond,
    double AllocatedBytesPerMicrotick,
    double FrameTimeMilliseconds);
