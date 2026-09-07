## 1. Implement the pure diagnostic protocol

- [ ] 1.1 Add the serializable controller state machine, recorded 8-bit LFSR seed, and 512-microtick request timer.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Diagnostic protocol uses the fixed reference contract :: Controller emits a challenge -->
- [ ] 1.2 Build the panel responder that computes rotate-left-one then XOR with `0xA7` through ordinary cells.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Diagnostic protocol uses the fixed reference contract :: Panel responder receives a request -->
- [ ] 1.3 Add the two 16-microtick reference links and accept a correct response at tick 48.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Reference links have explicit latency and response window :: Response arrives at the first accepted tick -->
- [ ] 1.4 Accept a correct response at tick 96.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Reference links have explicit latency and response window :: Response arrives at the last accepted tick -->
- [ ] 1.5 Latch the alarm at exact decision ticks for early and late arrivals.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Reference links have explicit latency and response window :: Response arrives outside the window -->

## 2. Add table-driven alarm cases

- [ ] 2.1 Add the missing-response timeout fixture and exact alarm tick.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Invalid responses latch the alarm :: Response is missing -->
- [ ] 2.2 Add duplicate response-valid handling, including one otherwise correct response.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Invalid responses latch the alarm :: Response is duplicated -->
- [ ] 2.3 Add wrong-value and invalid-lane-shape alarm fixtures.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Invalid responses latch the alarm :: Response value is malformed -->
- [ ] 2.4 Add `Unknown` and `HighImpedance` response-lane alarm fixtures.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Invalid responses latch the alarm :: Response contains unresolved values -->
- [ ] 2.5 Add accepted ordinal-based alarm reset and verify later requests resume.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Invalid responses latch the alarm :: Alarm is reset explicitly -->
- [ ] 2.6 Add a repeated-LFSR-challenge fixture that accepts a correct current-window response.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Repeated challenges do not add replay-attack detection :: LFSR challenge repeats -->

## 3. Add gameplay topology interactions

- [ ] 3.1 Add a passive tap action that observes without adding a drive.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Cable interactions use public topology rules :: Passive tap observes traffic -->
- [ ] 3.2 Add an active splice through ordinary drive insertion and resolution.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Cable interactions use public topology rules :: Active splice drives the lane -->
- [ ] 3.3 Add downstream disconnect without replacement and verify delayed release plus missing-response alarm.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Cable interactions use public topology rules :: Downstream system is disconnected without replacement -->
- [ ] 3.4 Connect the panel-built responder through public editing and cable APIs and verify accepted exchanges.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Player-built responder can satisfy the diagnostic :: Player inserts a valid bypass responder -->

## 4. Add durable and public acceptance

- [ ] 4.1 Save before an alarm decision, reload, replay, and compare decision tick and causal hash.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Save and replay preserve alarm decisions :: Run is saved before alarm decision -->
- [ ] 4.2 Build the Godot security demo scene using only public workbench and topology controls.
- [ ] 4.3 Add the documented downstream-disconnect and bypass practice flow.
<!-- covers: spatial-circuits/tamper-detection-gameplay :: Tamper-detection practice is the cross-layer fixture :: Public bypass practice completes -->

## 5. Run Stage 12 gates

- [ ] 5.1 Run formatting, dependency checks, `dotnet build`, every table-driven protocol case, and durable replay tests.
- [ ] 5.2 Run the bounded Godot bypass practice and retain its command log, trace, hashes, and scene result.
- [ ] 5.3 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
