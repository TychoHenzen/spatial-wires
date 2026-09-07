## Context

See `proposal.md` and `specs/spatial-circuits/devices-and-cables/spec.md`. Panels and chips can execute locally. This stage introduces a pure world graph and must keep every long-distance transition inside the Stage 3 scheduler.

## Goals / Non-Goals

**Goals:**

- Give panels and pure code one interchangeable world endpoint contract.
- Make disconnect and reconnect behavior derivable from lane state and epochs.
- Keep cable bundles visual while lanes remain independent causal units.

**Non-Goals:**

- Add bidirectional conductors, shorts, cut positions, or analog effects.
- Execute Godot Nodes as device backends.
- Persist durable save files.

## Decisions

### Use one device shell with a closed backend choice

`DeviceInstance` owns stable external ports and exactly one backend selected from panel and pure scripted forms. Both adapt committed port transitions and timers to future drive proposals through the scheduler contract.

Treating chips as a second world device kind was rejected because a standalone chip already fits a panel backend. Allowing several active backends was rejected because drive ownership and saved state would be ambiguous.

### Model each direction and lane separately

Each `CableLane` owns source and destination port IDs, positive latency, current source drive, connection state, epoch, and a FIFO of transport events. A `CableBundle` stores only related lane IDs and presentation metadata.

One queue per bundle was rejected because lane widths, directions, and transitions would interfere. Bidirectional lane state was deferred because disconnect and contention semantics differ.

### Encode disconnect as an ordered transport transition

At disconnect, retain existing queue entries and append a release transition after them with normal latency. Block later source sampling while disconnected. This uses the same delivery path as ordinary transitions.

Clearing the queue immediately was rejected because earlier physical transitions would disappear. Releasing the destination immediately was rejected because it violates cable latency.

### Use epochs for reconnect invalidation

Reconnect increments the lane epoch, drops undelivered old-epoch entries, and enqueues the current source drive under the new epoch. Delivery validates lane incarnation and epoch.

Trying to edit every queued event in place was rejected because stale references and snapshot ordering become harder to audit.

### Build the first diagnostic as pure fixture code

The controller and responder use pure timed backends with fixed fixture ports. Their trace proves device substitution and two delayed links without a Godot dependency.

## Risks / Trade-offs

- [Disconnect queue ordering is misinterpreted] -> Keep explicit transition and epoch traces for disconnect and reconnect cases.
- [Backend abstraction leaks panel internals] -> Define the contract only in committed ports, timers, serializable state, and future drives.
- [Cable queues grow under fast sources] -> Measure later and preserve every transport transition until Stage 15 justifies bounded storage changes.

## Migration Plan

1. Add device and port documents plus compatibility validation.
2. Add panel and pure scripted backends.
3. Add lane queues, disconnect, reconnect, bundles, and snapshots.
4. Run the pure two-link diagnostic practice.

Rollback leaves panel-local execution available. If published fixtures exist, retain cable document readers and reject execution with a clear unsupported-capability diagnostic.
