## Purpose

Defines one deterministic microtick scheduler, causal ordering contract, topology invalidation model, and replayable in-memory state for all circuit execution.

## ADDED Requirements

### Requirement: Scheduled work has canonical total order
Every scheduled item SHALL be ordered by `(dueTick, phase, targetStableId, targetIncarnation, targetPortOrLane, sourceStableId, sourcePort, eventKind, causalOrdinal)`. Equivalent worlds SHALL produce the same order regardless of insertion order.

#### Scenario: Shuffled insertion produces the same trace
- **WHEN** equivalent scheduled items are inserted in different orders
- **THEN** execution produces the same committed trace and end-of-tick hash sequence

#### Scenario: Distinct target ports remain distinct
- **WHEN** same-tick events target different ports on one stable target
- **THEN** canonical ordering includes each target port and delivers each event to its intended port

### Requirement: Microticks commit through fixed phases
Each microtick SHALL apply topology commands, invalidate removed incarnations, deliver due work, resolve drives, evaluate pure behavior, run any required main-thread barrier, reduce future proposals, commit state, record diagnostics and hashes, and then advance time in that order.

#### Scenario: Evaluation reads one committed snapshot
- **WHEN** multiple due events affect inputs during one microtick
- **THEN** behavior evaluation observes the resolved committed input snapshot for that microtick

#### Scenario: Effects do not mutate the current evaluation snapshot
- **WHEN** evaluated behavior proposes a causal effect
- **THEN** the effect is scheduled for `T + 1` or later and cannot alter the snapshot being evaluated at `T`

### Requirement: External and internal causality receives deterministic ordinals
The scheduler SHALL assign a monotonically increasing `acceptedOrdinal` to accepted external commands. It SHALL sort internal proposals by canonical target, source, output slot, and event kind before assigning persisted causal ordinals.

#### Scenario: Same-target commands follow acceptance order
- **WHEN** multiple external commands for the same target are accepted for one microtick
- **THEN** they apply in increasing accepted-ordinal order

#### Scenario: Internal proposal order does not depend on enumeration
- **WHEN** equivalent behaviors enumerate their proposals in different orders
- **THEN** persisted causal ordinals and the resulting trace remain identical

### Requirement: Persistent drives resolve independently of update order
Drive state SHALL be indexed by source identity and SHALL remain active until that source changes or releases it. Resolution SHALL use all committed source drives and SHALL not depend on update order.

#### Scenario: Unchanged source drive persists
- **WHEN** a source emits no new drive during a later microtick
- **THEN** its previously committed drive remains part of input resolution

#### Scenario: Source release removes only that source
- **WHEN** one source changes its drive to `HighImpedance`
- **THEN** resolution removes that source's active contribution without removing other source drives

### Requirement: Causal operations have positive delay and no re-entry
Active components and connections SHALL schedule causal effects no earlier than `T + 1`. The scheduler SHALL reject zero-delay effects and SHALL throw a re-entry error if a callback attempts to step the world or mutate causal state directly.

#### Scenario: Zero-delay proposal is rejected
- **WHEN** behavior proposes a causal effect for the current or a past microtick
- **THEN** the proposal is rejected with a stable diagnostic

#### Scenario: Callback attempts scheduler re-entry
- **WHEN** a callback attempts to step the world during an active step
- **THEN** the attempt fails without partially committing the nested step

### Requirement: Incarnations invalidate stale work
Runtime targets SHALL carry both a stable location and an incarnation identifier. Removing or replacing a target SHALL invalidate work addressed to its former incarnation and SHALL create the documented drive releases.

#### Scenario: Replacement does not receive stale event
- **WHEN** a target is removed, its stable location is reused, and an event for the old incarnation becomes due
- **THEN** the event cannot affect the replacement

#### Scenario: Removal releases persistent drives
- **WHEN** a driving target is removed
- **THEN** its source drives are released through the scheduler's documented topology phase

### Requirement: Temporal roots remain schedulable without new input
The scheduler SHALL keep clocks, timers, aging filters, delayed gates, cable queues, and pending topology commands active until their next due work is processed or cancelled.

#### Scenario: Timer fires after quiet microticks
- **WHEN** no new external input arrives before an active timer deadline
- **THEN** the timer still becomes due at its exact microtick

### Requirement: End-of-tick snapshots restore deterministic execution
An in-memory snapshot taken after the final phase SHALL include all causal state required to continue execution. Restoring a snapshot and replaying the same commands SHALL reproduce the same trace hashes.

#### Scenario: Ten-microtick snapshot practice reproduces hashes
- **WHEN** the public scheduled-drive fixture runs, snapshots, restores, and replays ten microticks
- **THEN** the restored run produces the same per-tick hash sequence as the original run

#### Scenario: Snapshot is requested during a microtick
- **WHEN** a caller requests a causal snapshot before the commit and record phases finish
- **THEN** the system refuses the request or defers it to the end-of-tick boundary
