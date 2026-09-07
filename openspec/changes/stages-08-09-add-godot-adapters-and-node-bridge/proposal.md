## Why

The pure world model needs a safe Godot-facing data boundary and a recorded path for Node behavior. Stages 8 and 9 establish both parts as one adapter boundary without moving causal authority into Godot objects.

## What Changes

- Add `[GlobalClass]` partial Resource DTOs with Variant-compatible exported data.
- Validate and deep-copy Resource DTOs into immutable core definitions with visible content hashes.
- Keep runtime arrays and mutable circuit state out of Resources, including cached shared Resources.
- Add a main-thread barrier that delivers committed input DTOs to scripted device Nodes.
- Add capability-limited Node output requests for `T + 1` or later, accepted ordinals, captured state, and replay logging.
- Reject scheduler re-entry from Godot events, C# events, deferred calls, and `_ExitTree`.
- Treat deleted Nodes as disconnected and replay recorded causal outputs without executing the Node.
- Add independent Resource-instance and recorded Node responder practice scenes.

## Capabilities

### New Capabilities

- `spatial-circuits/godot-resource-adapters`: Variant-compatible Resource DTOs and deep-copy conversion into validated immutable core definitions.
- `spatial-circuits/recorded-node-bridge`: Main-thread Node device execution through limited commands, state capture, re-entry protection, and replayed output.

### Modified Capabilities

None.

## Impact

- Extends `addons/spatial_circuits/` and Godot-facing tests while preserving pure-project dependency checks.
- Depends on portable documents, chips, devices, and cables from Stages 2, 6, and 7.
- Creates the bridge and trace DTOs required by the workbench and durable replay.
- Arbitrary Node code is recorded, not claimed as intrinsically deterministic.
