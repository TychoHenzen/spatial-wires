## Purpose

Defines flat spatial panel execution and the standard timed digital cells needed to construct, observe, and verify useful deterministic circuits.

## ADDED Requirements

### Requirement: Panel instances execute independent flat grids
A panel definition SHALL describe a bounded two-dimensional grid, stable cell locations, and named panel ports. Each panel instance SHALL own flat runtime state independent from its definition and from other instances.

#### Scenario: Two panels from one definition are independent
- **WHEN** two panel instances are created from one definition and one receives a topology edit
- **THEN** the other instance and the definition remain unchanged

#### Scenario: Invalid panel coordinate is rejected
- **WHEN** a definition or edit addresses a coordinate outside the panel bounds
- **THEN** validation rejects it before runtime state changes

### Requirement: Standard cell set is available
Panels SHALL support empty, wire, junction, crossing, constant, panel-port, probe, NAND, clock, DFF, and stability-filter cells with validated parameters and deterministic port orientation.

#### Scenario: Crossing keeps channels separate
- **WHEN** independent transitions pass through the two channels of a crossing
- **THEN** each transition reaches only the matching opposite channel

#### Scenario: Probe records committed values
- **WHEN** a probe observes a value that commits at a microtick boundary
- **THEN** its trace records that value and microtick without driving the circuit

### Requirement: Spatial routes use transport delay
Wire routes SHALL reproduce every transition after explicit positive microtick delay. A route with more delayed segments SHALL deliver both rising and falling edges later than a shorter route.

#### Scenario: Longer route delays both edges
- **WHEN** the same pulse enters short and long wire routes at the same microtick
- **THEN** both pulse edges leave the long route later by its additional route delay

#### Scenario: Unequal XOR paths expose a glitch
- **WHEN** the documented XOR fixture changes inputs across unequal routes
- **THEN** its probe records the expected transient value before the settled value

### Requirement: NAND uses exact inertial delay
A NAND cell SHALL hold at most one pending output candidate. Matching the committed output SHALL cancel a pending output. Repeating the pending candidate SHALL preserve its original deadline. A different candidate SHALL replace it with a deadline of `T + delay`.

#### Scenario: Repeated candidate keeps original deadline
- **WHEN** a NAND candidate remains equal to its pending candidate during later evaluations
- **THEN** the pending output matures at its original deadline

#### Scenario: Candidate returns to committed output
- **WHEN** a NAND candidate returns to the committed output before the pending output matures
- **THEN** the pending output is cancelled

#### Scenario: Candidate changes before maturity
- **WHEN** a NAND candidate changes to a different value before maturity
- **THEN** the prior pending output is replaced with a new deadline measured from the change microtick

### Requirement: DFF timing failures are deterministic
The first-release DFF SHALL default to one microtick of setup time, zero microticks of hold time, and one microtick of clock-to-output delay. Data and a rising clock delivered in the same microtick SHALL schedule `Unknown`.

#### Scenario: Stable data is captured
- **WHEN** data is stable for at least one microtick before a rising local clock edge
- **THEN** the DFF schedules that data at its output one microtick later

#### Scenario: Same-tick data and clock violate setup
- **WHEN** data and a rising clock reach the DFF in the same microtick
- **THEN** the DFF schedules `Unknown` at its output one microtick later

### Requirement: Stability filter requires consecutive candidate age
A stability filter configured with threshold `K` SHALL accept one of all four logic values only after that candidate remains present for `K` consecutive microticks. Separated pulses SHALL not accumulate age.

#### Scenario: Short glitch is rejected
- **WHEN** a candidate lasts fewer than `K` consecutive microticks
- **THEN** the filter keeps its prior committed output

#### Scenario: Stable candidate is accepted
- **WHEN** one candidate, including `Unknown` or `HighImpedance`, lasts for `K` consecutive microticks
- **THEN** the filter commits that candidate at the documented threshold boundary

### Requirement: Cell replacement is incarnation safe
Each runtime cell SHALL have an incarnation identifier. A topology edit that removes or replaces a cell SHALL release its drives and SHALL prevent events for the old incarnation from reaching the new cell.

#### Scenario: Replaced cell ignores old delayed output
- **WHEN** a cell is replaced before one of its delayed outputs matures
- **THEN** the delayed output cannot affect the replacement or its neighbors

### Requirement: Panel timing is runnable through the public runner
Versioned panel fixtures SHALL run through the command-line scenario surface with deterministic value and timing traces.

#### Scenario: XOR filtering practice is repeatable
- **WHEN** the public XOR fixture runs once without filtering and once with its configured filter
- **THEN** the first trace contains the known glitch and the second trace rejects it
