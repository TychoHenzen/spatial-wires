## Context

See `proposal.md` and `specs/spatial-circuits/runtime-panel-workbench/spec.md`. The core exposes panels, chips, devices, cables, accepted topology commands, and trace DTOs. The workbench is a Godot presentation and command adapter, not a second simulation engine.

## Goals / Non-Goals

**Goals:**

- Render and edit large panels without per-cell scene nodes.
- Make paused and running edit semantics visible and testable.
- Use the same definitions and trace values as the command-line runner.

**Non-Goals:**

- Add durable save and migration behavior.
- Put causal timing in `_Process(delta)`.
- Hide validation failures through automatic topology repair.

## Decisions

### Render from immutable presentation snapshots in batches

Project committed panel values, symbols, selection, and trace overlays into compact presentation buffers. Draw cells, wires, and overlays in layer batches through a small number of Godot canvas objects. Incremental dirty regions update buffers after commits.

One Node per cell was rejected by the architecture boundary. Re-reading mutable core arrays during draw was rejected because frames could observe partial changes.

### Route every authoring action through validated commands

Workbench tools build portable definition edits or runtime topology commands. A shared validator returns a preview and diagnostics before an accepted command receives its ordinal.

Directly changing runtime arrays from UI handlers was rejected because replay and incarnation invalidation would be bypassed.

### Separate paused staging from committed simulation state

Paused authoring keeps a staged immutable definition revision. Explicit commit or step converts the difference into accepted topology commands. Running authoring submits commands immediately for the next valid boundary.

Advancing the world on every paused click was rejected because users need to compose edits without changing time.

### Drive time controls with integer step requests

Microtick step requests exactly one scheduler step. Cycle step requests the configured count and stops there even if events remain. `_Process` may request presentation refresh but never supplies causal time.

Auto-settle was rejected because it would erase route timing and make cycles data-dependent.

### Build waveforms from committed trace DTOs

Probes and waveform views consume the same tick-value records as the runner and replay diagnostics. View zoom and frame rate affect only projection.

Sampling visible values each frame was rejected because short microtick glitches could disappear.

## Risks / Trade-offs

- [Batched redraw logic shows stale cells] -> Compare rendered buffers against committed revisions in Godot integration tests.
- [Staged edits conflict with later runtime state] -> Revalidate the staged diff against the current definition before commit.
- [Complex tool state obscures public behavior] -> Keep each tool as input-to-command logic and test it without rendering where possible.

## Migration Plan

1. Add the read-only batched panel and waveform projection.
2. Add time controls and paused staging.
3. Add panel, chip, port, and cable authoring commands.
4. Add running-edit logging and incarnation tests.
5. Run the complete XOR-to-delayed-device practice.

Rollback removes the workbench scene. Pure simulation, Resources, portable definitions, and runner fixtures remain usable.
