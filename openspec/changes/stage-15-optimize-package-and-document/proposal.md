## Why

The complete feature path is not release-ready until performance claims are measured and the final addon works outside the repository. Stage 15 adds evidence-based optimization, packaging, Windows qualification, and maintained user documentation.

## What Changes

- Measure cell count, active causal frontier, microticks per second, allocation rate, and render frame time on versioned fixtures.
- Optimize only measured bottlenecks and retain selectable reference execution.
- Require reference and optimized runs to produce identical canonical fixture hashes.
- Keep clocks, timers, filters, delayed gates, cable queues, and topology work active through long quiet periods.
- Permit worker execution only when no recorded Node device or cell backend is active.
- Package the pure core and self-contained Godot addon with manifests and version information.
- Add Windows CI, headless import, build, acceptance-scene, export smoke, and blank-consumer installation checks.
- Add maintained installation, architecture, extension, save, replay, diagnostics, performance, and platform-qualification documentation.

## Capabilities

### New Capabilities

- `spatial-circuits/release-performance-and-packaging`: Measured optimization, reference equivalence, safe worker eligibility, Windows qualification, final packaging, and blank-consumer release acceptance.

### Modified Capabilities

None.

## Impact

- Adds benchmarks, release packaging, Windows CI and export checks, and maintained documentation.
- Depends on Stages 12 through 14 and reuses the tamper-detection slice as cross-layer acceptance evidence.
- Preserves hidden-grid chip execution and the reference scheduler as correctness oracles.
- Linux and macOS qualification, behavioral chip macros, and web export remain outside this release.
