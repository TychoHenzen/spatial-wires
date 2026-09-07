## Context

See `proposal.md` and `specs/spatial-circuits/release-performance-and-packaging/spec.md`. All functional stages are prerequisites. This stage changes performance and distribution surfaces only when reference behavior remains observable and reproducible.

## Goals / Non-Goals

**Goals:**

- Establish repeatable baselines before selecting optimizations.
- Keep reference execution available as the release correctness oracle.
- Produce one independently consumable Windows-qualified addon package.

**Non-Goals:**

- Set unsupported performance targets without measurements.
- Introduce behavioral chip macros.
- Claim Linux, macOS, or web qualification.

## Decisions

### Version benchmark fixtures and environment records

Add benchmark projects only now. Each result records fixture and definition hashes, build mode, machine and runtime context, cell count, active frontier, throughput, allocation, and render timing. Store release baselines as data, not prose alone.

Microbenchmarks without end-to-end fixture identity were rejected because they cannot detect semantic shortcuts or compare releases reliably.

### Gate each optimization against the reference path

Introduce one selectable optimization at a time. Run direct tests, long-quiet temporal-root fixtures, and the full qualified hash set against both paths. Keep a runtime or test switch for reference execution.

Replacing the reference implementation was rejected because future regressions would lose their oracle.

### Base frontier activity on future causal obligations

The optimized frontier includes due events, persistent changes, clocks, timers, filter aging, pending gate deadlines, cable queues, and topology commands. Recent input activity alone never determines eligibility.

Skipping quiet components by last-input time was rejected because temporal roots must wake without new input.

### Centralize execution-mode admission

One world-level policy selects main-thread or worker execution before each run segment and topology publication. Any active recorded Node device or cell forces the main-thread barrier. Workers receive only detached pure data.

Letting adapters decide independently was rejected because a Node could be touched from a worker through an unnoticed path.

### Extend the proven Stage 1 staging pipeline for release packaging

Package pure binaries or source as selected by measured distribution needs, the Godot addon, version metadata, licenses, and a path-hash manifest. Install the package into a generated blank project outside the repository for acceptance.

Testing only the repository project was rejected because project references and source paths could mask a broken package.

### Make Windows CI replay local release gates

CI runs the same bounded scripts used locally for managed checks, Godot import and build, tamper-detection acceptance, export smoke, package verification, and blank-consumer practice. Preserve real child exit codes and bounded artifacts.

### Tie documentation to release evidence

Documentation links exact commands, package format, supported extension contracts, save and replay compatibility, benchmark context, and platform qualification status. Deferred platforms remain explicitly unverified.

## Risks / Trade-offs

- [Benchmarks are noisy] -> Record environment context, use repeated runs, and separate correctness hashes from performance statistics.
- [Optimization passes fixtures but breaks an untested root] -> Keep the reference path and add a fixture for every optimized causal category.
- [Package shape differs from Stage 1 source staging] -> Validate both manifest and independent consumer behavior before selecting the final form.
- [Windows export requires external templates] -> Validate required tools before the gate and report unexecuted checks as failures, not passes.

## Migration Plan

1. Record reference correctness and performance baselines.
2. Apply and gate measured optimizations one at a time.
3. Add execution-mode admission and worker equivalence tests.
4. Build and verify the final package in an independent consumer.
5. Add Windows CI, export smoke, and maintained documentation.
6. Run the final blank-project timed-exchange practice.

Rollback disables each optimization independently and falls back to reference execution. Release packaging does not replace source assets or prior valid packages until all gates pass.
