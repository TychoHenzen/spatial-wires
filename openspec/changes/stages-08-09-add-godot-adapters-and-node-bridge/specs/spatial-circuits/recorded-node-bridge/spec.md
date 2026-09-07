## Purpose

Defines a main-thread recorded bridge for Godot Node device behavior whose causal outputs can be replayed without executing arbitrary Node code.

## ADDED Requirements

### Requirement: Node inputs cross one main-thread barrier
When a world contains a recorded Node backend, each microtick SHALL synchronously deliver Variant-compatible DTOs for committed input transitions and due simulation timers during the bridge phase on the Godot main thread.

#### Scenario: Node receives committed inputs
- **WHEN** multiple port transitions become committed at microtick `T`
- **THEN** the Node receives their committed DTOs during the `T` bridge phase, not partially during delivery or resolution

#### Scenario: Node timer becomes due without input
- **WHEN** a recorded Node backend has a simulation timer due at `T` and no port changes
- **THEN** the Node receives the timer at the `T` bridge phase

### Requirement: Node outputs use limited future commands
The Node SHALL request outputs only through a capability object. Accepted outputs SHALL target `T + 1` or later, receive accepted ordinals, and enter the causal command log with any required captured Node state.

#### Scenario: Node requests same-tick output
- **WHEN** a Node requests an output for the current or a past microtick
- **THEN** the bridge rejects the request without changing current-tick state

#### Scenario: Multiple Node outputs are accepted
- **WHEN** a Node makes multiple valid requests during one bridge phase
- **THEN** each request receives a deterministic accepted ordinal and is logged in acceptance order

### Requirement: Node callbacks cannot re-enter causal execution
Godot signal handlers, generated C# events, deferred calls, `_ExitTree`, and bridge callbacks SHALL NOT step the world or mutate causal state directly.

#### Scenario: Re-entry is attempted from a Node callback
- **WHEN** any Node callback attempts to step or directly mutate the active world
- **THEN** the attempt fails safely and the outer microtick remains valid

### Requirement: Replay bypasses Node causal execution
During causal replay, recorded Node output commands and captured state SHALL be applied from the log. The Node SHALL not execute to generate causal output, and replay SHALL reproduce the original trace.

#### Scenario: Node is disabled during replay
- **WHEN** a recorded live exchange is replayed after its Node is disabled
- **THEN** the recorded outputs reproduce the original causal trace without invoking the Node

### Requirement: Node deletion disconnects its backend safely
Deleting or invalidating a recorded Node SHALL make its device backend disconnected, release its persistent drives through normal timing rules, and prevent later callbacks from the deleted incarnation.

#### Scenario: Node is deleted with pending work
- **WHEN** a Node is deleted while outputs or callbacks are pending
- **THEN** stale Node work cannot affect the replacement and the backend's drives release deterministically

### Requirement: Recorded Node practice is repeatable
The Godot practice scene SHALL replace the pure Stage 7 responder with a Node responder, record one exchange, disable the Node, and replay the same response.

#### Scenario: Live and replayed responder traces match
- **WHEN** the public Stage 9 practice flow records and replays the Node response
- **THEN** the live and replayed causal trace hashes match
