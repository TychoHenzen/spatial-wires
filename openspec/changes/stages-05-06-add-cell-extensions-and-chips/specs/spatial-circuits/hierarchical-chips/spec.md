## Purpose

Defines reusable hierarchical chips whose hidden source panels remain the exact timing and state authority in both expanded and collapsed views.

## ADDED Requirements

### Requirement: Chip definitions are portable and hash pinned
A chip definition SHALL contain a source panel definition, named port map, schema and behavior versions, content hash, symbol, parameters, and lower-level chip dependencies. A chip instance SHALL remain pinned to its recorded content hash until an explicit upgrade.

#### Scenario: Source asset changes after instantiation
- **WHEN** a source panel asset changes after a chip instance is created
- **THEN** the existing instance continues using its hash-pinned definition

#### Scenario: Explicit upgrade selects a new definition
- **WHEN** a compatible chip upgrade is explicitly accepted
- **THEN** the instance adopts the selected new hash through a validated migration boundary

### Requirement: Chip dependency graphs are acyclic
Validation SHALL reject any direct or indirect recursive chip dependency before runtime instantiation.

#### Scenario: Indirect recursion is present
- **WHEN** chip A depends on chip B and chip B eventually depends on chip A
- **THEN** validation fails with a diagnostic that identifies the dependency cycle

### Requirement: Chip instances own independent runtime state
Each panel SHALL own each placed chip instance, and every chip instance SHALL own an independent runtime instance of its hidden source grid.

#### Scenario: Two chip instances diverge independently
- **WHEN** two instances of one stateful chip receive different inputs
- **THEN** their internal state and outputs evolve independently

### Requirement: Collapse changes presentation only
Expanded and collapsed chip views SHALL execute the same hidden source grid with the same microtick timing, state, and port behavior.

#### Scenario: Expanded and collapsed traces match
- **WHEN** identical commands run against expanded and collapsed views of the same chip instance
- **THEN** their port traces and per-tick hashes are identical

### Requirement: Existing chips retain exact fallback execution
If new chip authoring is disabled or a presentation feature is unavailable, existing chip assets SHALL remain readable and executable through their expanded hidden grids.

#### Scenario: Authoring is disabled
- **WHEN** a valid existing chip is loaded while chip authoring is disabled
- **THEN** the chip still executes through its expanded-grid fallback

### Requirement: Packaged XOR chips are runnable through the public runner
The scenario runner SHALL load a packaged XOR chip and execute more than one independent instance in one fixture.

#### Scenario: Two-instance XOR practice fixture runs
- **WHEN** the public packaged-XOR fixture drives its two instances independently
- **THEN** each instance produces its expected trace without shared state
