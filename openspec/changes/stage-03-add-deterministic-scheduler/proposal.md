## Why

Portable circuit documents cannot execute until causal time and ordering have one deterministic meaning. Stage 3 establishes that meaning before panels and timed components are added.

## What Changes

- Add canonical scheduled-event identity and the ordered microtick phase pipeline.
- Add accepted command ordinals, persisted causal ordinals, persistent drives, and order-independent drive resolution.
- Add incarnation-aware invalidation for removed or replaced runtime targets.
- Add temporal-root tracking for clocks, timers, filters, delayed gates, queues, and pending topology work.
- Reject zero-delay causal operations and scheduler re-entry.
- Add end-of-tick in-memory snapshots, trace hashing, restore, and replay of pure core commands.
- Add a ten-microtick run, snapshot, restore, and replay practice fixture.

## Capabilities

### New Capabilities

- `spatial-circuits/deterministic-scheduler`: Canonical event ordering, microtick phases, drives, incarnations, temporal roots, snapshots, hashes, and core command replay.

### Modified Capabilities

None.

## Impact

- Extends `SpatialCircuits.Core` and its unit and fixture coverage.
- Depends on the Stage 2 domain document and scenario-runner contracts.
- Establishes the timing facade used by all later cells, chips, devices, cables, bridges, and saves.
- Does not add panel cell rules or durable on-disk snapshots.
