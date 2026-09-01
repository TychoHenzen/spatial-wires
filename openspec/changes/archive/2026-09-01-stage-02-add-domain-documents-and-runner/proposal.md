## Why

The Stage 1 package baseline has no circuit vocabulary or portable input format. Stage 2 must define stable data and diagnostics before scheduler behavior or saved assets depend on them.

## What Changes

- Add the four-state logic model, persistent drive resolution, stable domain identifiers, and explicit ownership definitions.
- Add immutable, schema-versioned panel and world documents with canonical JSON and stable content hashes.
- Add structured validation diagnostics with stable codes for invalid fixtures.
- Add a pure .NET command-line scenario runner that loads fixtures and emits deterministic traces.
- Ensure separate runtime instances created from one document never share mutable state.
- Add a public practice fixture that demonstrates `HighImpedance`, a valid driven value, and `Unknown` contention.

## Capabilities

### New Capabilities

- `spatial-circuits/domain-documents-and-runner`: Four-state logic, portable definitions, stable validation, canonical hashes, isolated runtime instances, and the core scenario runner.

### Modified Capabilities

None.

## Impact

- Extends the pure projects under `src/` and pure tests under `tests/SpatialCircuits.Core.Tests/`.
- Introduces the first authoritative saved-document contract and versioned scenario fixtures.
- Establishes the public runner surface used by Stages 3 through 7 and later regression checks.
- Does not add scheduling, panels, Godot Resource conversion, or runtime UI.
