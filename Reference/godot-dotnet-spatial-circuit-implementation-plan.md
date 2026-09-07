# Godot .NET Spatial Circuit Framework

## Reviewed end-to-end implementation plan

Date: 2026-08-31

Status: Planning only. No target repository was provided.

Initial platform: Windows desktop development with Godot 4.7.1 .NET. Linux and macOS remain intended later targets, but they are not claimed until their build and smoke checks exist.

## 1. Intended result

Build a reusable framework with four levels:

1. **Cells** process persistent digital values on deterministic microticks.
2. **Panels** contain editable two-dimensional cell grids.
3. **Devices** expose named external ports. A device hosts either a panel or timed code behavior.
4. **Cables** connect device ports and carry long-distance transitions with explicit latency.

A completed panel circuit can become a reusable **chip** inside another panel. Packaging hides the grid without changing its timing.

The first gameplay slice is a tamper-detection system. A controller sends periodic diagnostic challenges and expects correctly formed, correctly timed responses. Disconnecting the downstream system without replacing that behavior raises an alarm.

## 2. First-release boundaries

### Included

- Persistent `Low`, `High`, `Unknown`, and `HighImpedance` values.
- Wire propagation measured in microticks.
- NAND, clocks, D flip-flops, stability filters, probes, and panel ports.
- Runtime-editable panel grids.
- Directed long-distance cables with integer latency and disconnect behavior.
- Cable bundles rendered as one cable but modeled as independent directed lanes.
- Hierarchical chips that execute their hidden source grids.
- Pure C# custom cells and timed code devices.
- Godot Node bridges for scripted devices and sparse custom cells.
- Deterministic stepping, causal snapshots, replay logs, and waveform traces.
- A runtime workbench and a Godot editor authoring plugin.

### Deferred

- Analog voltage, current, resistance, loading, power, or transistor simulation.
- A charge-integrating capacitor cell. The core uses a digital stability filter.
- Bidirectional cable lanes, shorts, and position-aware cable cuts.
- Optimized behavioral chip macros.
- Untrusted runtime code or mod-code sandboxing.
- Multiplayer synchronization.
- Web export. Godot 4 C# projects do not currently support it.
- One Godot `Node` per ordinary grid cell.

## 3. Ownership model

Use these types without overlap:

| Type | Responsibility |
| --- | --- |
| `PanelDefinition` | Immutable editable-grid design |
| `PanelInstance` | Runtime state for one panel definition |
| `ChipDefinition` | Immutable packaged panel definition and port map |
| `ChipInstance` | Hierarchical component owned by one panel instance |
| `DeviceInstance` | World endpoint hosting exactly one backend |
| `PanelDeviceBackend` | Wraps one panel instance |
| `CoreScriptedDeviceBackend` | Pure C# timed device behavior |
| `RecordedNodeDeviceBackend` | Godot Node bridge whose output commands are logged |
| `CableLane` | One directed delayed connection between device ports |
| `CableBundle` | Visual and authoring group for related lanes |

A chip is never directly a second kind of world device. A standalone chip uses a `PanelDeviceBackend` wrapper.

## 4. Layering and package shape

```text
Godot scenes and gameplay
        │
        ├── Runtime PanelWorkbench
        ├── Editor authoring plugin
        ├── RecordedNodeDevice
        └── RecordedNodeCellBehavior
                    │ queued commands and committed presentation events
                    ▼
Godot adapter
        ├── Resource DTO conversion
        ├── main-thread bridge barrier
        └── Variant-compatible signal DTOs
                    │
                    ▼
Pure .NET simulation
        ├── deterministic scheduler
        ├── panels and cell rules
        ├── chip hierarchy
        ├── device graph and cables
        ├── snapshots, replay, and traces
        └── portable definition documents
```

Proposed projects:

```text
src/
  SpatialCircuits.Core/
  SpatialCircuits.Cells/
  SpatialCircuits.Hierarchy/
addons/spatial_circuits/
  plugin.cfg
  SpatialCircuits.Godot/
  SpatialCircuits.Editor/
examples/
  SpatialCircuits.SecurityDemo/
tests/
  SpatialCircuits.Core.Tests/
  SpatialCircuits.Godot.Tests/
  fixtures/
benchmarks/
docs/
```

