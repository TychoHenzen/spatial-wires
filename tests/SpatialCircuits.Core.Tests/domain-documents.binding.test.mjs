import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const projectPath = path.join(testDirectory, "SpatialCircuits.Core.Tests.csproj");

function runCSharpTest(testName) {
  const result = spawnSync(
    "dotnet",
    [
      "test",
      projectPath,
      "--filter",
      `FullyQualifiedName=${testName}`,
      "--nologo",
      "--verbosity",
      "minimal",
      "--logger",
      "console;verbosity=detailed",
    ],
    {
      cwd: repositoryRoot,
      encoding: "utf8",
      shell: process.platform === "win32",
    },
  );
  const output = [result.stdout, result.stderr].filter(Boolean).join("\n");
  return { ...result, output };
}

// covers: spatial-circuits/domain-documents-and-runner :: Four-state digital values resolve consistently :: No active drive resolves to high impedance
test("no active drive resolves to high impedance through the core", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DriveResolverTests.NoActiveDriveResolvesToHighImpedance");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DriveResolverTests\.NoActiveDriveResolvesToHighImpedance/);
});

// covers: spatial-circuits/domain-documents-and-runner :: Four-state digital values resolve consistently :: One compatible driven value is preserved
test("one compatible driven value is preserved through the core", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DriveResolverTests.UnanimousActiveDriveIsPreserved");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DriveResolverTests\.UnanimousActiveDriveIsPreserved/);
});

// covers: spatial-circuits/domain-documents-and-runner :: Four-state digital values resolve consistently :: Contention resolves to unknown
test("contention resolves to unknown through the core", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DriveResolverTests.ConflictingAndUnknownDrivesResolveToUnknownRegardlessOfOrder");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DriveResolverTests\.ConflictingAndUnknownDrivesResolveToUnknownRegardlessOfOrder/);
});

// covers: spatial-circuits/domain-documents-and-runner :: Portable definitions use stable identities and ownership :: Document identity survives process boundaries
test("document identity survives separate core processes", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DomainDocumentTests.DocumentIdentitySurvivesProcessBoundaries");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DomainDocumentTests\.DocumentIdentitySurvivesProcessBoundaries/);
});

// covers: spatial-circuits/domain-documents-and-runner :: Portable definitions use stable identities and ownership :: CLR type name is used as persisted behavior identity
test("CLR type names are rejected as behavior identities", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DomainDocumentTests.ClrTypeNameIsRejectedAsBehaviorIdentity");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DomainDocumentTests\.ClrTypeNameIsRejectedAsBehaviorIdentity/);
});

// covers: spatial-circuits/domain-documents-and-runner :: Canonical documents round-trip exactly :: Canonical round trip is byte stable
test("canonical document round trip is byte stable", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DomainDocumentTests.CanonicalRoundTripIsByteStable");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DomainDocumentTests\.CanonicalRoundTripIsByteStable/);
});

// covers: spatial-circuits/domain-documents-and-runner :: Canonical documents round-trip exactly :: Equivalent input ordering is normalized
test("equivalent source ordering has one canonical hash", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DomainDocumentTests.EquivalentInputOrderingIsNormalized");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DomainDocumentTests\.EquivalentInputOrderingIsNormalized/);
});

// covers: spatial-circuits/domain-documents-and-runner :: Validation diagnostics are stable and structured :: Invalid fixture reports a stable diagnostic
test("invalid fixture reports ordered structured diagnostics", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DomainDocumentTests.InvalidFixtureReportsStableOrderedStructuredDiagnostics");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DomainDocumentTests\.InvalidFixtureReportsStableOrderedStructuredDiagnostics/);
});

// covers: spatial-circuits/domain-documents-and-runner :: Validation diagnostics are stable and structured :: Unsupported schema is rejected before instantiation
test("unsupported schema is rejected before runtime activation", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.RuntimeFactoryTests.UnsupportedSchemaIsRejectedBeforeInstantiation");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.RuntimeFactoryTests\.UnsupportedSchemaIsRejectedBeforeInstantiation/);
});

// covers: spatial-circuits/domain-documents-and-runner :: Runtime instances do not share mutable state :: Two instances diverge independently
test("two runtime instances diverge independently", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.RuntimeFactoryTests.TwoInstancesDivergeIndependently");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.RuntimeFactoryTests\.TwoInstancesDivergeIndependently/);
});
