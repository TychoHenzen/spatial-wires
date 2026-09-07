## 1. Add Godot Resource DTOs and conversion

- [ ] 1.1 Add partial `[GlobalClass]` DTO families with only supported Variant-compatible authoring fields.
- [ ] 1.2 Reject Resource fields that attempt to store runtime causal state.
<!-- covers: spatial-circuits/godot-resource-adapters :: Resource DTOs contain only supported authoring data :: Resource contains runtime state -->
- [ ] 1.3 Add pinned-Godot save and load round trips for every exported field family.
<!-- covers: spatial-circuits/godot-resource-adapters :: Resource DTOs contain only supported authoring data :: Resource fields round-trip through Godot -->
- [ ] 1.4 Deep-copy conversion into immutable core definitions and prove cached-Resource instances remain independent.
<!-- covers: spatial-circuits/godot-resource-adapters :: Resource conversion validates and deep copies :: Cached Resource creates two devices -->
- [ ] 1.5 Prove editing a Resource cannot mutate a prior definition or running instance.
<!-- covers: spatial-circuits/godot-resource-adapters :: Resource conversion validates and deep copies :: Resource is edited after conversion -->
- [ ] 1.6 Publish a new schema version and hash only after a valid semantic edit.
<!-- covers: spatial-circuits/godot-resource-adapters :: Resource revisions have visible canonical identity :: Semantic Resource edit changes the revision -->
- [ ] 1.7 Preserve the prior valid definition after an invalid Resource edit.
<!-- covers: spatial-circuits/godot-resource-adapters :: Resource revisions have visible canonical identity :: Invalid Resource edit preserves the prior definition -->
- [ ] 1.8 Prove equivalent Resource and canonical JSON inputs produce equal definitions and hashes.
<!-- covers: spatial-circuits/godot-resource-adapters :: Portable documents remain authoritative :: Resource and JSON describe the same definition -->
- [ ] 1.9 Add the public two-instance Resource isolation scene.
<!-- covers: spatial-circuits/godot-resource-adapters :: Resource isolation is demonstrated in Godot :: Resource isolation practice runs -->

## 2. Implement the main-thread Node bridge

- [ ] 2.1 Add canonical synchronous delivery of fully committed input DTOs during the bridge phase.
<!-- covers: spatial-circuits/recorded-node-bridge :: Node inputs cross one main-thread barrier :: Node receives committed inputs -->
- [ ] 2.2 Deliver due simulation timers during the barrier even without new inputs.
<!-- covers: spatial-circuits/recorded-node-bridge :: Node inputs cross one main-thread barrier :: Node timer becomes due without input -->
- [ ] 2.3 Add callback-scoped output capabilities and reject current-tick or past requests.
<!-- covers: spatial-circuits/recorded-node-bridge :: Node outputs use limited future commands :: Node requests same-tick output -->
- [ ] 2.4 Assign accepted ordinals and log several valid Node requests in deterministic order.
<!-- covers: spatial-circuits/recorded-node-bridge :: Node outputs use limited future commands :: Multiple Node outputs are accepted -->
- [ ] 2.5 Guard scheduler access from Godot signals, C# events, deferred calls, `_ExitTree`, and bridge callbacks.
<!-- covers: spatial-circuits/recorded-node-bridge :: Node callbacks cannot re-enter causal execution :: Re-entry is attempted from a Node callback -->

## 3. Add Node recording, replay, and lifetime safety

- [ ] 3.1 Record accepted Node commands and captured state, then bypass the Node during causal replay.
<!-- covers: spatial-circuits/recorded-node-bridge :: Replay bypasses Node causal execution :: Node is disabled during replay -->
- [ ] 3.2 Invalidate capabilities and stale callbacks on Node deletion and queue deterministic backend disconnect.
<!-- covers: spatial-circuits/recorded-node-bridge :: Node deletion disconnects its backend safely :: Node is deleted with pending work -->
- [ ] 3.3 Build the Godot Node responder scene around the Stage 7 diagnostic.
- [ ] 3.4 Add the public live-record-disable-replay practice and compare causal hashes.
<!-- covers: spatial-circuits/recorded-node-bridge :: Recorded Node practice is repeatable :: Live and replayed responder traces match -->

## 4. Run Stage 8 and 9 gates

- [ ] 4.1 Run formatting, pure dependency checks, `dotnet build`, core tests, and pinned-Godot adapter tests.
- [ ] 4.2 Run the Resource isolation practice and record scene process results.
- [ ] 4.3 Run the Node record and replay practice and record matching traces and hashes.
- [ ] 4.4 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