The pure projects never reference `GodotSharp`. Stage 1 must prove that this package shape works in a blank Godot consumer project before later work depends on it.

## 5. Authoritative data boundary

Use a portable, schema-versioned core document as the authoritative format. Start with canonical JSON and stable content hashing.

Godot `Resource` classes are editor-facing DTOs and references. They are not core definitions and never contain live runtime state.

Conversion follows one direction at runtime:

```text
Godot Resource DTO
        ↓ validate and deep-copy
Immutable core definition
        ↓ instantiate
Mutable runtime state
```

The adapter must account for Godot Resource caching and Variant-compatible exported properties. Two instances loaded from one cached Resource must still receive independent runtime state.

Every saved world pins definitions by:

- Stable identifier.
- Schema version.
- Content hash.
- Required custom behavior identifier and version.
- Behavior assembly fingerprint where code affects deterministic state.

## 6. Exact microtick semantics

### Canonical event identity

Every scheduled item has a canonical key:

```text
(dueTick, phase, targetStableId, targetIncarnation, targetPortOrLane,
 sourceStableId, sourcePort, eventKind, causalOrdinal)
```

External commands receive a monotonically increasing `acceptedOrdinal` when the simulation accepts them. That value seeds the first causal ordinal produced by the command. Internal proposals are sorted by their canonical target, source, output slot, and event kind before the scheduler assigns persisted causal ordinals. Canonical serialization and hashing sort by the same event keys.

### Microtick phases

1. Apply accepted topology commands in ordinal order.
2. Invalidate removed component incarnations and create required release drives.
3. Deliver due wire, gate, cable, timer, port, external-drive, and replayed-Node events.
4. Resolve persistent drives into input values.
5. Evaluate cells and pure timed-device behaviors from the committed snapshot.
6. Run the main-thread Node bridge barrier when the world contains a recorded Node backend.
7. Reduce proposals and schedule effects for `T + 1` or later.
8. Commit component state as one batch.
9. Record probes, diagnostics, presentation events, and the snapshot hash.
10. Advance to `T + 1`.

The scheduler throws a re-entry error if a callback attempts to step the world or mutate causal state directly.

### Simultaneous and delayed behavior

- Active components and cables have a minimum latency of one microtick.
- Wires and cables use transport delay. Every transition is reproduced later.
- NAND uses inertial delay. If its candidate equals its committed output, it cancels any pending output. If the candidate matches the pending candidate, the original deadline remains. If it differs, it replaces the pending candidate with a new deadline of `T + delay`. A pending value commits only when it matures while still matching the current candidate.
- A stability filter treats all four logic values as candidates. It accepts a candidate after `K` consecutive microticks.
- Persistent drives are indexed by source identity. Drive resolution is order-independent.
- A component may emit at most one proposal per output during one evaluation. Violations are diagnostics.
- Multiple external commands for the same target apply in accepted-ordinal order.
- Clocks, aging filters, timers, delayed gates, and cable queues remain active even without a new input event.

### D flip-flop rule

The first release uses explicit integer timing:

- Default setup time: one microtick.
- Default hold time: zero microticks.
- Default clock-to-output delay: one microtick.
- Data and a rising clock arriving in the same microtick violate setup and schedule `Unknown`.
- A later mode may add nonzero hold time. It must define the resulting failure value before implementation.

### Topology replacement

Each runtime cell, chip, device backend, and cable has an incarnation identifier. Scheduled events target both stable location and incarnation.

Removing or replacing a component invalidates events targeting the old incarnation. Removal creates a documented drive release. Reusing the coordinate cannot receive stale events.

## 7. Time and physical interpretation

- One microtick is the only causal time unit.
- One architectural clock cycle groups a configured number of microticks.
- A cycle does not force the circuit to settle.
- A longer route has more transport delay.
- Clock edges propagate through ordinary timing rules.
- Storage samples the clock edge that reaches its local input.

A stability filter is not called a capacitor. It rejects brief candidate values but does not accumulate separated pulses. A later charge cell would need stored magnitude, leakage, thresholds, and hysteresis.

## 8. Extension model

### Pure custom cells

