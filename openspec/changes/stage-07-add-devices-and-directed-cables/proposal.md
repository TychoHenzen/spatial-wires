## Why

Panels and chips remain isolated until they can participate in a timed world graph. Stage 7 adds the first pure-core device and cable exchange before Godot integration begins.

## What Changes

- Add world `DeviceInstance` endpoints that host exactly one panel or pure scripted backend.
- Add named typed device ports and compatibility validation.
- Add directed `CableLane` transport queues with integer latency, persistent source drives, connection state, and epochs.
- Add visual and authoring `CableBundle` groups without merging lane causality.
- Implement exact disconnect release ordering and reconnect epoch invalidation.
- Preserve device and cable causal state through in-memory snapshots.
- Add a minimal pure controller and responder diagnostic over two delayed links as the practice fixture.

## Capabilities

### New Capabilities

- `spatial-circuits/devices-and-cables`: Device backends, ports, directed delayed lanes, bundles, epochs, and disconnect or reconnect behavior.

### Modified Capabilities

None.

## Impact

- Extends the pure core and hierarchy integration surfaces and scenario fixtures.
- Depends on scheduler, panel, standard-cell, and hierarchical-chip behavior.
- Establishes the device and cable DTOs later consumed by Godot adapters and gameplay.
- Bidirectional lanes, positional cuts, shorts, and analog cable behavior remain out of scope.
