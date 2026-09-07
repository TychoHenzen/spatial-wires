## 1. Add world devices and ports

- [ ] 1.1 Add device and port documents plus a single-backend device shell for panel and pure timed backends.
- [ ] 1.2 Prove a pure backend can replace a panel backend without changing compatible cable definitions.
<!-- covers: spatial-circuits/devices-and-cables :: Devices host exactly one backend :: Pure code device replaces a panel backend -->
- [ ] 1.3 Reject direction, width, and value-contract mismatches before topology publication.
<!-- covers: spatial-circuits/devices-and-cables :: Devices host exactly one backend :: Port contracts are incompatible -->
- [ ] 1.4 Add serializable pure timed-device state and timers that fire after quiet periods.
<!-- covers: spatial-circuits/devices-and-cables :: Pure timed devices use simulation time :: Timed device runs after quiet period -->

## 2. Implement directed cable transport

- [ ] 2.1 Add per-lane source, destination, latency, drive, epoch, state, and transport queue data.
- [ ] 2.2 Deliver every connected source transition at exactly `T + L`.
<!-- covers: spatial-circuits/devices-and-cables :: Cable lanes reproduce transitions with transport delay :: Cable arrival latency is exact -->
- [ ] 2.3 Add presentation-only cable bundles and prove their lanes remain causally independent.
<!-- covers: spatial-circuits/devices-and-cables :: Cable lanes reproduce transitions with transport delay :: Cable bundle keeps lanes independent -->

## 3. Implement connection lifecycle

- [ ] 3.1 Preserve queued transitions and append a normal-latency release during disconnect.
<!-- covers: spatial-circuits/devices-and-cables :: Disconnect preserves queued order and delayed release :: Disconnect occurs with queued transitions -->
- [ ] 3.2 Block later source changes from entering a disconnected lane.
<!-- covers: spatial-circuits/devices-and-cables :: Disconnect preserves queued order and delayed release :: Source changes after disconnect -->
- [ ] 3.3 Increment epochs on reconnect, invalidate old in-flight work, and launch the current source drive.
<!-- covers: spatial-circuits/devices-and-cables :: Reconnect starts a new cable epoch :: Old epoch has an undelivered event -->

## 4. Add snapshots and the core diagnostic

- [ ] 4.1 Extend in-memory snapshots with backend state, ports, lane states, epochs, queues, and in-flight transitions.
<!-- covers: spatial-circuits/devices-and-cables :: Device and cable state survives in-memory snapshots :: Snapshot restores an in-flight cable transition -->
- [ ] 4.2 Build the pure controller and responder fixture across two delayed links.
- [ ] 4.3 Add the public diagnostic practice command and exact challenge-response trace.
<!-- covers: spatial-circuits/devices-and-cables :: Core diagnostic exchange is publicly runnable :: Delayed diagnostic practice runs -->

## 5. Run Stage 7 gates

- [ ] 5.1 Run formatting, pure dependency checks, `dotnet build`, and device, cable, epoch, and snapshot tests.
- [ ] 5.2 Run the public two-link diagnostic and record exact trace ticks and process code.
- [ ] 5.3 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
