import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import test from "node:test";
import path from "node:path";
import { fileURLToPath } from "node:url";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const powershellTest = path.join(testDirectory, "Test-Toolchain.Unavailable.Tests.ps1");

// covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is unavailable
test("declared toolchain is unavailable", () => {
  const result = spawnSync(
    "powershell",
    ["-NoProfile", "-File", powershellTest],
    { cwd: repositoryRoot, encoding: "utf8" },
  );

  const output = [result.stdout, result.stderr].filter(Boolean).join("\n");
  assert.equal(result.error, undefined, output);
  assert.equal(result.status, 0, output);
});

test("unavailable toolchain checks return zero through a PowerShell wrapper", () => {
  const escapedTestPath = powershellTest.replaceAll("'", "''");
  const wrapper = [
    `pwsh -NoProfile -File '${escapedTestPath}'`,
    "if ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) { exit $LASTEXITCODE }",
  ].join("; ");
  const result = spawnSync(
    "pwsh",
    ["-NoProfile", "-Command", wrapper],
    { cwd: repositoryRoot, encoding: "utf8" },
  );

  const output = [result.stdout, result.stderr].filter(Boolean).join("\n");
  assert.equal(result.error, undefined, output);
  assert.equal(result.status, 0, output);
});
