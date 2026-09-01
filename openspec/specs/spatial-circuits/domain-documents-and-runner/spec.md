# spatial-circuits/domain-documents-and-runner Specification

## Purpose
Defines the portable four-state circuit vocabulary, authoritative document format, stable validation surface, and command-line runner used by every later simulation stage.
## Requirements
### Requirement: Four-state digital values resolve consistently
The domain SHALL expose `Low`, `High`, `Unknown`, and `HighImpedance` values. Drive resolution SHALL ignore `HighImpedance`, preserve one unanimous driven value, and return `Unknown` for incompatible or unknown active drives.

#### Scenario: No active drive resolves to high impedance
- **WHEN** an input has no drive or only `HighImpedance` drives
- **THEN** its resolved value is `HighImpedance`

#### Scenario: One compatible driven value is preserved
- **WHEN** every active drive on an input is `Low`
- **THEN** its resolved value is `Low`

#### Scenario: Contention resolves to unknown
- **WHEN** an input has both `Low` and `High` active drives
- **THEN** its resolved value is `Unknown` regardless of drive insertion order

### Requirement: Portable definitions use stable identities and ownership
Authoritative documents SHALL identify definitions, components, ports, behaviors, and schema versions with stable data identifiers. Documents SHALL represent definitions separately from mutable runtime instances and SHALL NOT persist CLR type names.

#### Scenario: Document identity survives process boundaries
- **WHEN** a valid document is loaded in separate processes
- **THEN** the same data identifiers refer to the same definitions, locations, ports, and behaviors

#### Scenario: CLR type name is used as persisted behavior identity
- **WHEN** a document stores a CLR type name where a stable behavior identifier is required
- **THEN** validation rejects the document with a stable diagnostic code

### Requirement: Canonical documents round-trip exactly
The system SHALL read and write schema-versioned canonical JSON. Canonical output SHALL use deterministic property and collection ordering, and its content hash SHALL be computed from the canonical bytes.

#### Scenario: Canonical round trip is byte stable
- **WHEN** a valid canonical document is loaded and written without a semantic change
- **THEN** the output bytes and content hash equal the original canonical bytes and hash

#### Scenario: Equivalent input ordering is normalized
- **WHEN** two valid inputs contain the same data in different permitted source orders
- **THEN** canonical writing produces identical bytes and content hashes

### Requirement: Validation diagnostics are stable and structured
Document validation SHALL reject unsupported schemas, invalid identifiers, invalid references, invalid parameters, and ownership conflicts before runtime instantiation. Each diagnostic SHALL include a stable code and a location that identifies the offending document element.

#### Scenario: Invalid fixture reports a stable diagnostic
- **WHEN** the same invalid fixture is validated more than once
- **THEN** each run returns the same diagnostic code and element location

#### Scenario: Unsupported schema is rejected before instantiation
- **WHEN** a document declares an unsupported schema version
- **THEN** validation fails without creating a runtime instance

### Requirement: Runtime instances do not share mutable state
Each runtime instance created from an immutable definition SHALL own independent mutable state, even when multiple instances originate from the same loaded document object.

#### Scenario: Two instances diverge independently
- **WHEN** two runtime instances are created from one definition and only one instance is mutated
- **THEN** the second instance and the source definition remain unchanged

### Requirement: Core scenarios run without Godot
The command-line scenario runner SHALL load a versioned fixture, validate it, execute supported pre-scheduler domain actions, print a deterministic machine-readable trace, and return a nonzero process code for invalid input or failed expectations.

#### Scenario: Drive-resolution practice fixture runs
- **WHEN** the public Stage 2 practice fixture is run
- **THEN** its trace includes `HighImpedance`, a valid driven value, and `Unknown` contention in the documented order

#### Scenario: Invalid fixture fails the process
- **WHEN** the runner receives an invalid fixture
- **THEN** it prints the stable validation diagnostic and returns a nonzero process code
