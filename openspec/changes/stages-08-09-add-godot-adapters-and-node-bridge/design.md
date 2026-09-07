## Context

See `proposal.md` and the Resource adapter and Node bridge specs. The repository already has a minimal `[GlobalClass]` Resource and Node shell. The new adapter must preserve the pure project boundary and Godot's main-thread and Variant constraints.

## Goals / Non-Goals

**Goals:**

- Convert editor-facing Godot data into the same immutable definitions as canonical JSON.
- Record all causal output from arbitrary Node code.
- Make Node deletion and re-entry safe at scheduler boundaries.

**Non-Goals:**

- Claim arbitrary Node code is deterministic.
- Let Godot Resources own runtime state.
- Run active Node bridges on worker threads.

## Decisions

### Add partial Resource DTOs only in the Godot assembly

Godot-facing partial classes expose Variant-compatible primitives, arrays, dictionaries, and Resource references. Conversion code maps them into pure document builders, then calls the same validation and canonicalization pipeline used by JSON.

Referencing Godot types from pure definitions was rejected because it breaks blank pure builds. Using Resources directly as definitions was rejected because Resources are cached mutable containers.

### Publish immutable revisions after successful conversion

Each conversion deep-copies data and returns either diagnostics or a new core definition with a canonical hash. Existing definitions and instances retain their prior revision after Resource edits.

Updating instances in place was rejected because two devices created from one cached Resource could share mutable state.

### Place one synchronous bridge barrier in the microtick

The adapter gathers committed input and timer DTOs for all recorded Node backends, enters one main-thread bridge phase, invokes live Nodes synchronously in canonical backend order, and returns accepted output requests for reduction.

Delivering inputs through deferred calls was rejected because frame timing would determine causal order. Calling Nodes during event delivery was rejected because they could observe partial input resolution.

### Give Nodes a per-callback capability object

The capability captures world identity, target incarnation, current tick, allowed ports, and callback lifetime. It accepts only `T + 1` or later requests, assigns external command ordinals through the core, and becomes invalid after the barrier.

Giving Nodes the world object was rejected because signals, events, and `_ExitTree` could re-enter the scheduler.

### Record commands and state, then bypass Nodes on replay

Live runs log accepted output commands and a versioned captured-state payload. Replay feeds those commands directly into the core and optionally emits presentation events under a separate policy.

Re-executing Nodes during replay was rejected because arbitrary scene state and frame timing are not reproducible.

### Treat Node lifetime as backend incarnation lifetime

The adapter observes tree exit and weak validity. Deletion queues a disconnect command, invalidates capabilities and callbacks, and relies on ordinary drive release semantics.

## Risks / Trade-offs

- [Variant conversion loses numeric or identifier precision] -> Use explicit adapters and round-trip fixtures for every exported field.
- [Node callback blocks the main thread] -> Keep callbacks bounded and add diagnostics. Performance isolation is deferred until measurements exist.
- [Captured state is not serializable] -> Validate state at acceptance and reject unsupported payloads before logging commands.
- [Godot version changes editor or bridge behavior] -> Pin and exercise the exact Godot .NET version through real headless scenes.

## Migration Plan

1. Replace the placeholder Resource with versioned DTO families while keeping its public install surface compatible.
2. Add conversion, deep-copy, hash display, and Resource isolation tests.
3. Add the barrier, capabilities, re-entry guards, logging, replay, and deletion behavior.
4. Run the Resource isolation and recorded responder practices.

Rollback removes the adapter and uses canonical JSON plus pure backends. Existing portable definitions and pure execution remain authoritative.
