## Why

The framework needs one end-to-end gameplay slice that exercises timing, editing, cables, Node integration, and replay together. Stage 12 provides that acceptance fixture through a deterministic non-cryptographic diagnostic.

## What Changes

- Add the specified 8-bit challenge and response bundles with request and response-valid strobes.
- Add a recorded deterministic 8-bit LFSR challenge source and the `rotate-left-one(challenge) XOR 0xA7` response rule.
- Emit one challenge every 512 microticks through two 16-microtick directed cable links.
- Accept one correct response from 48 through 96 microticks after request emission.
- Latch the alarm for missing, early, late, duplicate, malformed, `Unknown`, or `HighImpedance` responses, with explicit reset behavior.
- Add cut, reconnect, passive tap, active splice, and public bypass-construction interactions.
- Preserve alarm decisions through save and replay.
- Add the practice flow that disconnects the downstream panel and inserts a panel-built responder without raising the alarm.

## Capabilities

### New Capabilities

- `spatial-circuits/tamper-detection-gameplay`: Deterministic challenge-response timing, alarm rules, cable interactions, and the player-built bypass acceptance path.

### Modified Capabilities

None.

## Impact

- Adds the security demo scene, pure controller behavior, panel-built responder, and end-to-end acceptance fixtures.
- Depends on the recorded Node bridge, runtime workbench, and durable replay.
- Serves as the permanent cross-layer regression fixture for later changes.
- Makes no cryptographic or replay-attack-detection claim.
