## Purpose

Defines sparse gameplay-facing cell bindings that reuse the recorded Node barrier while preserving deterministic core timing, replay, and safe missing-binding behavior.

## ADDED Requirements

### Requirement: Node-backed cells use stable sparse bindings
A Node-backed custom cell SHALL store a stable binding identifier resolved by its owning panel's Godot adapter. This path SHALL be documented and enforced as sparse rather than used for ordinary or repeated grid cells.

#### Scenario: Binding resolves in the owning panel
- **WHEN** a valid panel contains a cell whose binding identifier is available
- **THEN** the owning panel connects that cell to exactly its registered Node behavior

#### Scenario: Binding is missing
- **WHEN** a saved cell references an unavailable binding identifier
- **THEN** the document remains readable through a missing-binding placeholder and reports a stable diagnostic

### Requirement: Node cells receive only committed transitions and due timers
The bridge SHALL deliver a Node-backed cell's committed input transitions, read-only captured state, and due simulation timers during the main-thread barrier. The Node SHALL request only future outputs and presentation events through limited capabilities.

#### Scenario: Input changes during one microtick
- **WHEN** several neighbor transitions resolve during microtick `T`
- **THEN** the Node cell receives only their final committed values during the `T` barrier

#### Scenario: Node cell requests same-tick mutation
- **WHEN** a Node cell requests an output or causal mutation at `T`
- **THEN** the request is rejected and cannot change the active microtick

### Requirement: Node cell state and outputs replay without Node execution
Accepted output commands, timers, captured state, and deterministic presentation-event policy SHALL enter snapshots and replay logs so causal replay bypasses the Node.

#### Scenario: Node cell is unavailable during replay
- **WHEN** a recorded Node-cell run is replayed without the binding Node
- **THEN** its causal outputs and trace hashes match the recorded run

### Requirement: Deleted bindings release safely
Deleting a bound Node or removing its binding SHALL invalidate that binding incarnation, cancel stale callbacks, and release its persistent drives through ordinary timing rules.

#### Scenario: Bound Node is deleted with a pending output
- **WHEN** a bound Node is deleted before its pending callback executes
- **THEN** the callback cannot affect the cell and its drives release deterministically

### Requirement: Worker execution refuses active recorded bindings
The simulation SHALL refuse worker-thread mode while any recorded Node-backed cell is active and SHALL identify the binding that requires the main-thread barrier.

#### Scenario: Worker mode starts with an active Node cell
- **WHEN** worker execution is requested for a world with an active recorded cell binding
- **THEN** startup fails before stepping with a diagnostic naming that binding

### Requirement: Node monitor practice preserves causal boundaries
The public Stage 13 practice SHALL place one Node-backed protocol monitor in a panel and emit a Godot alarm animation presentation event without mutating the current microtick.

#### Scenario: Protocol monitor emits animation event
- **WHEN** the monitor observes its configured committed protocol condition
- **THEN** it emits the recorded presentation event while causal state changes only through future accepted commands
