using System.Text.RegularExpressions;
using SpatialCircuits.Core;

namespace SpatialCircuits.Runner;

public static partial class FixtureValidator
{
    public static IReadOnlyList<FixtureDiagnostic> Validate(RunnerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var diagnostics = new List<FixtureDiagnostic>();
        if (fixture.FixtureSchema != FixtureVersion.Current)
        {
            Add(diagnostics, FixtureDiagnosticCodes.SchemaUnsupported, "$.fixtureSchema", "Fixture schema is not supported.");
        }

        if (fixture.TraceSchema != TraceVersion.Current)
        {
            Add(diagnostics, FixtureDiagnosticCodes.TraceSchemaUnsupported, "$.traceSchema", "Trace schema is not supported.");
        }

        if (!StableIdPattern().IsMatch(fixture.FixtureId))
        {
            Add(diagnostics, FixtureDiagnosticCodes.IdInvalid, "$.fixtureId", "Fixture identifier is not stable data.");
        }

        if (fixture.Action != FixtureAction.ResolveDrives)
        {
            Add(diagnostics, FixtureDiagnosticCodes.ActionUnsupported, "$.action", "Fixture action is not supported.");
        }

        if (fixture.Cases.Length == 0)
        {
            Add(diagnostics, FixtureDiagnosticCodes.CasesRequired, "$.cases", "Fixture must contain at least one case.");
        }

        ValidateCases(fixture, diagnostics);
        return diagnostics.AsReadOnly();
    }

    private static void ValidateCases(RunnerFixture fixture, ICollection<FixtureDiagnostic> diagnostics)
    {
        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        for (var caseIndex = 0; caseIndex < fixture.Cases.Length; caseIndex++)
        {
            var item = fixture.Cases[caseIndex];
            var path = $"$.cases[{caseIndex}]";
            if (!StableIdPattern().IsMatch(item.CaseId))
            {
                Add(diagnostics, FixtureDiagnosticCodes.CaseIdInvalid, $"{path}.caseId", "Case identifier is not stable data.");
            }
            else if (!caseIds.Add(item.CaseId))
            {
                Add(diagnostics, FixtureDiagnosticCodes.CaseIdDuplicate, $"{path}.caseId", "Case identifier is duplicated.");
            }

            for (var driveIndex = 0; driveIndex < item.Drives.Length; driveIndex++)
            {
                if (!FixtureLogicValue.TryParse(item.Drives[driveIndex], out _))
                {
                    Add(
                        diagnostics,
                        FixtureDiagnosticCodes.DriveInvalid,
                        $"{path}.drives[{driveIndex}]",
                        "Drive value is not a four-state logic value.");
                }
            }

            if (!FixtureLogicValue.TryParse(item.Expected, out _))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.ExpectedInvalid,
                    $"{path}.expected",
                    "Expected value is not a four-state logic value.");
            }
        }
    }

    private static void Add(
        ICollection<FixtureDiagnostic> diagnostics,
        string code,
        string path,
        string message) => diagnostics.Add(new FixtureDiagnostic(code, path, message));

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdPattern();
}
