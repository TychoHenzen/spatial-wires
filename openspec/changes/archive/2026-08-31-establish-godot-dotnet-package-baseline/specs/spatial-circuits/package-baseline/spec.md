## Purpose

Defines a reproducible and portable Godot .NET package boundary that later SpatialCircuits behavior can extend without changing its installation model.

## ADDED Requirements

### Requirement: Reproducible development baseline
The package baseline SHALL declare Godot 4.7.1 .NET and one compatible .NET SDK selection so a clean checkout resolves the intended toolchain without using an arbitrary newer SDK.

#### Scenario: Declared toolchain is available
- **WHEN** a developer prepares the repository with the declared Godot and .NET versions
- **THEN** the baseline build resolves those versions and proceeds without an undeclared toolchain substitution

#### Scenario: Declared toolchain is unavailable
- **WHEN** a required declared tool version is missing or incompatible
- **THEN** the baseline check fails before compilation with a diagnostic that identifies the missing or incompatible tool

### Requirement: Enforced dependency boundary
The package baseline SHALL keep pure .NET projects buildable without a `GodotSharp` dependency and SHALL isolate Godot APIs to Godot-facing projects or consumer code.

#### Scenario: Pure projects build independently
- **WHEN** the pure .NET projects are built without loading a Godot project
- **THEN** they compile successfully without resolving `GodotSharp`

#### Scenario: Godot dependency does not cross the boundary
- **WHEN** automated dependency checks inspect the project graph and compiled references
- **THEN** no pure .NET project references a Godot-facing project or `GodotSharp`

### Requirement: Installable addon unit
The package baseline SHALL produce an addon unit that a blank Godot 4.7.1 .NET project can install at `res://addons/spatial_circuits` without repository-relative dependencies outside that unit.

#### Scenario: Blank consumer discovers the addon
- **WHEN** the addon unit is copied into a blank compatible consumer and the project is imported
- **THEN** Godot discovers valid addon metadata and can enable the addon without a load or compilation error

### Requirement: Public Node and Resource types
The installed addon SHALL expose one instantiable custom Node type and one constructible custom Resource type through supported Godot .NET registration surfaces.

#### Scenario: Consumer uses public addon types
- **WHEN** a blank consumer instantiates the public Node and creates or loads the public Resource
- **THEN** both types are usable without referencing files outside the installed addon unit

### Requirement: Clean editor plugin lifecycle
The editor plugin SHALL support enable, disable, and re-enable operations without retaining its dock, registered handlers, or other editor-owned state after disablement.

#### Scenario: Plugin is disabled cleanly
- **WHEN** the enabled plugin is disabled in the editor
- **THEN** its dock and registered handlers are removed and no plugin-owned editor state remains active

#### Scenario: Plugin is enabled again
- **WHEN** the disabled plugin is enabled again in the same project
- **THEN** it restores one working instance of each editor integration without duplicates

### Requirement: Bounded headless verification
The package baseline SHALL provide bounded headless checks for Godot import, managed-solution build, and smoke-scene execution.

#### Scenario: Headless baseline succeeds
- **WHEN** the addon and blank consumer are valid
- **THEN** the import, managed build, and smoke-scene checks finish within their configured bounds and return successful process codes

#### Scenario: Smoke assertion fails
- **WHEN** a gdUnit4 runtime test cannot instantiate or use a required public addon type
- **THEN** the bounded gdUnit4 run terminates and returns a failing process code instead of hanging or reporting success

### Requirement: Portable second-consumer practice check
The package baseline SHALL include a repeatable practice check that installs the produced addon unit into a second blank Godot .NET project and exercises only the addon's public surface.

#### Scenario: Addon is moved to an independent consumer
- **WHEN** the produced addon unit is copied to a second blank project outside the source project tree
- **THEN** that project imports, builds, and instantiates the public Node and Resource without source-tree path dependencies