This is the preferred path for reusable and numerous custom cells.

A rule receives immutable parameters, read-only neighbor values, read-only instance state, current microtick, and a restricted effect builder. It returns next state and future drives.

Rules register under stable namespaced identifiers such as `example:pattern_detector/v1`. Saved layouts never store CLR type names.

### Pure timed code devices

This is the preferred path for deterministic complex devices such as the security controller.

A `CoreScriptedDeviceBackend` receives committed port transitions and simulation timers. It returns future port drives and next serializable state. It cannot access Godot Nodes or wall-clock time.

### Godot Node device bridge

This fulfills the requirement that a Godot Node can replace a panel.

During a live run:

1. The scheduler reaches the main-thread bridge phase.
2. The adapter delivers Variant-compatible input DTOs synchronously.
3. The Node may use a capability object to request outputs at `T + 1` or later.
4. Each accepted Node output becomes an external command with a tick and ordinal.
5. The command and any captured Node state enter the replay log.

During replay, the Node is not executed for causal output. Recorded commands are replayed instead. Presentation events may be suppressed or deduplicated.

A Node that hosts a pure timed backend may remain deterministic because causal behavior stays in the core.

### Sparse Godot-backed cells

Use the same recorded bridge for a small number of gameplay-facing cells. A panel cell stores a binding identifier resolved by its owning panel Node.

Do not use this path for ordinary wires or large repeated cell sets. A world with active recorded Node behavior cannot use worker-thread simulation without a main-thread barrier.

## 9. Chips and hierarchy

A `ChipDefinition` contains a source `PanelDefinition`, named ports, version, content hash, symbol, parameters, and lower-level chip dependencies.

Chip dependency graphs are acyclic.

The first release has one normative execution mode: the hidden source grid continues running when the chip is collapsed. Expanded and collapsed views are therefore identical by construction.

Behavioral macro replacement is deferred. A future macro may claim semantic equivalence only when:

- It is generated by a transformation proven by construction, or
- Exhaustive state exploration proves equivalence for the bounded chip.

Golden traces and fuzzing provide regression evidence. They do not prove equivalence.

Instances pin a definition content hash. Upgrades are explicit. If chip authoring is disabled, existing assets retain a reader and expanded-grid fallback.

## 10. Cable semantics

The first release models directed lanes. A cable bundle is only a group of lanes for rendering and authoring.

Each lane has source port, destination port, integer latency, connection state, epoch, persistent source drive, and a transport queue.

Disconnect at microtick `T` behaves as follows:

1. Earlier queued transitions remain in order.
2. A `HighImpedance` source-drive change enters behind them.
3. The release reaches the destination after the same lane latency.
4. No later source changes enter while disconnected.

Reconnect creates a new epoch, discards undelivered events from the old epoch, and launches the source's current drive with normal latency.

This model has no physical cut position. Positional cuts, bidirectional conductors, and shorts require a later segmented-cable model.

## 11. Complete causal snapshot

Snapshots occur only after the final phase of a microtick.

They include:

- Current microtick, next command ordinal, and next causal event ordinal.
- Definition manifest and content hashes.
- Runtime incarnation identifiers.
- Cell, chip, panel, device, and security-protocol state.
- Persistent drive tables and resolved values.
- Future-event queue and cancellation state.
- Pending gate outputs and filter candidate ages.
- Clock phase, next edge, and DFF edge history.
- Custom-rule timers and deterministic random state.
- Cable queues, epochs, and in-flight transitions.
- Godot bridge input and output queues.
- Pending topology edits and replay-log cursor.

Saves use atomic replacement. Validate the new save before replacing the previous valid file.

## 12. Tamper-detection gameplay contract

This is a gameplay diagnostic, not cryptographic security.

The first acceptance fixture uses:

- An 8-lane challenge bundle and request strobe.
- An 8-lane response bundle and response-valid strobe.
- A controller seeded with a recorded deterministic 8-bit LFSR.
- One challenge every 512 microticks.
- Response function `rotate-left-one(challenge) XOR 0xA7`.
- Two 16-microtick directed cable latencies in the reference layout.
- An accepted response arrival window of 48 through 96 microticks after request emission.
- A latched alarm for missing, early, late, duplicate, malformed, `Unknown`, or `HighImpedance` responses.
- Explicit alarm reset behavior.

