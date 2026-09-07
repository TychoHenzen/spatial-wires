## Purpose

Defines the Godot Resource authoring boundary that converts Variant-compatible editor data into validated immutable core definitions without sharing runtime state.

## ADDED Requirements

### Requirement: Resource DTOs contain only supported authoring data
Godot-facing definition Resources SHALL expose only Variant-compatible, serializable authoring fields and references. They SHALL NOT contain runtime arrays, event queues, mutable component state, or other live causal state.

#### Scenario: Resource contains runtime state
- **WHEN** a Resource conversion encounters a field that represents live runtime state
- **THEN** conversion rejects it with a validation diagnostic

#### Scenario: Resource fields round-trip through Godot
- **WHEN** a valid definition Resource is saved and loaded by the pinned Godot version
- **THEN** its supported authoring values remain equivalent

### Requirement: Resource conversion validates and deep copies
Converting a Resource SHALL validate all data and deep-copy it into an immutable core definition. Later Resource edits SHALL not mutate existing definitions or runtime instances.

#### Scenario: Cached Resource creates two devices
- **WHEN** two devices are created from one cached Resource
- **THEN** they receive independent runtime state and neither writes live state into the Resource

#### Scenario: Resource is edited after conversion
- **WHEN** a Resource changes after a core definition was created
- **THEN** the prior definition and its instances remain unchanged

### Requirement: Resource revisions have visible canonical identity
Each successful conversion SHALL expose the resulting schema version and canonical content hash. Editing authoritative authoring data SHALL create a new validated definition revision and hash.

#### Scenario: Semantic Resource edit changes the revision
- **WHEN** a valid authoring field changes and conversion succeeds
- **THEN** the adapter reports a new immutable definition with its new content hash

#### Scenario: Invalid Resource edit preserves the prior definition
- **WHEN** an edit fails validation
- **THEN** no new runtime definition is published and the prior valid definition remains usable

### Requirement: Portable documents remain authoritative
Resource conversion SHALL produce the same semantic core definition as the equivalent canonical portable document. Resource identity or Godot cache identity SHALL not replace the core schema version, stable identifier, or content hash.

#### Scenario: Resource and JSON describe the same definition
- **WHEN** equivalent Resource and canonical JSON inputs are converted
- **THEN** their core definitions and canonical content hashes match

### Requirement: Resource isolation is demonstrated in Godot
The adapter SHALL include a Godot practice scene that loads one panel Resource twice and runs two independent instances.

#### Scenario: Resource isolation practice runs
- **WHEN** the public Stage 8 practice scene mutates one loaded instance
- **THEN** the other instance and the shared Resource remain unchanged
