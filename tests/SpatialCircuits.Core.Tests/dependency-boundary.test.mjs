import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const projectPath = path.join(testDirectory, "SpatialCircuits.Core.Tests.csproj");

// covers: spatial-circuits/package-baseline :: Enforced dependency boundary :: Godot dependency does not cross the boundary
test("pure projects reject Godot project and assembly dependencies", () => {
  const result = spawnSync(
    "dotnet",
    [
      "test",
      projectPath,
      "--filter",
      "FullyQualifiedName~SpatialCircuits.Core.Tests.DependencyBoundaryTests",
      "--nologo",
    ],
    {
      cwd: repositoryRoot,
      encoding: "utf8",
      shell: process.platform === "win32",
    },
  );

  assert.equal(
    result.status,
    0,
    `Dependency boundary tests failed.\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}`,
  );
});
