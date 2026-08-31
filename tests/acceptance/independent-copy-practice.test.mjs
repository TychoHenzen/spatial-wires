import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const practiceRunner = path.join(repositoryRoot, "scripts", "Run-IndependentCopyPractice.ps1");

// covers: spatial-circuits/package-baseline :: Portable second-consumer practice check :: Addon is moved to an independent consumer
test("gdUnit4 accepts the staged addon in an independent consumer", () => {
  const result = spawnSync(
    "powershell",
    ["-NoProfile", "-File", practiceRunner, "-GodotExecutable", process.env.GODOT_BIN ?? ""],
    {
      cwd: repositoryRoot,
      stdio: "inherit",
      timeout: 720_000,
    },
  );

  assert.equal(result.status, 0);
});