A passive tap observes without driving. An active splice is a topology edit and follows ordinary drive-resolution rules.

The protocol does not claim replay-attack detection. The 8-bit challenge eventually repeats, and any correct response inside the current request window is accepted. This limitation is intentional for a non-cryptographic gameplay diagnostic.

The successful player solution disconnects the original downstream panel and inserts a panel-built responder through public editing and cable APIs.

## 13. Construction stages

Every stage leaves a runnable system. Pre-UI stages use the core scenario runner and versioned fixtures.

### Stage 1. Prove the package and blank-consumer shape

**Depends on:** Nothing.

**Work:** Create the pure .NET projects, `res://addons/spatial_circuits`, `plugin.cfg`, a `[Tool]` C# editor plugin, one custom Node, one custom Resource, and a blank consumer project. Pin Godot 4.7.1 and the .NET SDK used by the project.

**Verification:**

```text
dotnet build <solution>
<godot-dotnet> --headless --path <consumer> --import
<godot-dotnet> --headless --path <consumer> --build-solutions --quit
<godot-dotnet> --headless --path <consumer> --scene res://tests/Smoke.tscn --quit-after 300
```

The smoke scene must return a failing process code on failure and quit itself on success. Enable and disable the editor plugin. Confirm that its dock and handlers are removed cleanly.

**Practice test:** Copy the addon into a second blank project. Instantiate its Node and Resource.

**Rollback:** Delete the isolated package spike. No saved format exists.

### Stage 2. Freeze vocabulary, ownership, format, and scenario runner

**Depends on:** Stage 1.

**Work:** Define logic values, identifiers, ownership types, immutable documents, canonical JSON, validation diagnostics, content hashes, and a command-line core scenario runner.

**Verification:** Exhaustive logic resolution passes. Canonical documents round-trip byte-for-byte. Invalid fixtures return stable codes. Different runtime instances from one document do not share state.

**Practice test:** Run a fixture with two drivers and print `HighImpedance`, valid drive, and `Unknown` contention traces.

**Rollback:** Replace documents before any later asset is published.

### Stage 3. Specify and implement the scheduler algebra

**Depends on:** Stage 2.

**Work:** Implement the canonical event key, phases, persistent drives, command ordinals, incarnation invalidation, active temporal roots, in-memory end-of-tick snapshots, trace hashing, and replay of core commands.

**Verification:** Direct tests cover same-target commands, distinct target ports, internal causal ordinals, same-tick data and clock, exact NAND restart and cancellation, removed and replaced targets, zero-delay rejection, temporal roots, and shuffled insertion. Snapshot restore reproduces the same hash sequence.

**Practice test:** Run, snapshot, restore, and replay a ten-microtick scheduled-drive fixture.

**Rollback:** Retain Stage 2 documents and replace the scheduler behind its public facade.

### Stage 4. Build panels and standard cells

**Depends on:** Stage 3.

**Work:** Use flat runtime arrays. Implement empty, wire, junction, crossing, constants, panel ports, probe, NAND, clock, DFF, and stability-filter rules. Add cell incarnation handling and golden timing fixtures.

**Verification:** A longer wire delays both edges. Unequal paths create a known glitch. The filter rejects the configured glitch. Same-tick DFF data and clock produce `Unknown`. Old cell events cannot reach a replacement.

**Practice test:** Run the XOR glitch fixture in the scenario runner, with and without filtering.

**Rollback:** Disable a registered rule while keeping the scheduler and document reader.

### Stage 5. Add pure custom cells

**Depends on:** Stage 4.

**Parallel with:** Stage 6.

**Work:** Add rule registration, restricted evaluation contexts, parameter validation, state serialization, behavior versions, and migration hooks. Build a reference pattern-detector extension outside the standard library.

**Verification:** The fixture loads by stable identifier. Missing registrations fail clearly. Rule state survives snapshot and replay. Past or duplicate output proposals fail validation.

**Practice test:** Load a JSON fixture containing the external pattern-detector rule and print its timed output.

**Rollback:** Remove the reference extension while retaining the registry contract and missing-rule diagnostic.

