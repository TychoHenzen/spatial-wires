# Review ledger: issue #2

This ledger records the findings from the repository review that led to issue
#2. It is evidence for the issue acceptance criteria, not a planning contract.

| Finding | Disposition | Evidence |
| --- | --- | --- |
| LOCAL-1 | Fixed | `src/SpatialCircuits.Core/CircuitValidator.cs` returns ordered structured diagnostics for invalid circuits. |
| LOCAL-2 | Fixed | `tests/SpatialCircuits.Core.Tests/DependencyBoundaryTests.cs` inventories `SpatialCircuits.Runner` and its compiled assembly. |
| LOCAL-3 | Fixed | `tests/acceptance/blank-consumer-addon.test.mjs` proves the outer timeout exceeds the timeouts declared by `Run-BlankConsumerAcceptance.ps1`. |
| LOCAL-4 | Fixed | `.github/workflows/ci.yml` runs the unavailable-toolchain, bounded-process, and addon-staging safety checks. |
| LOCAL-5 | Superseded | OpenSpec is deprecated for this repository. No OpenSpec artifact was edited or extended. |
| LOCAL-6 | Fixed | `Reference/godot-dotnet-spatial-circuit-implementation-plan.md` uses the pinned Godot 4.7.1 toolchain. |
