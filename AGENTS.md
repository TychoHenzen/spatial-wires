# SpatialWires project guidance

## Current situation

SpatialWires is a Godot 4.7 .NET project for deterministic spatial digital circuits.

The repository has a Stage 1 package baseline with a solution, pure .NET source projects, a Godot adapter, and automated boundary checks. The deterministic circuit domain is not implemented yet.

Files under `Reference/` are planning input. OpenSpec changes become the implementation contract after review and validation.

## Architecture boundaries

- Keep the simulation core in pure C# projects with no `GodotSharp` reference.
- Put Godot Nodes, Resources, editor tools, and Variant conversion behind adapter projects.
- Use explicit integer microticks for causal time. Never use `_Process(delta)` as circuit time.
- Keep portable, versioned documents authoritative. Treat Godot Resources as editor-facing DTOs.
- Store runtime state in independent instances. Never store live runtime state in shared Resources.
- Use flat simulation data and batched rendering. Do not create one Godot Node per ordinary cell.

## Working method

- Use OpenSpec for planned features and behavior changes.
- Keep proposal, design, specs, and tasks consistent before implementation starts.
- Bind each OpenSpec scenario to a unit, integration, or acceptance test when implemented.
- Treat syntax validation and coverage indexing as structure checks, not proof of behavior.
- Make each implementation stage independently runnable.
- Give every stage one explicit practice test using the real public surface.
- Preserve unrelated files and user changes.

## Planned layout

The reviewed plan proposes these main roots:

```text
src/          Pure .NET simulation projects
addons/       Godot adapter and editor plugin
examples/     Runnable Godot examples
tests/        Unit, integration, and acceptance tests
benchmarks/   Measured performance checks
docs/         Maintained technical documentation
```

Do not create the full layout before its first OpenSpec change requires it.

## Validation

- Run the narrowest relevant tests after each change.
- Run `dotnet build` and `dotnet test` after a solution and test projects exist.
- Run bounded Godot headless import, build, and smoke scenes for adapter changes.
- Run strict OpenSpec validation for changed planning artifacts.
- Report exact commands and outcomes. State clearly when a check cannot run.
- Do not claim completion from task boxes, commit text, or fixture-only tests.

## GitHub delivery workflow

- Ideas enter the linked Project through `/add-backlog-idea` as Backlog issues.
- `/refine-backlog-item` makes a Todo PBI.
- `/next-ticket` implements and pushes one issue branch.
- `/submit-draft-pr` creates its draft pull request.
- Review is read-only until explicit acceptance.
- `/complete-pr` alone owns ready, merge, issue confirmation, and branch deletion.
- `portable-core` is the required CI gate for pure .NET simulation and runner tests.
- The Godot adapter runs in a separate weekly or manually dispatched workflow.

## C# conventions

- Enable nullable reference types in new projects.
- Prefer immutable definitions and explicit mutable runtime instances.
- Use stable domain identifiers in saved data. Do not persist CLR type names.
- Keep deterministic ordering explicit for commands, events, serialization, and hashing.
- Reject invalid zero-delay causal operations at the boundary.