### Stage 6. Add exact hierarchical chips

**Depends on:** Stage 4.

**Parallel with:** Stage 5.

**Work:** Add chip definitions, port maps, version pinning, content hashes, acyclic dependencies, panel-owned instances, collapse and expand state, and hidden-grid execution.

**Verification:** Expanded and collapsed traces match by construction. Two instances have independent state. Recursive definitions fail. A changed source asset does not alter a hash-pinned instance.

**Practice test:** Package the XOR fixture and run two independent instances in the scenario runner.

**Rollback:** Disable new chip authoring. Keep reading and expanded-grid execution for existing definitions.

### Stage 7. Add devices, directed cables, and a thin diagnostic trace

**Depends on:** Stages 3, 4, and 6.

**Work:** Add device backends, directed lanes, bundles, transport queues, epochs, connect, disconnect, reconnect, and the exact release rule. Add a minimal pure controller and responder trace before any Godot UI.

**Verification:** Arrival latency is exact. Disconnect releases after queued transitions. Reconnect invalidates the old epoch. Width and direction mismatches fail. Device and cable causal state survives in-memory snapshot restore.

**Practice test:** Run a core fixture that sends one diagnostic challenge through two delayed device links and prints the response trace.

**Rollback:** Panel-local execution remains available. Keep cable document readers if fixtures exist.

### Stage 8. Add Godot Resource adapters

**Depends on:** Stages 2, 6, and 7.

**Work:** Create `[GlobalClass]` partial Resource DTOs using Variant-compatible exports. Convert them into deep-copied immutable core definitions. Add validation, content-hash display, and Resource-to-core round trips.

**Verification:** Two devices built from one cached Resource have isolated runtime state. Editing a Resource creates a new validated definition revision. No runtime array appears in a Resource.

**Practice test:** Load one panel Resource twice in Godot and run two independent instances.

**Rollback:** Keep portable JSON as the authority and remove only the adapter.

### Stage 9. Add the scripted-device Node bridge

**Depends on:** Stages 7 and 8.

**Work:** Implement the main-thread barrier, Variant-compatible input DTOs, capability-limited output requests, accepted ordinals, replay logging, state capture, Node deletion handling, and re-entry guards. Build a minimal Godot scene around the Stage 7 diagnostic.

**Verification:** Godot event, C# event, deferred call, and `_ExitTree` re-entry attempts fail safely. Outputs cannot affect the same tick. Live Node outputs are logged. Replay bypasses the Node and reproduces the trace. A deleted Node becomes disconnected.

**Practice test:** Replace the pure responder with a Godot Node responder, record one run, disable the Node, and replay the same response.

**Rollback:** Use `CoreScriptedDeviceBackend`. Pure devices and panels remain functional.

### Stage 10. Build the runtime panel workbench

**Depends on:** Stages 4, 6, 7, and 8.

**Parallel with:** Stage 11 after shared trace DTOs are fixed.

**Work:** Add batched panel rendering, palette, paint, erase, rotate, select, ports, chip placement, cable bundle authoring, pause, microtick step, cycle step, probes, and waveform display. Live edits become accepted topology commands.

**Verification:** Rendering creates no per-cell Nodes. Paused edits commit only when requested. Running edits appear in the command log. Editing a live cell invalidates old incarnation events.

**Practice test:** Draw the XOR, observe its glitch, package it, and connect its device to a delayed cable bundle.

**Rollback:** Keep simulation and Resource assets. Remove only the workbench scene.

### Stage 11. Add durable save, replay, migration, and diagnostics

**Depends on:** Stages 3, 7, 8, and 9.

**Parallel with:** Stage 10.

**Work:** Serialize the complete causal snapshot, definition manifest, assembly fingerprints, bridge queues, command log, and presentation-event policy. Add atomic saves, migrations, trace export, and snapshot diffing.

**Verification:** Save during an in-flight tagged pulse. Reload on the same tick and reproduce arrival. Rendering frame-rate changes do not change hashes. A modified definition hash blocks replay unless an explicit migration exists. A failed save preserves the prior file.

**Practice test:** Record the Stage 9 Node exchange, reload it, and replay it without executing the Node.

