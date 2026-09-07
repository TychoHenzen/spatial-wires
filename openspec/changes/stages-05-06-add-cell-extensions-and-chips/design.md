## Context

See `proposal.md` and both capability specs. Custom cells and chips depend only on the Stage 4 panel and timing contracts. They can be built in parallel after their shared definition, snapshot, and port conventions are fixed.

## Goals / Non-Goals

**Goals:**

- Let external pure behavior participate without scheduler access.
- Package panels as exact hierarchical components with independent state.
- Preserve portable version and migration boundaries for both extension paths.

**Non-Goals:**

- Load untrusted code or provide a sandbox.
- Replace hidden-grid execution with behavioral macros.
- Add Godot-backed cell behavior.

## Decisions

### Expose a narrow pure rule registry

Registrations provide a stable namespaced ID, behavior version, parameter validator, state codec, migration entries, and evaluator. Evaluation receives value copies and a bounded effect builder. It has no world, clock, filesystem, thread, or Godot access.

Passing the scheduler into extensions was rejected because it permits re-entry and hidden causal mutation. Persisting CLR type names was rejected because assemblies can move without changing behavior identity.

### Validate all proposals at the effect boundary

The effect builder records next state, future timers, and at most one drive per output. It rejects past deadlines, duplicate outputs, invalid ports, and non-serializable state before reduction.

Trusting each extension to obey scheduler rules was rejected because one rule could make the whole world nondeterministic.

### Represent chips as hash-pinned definition graphs

A chip document embeds or references one immutable source panel, its named port map, symbol, parameters, content hash, version, and dependency manifest. Validation topologically sorts dependencies and rejects cycles.

Resolving the latest source asset at runtime was rejected because existing instances would silently change. Flattening source panels during authoring was rejected because exact state and timing would be lost.

### Run every chip as an owned nested panel instance

The parent panel owns the chip incarnation and nested panel state. Port events cross explicit scheduled boundaries. Collapse is presentation metadata only, so the same nested instance runs in both views.

Separate expanded and collapsed execution engines were rejected because equivalence would become an ongoing proof obligation.

### Keep the two stage tracks independently runnable

Shared contracts land first. The external pattern-detector fixture verifies custom cells. The two-instance XOR fixture verifies chips. Either track can be disabled without removing the other's public contracts.

## Risks / Trade-offs

- [External assemblies change without version changes] -> Record behavior version and assembly fingerprint in later durable manifests.
- [Deep hierarchy costs more than flattened execution] -> Keep hidden grids as the reference path and defer measured optimization to Stage 15.
- [Migration hooks mutate source data] -> Transform copies and publish only validated results.

## Migration Plan

1. Freeze shared behavior identity, state codec, and nested-port contracts.
2. Implement and verify the pure rule registry and external pattern detector.
3. Implement and verify chip documents, cycle detection, and nested instances.
4. Run both public practice fixtures.

Rollback removes the reference extension and disables new chip authoring. Registry diagnostics and expanded-grid reading remain available for existing documents.
