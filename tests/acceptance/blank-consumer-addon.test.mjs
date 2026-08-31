import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const acceptanceRunner = path.join(testDirectory, "Run-BlankConsumerAcceptance.ps1");
const godotExecutable = process.env.GODOT_BIN;

// covers: spatial-circuits/package-baseline :: Installable addon unit :: Blank consumer discovers the addon
test("launch gdUnit4 blank-consumer addon acceptance", () => {
  // This adapter asserts only successful gdUnit4 process exit. All behavior assertions stay in the C# suite.
  assert.ok(godotExecutable, "GODOT_BIN must identify the pinned Godot 4.7.1 .NET executable.");
  assert.doesNotThrow(
    () => execFileSync(
      "powershell",
      ["-NoProfile", "-File", acceptanceRunner, "-GodotExecutable", godotExecutable],
      {
        cwd: repositoryRoot,
        stdio: "inherit",
        timeout: 240_000,
      },
    ),
  );
});