**Rollback:** Keep old fixtures and readers. Never overwrite source assets during failed migration.

### Stage 12. Build the tamper-detection gameplay slice

**Depends on:** Stages 9, 10, and 11.

**Work:** Implement the Section 12 protocol as a pure controller device and a panel-built responder. Add cut, reconnect, passive tap, active splice, bypass construction, alarm latch, and reset gameplay.

**Verification:** Intact exchanges pass. Missing, early, late, duplicate, malformed, `Unknown`, and `HighImpedance` responses alarm at the specified tick. A repeated challenge accepts a correct response in its current window. A player-built valid responder permits downstream disconnection. Save and replay preserve the alarm decision.

**Practice test:** Disconnect the original downstream panel and insert a constructed responder without raising the alarm.

**Rollback:** Keep this scene as a permanent acceptance fixture even if it is excluded from the distributable core package.

### Stage 13. Add sparse Node-backed custom cells

**Depends on:** Stages 5, 9, 10, 11, and 12.

**Work:** Reuse the recorded Node barrier for cell bindings. Add binding IDs, missing-binding diagnostics, state capture, timers, output capabilities, and editor placement. Document that this path is sparse and main-thread-bound.

**Verification:** A Node-backed cell receives only committed transitions and due timers. Its outputs replay without Node execution. Missing or deleted bindings release drives safely. Worker mode refuses to start while these bindings are active.

**Practice test:** Place one Node-backed protocol monitor in a panel. Make it emit a Godot alarm animation event without mutating the current microtick.

**Rollback:** Replace the bound cell with a missing-binding placeholder that preserves the saved document.

### Stage 14. Add the Godot editor authoring plugin

**Depends on:** Stages 8, 10, and 12.

**Work:** Wrap the runtime workbench in the proven addon shell. Put 4.7 dock registration behind a small adapter because the current dock surface is experimental. Add import, validation, content-hash, and open-definition tools.

**Verification:** Enable, use, disable, and re-enable the plugin in the pinned Godot version. Confirm no dock or signal-handler leak. Re-run the blank-consumer installation test.

**Practice test:** Author a panel and chip entirely through the editor plugin, then open the same assets in the runtime workbench.

**Rollback:** Keep runtime authoring and portable documents. Disable only editor integration.

### Stage 15. Measure, optimize, package, and document

**Depends on:** Stages 12 through 14.

**Work:** Record cell count, active causal frontier, microticks per second, allocation rate, and render frame time. Optimize only measured bottlenecks. Keep temporal roots active. Allow worker execution only when no recorded Node backend is active. Package the core and addon. Add Windows CI and document later Linux/macOS qualification.

Behavioral chip macros remain out of scope. Keep hidden-grid execution as the correctness oracle.

**Verification:** Optimized and reference runs produce identical fixture hashes. Long quiet periods followed by clocks, timers, filters, or cable arrivals remain correct. A blank consumer installs the final package. Headless import, build, acceptance scene, and Windows export smoke checks pass.

**Practice test:** Install the final addon into a blank project and build a two-device timed exchange without using the demo project.

**Rollback:** Disable each optimization separately. Keep reference execution available.

## 14. Dependency graph

```text
1 Package spike
    ↓
2 Domain, format, runner
    ↓
3 Scheduler algebra
    ↓
4 Panels and cells
    ├──────────────┐
    ↓              ↓
5 Custom cells    6 Chips
    └──────┬───────┘
           ↓
7 Devices, cables, core diagnostic
           ↓
8 Resource adapters
           ↓
9 Scripted-device Node bridge
    ┌──────┴─────────┐
    ↓                ↓
10 Runtime UI       11 Persistence and replay
    └──────┬─────────┘
           ↓
12 Tamper-detection slice
    ┌──────┴─────────┐
    ↓                ↓
13 Node cells       14 Editor plugin
    └──────┬─────────┘
           ↓
15 Optimize and package
```

Safe parallel work:

- Stages 5 and 6 may run in parallel after Stage 4.
- Stages 10 and 11 may run in parallel after their shared DTOs are fixed.
- Stages 13 and 14 may run in parallel after Stage 12.

