## Context

See `proposal.md` for motivation and `specs/spatial-circuits/domain-documents-and-runner/spec.md` for behavior. The repository has empty pure domain projects and a proven Godot package boundary. This stage creates the first durable contract, so later stages must consume it without adding Godot dependencies.

## Goals / Non-Goals

**Goals:**

- Make one immutable portable model the input to runtime factories, hashing, fixtures, and later Resource conversion.
- Give validation and runner failures stable machine-readable identities.
- Keep canonical bytes testable across processes and source ordering.

**Non-Goals:**

- Schedule microticks or define timed cell behavior.
- Treat JSON object layout as mutable runtime state.
- Add a second Godot-specific source of truth.

## Decisions

### Use explicit domain value types and immutable definition records

Define logic values, stable identifiers, coordinates, ports, parameters, and document versions in `SpatialCircuits.Core`. Definition objects expose read-only collections and validate through factories. Runtime state is created separately and never stored back into definitions.

Using mutable DTOs throughout was rejected because shared loaded objects could leak state. Using strings for every identifier was rejected because categories and validation would remain ambiguous.

### Canonicalize through one writer before hashing

Parse supported JSON into the immutable model, then write through one canonical serializer with fixed property order, ordinal collection ordering, normalized numeric forms, and UTF-8 output. Hash only those canonical bytes.

Hashing source JSON was rejected because whitespace and permitted source ordering would change identity. Relying on reflection property order was rejected because it is not the saved-format contract.

### Return diagnostics as data

Validation returns ordered diagnostics containing a stable code, severity, document path, and short message. Runner text is a presentation of those records. Tests bind to codes and paths, not full prose.

Exceptions as the normal validation surface were rejected because fixture failures need several stable findings and reliable process output.

### Add a pure console runner project

Add `src/SpatialCircuits.Runner/` as a pure .NET executable that references only pure projects. Its versioned fixture envelope selects a scenario action, inputs, expected observations, and trace format version.

Embedding the runner only in tests was rejected because each pre-UI stage needs a public practice surface. Putting it in the Godot host was rejected because it would reverse the dependency boundary.

## Risks / Trade-offs

- [Canonical serialization rules are incomplete] -> Add property-order, collection-order, encoding, and round-trip fixtures before publishing later assets.
- [Early document fields become expensive to change] -> Keep schema versions explicit and delay publication claims until the stage practice passes.
- [Diagnostic prose changes break consumers] -> Treat code and path as stable while allowing message text to improve.

## Migration Plan

1. Add domain primitives and validation without changing the Stage 1 public addon types.
2. Add canonical document reading, writing, and hash fixtures.
3. Add isolated runtime factories and the console runner.
4. Run the public drive-resolution fixture.

Rollback removes the unpublished Stage 2 format and runner. The archived Stage 1 package baseline remains usable.
