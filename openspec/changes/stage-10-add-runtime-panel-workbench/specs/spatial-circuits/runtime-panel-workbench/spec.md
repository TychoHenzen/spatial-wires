## Purpose

Defines the runtime Godot workbench for building, timing, observing, packaging, and connecting circuits through deterministic public editing surfaces.

## ADDED Requirements

### Requirement: Panel rendering is batched
The workbench SHALL render ordinary panel cells and wires in batches and SHALL NOT create one Godot Node per ordinary cell.

#### Scenario: Large panel is displayed
- **WHEN** the workbench displays a populated panel
- **THEN** ordinary cell count does not cause a matching count of Godot Nodes

### Requirement: Runtime authoring covers panel and world topology
The workbench SHALL provide palette, paint, erase, rotate, select, panel-port, chip-placement, and cable-bundle authoring through validated public actions.

#### Scenario: User builds a connected chip device
- **WHEN** a user paints a valid panel, packages it as a chip, places its device, and authors a compatible cable bundle
- **THEN** the resulting portable definitions validate and can run

#### Scenario: Authoring action is invalid
- **WHEN** an action would create an out-of-bounds cell, invalid orientation, recursive chip, or incompatible cable
- **THEN** the action is rejected with a visible diagnostic and does not partially alter the definition

### Requirement: Live edits are accepted topology commands
Every edit to a running circuit SHALL receive an accepted command ordinal and SHALL commit at a microtick boundary. Replacing a live component SHALL invalidate its former incarnation.

#### Scenario: User edits while simulation runs
- **WHEN** a valid topology edit is accepted during execution
- **THEN** it appears in the command log and commits at its assigned microtick boundary

#### Scenario: Live cell replacement has delayed work
- **WHEN** a running cell is replaced before its delayed event matures
- **THEN** the old event cannot affect the new cell

### Requirement: Paused edits require explicit commitment
While paused, the workbench SHALL stage valid edits without advancing causal time. Staged edits SHALL commit only when the user explicitly commits or steps the simulation.

#### Scenario: User edits while paused
- **WHEN** a user stages an edit and takes no commit or step action
- **THEN** simulation state and current microtick remain unchanged

#### Scenario: User steps after a paused edit
- **WHEN** a staged edit is followed by one microtick step
- **THEN** the edit commits in the topology phase of that step

### Requirement: Time controls use explicit microticks
The workbench SHALL provide pause, one-microtick step, and configured-cycle step controls. A cycle SHALL advance its configured number of microticks and SHALL not force the circuit to settle.

#### Scenario: Cycle step contains unsettled work
- **WHEN** one cycle ends while causal events remain pending
- **THEN** the workbench stops at the cycle boundary and leaves those events pending

### Requirement: Probes and waveforms show committed traces
Users SHALL be able to place probes and inspect four-state values against exact microtick positions without the presentation frame rate changing causal execution.

#### Scenario: Unequal-path glitch is observed
- **WHEN** the XOR fixture runs in the workbench
- **THEN** its waveform shows the known transient at the same microticks as the core runner trace

### Requirement: Complete workbench practice flow is runnable
The public Stage 10 practice flow SHALL draw the XOR, observe its glitch, package it as a chip, and connect its device through a delayed cable bundle.

#### Scenario: Workbench practice completes
- **WHEN** the documented practice actions are performed through public workbench controls
- **THEN** the resulting exchange runs and its waveform contains the expected panel and cable timing
