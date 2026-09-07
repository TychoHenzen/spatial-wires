## Purpose

Defines measured performance qualification, equivalence-preserving optimization, safe execution modes, and independent Windows release packaging for the completed framework.

## ADDED Requirements

### Requirement: Performance claims use versioned measurements
Release benchmarks SHALL report fixture identity and hash, cell count, active causal frontier, microticks per second, allocation rate, render frame time, execution mode, build configuration, and machine context.

#### Scenario: Release benchmark is recorded
- **WHEN** a release performance run completes
- **THEN** its output contains every required metric and enough fixture identity to reproduce the run

### Requirement: Optimizations preserve canonical behavior
Every optimized execution path SHALL remain selectable against a reference path. Both paths SHALL produce identical canonical fixture traces and per-tick hashes for the qualified fixture set.

#### Scenario: Optimized and reference runs agree
- **WHEN** the release fixture set runs through reference and optimized modes
- **THEN** every canonical trace and per-tick causal hash is identical

#### Scenario: Optimization diverges
- **WHEN** an optimized run differs from its reference hash sequence
- **THEN** qualification fails and reports the first differing tick and snapshot path

### Requirement: Quiet periods preserve temporal roots
Frontier optimization SHALL retain clocks, timers, stability filters, delayed gates, cable queues, and pending topology commands until their due work completes or is cancelled.

#### Scenario: Clock follows a long quiet interval
- **WHEN** no external input occurs before an active clock edge or timer deadline
- **THEN** optimized execution processes it at the same microtick and with the same hash as reference execution

### Requirement: Worker mode has strict eligibility
Worker execution SHALL be available only when the active world contains no recorded Node device backend and no recorded Node-backed cell. Eligibility SHALL be checked before stepping and whenever topology changes.

#### Scenario: Recorded Node becomes active
- **WHEN** topology would activate a recorded Node backend during worker execution
- **THEN** the system transitions through the documented safe barrier or rejects the edit before any worker touches a Godot object

### Requirement: Release packages are self-contained and identified
The release SHALL package the pure core and one self-contained Godot addon with version metadata and a manifest of included paths and hashes. The addon SHALL have no repository-relative runtime dependency.

#### Scenario: Package manifest is verified
- **WHEN** a release package is produced
- **THEN** every included file matches the manifest and no excluded source-tree path is required

### Requirement: Windows release gates exercise public surfaces
Windows qualification SHALL run managed build and tests, dependency boundaries, headless Godot import, managed-solution build, the tamper-detection acceptance scene, export smoke, and independent blank-consumer installation with bounded processes and real exit codes.

#### Scenario: Windows release gates pass
- **WHEN** all required checks run against the release candidate
- **THEN** each bounded process exits successfully and the acceptance hashes match their qualified fixtures

#### Scenario: One release process fails
- **WHEN** any required process returns nonzero, times out, or produces a mismatched acceptance hash
- **THEN** release qualification fails and preserves that process's diagnostic output

### Requirement: Documentation states supported and unqualified surfaces
Maintained release documentation SHALL cover installation, architecture boundaries, portable formats, extension contracts, save and replay, diagnostics, performance evidence, Windows qualification, and the unverified Linux and macOS status. It SHALL state that web export and behavioral chip macros are outside the first release.

#### Scenario: User checks platform support
- **WHEN** a user reads the release platform documentation
- **THEN** Windows qualification is evidenced and Linux, macOS, and web are not presented as qualified

### Requirement: Final package practice works outside the demo
The public Stage 15 practice SHALL install the final addon into a blank compatible project and build a two-device timed exchange without using the repository demo project.

#### Scenario: Blank-project release practice completes
- **WHEN** the documented final practice installs only the release package into a blank project
- **THEN** the project imports, builds, and runs the expected two-device timed exchange
