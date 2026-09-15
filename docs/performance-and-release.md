# Performance and release

SpatialWires targets Godot 4.7.1 .NET with the .NET 8.0.410 SDK. The pure
simulation projects do not reference GodotSharp. Godot Nodes, Resources, and
editor tools live in `addons/spatial_circuits`.

Use the existing guides for the public contracts:

- [Node-backed cells](node-backed-cells.md) describes adapter boundaries and
  main-thread ownership.
- [Durable state](durable-state.md) describes save, replay, formats, and
  diagnostics.
- [Editor authoring](editor-authoring.md) describes Resource conversion,
  extension points, and editor lifecycle.
- [Independent addon practice](independent-addon-practice.md) describes a
  second-consumer installation check.

## Performance evidence

Run the checked-in temporal fixture with a Release build:

```powershell
dotnet run --project benchmarks/SpatialCircuits.Benchmarks.csproj --configuration Release -- benchmarks/fixtures/temporal-frontier.fixture.json benchmarks/results/temporal-frontier.json
```

The report records the fixture hash, panel cell count, active frontier,
throughput, allocation, pure-step frame time, execution mode, build
configuration, timestamp, and machine context. It stores reference and
optimized traces and reports the first differing tick and snapshot path.

Reference mode evaluates the complete panel index range. Optimized mode uses
the active-cell frontier. Both modes are selectable and must produce equal
per-tick hashes before a report is accepted. Clocks, timers, filters, delayed
gates, cable queues, and topology therefore remain on the same causal ticks.

The benchmark frame-time value measures pure simulation work. It does not
claim a Godot render-frame rate.

## Package and Windows qualification

Create a self-contained source package outside the repository:

```powershell
powershell -NoProfile -File .\scripts\Package-SpatialCircuitsRelease.ps1 `
  -OutputRoot C:\Temp\spatial-circuits-package
```

The package contains the addon and identified source dependencies. Its metadata
stores version information and sorted relative paths with SHA-256 hashes. The
stager rejects binaries, linked files, project references, and repository
relative addon dependencies.

Run the bounded Windows qualification with the same Godot executable:

```powershell
powershell -NoProfile -File .\scripts\Test-WindowsRelease.ps1 `
  -GodotExecutable C:\Development\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe
```

The command runs managed build and pure tests, Godot import and adapter tests,
Node acceptance tests, package checks, Windows export, blank-consumer package
acceptance, and the independent addon practice. Each child process has a
timeout and a nonzero exit code fails the suite. The same command is available
through `.github/workflows/windows-release.yml`.

Worker execution is admitted only when no recorded Node behavior is active.
When Node behavior is present, the workbench refuses worker mode before the
simulation step touches a Godot object. Linux, macOS, and web qualification
remain unverified.
