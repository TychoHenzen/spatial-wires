## Why

The deterministic scheduler needs a spatial circuit model that users can run and inspect. Stage 4 supplies the first useful panel circuits while preserving flat data and explicit timing.

## What Changes

- Add immutable panel layouts and independent flat-array panel runtime instances.
- Add empty, wire, junction, crossing, constant, panel-port, probe, NAND, clock, DFF, and stability-filter cell rules.
- Apply transport delay to routes and the specified inertial-delay behavior to NAND outputs.
- Apply the explicit DFF setup, hold, and clock-to-output timing contract.
- Add cell incarnation handling so replaced cells cannot receive stale events.
- Add golden timing fixtures for route delay, glitches, filtering, DFF setup failure, and replacement safety.
- Add an XOR glitch practice fixture that runs with and without a stability filter.

## Capabilities

### New Capabilities

- `spatial-circuits/panels-and-standard-cells`: Flat panel execution, standard timed cells, probes, ports, and topology-safe cell replacement.

### Modified Capabilities

None.

## Impact

- Extends the pure `Core` and `Cells` projects and versioned scenario fixtures.
- Depends on the Stage 3 scheduler and Stage 2 document contracts.
- Creates the panel execution surface required by custom cells, chips, devices, and the workbench.
- Does not add chips, world devices, cables, or Godot UI.
