## Purpose

Defines Godot editor tools that author the same portable panel and chip assets as the runtime workbench and cleanly detach from a consumer project.

## ADDED Requirements

### Requirement: Editor authoring wraps the runtime workbench contract
The editor plugin SHALL expose panel and chip authoring through the runtime workbench's validated actions and portable document format. Editor-only state SHALL not become authoritative circuit data.

#### Scenario: Panel is authored in the editor
- **WHEN** a user creates and saves a panel through the editor plugin
- **THEN** the runtime workbench opens the same portable definition with the same canonical hash

#### Scenario: Chip is authored in the editor
- **WHEN** a user packages a valid panel as a chip through the editor plugin
- **THEN** the resulting asset validates and executes through hidden-grid runtime behavior

### Requirement: Editor tools expose import and validation identity
The plugin SHALL import supported portable definitions, show stable validation diagnostics, show schema version and content hash, and open referenced definitions without mutating valid source data on failure.

#### Scenario: Invalid definition is imported
- **WHEN** import validation fails
- **THEN** the editor shows the stable diagnostics and does not publish a partial Resource revision

#### Scenario: User opens a referenced definition
- **WHEN** a valid chip or device reference is selected
- **THEN** the plugin opens the definition matching its pinned stable identifier and content hash

### Requirement: Dock integration has a clean lifecycle
The Godot 4.7 dock surface SHALL be isolated behind one plugin-owned adapter. Enabling, disabling, and re-enabling the plugin SHALL not leak docks, signal handlers, registrations, or duplicate workbench instances.

#### Scenario: Plugin is disabled after use
- **WHEN** an enabled editor plugin is disabled
- **THEN** all plugin-owned docks, handlers, and registrations are removed

#### Scenario: Plugin is re-enabled
- **WHEN** the plugin is enabled again in the same editor session
- **THEN** exactly one working instance of each editor integration is present

### Requirement: Final editor addon remains independently installable
The Stage 1 blank-consumer installation and lifecycle checks SHALL pass with the Stage 14 editor tools included in the staged addon.

#### Scenario: Updated addon is installed in a blank consumer
- **WHEN** the staged addon is copied to an independent compatible Godot project
- **THEN** the project imports, builds, enables, uses, disables, and re-enables the plugin without source-tree dependencies

### Requirement: Editor-to-runtime practice is portable
The public Stage 14 practice SHALL author a panel and chip entirely through the editor plugin and then open and run the same assets in the runtime workbench.

#### Scenario: Authored assets cross surfaces
- **WHEN** the documented editor practice saves its panel and chip
- **THEN** the runtime workbench opens them with matching identifiers and hashes and executes the expected trace
