## Why

After the gameplay slice passes, the remaining Godot extension paths are sparse Node-backed cells and editor-native authoring. Stages 13 and 14 add both without changing pure simulation authority or runtime asset portability.

## What Changes

- Reuse the recorded main-thread barrier for sparse Node-backed cell bindings.
- Add stable binding identifiers, committed-transition and timer delivery, limited output requests, state capture, and replay.
- Release drives safely for missing or deleted bindings and refuse worker mode while recorded bindings are active.
- Preserve missing bindings as document placeholders for later repair.
- Wrap the runtime workbench in the existing editor addon shell through a small Godot 4.7 dock adapter.
- Add editor import, validation, content-hash, open-definition, panel, and chip authoring tools.
- Verify clean enable, disable, and re-enable behavior and repeat blank-consumer installation.
- Add separate Node monitor and editor-to-runtime asset practice flows.

## Capabilities

### New Capabilities

- `spatial-circuits/node-backed-custom-cells`: Sparse recorded Node cell bindings with safe deletion, replay, timers, and worker-mode restrictions.
- `spatial-circuits/editor-authoring-tools`: Godot editor import, validation, content-hash, panel, and chip authoring over the runtime workbench.

### Modified Capabilities

None.

## Impact

- Extends the Godot adapter, editor plugin, workbench, and Godot-facing tests.
- Depends on custom cells, the Node bridge, workbench, durable state, and tamper-detection fixture.
- Keeps ordinary cells in flat pure data and uses Node bindings only for sparse gameplay-facing behavior.
- Keeps runtime authoring and portable documents available when editor integration is disabled.
