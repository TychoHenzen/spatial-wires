import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const failureRunner = path.join(testDirectory, "Run-FailingPublicTypeGdUnit.ps1");
const godotExecutable = process.env.GODOT_BIN;
let gdUnitFailureResult;

function runFailingGdUnitOnce() {
  if (gdUnitFailureResult !== undefined) {
    return gdUnitFailureResult;
  }

  try {
    const stdout = execFileSync(
      "powershell",
      ["-NoProfile", "-File", failureRunner, "-GodotExecutable", godotExecutable ?? ""],
      {
        cwd: repositoryRoot,
        encoding: "utf8",
        maxBuffer: 16 * 1024 * 1024,
        timeout: 240_000,
      },
    );
    gdUnitFailureResult = { exitCode: 0, stdout, stderr: "" };
  } catch (error) {
    gdUnitFailureResult = {
      exitCode: Number.isInteger(error?.status) ? error.status : -1,
      stdout: error?.stdout?.toString() ?? "",
      stderr: error?.stderr?.toString() ?? "",
    };
  }

  process.stdout.write(gdUnitFailureResult.stdout);
  process.stderr.write(gdUnitFailureResult.stderr);
  return gdUnitFailureResult;
}

// covers: spatial-circuits/package-baseline :: Bounded headless verification :: Smoke assertion fails
test("failing gdUnit4 public-type assertion exits nonzero within the bound", () => {
  const result = runFailingGdUnitOnce();
  assert.equal(result.exitCode, 1);
  assert.match(result.stdout, /EXPECTED_GDUNIT_FAILURE exit=1 timedOut=False durationMs=\d+/);
});
