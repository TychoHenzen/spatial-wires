## Why

The framework has no public runtime surface for building or observing circuits. Stage 10 adds a usable workbench while keeping edits and time progression inside deterministic command boundaries.

## What Changes

- Add batched panel rendering without one Godot Node per ordinary cell.
- Add palette, paint, erase, rotate, selection, ports, chip placement, and cable bundle authoring.
- Add pause, single-microtick step, configured-cycle step, probes, and waveform display.
- Route live edits through accepted topology commands at microtick boundaries.
- Ensure paused edits commit only on an explicit commit or step action.
- Record running edits in the command log and invalidate old cell incarnations.
- Add the public practice flow that draws an XOR, observes its glitch, packages it, and connects delayed devices.

## Capabilities

### New Capabilities

- `spatial-circuits/runtime-panel-workbench`: Batched runtime rendering, circuit editing, deterministic stepping, probes, waveforms, chip placement, and cable authoring.

### Modified Capabilities

None.

## Impact

- Adds a Godot runtime scene and adapter-facing presentation code.
- Depends on panels, chips, devices, cables, and Resource adapters.
- Shares stable trace DTOs with Stage 11 but does not define durable save behavior.
- Leaves the pure simulation and portable assets usable without the workbench.
