import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import test from "node:test";
import path from "node:path";
import { fileURLToPath } from "node:url";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const testProject = path.join(testDirectory, "SpatialCircuits.Core.Tests.csproj");
const testName = "SpatialCircuits.Core.Tests.PureProjectBuildTests.PureProjectsBuildAsNet8AssembliesWithoutGodotSharp";

// covers: spatial-circuits/package-baseline :: Enforced dependency boundary :: Pure projects build independently
test("pure projects build independently", () => {
  const result = spawnSync(
    "dotnet",
    ["test", testProject, "--filter", `FullyQualifiedName=${testName}`, "--nologo", "--verbosity", "minimal"],
    { cwd: repositoryRoot, encoding: "utf8" },
  );

  const output = [result.stdout, result.stderr].filter(Boolean).join("\n");
  assert.equal(result.error, undefined, output);
  assert.equal(result.status, 0, output);
});
