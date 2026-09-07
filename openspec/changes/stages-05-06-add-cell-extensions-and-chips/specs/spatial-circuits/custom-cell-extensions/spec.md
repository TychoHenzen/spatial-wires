## Purpose

Defines a pure, deterministic extension contract for reusable custom cell rules without adding project-specific behavior to the standard-cell library.

## ADDED Requirements

### Requirement: Custom rules use stable registrations
Custom cell rules SHALL register under stable namespaced behavior identifiers with explicit behavior versions. Saved layouts SHALL resolve those identifiers without persisting CLR type names.

#### Scenario: Registered external rule loads
- **WHEN** a fixture references an available namespaced behavior identifier and supported version
- **THEN** validation resolves the registered rule without a core-library change

#### Scenario: Registration is missing
- **WHEN** a fixture references an unavailable behavior identifier or version
- **THEN** validation fails with a diagnostic that identifies the missing registration

### Requirement: Rule inputs and effects are restricted
A custom rule SHALL receive immutable parameters, committed neighbor values, read-only instance state, and the current microtick. It SHALL request only validated next state and future output drives through a restricted effect surface.

#### Scenario: Rule proposes an effect in the past
- **WHEN** a rule requests an output for the current or a past microtick
- **THEN** validation rejects the proposal without changing causal state

#### Scenario: Rule duplicates one output proposal
- **WHEN** a rule emits more than one proposal for the same output during one evaluation
- **THEN** the evaluation reports a deterministic diagnostic and commits no ambiguous output

### Requirement: Custom parameters and state are portable
Custom rule parameters SHALL validate before instantiation. Rule instance state and deterministic random state SHALL be serializable through snapshots and replay.

#### Scenario: Invalid custom parameter is rejected
- **WHEN** a fixture supplies a parameter outside the registered rule contract
- **THEN** validation fails before the rule instance runs

#### Scenario: Rule state survives snapshot and replay
- **WHEN** a stateful custom rule runs, snapshots, restores, and replays the same commands
- **THEN** its state, outputs, and trace hashes match the original run

### Requirement: Behavior versions migrate explicitly
Changing a saved custom behavior version SHALL require a registered migration that validates the resulting parameters and state. Missing or failed migrations SHALL preserve the original data.

#### Scenario: Supported behavior migration succeeds
- **WHEN** a saved rule version has an explicit valid migration to the requested version
- **THEN** the migrated fixture validates under the new version

#### Scenario: Behavior migration is unavailable
- **WHEN** a saved rule version has no migration to the requested version
- **THEN** loading fails clearly and leaves the source fixture unchanged

### Requirement: External pattern detector proves the extension surface
The Stage 5 reference pattern detector SHALL live outside the standard-cell library and SHALL run through the same public fixture and trace surface as built-in cells.

#### Scenario: Pattern-detector practice fixture runs
- **WHEN** the public JSON fixture loads the reference pattern-detector registration
- **THEN** the runner prints its expected timed output trace
