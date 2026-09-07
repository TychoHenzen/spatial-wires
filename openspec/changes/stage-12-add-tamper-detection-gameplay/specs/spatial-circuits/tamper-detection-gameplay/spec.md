## Purpose

Defines the first end-to-end gameplay acceptance slice, a deterministic timed diagnostic that players can inspect, interrupt, and reproduce with panel-built behavior.

## ADDED Requirements

### Requirement: Diagnostic protocol uses the fixed reference contract
The reference layout SHALL use an 8-lane challenge bundle and request strobe, an 8-lane response bundle and response-valid strobe, a recorded deterministic 8-bit LFSR, and `rotate-left-one(challenge) XOR 0xA7` as the expected response.

#### Scenario: Controller emits a challenge
- **WHEN** 512 microticks have elapsed since the prior request according to the recorded initial LFSR state
- **THEN** the controller emits the next 8-bit challenge and request strobe and records the protocol state

#### Scenario: Panel responder receives a request
- **WHEN** the panel-built responder receives a complete challenge and request strobe
- **THEN** it computes and emits `rotate-left-one(challenge) XOR 0xA7` through ordinary timed panel behavior

### Requirement: Reference links have explicit latency and response window
The reference layout SHALL use two directed cable latencies of 16 microticks. The controller SHALL accept one correct response whose response-valid arrival is from 48 through 96 microticks inclusive after request emission.

#### Scenario: Response arrives at the first accepted tick
- **WHEN** one correct response arrives exactly 48 microticks after request emission
- **THEN** the controller accepts it without latching the alarm

#### Scenario: Response arrives at the last accepted tick
- **WHEN** one correct response arrives exactly 96 microticks after request emission
- **THEN** the controller accepts it without latching the alarm

#### Scenario: Response arrives outside the window
- **WHEN** a response-valid strobe arrives before tick 48 or after tick 96 relative to its request
- **THEN** the controller latches the alarm at the specified decision tick

### Requirement: Invalid responses latch the alarm
For each active request, the controller SHALL latch the alarm for a missing, early, late, duplicate, malformed, `Unknown`, or `HighImpedance` response. A latched alarm SHALL remain active until the explicit reset action succeeds.

#### Scenario: Response is missing
- **WHEN** no valid response arrives by the end of the accepted window
- **THEN** the alarm latches at the documented timeout tick

#### Scenario: Response is duplicated
- **WHEN** more than one response-valid event is associated with one request
- **THEN** the alarm latches even if one response value was correct

#### Scenario: Response value is malformed
- **WHEN** a response has the wrong computed value or invalid lane shape
- **THEN** the alarm latches at response evaluation

#### Scenario: Response contains unresolved values
- **WHEN** any response lane resolves to `Unknown` or `HighImpedance` at response-valid arrival
- **THEN** the alarm latches at response evaluation

#### Scenario: Alarm is reset explicitly
- **WHEN** the defined reset action is accepted after the alarm latches
- **THEN** the alarm clears at its assigned microtick boundary and later requests are evaluated normally

### Requirement: Repeated challenges do not add replay-attack detection
The protocol SHALL evaluate a correct response against the currently active request window. It SHALL not reject a value solely because the 8-bit challenge appeared in an earlier completed request.

#### Scenario: LFSR challenge repeats
- **WHEN** a repeated challenge receives the correct response inside its current window
- **THEN** the response is accepted if no other alarm condition occurs

### Requirement: Cable interactions use public topology rules
The gameplay slice SHALL expose disconnect, reconnect, passive tap, and active splice actions through the same public topology and drive-resolution contracts as other circuits.

#### Scenario: Passive tap observes traffic
- **WHEN** a passive tap is attached to a diagnostic lane
- **THEN** it records transitions without adding a drive or changing the intact exchange

#### Scenario: Active splice drives the lane
- **WHEN** an active splice adds a conflicting drive
- **THEN** ordinary drive resolution determines the observed value and the protocol evaluates that result

#### Scenario: Downstream system is disconnected without replacement
- **WHEN** the original downstream panel is disconnected and no responder replaces it
- **THEN** the delayed cable release and missing response cause the alarm through ordinary timing rules

### Requirement: Player-built responder can satisfy the diagnostic
A responder built from public panel cells and connected through public cable editing SHALL be accepted when it produces the exact value and timing for each active request.

#### Scenario: Player inserts a valid bypass responder
- **WHEN** the original downstream panel is disconnected and the panel-built responder is inserted through public editing and cable APIs
- **THEN** exchanges continue without latching the alarm

### Requirement: Save and replay preserve alarm decisions
Protocol state, active request windows, LFSR state, cable state, responses, and alarm state SHALL be part of durable causal snapshots and replay.

#### Scenario: Run is saved before alarm decision
- **WHEN** a run is saved with a response pending and then reloaded and replayed
- **THEN** the alarm decision occurs at the same microtick with the same causal hash

### Requirement: Tamper-detection practice is the cross-layer fixture
The documented Stage 12 practice SHALL disconnect the downstream panel, insert a constructed responder, and complete at least one diagnostic exchange without an alarm.

#### Scenario: Public bypass practice completes
- **WHEN** the documented practice uses only public workbench and cable controls
- **THEN** the final trace shows downstream disconnection, bypass insertion, and an accepted response without alarm
