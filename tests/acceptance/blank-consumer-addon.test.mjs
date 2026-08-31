import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const acceptanceRunner = path.join(testDirectory, "Run-BlankConsumerAcceptance.ps1");
const godotExecutable = process.env.GODOT_BIN;
let gdUnitAcceptanceExitCode;

function runGdUnitAcceptanceOnce() {
  if (gdUnitAcceptanceExitCode !== undefined) {
    return gdUnitAcceptanceExitCode;
  }

  try {
    execFileSync(
      "powershell",
      ["-NoProfile", "-File", acceptanceRunner, "-GodotExecutable", godotExecutable ?? ""],
      {
        cwd: repositoryRoot,
        stdio: "inherit",
        timeout: 240_000,
      },
    );
    gdUnitAcceptanceExitCode = 0;
  } catch (error) {
    gdUnitAcceptanceExitCode = Number.isInteger(error?.status) ? error.status : 1;
  }

  return gdUnitAcceptanceExitCode;
}

// covers: spatial-circuits/package-baseline :: Installable addon unit :: Blank consumer discovers the addon
test("gdUnit4 accepts the blank consumer", () => {
  assert.equal(runGdUnitAcceptanceOnce(), 0);
});

// covers: spatial-circuits/package-baseline :: Clean editor plugin lifecycle :: Plugin is disabled cleanly
test("gdUnit4 observes the plugin disabled cleanly", () => {
  assert.equal(runGdUnitAcceptanceOnce(), 0);
});

// covers: spatial-circuits/package-baseline :: Clean editor plugin lifecycle :: Plugin is enabled again
test("gdUnit4 observes the plugin enabled again", () => {
  assert.equal(runGdUnitAcceptanceOnce(), 0);
});
