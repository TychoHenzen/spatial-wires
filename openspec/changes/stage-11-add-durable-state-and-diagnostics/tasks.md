## 1. Define complete durable state

- [ ] 1.1 Add the versioned snapshot DTO and canonical mapping for every documented causal-state category.
- [ ] 1.2 Save and restore an in-flight tagged cable pulse and compare its arrival tick and hashes.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Durable snapshots capture complete causal state :: Save contains an in-flight tagged pulse -->
- [ ] 1.3 Reject snapshots with missing causal fields or unresolved definition references before publishing a world.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Durable snapshots capture complete causal state :: Snapshot omits required causal state -->
- [ ] 1.4 Add complete manifests for definitions, schemas, behavior versions, hashes, and assembly fingerprints.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Definitions and behavior code are pinned :: Definition hash changed before replay -->

## 2. Add safe save and migration transactions

- [ ] 2.1 Write, flush, reload, validate, and atomically replace candidate saves while retaining the prior valid file.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Saves replace prior files atomically :: New save validation fails -->
- [ ] 2.2 Add ordered copy-based migrations and publish validated successful results with new hashes.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Migrations are explicit and non-destructive :: Migration succeeds -->
- [ ] 2.3 Add failed transformation and validation fixtures that preserve source assets and prior saves.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Migrations are explicit and non-destructive :: Migration fails -->

## 3. Add durable replay and diagnostics

- [ ] 3.1 Restore Node commands, captured state, bridge queues, and cursors while bypassing Node execution.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Recorded Node behavior replays without Node execution :: Saved Node exchange is replayed -->
- [ ] 3.2 Separate presentation event policy from causal replay and compare hashes across frame rates.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Presentation timing does not affect causal hashes :: Same replay uses different frame rates -->
- [ ] 3.3 Add canonical trace export and stable-path snapshot comparison with first-difference output.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Traces and snapshot differences are canonical :: Two snapshots diverge -->

## 4. Add the public durable replay practice

- [ ] 4.1 Add the command and fixture that records, saves, reloads, and replays the Stage 9 Node exchange.
<!-- covers: spatial-circuits/durable-state-and-diagnostics :: Durable replay practice is publicly runnable :: Durable practice reproduces the run -->

## 5. Run Stage 11 gates

- [ ] 5.1 Run formatting, dependency checks, `dotnet build`, save failure tests, migration tests, and replay tests.
- [ ] 5.2 Run the public durable replay practice and record matching commands, hashes, and process codes.
- [ ] 5.3 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
