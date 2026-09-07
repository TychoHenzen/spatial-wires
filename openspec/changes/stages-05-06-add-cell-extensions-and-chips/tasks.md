## 1. Freeze shared extension contracts

- [ ] 1.1 Add stable behavior identity, version, state-codec, migration, nested-port, and snapshot contracts used by both stages.
- [ ] 1.2 Add pure dependency checks that prevent custom rules and hierarchy code from accessing Godot or scheduler mutation APIs.

## 2. Implement pure custom cells

- [ ] 2.1 Add the namespaced rule registry and resolve an external supported registration.
<!-- covers: spatial-circuits/custom-cell-extensions :: Custom rules use stable registrations :: Registered external rule loads -->
- [ ] 2.2 Report stable missing-registration and unsupported-version diagnostics.
<!-- covers: spatial-circuits/custom-cell-extensions :: Custom rules use stable registrations :: Registration is missing -->
- [ ] 2.3 Add immutable rule context and reject current-tick or past effects.
<!-- covers: spatial-circuits/custom-cell-extensions :: Rule inputs and effects are restricted :: Rule proposes an effect in the past -->
- [ ] 2.4 Limit one proposal per output per evaluation and reject duplicates atomically.
<!-- covers: spatial-circuits/custom-cell-extensions :: Rule inputs and effects are restricted :: Rule duplicates one output proposal -->
- [ ] 2.5 Validate custom parameters before rule instantiation.
<!-- covers: spatial-circuits/custom-cell-extensions :: Custom parameters and state are portable :: Invalid custom parameter is rejected -->
- [ ] 2.6 Serialize rule state and deterministic random state through snapshot and replay.
<!-- covers: spatial-circuits/custom-cell-extensions :: Custom parameters and state are portable :: Rule state survives snapshot and replay -->
- [ ] 2.7 Add copy-based supported behavior migrations with post-migration validation.
<!-- covers: spatial-circuits/custom-cell-extensions :: Behavior versions migrate explicitly :: Supported behavior migration succeeds -->
- [ ] 2.8 Preserve source data and report unavailable migration paths.
<!-- covers: spatial-circuits/custom-cell-extensions :: Behavior versions migrate explicitly :: Behavior migration is unavailable -->
- [ ] 2.9 Build the external pattern-detector package and public timed-output fixture.
<!-- covers: spatial-circuits/custom-cell-extensions :: External pattern detector proves the extension surface :: Pattern-detector practice fixture runs -->

## 3. Implement exact hierarchical chips

- [ ] 3.1 Add portable chip definitions, dependency manifests, content pinning, and unchanged-instance tests after source edits.
<!-- covers: spatial-circuits/hierarchical-chips :: Chip definitions are portable and hash pinned :: Source asset changes after instantiation -->
- [ ] 3.2 Add explicit compatible upgrade and validated migration handling.
<!-- covers: spatial-circuits/hierarchical-chips :: Chip definitions are portable and hash pinned :: Explicit upgrade selects a new definition -->
- [ ] 3.3 Topologically validate dependencies and report direct and indirect cycles.
<!-- covers: spatial-circuits/hierarchical-chips :: Chip dependency graphs are acyclic :: Indirect recursion is present -->
- [ ] 3.4 Create panel-owned nested instances and prove two stateful chips remain independent.
<!-- covers: spatial-circuits/hierarchical-chips :: Chip instances own independent runtime state :: Two chip instances diverge independently -->
- [ ] 3.5 Route ports through hidden source grids and prove expanded and collapsed hashes match.
<!-- covers: spatial-circuits/hierarchical-chips :: Collapse changes presentation only :: Expanded and collapsed traces match -->
- [ ] 3.6 Keep existing chips readable and executable through expanded-grid fallback when authoring is disabled.
<!-- covers: spatial-circuits/hierarchical-chips :: Existing chips retain exact fallback execution :: Authoring is disabled -->
- [ ] 3.7 Add the public packaged-XOR fixture with two independently driven instances.
<!-- covers: spatial-circuits/hierarchical-chips :: Packaged XOR chips are runnable through the public runner :: Two-instance XOR practice fixture runs -->

## 4. Run Stage 5 and 6 practice gates

- [ ] 4.1 Run formatting, pure dependency checks, `dotnet build`, custom-rule tests, and hierarchy tests.
- [ ] 4.2 Run the external pattern-detector practice and record its timed trace.
- [ ] 4.3 Run the two-instance XOR chip practice and record independent traces and hashes.
- [ ] 4.4 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
