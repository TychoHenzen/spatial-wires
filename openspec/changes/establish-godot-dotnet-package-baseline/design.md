## Context

See `proposal.md` for motivation and `specs/spatial-circuits/package-baseline/spec.md` for the behavior contract.

The repository currently contains one Godot project and no managed solution. Stage 1 in the reviewed implementation plan requires a package spike that proves the pure .NET boundary, C# editor addon, blank consumer, and headless checks before simulation code exists.

[Godot 4.7 C# guidance](https://docs.godotengine.org/en/4.7/tutorials/scripting/c_sharp/index.html) requires the .NET-enabled editor and supports .NET 8 or later. Godot's [plugin installation guidance](https://docs.godotengine.org/en/stable/tutorials/plugins/editor/installing_plugins.html) uses an `addons/<plugin-name>` directory with `plugin.cfg`. Its [plugin construction guidance](https://docs.godotengine.org/en/stable/tutorials/plugins/editor/making_plugins.html) requires removing an added dock during plugin shutdown.

## Goals / Non-Goals

**Goals:**

- Freeze one Windows-first Godot 4.7.1 .NET and .NET 8 build baseline.
- Prove the planned dependency direction before domain code depends on it.
- Produce a self-contained addon unit that compiles inside an independent consumer.
- Exercise enable, disable, and re-enable behavior through real Godot editor APIs.
- Keep every automated process bounded and preserve its actual exit code.
- Leave one practice command that installs and uses the addon outside the source tree.

**Non-Goals:**

- Circuit values, scheduling, panels, chips, devices, cables, or gameplay.
- A durable definition or save format.
- Linux, macOS, mobile, web, export-template, or package-registry qualification.
- Precompiled release optimization or a stable public API beyond the baseline Node and Resource identities.

## Decisions

### Pin Godot 4.7.1 and .NET 8 separately

Add `global.json` pinned to the installed .NET SDK `8.0.410` with roll-forward disabled. Add a small version manifest for Godot `4.7.1`, the `.NET` editor variant, and `net8.0` targets.

A PowerShell entry point accepts an explicit Godot executable path or a project-specific environment variable. It runs `--version` and rejects the standard non-.NET editor or a different Godot version before build work starts.

This keeps the engine binary outside Git while making the required version observable. Relying on `PATH` alone was rejected because this machine currently has no `godot` command and may contain several editor variants later. Allowing the newest installed SDK was rejected because the repository would silently change compilers.

### Use one solution with three pure projects and one Godot host

Create `SpatialWires.sln` with these first managed boundaries:

```text
src/SpatialCircuits.Core/
src/SpatialCircuits.Cells/
src/SpatialCircuits.Hierarchy/
tests/SpatialCircuits.Core.Tests/
SpatialWires.csproj
```

The three `src` projects target `net8.0` and never reference `GodotSharp`. Project references point only inward toward lower pure layers. `SpatialWires.csproj` uses `Godot.NET.Sdk/4.7.1` and is the repository's Godot host.

An automated project-graph check inspects project references and resolved assemblies. This catches an accidental `GodotSharp` reference even when empty placeholder code still compiles.

Creating every later test, benchmark, example, and editor project now was rejected. Those projects belong to later independently runnable changes.

### Run Godot-facing tests through gdUnit4

Pin gdUnit4 `6.2.0`, which supports Godot 4.7.1, for editor, Node, Resource, scene, lifecycle, and consumer tests. Keep it outside the staged `spatial_circuits` product addon so consumers do not receive the test framework.

Keep `tests/SpatialCircuits.Core.Tests` on xUnit because pure-project tests must not acquire `GodotSharp`. Use `tests/SpatialWires.Godot.Tests` only for Godot-facing tests, with `gdUnit4.api/5.0.0` and `gdUnit4.test.adapter/3.0.0`. Do not add `gdUnit4.analyzers/1.0.0` because its Roslyn 4.14 dependency is incompatible with the compiler in the pinned .NET SDK 8.0.410. Mark tests that instantiate Godot objects or scenes with gdUnit4's Godot-runtime requirement.

Run the Godot-facing suite through `dotnet test` and the gdUnit4 test adapter. Supply the exact Godot 4.7.1 .NET executable as `GODOT_BIN`, apply bounded adapter and session timeouts, and preserve its real exit code. Thin `.mjs` files may launch these commands only because dod-guard 4.10.2 cannot discover C# test declarations; they contain no assertions and are removed when dod-guard gains native C# coverage bindings.

### Build a source addon unit through a staging step

Keep authored C# under the planned `src/` and `addons/spatial_circuits/` roots. A packaging script stages one temporary `addons/spatial_circuits` unit containing:

- `plugin.cfg` and the C# `[Tool]` editor plugin.
- The dock scene and editor-facing source.
- One `[GlobalClass]` Node and one `[GlobalClass]` Resource.
- The pure managed source required by the public addon types.
- A generated manifest of included paths and hashes.

The blank consumer compiles the staged C# source through its own `Godot.NET.Sdk/4.7.1` project. The staged addon has no `ProjectReference`, linked source path, or binary path back into the repository.

Shipping repository-relative project references was rejected because copying only `addons/` would fail. Committing generated DLLs was rejected because it would add platform-sensitive build output before release packaging exists. Maintaining hand-copied duplicate source was rejected because it would drift.

### Test the actual editor lifecycle

Give the dock and registered public types stable test-visible names. A bounded headless editor harness enables the plugin, observes one dock and its registrations, disables it, observes their removal, then enables it again and observes exactly one instance.

The plugin owns every editor registration. `_ExitTree` removes the dock and disconnects handlers even after partial initialization. Lifecycle assertions run against the editor scene and public registrations, not log text alone.

Testing only `_EnterTree` and `_ExitTree` as ordinary C# methods was rejected because it would not exercise Godot ownership or editor cleanup.

### Use gdUnit4 runtime tests and an outer watchdog

The blank consumer contains gdUnit4 C# runtime tests that instantiate the public Node, construct and round-trip the public Resource, and exercise the plugin lifecycle through real Godot APIs. gdUnit4 owns assertions, test discovery, runtime setup, and the final process result.

The gdUnit4 adapter and outer process runner apply fixed timeouts. Godot also receives `--quit-after 300` as a final watchdog. A test assertion failure, adapter timeout, or watchdog exit is reported as failure rather than accepted as a successful smoke run.

### Keep build, acceptance, and practice commands separate

Provide three Windows-first entry points:

1. A managed command that restores, builds, tests, and checks the dependency graph.
2. An acceptance command that stages the addon and runs import, managed build, plugin lifecycle, and smoke checks in the maintained blank fixture.
3. A practice command that creates a unique temporary project outside the repository, copies only the staged addon unit, and repeats import, build, and public-type smoke checks.

Each child process gets a fixed timeout and captured stdout, stderr, and exit code. Temporary cleanup targets only the unique directory created by that run.

Combining the maintained fixture and independent-copy practice into one check was rejected because a repository-relative dependency could pass the fixture while failing real installation.

## Risks / Trade-offs

- [C# source addons compile in the consumer assembly] -> Keep the initial public types small and make the staging manifest explicit. Revisit binary or NuGet packaging in a later release change.
- [Godot 4.7 editor dock APIs are experimental] -> Isolate dock calls in one adapter and keep the enable, disable, re-enable acceptance test.
- [Headless editor behavior can differ from an interactive editor] -> Use real editor APIs headlessly now and retain one documented interactive enable-disable practice check.
- [An exact SDK pin can become unavailable on a new machine] -> Fail with the exact required version and a direct installation diagnostic. Change the pin only through a reviewed change.
- [A staging script can omit source] -> Generate a path and hash manifest, then compile and exercise the staged copy outside the repository.
- [The current machine has no Godot executable on `PATH`] -> Accept an explicit executable path and validate it before running Godot checks. Do not treat unexecuted Godot checks as passing.

## Migration Plan

1. Add the pinned toolchain declarations and verification entry point.
2. Add the pure project graph, Godot host project, and managed tests.
3. Add the editor addon shell and public Node and Resource.
4. Add staging, blank-consumer, lifecycle, and smoke harnesses.
5. Run the independent-copy practice check and retain its command as Stage 1 evidence.

No existing implementation or saved format requires migration. Rollback removes the files introduced by this change and restores the current Godot shell. No user data needs conversion.
