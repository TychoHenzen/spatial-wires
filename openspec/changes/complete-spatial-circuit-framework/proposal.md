## Why

The proven package baseline does not yet provide the deterministic circuit behavior, authoring surfaces, persistence, gameplay slice, or distributable release described by the reviewed implementation plan. Completing Stages 2 through 15 now gives each later stage a testable contract while preserving Stage 1 as the installation and dependency prerequisite.

## What Changes

- Add portable circuit documents, stable identifiers, canonical serialization, validation, content hashes, and a command-line scenario runner.
- Add deterministic microtick scheduling, panels, standard cells, pure custom cells, exact hierarchical chips, devices, and delayed directed cables.
- Add Godot Resource conversion, recorded Node bridges, a runtime workbench, durable saves, replay, migrations, traces, and diagnostics.
- Add the tamper-detection gameplay slice, sparse Node-backed cells, and editor authoring tools.
- Add measured optimization, reference-versus-optimized equivalence checks, Windows packaging, blank-consumer acceptance, and maintained documentation.
- Keep every construction stage independently runnable with one public-surface practice test and a documented rollback boundary.
- Keep analog simulation, behavioral chip macros, untrusted code, multiplayer, web export, and cross-platform qualification outside the first release.

## Capabilities

### New Capabilities

- `spatial-circuits/domain-documents-and-runner`: Portable definitions, four-state logic, stable diagnostics, canonical hashes, independent runtime instances, and the core scenario runner.
- `spatial-circuits/deterministic-scheduler`: Canonical event ordering, microtick phases, persistent drives, incarnation invalidation, snapshots, hashes, and core command replay.
- `spatial-circuits/panels-and-standard-cells`: Flat panel execution, standard timed cells, probes, ports, and topology-safe replacement.
- `spatial-circuits/custom-cell-extensions`: Stable pure-cell registration, restricted effects, validation, serialized state, behavior versions, and migrations.
- `spatial-circuits/hierarchical-chips`: Hash-pinned chip definitions, acyclic hierarchy, independent instances, and exact hidden-grid execution.
- `spatial-circuits/devices-and-cables`: Device backends, ports, directed cable lanes, bundles, transport delay, epochs, and disconnect or reconnect behavior.
- `spatial-circuits/godot-resource-adapters`: Variant-compatible Resource DTOs and deep-copy conversion into validated immutable core definitions.
- `spatial-circuits/recorded-node-bridge`: Main-thread Node device execution through capability-limited commands, state capture, re-entry protection, and replayed outputs.
- `spatial-circuits/runtime-panel-workbench`: Batched runtime rendering, editing, stepping, probes, waveforms, chip placement, and cable authoring.
- `spatial-circuits/durable-state-and-diagnostics`: Complete causal saves, atomic replacement, migrations, replay, trace export, and snapshot comparison.
- `spatial-circuits/tamper-detection-gameplay`: The deterministic challenge-response fixture, alarm rules, cable interactions, and player-built bypass acceptance path.
- `spatial-circuits/node-backed-custom-cells`: Sparse recorded Node cell bindings with safe deletion, replay, timers, and worker-mode restrictions.
- `spatial-circuits/editor-authoring-tools`: Godot editor import, validation, content-hash, panel, and chip authoring over the runtime workbench.
- `spatial-circuits/release-performance-and-packaging`: Measured optimization, reference equivalence, Windows CI and export checks, final addon packaging, and blank-consumer qualification.

### Modified Capabilities

None.

## Impact

- Extends the pure projects under `src/` and the Godot adapter under `addons/spatial_circuits/` without allowing `GodotSharp` into the simulation core.
- Adds versioned fixtures, test projects, runnable examples, benchmarks, packaging checks, and maintained technical documentation.
- Establishes public document, scenario-runner, workbench, addon, and replay surfaces that later changes must migrate explicitly.
- Retains the archived `spatial-circuits/package-baseline` capability unchanged as the Stage 1 prerequisite.
