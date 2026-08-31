import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import test from "node:test";
import path from "node:path";
import { fileURLToPath } from "node:url";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const powershellTest = path.join(testDirectory, "Test-Toolchain.Available.Tests.ps1");

// covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is available
test("declared toolchain is available", () => {
  const result = spawnSync(
    "powershell",
    ["-NoProfile", "-File", powershellTest],
    { cwd: repositoryRoot, encoding: "utf8" },
  );

  const output = [result.stdout, result.stderr].filter(Boolean).join("\n");
  assert.equal(result.error, undefined, output);
  assert.equal(result.status, 0, output);
});
