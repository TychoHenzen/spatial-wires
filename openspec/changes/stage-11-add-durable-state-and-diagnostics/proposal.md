## Why

In-memory snapshots cannot preserve a running world across process boundaries or explain divergent runs. Stage 11 adds durable causal state, replay controls, migration, and diagnostics before gameplay depends on them.

## What Changes

- Serialize complete end-of-tick causal snapshots, definition manifests, assembly fingerprints, bridge queues, and replay cursors.
- Preserve pending events, cancellations, drives, component state, cable queues, Node commands, topology edits, and deterministic random state.
- Add atomic save replacement that validates the new file before replacing the prior valid file.
- Add versioned migrations that never overwrite source assets on failure.
- Block replay against changed definition or behavior fingerprints unless an explicit migration exists.
- Add canonical trace export, snapshot hashes, snapshot comparison, and presentation-event replay policy.
- Add a practice flow that records the Stage 9 Node exchange, reloads it, and replays without Node execution.

## Capabilities

### New Capabilities

- `spatial-circuits/durable-state-and-diagnostics`: Complete causal saves, atomic replacement, migrations, replay, trace export, and snapshot comparison.

### Modified Capabilities

None.

## Impact

- Extends pure snapshot serialization and Godot bridge persistence surfaces.
- Depends on scheduler, devices, cables, Resource adapters, and the recorded Node bridge.
- Establishes the durable contract used by gameplay acceptance and later optimization equivalence checks.
- Does not add gameplay rules or editor UI.
