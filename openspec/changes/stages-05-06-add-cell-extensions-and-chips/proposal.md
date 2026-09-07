## Why

Standard cells alone cannot express reusable project-specific behavior or packaged subcircuits. Stages 5 and 6 add these parallel extension paths over the same Stage 4 panel contract.

## What Changes

- Add stable namespaced custom-cell rule registration with restricted read and effect contexts.
- Add custom-cell parameter validation, serializable state, behavior versions, and migration hooks.
- Add an external reference pattern-detector rule without changing the standard-cell library.
- Add hash-pinned chip definitions with named port maps, parameters, symbols, and lower-level dependencies.
- Reject recursive chip dependency graphs and keep each chip instance's runtime state independent.
- Execute a chip's hidden source grid identically in expanded and collapsed views.
- Add separate public practice fixtures for the external pattern detector and two independent XOR chip instances.

## Capabilities

### New Capabilities

- `spatial-circuits/custom-cell-extensions`: Stable pure-cell registration, restricted evaluation, validation, serialized state, behavior versions, and migrations.
- `spatial-circuits/hierarchical-chips`: Hash-pinned acyclic chip definitions, independent panel-owned instances, and exact hidden-grid execution.

### Modified Capabilities

None.

## Impact

- Extends the pure `Cells` and `Hierarchy` projects plus core fixtures.
- Depends on Stage 4 panels and standard cells.
- Preserves hidden-grid execution as the correctness path. Behavioral chip macros remain out of scope.
- Does not add Godot-backed cells, devices, cables, or editor authoring.
