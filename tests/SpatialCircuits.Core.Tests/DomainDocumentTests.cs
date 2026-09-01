using System.Diagnostics;
using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Core.Tests;

public sealed class DomainDocumentTests
{
    [Fact]
    public void DocumentIdentitySurvivesProcessBoundaries()
    {
        var document = TestDocuments.ValidDocument();
        var fixturePath = Path.Combine(Path.GetTempPath(), $"spatial-circuits-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllBytes(fixturePath, CircuitDocumentCodec.Write(document));
            var first = RunIdentityProbe(fixturePath);
            var second = RunIdentityProbe(fixturePath);

            Assert.Equal(first, second);
            Assert.Equal(
                "document=doc-1;definition=def-and;component=component-a;port=input-a@2,3;behavior=spatial:and/v1",
                first);
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Fact]
    public void ClrTypeNameIsRejectedAsBehaviorIdentity()
    {
        var definition = CircuitDefinition.Create(
            new DefinitionId("def-string"),
            new BehaviorId("System.String, System.Private.CoreLib"),
            []);
        var document = CircuitDocument.Create(new DocumentId("doc"), [definition]);

        var diagnostic = Assert.Single(CircuitDocumentValidator.Validate(document));

        Assert.Equal(DiagnosticCodes.ClrBehaviorType, diagnostic.Code);
        Assert.Equal("$.definitions[0].behaviorId", diagnostic.DocumentPath);
    }

    [Fact]
    public void CanonicalRoundTripIsByteStable()
    {
        var document = TestDocuments.ValidDocument();
        var bytes = CircuitDocumentCodec.Write(document);
        var roundTrip = CircuitDocumentCodec.Write(CircuitDocumentCodec.Read(bytes));

        Assert.Equal(bytes, roundTrip);
        Assert.Equal(
            CircuitDocumentCodec.ComputeContentHashHex(document),
            CircuitDocumentCodec.ComputeContentHashHex(CircuitDocumentCodec.Read(roundTrip)));
    }

    [Fact]
    public void EquivalentInputOrderingIsNormalized()
    {
        var andDefinition = TestDocuments.AndDefinition();
        var otherDefinition = CircuitDefinition.Create(
            new DefinitionId("def-buffer"),
            new BehaviorId("spatial:buffer/v1"),
            [new PortDefinition(new PortId("output"), new GridCoordinate(0, 0))]);
        var first = CircuitDocument.Create(
            new DocumentId("doc"),
            [andDefinition, otherDefinition],
            [
                ComponentDefinition.Create(new ComponentId("component-b"), new DefinitionId("def-buffer"), new GridCoordinate(1, 0)),
                ComponentDefinition.Create(new ComponentId("component-a"), new DefinitionId("def-and"), new GridCoordinate(0, 0))
            ],
            [
                new Ownership(new OwnerId("panel"), new ComponentId("component-b")),
                new Ownership(new OwnerId("panel"), new ComponentId("component-a"))
            ]);
        var second = CircuitDocument.Create(
            new DocumentId("doc"),
            [otherDefinition, andDefinition],
            [
                ComponentDefinition.Create(new ComponentId("component-a"), new DefinitionId("def-and"), new GridCoordinate(0, 0)),
                ComponentDefinition.Create(new ComponentId("component-b"), new DefinitionId("def-buffer"), new GridCoordinate(1, 0))
            ],
            [
                new Ownership(new OwnerId("panel"), new ComponentId("component-a")),
                new Ownership(new OwnerId("panel"), new ComponentId("component-b"))
            ]);

        Assert.Equal(CircuitDocumentCodec.Write(first), CircuitDocumentCodec.Write(second));
        Assert.Equal(
            CircuitDocumentCodec.ComputeContentHashHex(first),
            CircuitDocumentCodec.ComputeContentHashHex(second));
    }

    [Fact]
    public void DocumentCollectionsDoNotExposeMutableSourceState()
    {
        var definitions = new[] { TestDocuments.AndDefinition() };
        var components = new[]
        {
            ComponentDefinition.Create(
                new ComponentId("component-a"),
                new DefinitionId("def-and"),
                new GridCoordinate(0, 0))
        };
        var document = CircuitDocument.Create(new DocumentId("doc"), definitions, components);

        definitions[0] = CircuitDefinition.Create(
            new DefinitionId("replacement"),
            new BehaviorId("spatial:replacement/v1"),
            []);
        components[0] = ComponentDefinition.Create(
            new ComponentId("replacement"),
            new DefinitionId("replacement"),
            new GridCoordinate(9, 9));

        Assert.Equal("def-and", Assert.Single(document.Definitions).Id.Value);
        Assert.Equal("component-a", Assert.Single(document.Components).Id.Value);
    }

    [Fact]
    public void InvalidFixtureReportsStableOrderedStructuredDiagnostics()
    {
        var invalidDefinition = CircuitDefinition.Create(
            new DefinitionId("Bad Definition"),
            new BehaviorId("spatial:bad/v1"),
            [new PortDefinition(new PortId("Bad Port"), new GridCoordinate(0, 0))],
            [new ParameterDefinition("Bad Parameter", "0")]);
        var invalidComponent = ComponentDefinition.Create(
            new ComponentId("Bad Component"),
            new DefinitionId("Bad Definition"),
            new GridCoordinate(0, 0));
        var missingReference = ComponentDefinition.Create(
            new ComponentId("component-missing"),
            new DefinitionId("not-present"),
            new GridCoordinate(1, 0));
        var unknownParameter = ComponentDefinition.Create(
            new ComponentId("component-parameters"),
            new DefinitionId("Bad Definition"),
            new GridCoordinate(2, 0),
            [new KeyValuePair<string, string>("unknown", "1")]);
        var document = CircuitDocument.Create(
            new DocumentId("Bad Document"),
            [invalidDefinition],
            [invalidComponent, missingReference, unknownParameter],
            [
                new Ownership(new OwnerId("owner-a"), new ComponentId("component-parameters")),
                new Ownership(new OwnerId("owner-b"), new ComponentId("component-parameters"))
            ]);

        var first = CircuitDocumentValidator.Validate(document);
        var second = CircuitDocumentValidator.Validate(document);

        Assert.Equal(first, second);
        Assert.Equal(
            [
                (DiagnosticCodes.InvalidDocumentId, "$.documentId"),
                (DiagnosticCodes.InvalidDefinitionId, "$.definitions[0].id"),
                (DiagnosticCodes.InvalidPortId, "$.definitions[0].ports[0].id"),
                (DiagnosticCodes.InvalidParameter, "$.definitions[0].parameters[0].name"),
                (DiagnosticCodes.InvalidComponentId, "$.components[0].id"),
                (DiagnosticCodes.MissingDefinitionReference, "$.components[1].definitionId"),
                (DiagnosticCodes.UnknownParameter, "$.components[2].parameters[0].name"),
                (DiagnosticCodes.OwnershipConflict, "$.ownerships[1].componentId")
            ],
            first.Select(diagnostic => (diagnostic.Code, diagnostic.DocumentPath)));
        Assert.All(first, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        });
    }

    private static string RunIdentityProbe(string fixturePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "tests",
            "SpatialCircuits.Core.Tests",
            "ProcessProbe",
            "SpatialCircuits.Core.Tests.ProcessProbe.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(fixturePath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Identity probe did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Identity probe exited {process.ExitCode}.\n{stderr}");
        return stdout.Trim();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpatialWires.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate SpatialWires.sln.");
    }
}
