namespace SpatialCircuits.Runner;

public static class RunnerApplication
{
    public const int Success = 0;
    public const int InvalidInput = 2;
    public const int FailedExpectation = 3;

    public static int Run(IReadOnlyList<string> arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        if (arguments.Count != 1)
        {
            return Fail(output, new FixtureDiagnostic(
                FixtureDiagnosticCodes.PathRequired,
                "$args[0]",
                "Pass one fixture file path."));
        }

        var readResult = ReadFixtureFile(arguments[0]);
        if (readResult.Diagnostics.Count > 0)
        {
            return Fail(output, readResult.Diagnostics);
        }

        var fixture = readResult.Fixture!;
        var diagnostics = FixtureValidator.Validate(fixture);
        if (diagnostics.Count > 0)
        {
            return Fail(output, diagnostics);
        }

        var records = FixtureExecutor.Execute(fixture);
        foreach (var record in records)
        {
            TraceOutput.WriteTrace(output, record);
        }

        return records.All(record => record.Passed) ? Success : FailedExpectation;
    }

    private static FixtureReadResult ReadFixtureFile(string path)
    {
        if (!File.Exists(path))
        {
            return Failure(
                FixtureDiagnosticCodes.FileNotFound,
                "$args[0]",
                "Fixture file does not exist.");
        }

        try
        {
            return FixtureCodec.Read(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return Failure(
                FixtureDiagnosticCodes.FileUnreadable,
                "$args[0]",
                "Fixture file could not be read.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                FixtureDiagnosticCodes.FileUnreadable,
                "$args[0]",
                "Fixture file could not be read.");
        }
    }

    private static int Fail(TextWriter output, FixtureDiagnostic diagnostic) => Fail(output, [diagnostic]);

    private static int Fail(TextWriter output, IReadOnlyList<FixtureDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            TraceOutput.WriteDiagnostic(output, diagnostic);
        }

        return InvalidInput;
    }

    private static FixtureReadResult Failure(string code, string path, string message) =>
        new(null, [new FixtureDiagnostic(code, path, message)]);
}
