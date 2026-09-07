## Context

See `proposal.md` and `specs/spatial-circuits/durable-state-and-diagnostics/spec.md`. The scheduler already exposes complete in-memory snapshots and the Node bridge records commands. This stage defines the first durable runtime-state contract.

## Goals / Non-Goals

**Goals:**

- Serialize every cause needed to continue at the next microtick.
- Preserve prior valid files across save and migration failures.
- Make replay divergence diagnosable by stable paths.

**Non-Goals:**

- Save mid-phase mutable object graphs.
- Silently accept changed definitions or behavior assemblies.
- Make presentation events causal.

## Decisions

### Define a versioned durable snapshot DTO separate from runtime objects

Map an end-of-tick in-memory snapshot into explicit portable records for counters, manifests, incarnations, drives, queues, rule state, devices, cables, bridge state, topology edits, and cursors. Canonical ordering follows scheduler event keys and stable IDs.

General-purpose object serialization was rejected because private fields, references, and collection order are not a stable contract.

### Use validate-then-replace save transactions

Write a candidate in the target directory, flush it, load and validate it independently, then replace the destination atomically while retaining the prior valid path until success. Cleanup targets only the known candidate.

Writing directly to the destination was rejected because interruption can destroy the only valid save.

### Pin a complete deterministic manifest

Record definition IDs, schema versions, content hashes, custom behavior IDs and versions, and assembly fingerprints. Load and replay resolve the entire manifest before creating runtime state.

Best-effort loading against newer assets was rejected because divergence could appear much later and look like a scheduler fault.

### Migrate copies through an ordered registry

Each migration declares input and output schema ranges and transforms a detached DTO. Chain selection is explicit. Each step validates, and publication occurs only after final canonicalization.

In-place migration was rejected because failed transformations could corrupt source assets.

### Separate causal and presentation replay channels

Causal commands always replay in recorded order. Presentation events carry IDs and a policy for emit, suppress, or deduplicate. They never alter snapshot hashes.

Treating audiovisual callbacks as causal was rejected because frame and scene state would affect deterministic replay.

### Diff canonical snapshot paths

Normalize both snapshots and walk stable paths in canonical order. Report the first difference plus bounded context. Trace export uses the same identifiers and event keys.

Dumping full object graphs was rejected because output is noisy and ordering can hide the first causal divergence.

## Risks / Trade-offs

- [A causal field is omitted] -> Add save-load fixtures for every temporal root and require uninterrupted hash equivalence.
- [Atomic replacement semantics vary by platform] -> Qualify Windows first and keep a recoverable prior file until replacement succeeds.
- [Assembly fingerprints change for behavior-neutral builds] -> Require an explicit migration or compatibility declaration rather than guessing.

## Migration Plan

1. Freeze the snapshot DTO and canonical manifest.
2. Add save transactions and negative failure fixtures.
3. Add load, manifest resolution, and migration registry.
4. Add replay policy, trace export, and snapshot diffing.
5. Run the saved Node exchange practice.

Rollback retains old readers and fixtures. Failed migrations never overwrite source assets or the previous valid save.
