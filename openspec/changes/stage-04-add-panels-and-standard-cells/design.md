## Context

See `proposal.md` and `specs/spatial-circuits/panels-and-standard-cells/spec.md`. The scheduler provides deterministic delivery and state commits. The panel model must remain pure, flat, and suitable for later batched rendering.

## Goals / Non-Goals

**Goals:**

- Execute spatial connectivity without per-cell objects in the runtime hot path.
- Keep each standard rule's timed state explicit and snapshot-compatible.
- Make topology replacement use scheduler incarnations rather than special cases.

**Non-Goals:**

- Add hierarchical chips, custom rule plugins, or Godot presentation.
- Auto-settle a circuit at cycle boundaries.
- Model analog charge or electrical loading.

## Decisions

### Compile definitions into flat panel arrays

Validate coordinates and orientations once, then compile cell kinds, parameters, connectivity, incarnation slots, input indices, output indices, and mutable rule state into parallel flat arrays. Definitions remain immutable.

One object or Godot Node per cell was rejected because it raises memory, dispatch, and rendering costs. A graph of mutable cell objects was rejected because snapshot ordering becomes less explicit.

### Register standard rules behind stable behavior identifiers

Built-in rules use the same stable identity and parameter-validation concepts later exposed to custom rules. The runtime dispatch table maps compact rule indices to pure evaluators.

Hard-coded type switches throughout the scheduler were rejected because Stage 5 extensions would require core scheduler changes.

### Keep wire transport and active-cell state explicit

Routes schedule each edge through ordinary future events. NAND stores its pending candidate and deadline. DFF stores data and edge history. Filters store candidate and consecutive age. Clocks store phase and next edge.

Recomputing timed state from recent inputs was rejected because snapshots and quiet temporal roots would be incomplete.

### Use versioned golden fixtures plus direct rule tests

Direct tests isolate each timing rule. Golden fixtures cover routed interactions, including the XOR glitch and filtered variant. Golden traces are regression evidence, not proof of all circuit behavior.

Testing only final settled outputs was rejected because it would miss transient and setup timing defects.

## Risks / Trade-offs

- [Flat arrays make editing indices complex] -> Centralize definition compilation and topology-edit remapping with invariants.
- [Standard-rule dispatch becomes a hidden extension API] -> Freeze only stable behavior identifiers and validated effects, not internal delegate shapes.
- [Golden traces are updated to fit a bug] -> Derive expected ticks from specs and review trace changes as format or semantic migrations.

## Migration Plan

1. Add panel documents and compilation validation.
2. Add passive connectivity, constants, ports, and probes.
3. Add NAND, clocks, DFFs, and filters with direct timing tests.
4. Add replacement safety and golden routed fixtures.
5. Run the XOR practice with and without filtering.

Rollback disables affected registered rules while keeping Stage 2 documents and the Stage 3 scheduler readable.
