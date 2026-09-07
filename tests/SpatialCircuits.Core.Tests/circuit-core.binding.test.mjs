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

test("no active drive resolves to high impedance through the core", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DriveResolverTests.NoActiveDriveResolvesToHighImpedance");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DriveResolverTests\.NoActiveDriveResolvesToHighImpedance/);
});

test("one compatible driven value is preserved through the core", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DriveResolverTests.UnanimousActiveDriveIsPreserved");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DriveResolverTests\.UnanimousActiveDriveIsPreserved/);
});

test("contention resolves to unknown through the core", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.DriveResolverTests.ConflictingAndUnknownDrivesResolveToUnknownRegardlessOfOrder");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.DriveResolverTests\.ConflictingAndUnknownDrivesResolveToUnknownRegardlessOfOrder/);
});

test("CLR type names are rejected as behavior identities", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.CircuitTests.ClrTypeNameIsRejectedAsBehaviorIdentity");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.CircuitTests\.ClrTypeNameIsRejectedAsBehaviorIdentity/);
});

test("invalid circuits report ordered structured diagnostics", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.CircuitTests.InvalidCircuitReportsStableOrderedStructuredDiagnostics");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.CircuitTests\.InvalidCircuitReportsStableOrderedStructuredDiagnostics/);
});

test("invalid circuits are rejected before runtime activation", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.RuntimeFactoryTests.InvalidCircuitIsRejectedBeforeInstantiation");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.RuntimeFactoryTests\.InvalidCircuitIsRejectedBeforeInstantiation/);
});

test("two runtime instances diverge independently", () => {
  const result = runCSharpTest("SpatialCircuits.Core.Tests.RuntimeFactoryTests.TwoInstancesDivergeIndependently");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Passed SpatialCircuits\.Core\.Tests\.RuntimeFactoryTests\.TwoInstancesDivergeIndependently/);
});
