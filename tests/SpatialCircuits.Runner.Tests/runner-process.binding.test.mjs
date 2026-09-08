import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, "..", "..");
const runnerProject = path.join(
  repositoryRoot,
  "src",
  "SpatialCircuits.Runner",
  "SpatialCircuits.Runner.csproj",
);
const runnerAssembly = path.join(
  repositoryRoot,
  "src",
  "SpatialCircuits.Runner",
  "bin",
  "Debug",
  "net8.0",
  "SpatialCircuits.Runner.dll",
);

function buildRunner() {
  return spawnSync(
    "dotnet",
    ["build", runnerProject, "--nologo", "--verbosity", "minimal"],
    { cwd: repositoryRoot, encoding: "utf8" },
  );
}

function runFixture(fixturePath) {
  const build = buildRunner();
  const buildOutput = [build.stdout, build.stderr].filter(Boolean).join("\n");
  assert.equal(build.status, 0, buildOutput);

  return spawnSync("dotnet", [runnerAssembly, fixturePath], {
    cwd: repositoryRoot,
    encoding: "utf8",
  });
}

function parseJsonLines(output) {
  return output
    .split(/\r?\n/u)
    .filter((line) => line.length > 0)
    .map((line) => JSON.parse(line));
}

test("public drive-resolution fixture prints the exact ordered trace", () => {
  const fixture = path.join(
    repositoryRoot,
    "examples",
    "stage-02",
    "drive-resolution.fixture.json",
  );
  const result = runFixture(fixture);
  const output = [result.stdout, result.stderr].filter(Boolean).join("\n");

  assert.equal(result.status, 0, output);
  assert.equal(result.stderr, "");
  assert.deepEqual(parseJsonLines(result.stdout), [
    {
      traceSchema: { major: 1, minor: 0 },
      fixtureId: "stage-02-drive-resolution",
      sequence: 0,
      caseId: "no-active-drive",
      observed: "HighImpedance",
      expected: "HighImpedance",
      passed: true,
    },
    {
      traceSchema: { major: 1, minor: 0 },
      fixtureId: "stage-02-drive-resolution",
      sequence: 1,
      caseId: "valid-low-drive",
      observed: "Low",
      expected: "Low",
      passed: true,
    },
    {
      traceSchema: { major: 1, minor: 0 },
      fixtureId: "stage-02-drive-resolution",
      sequence: 2,
      caseId: "contention",
      observed: "Unknown",
      expected: "Unknown",
      passed: true,
    },
  ]);
});

test("public scheduled-drive practice restores and replays identical hashes", () => {
  const fixture = path.join(
    repositoryRoot,
    "examples",
    "stage-03",
    "scheduled-drive.fixture.json",
  );
  const result = runFixture(fixture);
  const output = [result.stdout, result.stderr].filter(Boolean).join("\n");

  assert.equal(result.status, 0, output);
  assert.equal(result.stderr, "");
  const records = parseJsonLines(result.stdout);
  assert.equal(records.length, 10);
  assert.deepEqual(
    records.map((record) => record.originalHash),
    records.map((record) => record.replayedHash),
  );
  assert.ok(records.slice(0, 5).every((record) => record.restoredHash === null));
  assert.ok(
    records.slice(5).every((record) => record.originalHash === record.restoredHash),
  );
  assert.ok(records.slice(5).every((record) => record.observed === record.restoredObserved));
  assert.ok(
    records.slice(5).every(
      (record) => record.deliveredEvents === record.restoredDeliveredEvents,
    ),
  );
  assert.ok(
    records.slice(5).every(
      (record) => record.diagnostics === record.restoredDiagnostics,
    ),
  );
  assert.ok(records.every((record) => record.passed));
  assert.equal(records[0].observed, "High");
  assert.equal(records[6].observed, "HighImpedance");
});

test("a failed expectation prints its observation and returns a nonzero code", () => {
  const fixture = path.join(
    testDirectory,
    "Fixtures",
    "failed-expectation.fixture.json",
  );
  const result = runFixture(fixture);

  assert.equal(result.status, 3, result.stdout);
  assert.equal(result.stderr, "");
  assert.deepEqual(parseJsonLines(result.stdout), [
    {
      traceSchema: { major: 1, minor: 0 },
      fixtureId: "failed-expectation",
      sequence: 0,
      caseId: "wrong-expectation",
      observed: "Low",
      expected: "High",
      passed: false,
    },
  ]);
});

test("invalid fixtures print stable diagnostics and fail without stack traces", () => {
  const cases = [
    {
      file: "invalid-drive.fixture.json",
      code: "FIXTURE_DRIVE_INVALID",
      path: "$.cases[0].drives[0]",
    },
    {
      file: "malformed.fixture.json",
      code: "FIXTURE_JSON_INVALID",
      path: "$",
    },
  ];

  for (const expected of cases) {
    const fixture = path.join(testDirectory, "Fixtures", expected.file);
    const result = runFixture(fixture);
    const output = [result.stdout, result.stderr].filter(Boolean).join("\n");

    assert.notEqual(result.status, 0, output);
    assert.equal(result.stderr, "");
    assert.doesNotMatch(output, /Unhandled exception|stack trace|\sat System\./iu);
    const diagnostics = parseJsonLines(result.stdout);
    assert.equal(diagnostics.length, 1);
    assert.equal(diagnostics[0].kind, "diagnostic");
    assert.equal(diagnostics[0].code, expected.code);
    assert.equal(diagnostics[0].severity, "Error");
    assert.equal(diagnostics[0].path, expected.path);
    assert.equal(typeof diagnostics[0].message, "string");
    assert.deepEqual(diagnostics[0].traceSchema, { major: 1, minor: 0 });
  }
});
