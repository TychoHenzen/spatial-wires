import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const acceptanceRunner = path.join(testDirectory, "Run-BlankConsumerAcceptance.ps1");
const childPipelineTimeoutsMilliseconds = [
  ...readFileSync(acceptanceRunner, "utf8").matchAll(/-TimeoutMilliseconds\s+(\d+)/g),
].map((match) => Number(match[1]));
assert.ok(
  childPipelineTimeoutsMilliseconds.length > 0,
  "The blank-consumer acceptance runner must declare child process timeouts.",
);
const godotExecutable = process.env.GODOT_BIN;
const childPipelineMaximumMilliseconds = childPipelineTimeoutsMilliseconds.reduce(
  (total, timeout) => total + timeout,
  0,
);
const acceptanceTimeoutMilliseconds = 960_000;
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
        timeout: acceptanceTimeoutMilliseconds,
      },
    );
    gdUnitAcceptanceExitCode = 0;
  } catch (error) {
    gdUnitAcceptanceExitCode = Number.isInteger(error?.status) ? error.status : 1;
  }

  return gdUnitAcceptanceExitCode;
}

test("outer timeout exceeds the bounded child pipeline", () => {
  assert.ok(acceptanceTimeoutMilliseconds > childPipelineMaximumMilliseconds);
});

test("bounded headless baseline pipeline succeeds", () => {
  assert.equal(runGdUnitAcceptanceOnce(), 0);
});

test("gdUnit4 accepts the blank consumer", () => {
  assert.equal(runGdUnitAcceptanceOnce(), 0);
});

test("gdUnit4 observes the plugin disabled cleanly", () => {
  assert.equal(runGdUnitAcceptanceOnce(), 0);
});

test("gdUnit4 observes the plugin enabled again", () => {
  assert.equal(runGdUnitAcceptanceOnce(), 0);
});
