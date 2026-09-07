## Context

See `proposal.md` and `specs/spatial-circuits/tamper-detection-gameplay/spec.md`. The required core, Node bridge, workbench, and durable replay surfaces now exist. This stage is both gameplay and the permanent cross-layer acceptance fixture.

## Goals / Non-Goals

**Goals:**

- Keep protocol decisions in a pure serializable device state machine.
- Build the successful replacement responder from ordinary public panel behavior.
- Assert exact decision ticks across live run, save, load, and replay.

**Non-Goals:**

- Provide cryptographic authentication or replay-attack detection.
- Add special cable rules for the demo.
- Accept a scripted shortcut as the required player-built responder.

## Decisions

### Implement the controller as a pure timed backend

Controller state contains the LFSR state, next request tick, active challenge, request tick, response count, window state, alarm latch, and reset state. It samples only committed ports and simulation timers.

Implementing the controller in a Godot Node was rejected because the acceptance oracle should remain deterministic without recorded external behavior.

### Encode protocol timing as fixture constants and explicit timers

The reference fixture owns bundle widths, `512`, both `16`-microtick links, and inclusive `48..96` acceptance bounds. Request, first-valid, timeout, duplicate, and reset decisions produce trace events with exact ticks.

Deriving deadlines from frame time or scene animation was rejected because presentation changes would alter gameplay.

### Build the reference responder as a panel asset

The responder uses standard cells and public panel ports to compute rotate-left then XOR with `0xA7`. Its routed timing is part of the accepted response window. A pure scripted responder may support focused tests but cannot replace the acceptance asset.

Hard-coding the successful answer in the controller or scene was rejected because it would not prove player construction surfaces.

### Express taps and splices as normal topology edits

Disconnect and reconnect use cable lane commands. A passive tap adds an observer only. An active splice adds an ordinary drive source, so four-state resolution and protocol input validation decide the outcome.

Adding gameplay-only cable mutation was rejected because it would bypass Stage 7 semantics.

### Use a table-driven alarm acceptance suite

Versioned fixtures vary one condition at a time across intact, missing, early, late, duplicate, malformed, unknown, high-impedance, repeated-challenge, reset, and bypass cases. Each records expected decision tick and hash.

Checking only whether an alarm eventually appears was rejected because timing is part of the gameplay contract.

## Risks / Trade-offs

- [Panel responder timing drifts after lower-level changes] -> Keep its routed definition hash and exact trace as permanent acceptance evidence.
- [Alarm cases overlap] -> Isolate each fault fixture and define precedence when several invalid observations share one tick.
- [The demo is mistaken for security] -> Label it as a gameplay diagnostic in the UI, docs, and spec-facing fixture names.

## Migration Plan

1. Add the pure protocol state machine and table-driven core fixtures.
2. Build and verify the panel responder.
3. Add the Godot scene and topology interactions through public APIs.
4. Add durable save and replay acceptance for alarm decisions.
5. Run the downstream-disconnect and bypass practice.

Rollback can exclude the gameplay scene from distribution while retaining it as an acceptance fixture. Core framework behavior remains unchanged.
