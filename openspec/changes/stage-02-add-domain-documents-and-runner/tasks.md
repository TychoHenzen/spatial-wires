## 1. Establish domain primitives

- [x] 1.1 Add the four logic values and an exhaustive order-independent drive resolver with the no-drive case.
<!-- status: completed -->
<!-- covers: spatial-circuits/domain-documents-and-runner :: Four-state digital values resolve consistently :: No active drive resolves to high impedance -->
- [x] 1.2 Add resolver cases for unanimous active drives.
<!-- status: completed -->
<!-- covers: spatial-circuits/domain-documents-and-runner :: Four-state digital values resolve consistently :: One compatible driven value is preserved -->
- [x] 1.3 Add resolver cases for conflicting and unknown active drives across shuffled source order.
<!-- status: completed -->
<!-- covers: spatial-circuits/domain-documents-and-runner :: Four-state digital values resolve consistently :: Contention resolves to unknown -->
- [ ] 1.4 Add typed stable identifiers, ownership records, and schema-version values to the pure core.
<!-- covers: spatial-circuits/domain-documents-and-runner :: Portable definitions use stable identities and ownership :: Document identity survives process boundaries -->
- [ ] 1.5 Reject persisted CLR type names where stable behavior identifiers are required.
<!-- covers: spatial-circuits/domain-documents-and-runner :: Portable definitions use stable identities and ownership :: CLR type name is used as persisted behavior identity -->

## 2. Add canonical portable documents

- [ ] 2.1 Add immutable document builders and canonical UTF-8 JSON round-trip fixtures.
<!-- covers: spatial-circuits/domain-documents-and-runner :: Canonical documents round-trip exactly :: Canonical round trip is byte stable -->
- [ ] 2.2 Normalize permitted source ordering and prove equivalent inputs produce identical canonical hashes.
<!-- covers: spatial-circuits/domain-documents-and-runner :: Canonical documents round-trip exactly :: Equivalent input ordering is normalized -->
- [ ] 2.3 Add ordered structured diagnostics with stable codes and element paths for invalid fixtures.
<!-- covers: spatial-circuits/domain-documents-and-runner :: Validation diagnostics are stable and structured :: Invalid fixture reports a stable diagnostic -->
- [ ] 2.4 Reject unsupported schema versions before any runtime factory is invoked.
<!-- covers: spatial-circuits/domain-documents-and-runner :: Validation diagnostics are stable and structured :: Unsupported schema is rejected before instantiation -->

## 3. Separate definitions from runtime state

- [ ] 3.1 Add runtime factories that deep-copy mutable state and prove two instances from one definition diverge independently.
<!-- covers: spatial-circuits/domain-documents-and-runner :: Runtime instances do not share mutable state :: Two instances diverge independently -->

## 4. Build the public scenario runner

- [ ] 4.1 Add the pure `SpatialCircuits.Runner` console project, versioned fixture envelope, and deterministic trace output.
- [ ] 4.2 Add the public drive-resolution fixture and command that prints high impedance, a valid drive, and contention.
<!-- covers: spatial-circuits/domain-documents-and-runner :: Core scenarios run without Godot :: Drive-resolution practice fixture runs -->
- [ ] 4.3 Make invalid fixtures print structured diagnostics and return a nonzero process code.
<!-- covers: spatial-circuits/domain-documents-and-runner :: Core scenarios run without Godot :: Invalid fixture fails the process -->

## 5. Run Stage 2 gates

- [ ] 5.1 Run formatting, pure dependency checks, `dotnet build`, and the narrow Stage 2 test suite.
- [ ] 5.2 Run the public drive-resolution practice command and record its exact trace and process code.
- [ ] 5.3 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
