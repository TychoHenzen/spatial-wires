## Context

See `proposal.md` and `specs/spatial-circuits/deterministic-scheduler/spec.md`. Stage 2 supplies stable documents, identifiers, traces, and the runner. This stage must freeze deterministic causal semantics before any concrete cell implementation depends on them.

## Goals / Non-Goals

**Goals:**

- Make event ordering and microtick phases explicit in data and tests.
- Separate committed state, evaluation proposals, and future scheduled effects.
- Keep enough in-memory state to prove restore and replay equivalence.

**Non-Goals:**

- Optimize the event frontier.
- Serialize durable save files.
- Add Godot callbacks or standard cell rules.

## Decisions

### Represent every scheduled item with the full canonical key

Store due tick, phase, stable target and incarnation, target port or lane, stable source and port, event kind, and persisted causal ordinal on each queue entry. Use one comparer for queue order, canonical serialization, hashing, and diagnostic output.

Using insertion order as a tie-breaker was rejected because collection enumeration would affect behavior. Using only due tick and target was rejected because same-target ports and sources would remain ambiguous.

### Execute phases through one non-reentrant step coordinator

The world step owns an explicit phase state. It buffers accepted topology commands and evaluation proposals, commits only at defined barriers, and rejects nested stepping or direct mutation while active.

Letting components mutate world state during callbacks was rejected because same-tick behavior would depend on callback order.

### Persist ordinals at acceptance and reduction boundaries

External commands receive ordinals when accepted. Internal proposals are collected, validated, canonically sorted, then assigned causal ordinals. Ordinal counters are part of snapshots.

Assigning internal ordinals during component enumeration was rejected because data layout could alter traces.

### Model persistent drives and incarnations as indexed world state

Drive tables key entries by stable source identity. Runtime targets use monotonically assigned incarnations. Topology removal invalidates old targets before generated release drives enter delivery.

Deleting queued events eagerly was rejected because it complicates queue mutation and diagnostics. Incarnation checks make stale work harmless and observable.

### Snapshot committed state through a reference representation

Create an immutable end-of-tick snapshot containing counters, target incarnations, drive tables, event queue, cancellation state, temporal roots, and trace cursor. Restore builds a fresh mutable world from that snapshot.

Serializing implementation object graphs was rejected because reference identity and container internals are not causal state.

## Risks / Trade-offs

- [Canonical comparer omits a causal dimension] -> Add adversarial same-tick and shuffled-insertion fixtures for every key field.
- [Phase buffering increases allocation] -> Preserve the simple reference path until Stage 15 measurements justify optimization.
- [Stale events accumulate] -> Retain them for correctness first, then measure bounded pruning without changing hashes.

## Migration Plan

1. Add event, command, phase, drive, and incarnation data contracts behind the Stage 2 runner.
2. Add the reference step coordinator and direct phase tests.
3. Add snapshots, restore, hashing, and command replay.
4. Run the ten-microtick public practice fixture.

Rollback keeps Stage 2 documents and replaces the scheduler behind its public runner facade.
