## Purpose

Defines pure-core world devices and directed delayed cable lanes so panels, chips, and timed code can exchange deterministic transitions across a world graph.

## ADDED Requirements

### Requirement: Devices host exactly one backend
Each world device SHALL expose stable named ports and SHALL host exactly one panel backend or pure timed code backend. Port direction, width, and value compatibility SHALL validate before connection.

#### Scenario: Pure code device replaces a panel backend
- **WHEN** a pure timed backend exposes the same validated external port contract as a panel backend
- **THEN** either backend can occupy the device without changing connected cable definitions

#### Scenario: Port contracts are incompatible
- **WHEN** a connection has a direction or width mismatch
- **THEN** validation rejects the connection before causal state changes

### Requirement: Pure timed devices use simulation time
A pure timed device SHALL receive committed port transitions and simulation timers and SHALL return serializable next state and drives for future microticks. It SHALL not depend on wall-clock time or Godot objects.

#### Scenario: Timed device runs after quiet period
- **WHEN** a pure device has a due simulation timer and receives no new port transition
- **THEN** it evaluates at the timer's exact microtick

### Requirement: Cable lanes reproduce transitions with transport delay
Each cable lane SHALL represent one directed source-to-destination connection with positive integer latency, connection state, epoch, persistent source drive, and ordered transport queue. Every accepted source transition SHALL arrive after exactly the lane latency while connected.

#### Scenario: Cable arrival latency is exact
- **WHEN** a source transition enters a connected lane of latency `L` at microtick `T`
- **THEN** the destination receives that transition at microtick `T + L`

#### Scenario: Cable bundle keeps lanes independent
- **WHEN** a visual cable bundle contains multiple directed lanes with different transitions
- **THEN** each lane retains its own source, destination, queue, direction, and value history

### Requirement: Disconnect preserves queued order and delayed release
Disconnecting a lane at microtick `T` SHALL retain earlier queued transitions, enqueue a `HighImpedance` source-drive change behind them, deliver that release after the same lane latency, and reject later source changes while disconnected.

#### Scenario: Disconnect occurs with queued transitions
- **WHEN** a lane disconnects while transitions are in flight
- **THEN** the queued transitions arrive in order before the delayed `HighImpedance` release

#### Scenario: Source changes after disconnect
- **WHEN** the source changes while its lane is disconnected
- **THEN** no new transition enters that lane's queue

### Requirement: Reconnect starts a new cable epoch
Reconnecting a lane SHALL create a new epoch, discard undelivered events from older epochs, and launch the source's current drive with normal lane latency.

#### Scenario: Old epoch has an undelivered event
- **WHEN** a lane reconnects before an old-epoch event arrives
- **THEN** the old event cannot affect the destination and the current source drive arrives in the new epoch

### Requirement: Device and cable state survives in-memory snapshots
In-memory snapshots SHALL preserve device state, port drives, cable connection state, epochs, transport queues, and in-flight transitions.

#### Scenario: Snapshot restores an in-flight cable transition
- **WHEN** a world snapshots before a cable transition arrives and then restores
- **THEN** the transition arrives once at the same microtick as in the uninterrupted run

### Requirement: Core diagnostic exchange is publicly runnable
The scenario runner SHALL provide a pure controller and responder fixture that exchanges one challenge through two delayed device links and prints the timed response trace.

#### Scenario: Delayed diagnostic practice runs
- **WHEN** the public Stage 7 diagnostic fixture runs
- **THEN** the printed challenge and response ticks reflect both configured link latencies
