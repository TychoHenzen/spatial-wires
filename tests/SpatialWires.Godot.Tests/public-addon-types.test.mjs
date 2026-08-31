import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const gdUnitRunner = path.join(testDirectory, "Run-GdUnit.ps1");

// covers: spatial-circuits/package-baseline :: Public Node and Resource types :: Consumer uses public addon types
test("launch gdUnit4 public addon type suite", () => {
  // This checks only that the gdUnit4 process exits successfully. Behavioral assertions stay in the C# suite.
  assert.doesNotThrow(
    () => execFileSync(
      "powershell",
      ["-NoProfile", "-File", gdUnitRunner],
      {
        cwd: repositoryRoot,
        stdio: "inherit",
        timeout: 240_000,
      },
    ),
  );
});
