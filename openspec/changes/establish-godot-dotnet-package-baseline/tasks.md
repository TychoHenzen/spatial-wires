## 1. Pin and verify the toolchain

- [x] 1.1 Add `global.json` for SDK `8.0.410`, add the Godot 4.7.2 .NET version manifest, and implement the successful version check.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is available -->
- [x] 1.2 Add automated missing-version, wrong-version, and non-.NET-editor cases that fail before compilation with the exact offending tool in the diagnostic.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is unavailable -->
- [x] 1.3 Retarget the completed toolchain declaration and version checks from Godot 4.7.2 .NET to the installed Godot 4.7.1 .NET baseline.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is available -->

## 2. Establish the managed project graph

- [x] 2.1 Create `SpatialWires.sln`, the three planned pure `net8.0` projects, and a managed test project with inward-only project references.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Enforced dependency boundary :: Pure projects build independently -->
- [x] 2.2 Add a project-graph and resolved-assembly test that rejects `GodotSharp` or Godot-facing references from every pure project, including a failing fixture.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Enforced dependency boundary :: Godot dependency does not cross the boundary -->
- [x] 2.3 Add the `Godot.NET.Sdk/4.7.2` host project and include it in the solution without reversing a pure-project dependency.
<!-- status: completed -->
- [x] 2.4 Retarget the completed host project and managed Godot references from `Godot.NET.Sdk/4.7.2` to `Godot.NET.Sdk/4.7.1` while preserving the pure-project dependency boundary.
<!-- status: completed -->
- [x] 2.5 Pin gdUnit4 6.2.0 and its C# test adapter, migrate every Godot-facing xUnit test to gdUnit4, and keep pure-project tests on xUnit without `GodotSharp`.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Public Node and Resource types :: Consumer uses public addon types -->

## 3. Build the portable addon unit

- [x] 3.1 Add `addons/spatial_circuits/plugin.cfg`, the `[Tool]` editor plugin, its dock, and stable registration names.
<!-- status: completed -->
- [x] 3.2 Add the `[GlobalClass]` Node and Resource types with no live simulation state or saved-format contract.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Public Node and Resource types :: Consumer uses public addon types -->
- [x] 3.3 Add the staging script that copies required source into one addon unit, writes its path and hash manifest, and rejects repository-relative references.
<!-- status: completed -->
- [x] 3.4 Add the maintained blank-consumer fixture and verify through gdUnit4 that import discovers and enables the staged addon without load or compilation errors.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Installable addon unit :: Blank consumer discovers the addon -->

## 4. Verify the editor plugin lifecycle

- [x] 4.1 Make partial initialization and `_ExitTree` cleanup remove every dock, registration, and connected handler owned by the plugin.
<!-- status: completed -->
- [x] 4.2 Add a bounded gdUnit4 headless editor case that enables then disables the plugin and observes no remaining plugin-owned editor state.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Clean editor plugin lifecycle :: Plugin is disabled cleanly -->
- [x] 4.3 Extend the gdUnit4 lifecycle case to re-enable the plugin and observe exactly one dock and one copy of each registration.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Clean editor plugin lifecycle :: Plugin is enabled again -->

## 5. Add bounded build and smoke checks

- [x] 5.1 Add one process runner that applies fixed timeouts, captures stdout and stderr, preserves exit codes, and terminates only its own child process tree.
<!-- status: completed -->
- [x] 5.2 Add the successful managed build, Godot import, managed-solution build, and bounded gdUnit4 lifecycle and public-type pipeline.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Bounded headless verification :: Headless baseline succeeds -->
- [x] 5.3 Add a negative gdUnit4 fixture whose failed public-type assertion exits nonzero before the outer `--quit-after 300` watchdog.
<!-- status: completed -->
<!-- covers: spatial-circuits/package-baseline :: Bounded headless verification :: Smoke assertion fails -->

## 6. Prove independent installation

- [ ] 6.1 Add a practice command that creates a unique temporary Godot .NET project outside the repository, copies only the staged addon, and runs import, build, Node, and Resource checks through gdUnit4.
<!-- covers: spatial-circuits/package-baseline :: Portable second-consumer practice check :: Addon is moved to an independent consumer -->
- [ ] 6.2 Make temporary cleanup validate and remove only the unique practice directory created by the current run.
- [ ] 6.3 Document the exact practice command, required Godot executable input, expected outputs, and rollback boundary.

## 7. Run the Stage 1 gates

- [ ] 7.1 Run formatting, the managed test suite, and the dependency-graph check from a clean build output state.
- [ ] 7.2 Run the maintained gdUnit4 blank-consumer acceptance command with the declared Godot 4.7.1 .NET executable.
- [ ] 7.3 Run the independent-copy practice command and record its actual command, process codes, and generated manifest hash.
- [ ] 7.4 Run strict OpenSpec validation and the dod-guard coverage check, requiring all eleven scenarios to be bound with zero regressions.
