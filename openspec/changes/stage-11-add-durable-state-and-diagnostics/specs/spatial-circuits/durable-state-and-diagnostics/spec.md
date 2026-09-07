## Purpose

Defines complete durable causal saves, controlled migration and replay, and diagnostic traces that can explain or reproduce deterministic world execution.

## ADDED Requirements

### Requirement: Durable snapshots capture complete causal state
A durable snapshot SHALL be written only after the final phase of a microtick. It SHALL include time and ordinals, definition manifests and hashes, incarnations, component and protocol state, drives, resolved values, future and cancelled events, delayed-cell state, temporal roots, deterministic random state, cable queues and epochs, bridge queues, topology edits, and replay position.

#### Scenario: Save contains an in-flight tagged pulse
- **WHEN** a snapshot is saved after a pulse enters a delayed cable and before it arrives
- **THEN** loading the snapshot delivers that pulse once at the same microtick as the uninterrupted run

#### Scenario: Snapshot omits required causal state
- **WHEN** validation finds a missing required causal field or referenced definition
- **THEN** loading fails before a partial world is published

### Requirement: Saves replace prior files atomically
Saving SHALL write and validate a new file before atomically replacing the previous valid save. A write, validation, or replacement failure SHALL preserve the prior valid file.

#### Scenario: New save validation fails
- **WHEN** the candidate save cannot be validated
- **THEN** the prior save remains byte-for-byte available and loadable

### Requirement: Definitions and behavior code are pinned
Each save and replay log SHALL pin stable definition identifiers, schema versions, content hashes, custom behavior identifiers and versions, and deterministic behavior assembly fingerprints.

#### Scenario: Definition hash changed before replay
- **WHEN** replay resolves a definition or behavior fingerprint different from the recorded manifest
- **THEN** replay is blocked unless an explicit compatible migration is selected

### Requirement: Migrations are explicit and non-destructive
A migration SHALL declare source and target versions, transform a copy, and validate the result before publication. Failure SHALL leave source assets and saves unchanged and readable by their prior reader.

#### Scenario: Migration succeeds
- **WHEN** an explicit migration transforms a supported source and the result validates
- **THEN** the migrated copy is published with its new version and hash

#### Scenario: Migration fails
- **WHEN** transformation or validation fails
- **THEN** no source asset or prior valid save is overwritten

### Requirement: Recorded Node behavior replays without Node execution
Replay SHALL consume recorded Node commands, bridge state, and replay cursors from durable data while bypassing Node causal execution.

#### Scenario: Saved Node exchange is replayed
- **WHEN** the Stage 9 exchange is loaded and replayed with the Node unavailable
- **THEN** the response and per-tick hashes match the recorded run

### Requirement: Presentation timing does not affect causal hashes
Changing rendering frame rate, suppressing replay presentation events, or deduplicating them SHALL not change causal command order, state, or snapshot hashes.

#### Scenario: Same replay uses different frame rates
- **WHEN** one replay is rendered at different presentation frame rates
- **THEN** every causal snapshot hash remains identical

### Requirement: Traces and snapshot differences are canonical
The system SHALL export deterministic machine-readable traces and SHALL compare snapshots by stable causal paths and values. Diagnostics SHALL identify the first observable difference without relying on object address or enumeration order.

#### Scenario: Two snapshots diverge
- **WHEN** canonical snapshot comparison receives states with one different pending event
- **THEN** it reports the stable event path and differing values

### Requirement: Durable replay practice is publicly runnable
The public Stage 11 practice flow SHALL record the Stage 9 Node exchange, save it, reload it, and replay it without Node execution.

#### Scenario: Durable practice reproduces the run
- **WHEN** the documented save and replay practice completes
- **THEN** the original and replayed command traces and causal hashes match
