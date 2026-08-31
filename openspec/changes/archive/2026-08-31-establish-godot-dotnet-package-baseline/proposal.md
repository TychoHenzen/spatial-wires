## Why

SpatialWires has a Godot project shell and reviewed architecture, but it does not yet have a buildable .NET package or a proven consumer boundary. This first change establishes that boundary before simulation behavior or saved formats depend on it.

## What Changes

- Add a pinned Godot 4.7.1 .NET development baseline and a repository-pinned compatible .NET SDK.
- Add the initial solution and project structure for pure .NET code, Godot integration, and automated tests.
- Pin gdUnit4 for Godot-facing C# and scene tests while keeping pure simulation tests Godot-free.
- Add an installable `res://addons/spatial_circuits` shell with `plugin.cfg`, a C# editor plugin, one custom Node, and one custom Resource.
- Add a blank Godot .NET consumer fixture that imports, builds, enables, exercises, disables, and removes the addon cleanly.
- Add bounded command-line checks for .NET build and test, Godot headless import and build, and a self-terminating smoke scene.
- Add a portable practice check that copies the addon into a second blank project and instantiates its public Node and Resource.
- Keep circuit simulation, runtime data formats, panel behavior, and gameplay outside this baseline change.

## Capabilities

### New Capabilities

- `spatial-circuits/package-baseline`: Defines the reproducible Godot .NET package shape, blank-consumer contract, plugin lifecycle, and portable smoke checks.

### Modified Capabilities

None.

## Impact

- Adds the first solution, project, test, addon, and consumer-fixture files.
- Establishes the dependency direction between pure .NET projects and Godot-facing adapters.
- Adds Godot 4.7.1 .NET and a compatible .NET SDK as development and verification dependencies.
- Creates the public addon identity and paths that later SpatialWires changes will extend.
- Does not define circuit behavior or a stable saved-data schema.
