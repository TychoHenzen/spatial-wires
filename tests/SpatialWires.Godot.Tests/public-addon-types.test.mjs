import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const projectPath = path.join(testDirectory, "SpatialWires.Godot.Tests.csproj");

// covers: spatial-circuits/package-baseline :: Public Node and Resource types :: Consumer uses public addon types
test("compiled addon exposes stateless public Node and Resource global classes", () => {
  const result = spawnSync(
    "dotnet",
    [
      "test",
      projectPath,
      "--filter",
      "FullyQualifiedName~SpatialWires.Godot.Tests.PublicAddonTypeTests",
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
    `Public addon type tests failed.\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}`,
  );
});