Do not freeze scheduler or serialized contracts merely to enable parallel work. Change them through versioned fixtures until the gameplay slice passes.

## 15. Release acceptance scenarios

1. Longer routes delay both signal edges.
2. Unequal paths create a visible glitch.
3. A stability filter rejects only pulses shorter than its threshold.
4. A DFF reports setup failure deterministically.
5. Removed components cannot deliver stale events to replacements.
6. Expanded and collapsed chips have identical execution.
7. Cable disconnect releases the far endpoint after queued transitions.
8. A pure custom rule loads without core changes.
9. A pure code device replaces a panel on the same ports.
10. A Godot Node device records outputs and replays without Node execution.
11. A sparse Node cell cannot re-enter the scheduler.
12. Save/load preserves every pending causal event.
13. Live editing is recorded at microtick boundaries.
14. The player-built diagnostic responder avoids the alarm after disconnection.
15. A blank Godot .NET project consumes the packaged addon.

## 16. Anti-patterns to reject

| Anti-pattern | Required response |
| --- | --- |
| One Node per cell | Use flat arrays and batched drawing |
| `_Process(delta)` as circuit time | Use explicit microticks |
| Direct Node mutation of the core | Queue accepted commands through capabilities |
| Claiming arbitrary Node code is deterministic | Record its outputs or move behavior into pure core code |
| Resource objects used as immutable core state | Validate and deep-copy into core definitions |
| CLR type names in assets | Use stable namespaced behavior identifiers |
| Truth tables replacing timed chips | Continue hidden-grid execution |
| Golden traces called proof | Call them regression evidence |
| Filters placed everywhere | Require intentional placement and waveform evidence |
| Auto-settle at clock boundaries | Keep fixed microtick timing |
| Worker thread touching Nodes or Resources | Keep Godot objects on the main thread |
| Frontier based only on recent input | Include clocks, filters, timers, queues, and topology commands |

## 17. Plan mutation protocol

1. Record the changed decision and affected invariants.
2. Update scheduler semantics before changing behavior code.
3. Update the dependency graph before reordering stages.
4. Split any stage that stops being independently runnable.
5. Add a fixture for every semantic change.
6. Preserve old serialization fixtures and readers.
7. Keep the reference scheduler and hidden-grid chip execution.
8. Re-run the tamper-detection slice after any cross-layer change.

## 18. Verified Godot constraints

- Godot 4.7.1 is the pinned maintenance release for this repository.
- C# requires the .NET-enabled Godot editor.
- Godot 4 C# projects currently cannot export to the web platform.
- C# Godot signals use `[Signal]` delegates and generated C# events.
- Signal and exported Resource data must use Godot-supported Variant-compatible types.
- Resources are cached mutable data containers, so runtime state needs a copy boundary.
- C# editor plugins and custom docks are supported. Pin the first release because the 4.7 dock surface is experimental.
- The active scene tree is not generally thread-safe.
- `--headless`, `--path`, `--import`, `--build-solutions`, `--scene`, and `--quit-after` support bounded CI workflows when used together.

Official references:

- [Godot 4.7.1 maintenance release](https://godotengine.org/article/maintenance-release-godot-4-7-1/)
- [Godot 4.7 C#/.NET](https://docs.godotengine.org/en/4.7/tutorials/scripting/c_sharp/index.html)
- [Godot 4.7 Signal](https://docs.godotengine.org/en/4.7/classes/class_signal.html)
- [Godot 4.7 C# exported properties](https://docs.godotengine.org/en/4.7/tutorials/scripting/c_sharp/c_sharp_exports.html)
- [Godot 4.7 Resource](https://docs.godotengine.org/en/4.7/classes/class_resource.html)
- [Godot 4.7 ResourceLoader](https://docs.godotengine.org/en/4.7/classes/class_resourceloader.html)
- [Godot 4.7 editor plugins](https://docs.godotengine.org/en/4.7/tutorials/plugins/editor/making_plugins.html)
- [Godot 4.7 thread-safe APIs](https://docs.godotengine.org/en/4.7/tutorials/performance/thread_safe_apis.html)
- [Godot 4.7 command-line reference](https://docs.godotengine.org/en/4.7/tutorials/editor/command_line_tutorial.html)
