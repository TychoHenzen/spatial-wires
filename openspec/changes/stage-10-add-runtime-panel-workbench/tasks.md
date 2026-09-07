## 1. Add batched presentation and traces

- [ ] 1.1 Add immutable panel presentation snapshots and batched cell, wire, and overlay rendering.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Panel rendering is batched :: Large panel is displayed -->
- [ ] 1.2 Add probe and waveform projection from committed core trace DTOs.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Probes and waveforms show committed traces :: Unequal-path glitch is observed -->

## 2. Add validated runtime authoring

- [ ] 2.1 Add palette, paint, erase, rotate, select, and panel-port tools as validated definition actions.
- [ ] 2.2 Add chip packaging, placement, device placement, and cable-bundle authoring through shared validation.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Runtime authoring covers panel and world topology :: User builds a connected chip device -->
- [ ] 2.3 Show diagnostics and preserve the prior definition for invalid authoring actions.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Runtime authoring covers panel and world topology :: Authoring action is invalid -->

## 3. Integrate deterministic edit and time controls

- [ ] 3.1 Submit running edits as accepted topology commands with ordinals and command-log entries.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Live edits are accepted topology commands :: User edits while simulation runs -->
- [ ] 3.2 Prove running replacement invalidates delayed work for the old cell incarnation.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Live edits are accepted topology commands :: Live cell replacement has delayed work -->
- [ ] 3.3 Stage paused edits without advancing or mutating committed causal state.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Paused edits require explicit commitment :: User edits while paused -->
- [ ] 3.4 Commit staged edits in the topology phase of an explicit one-microtick step.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Paused edits require explicit commitment :: User steps after a paused edit -->
- [ ] 3.5 Add pause, microtick-step, and configured-cycle controls that never auto-settle.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Time controls use explicit microticks :: Cycle step contains unsettled work -->

## 4. Add the public workbench practice

- [ ] 4.1 Build the runtime workbench scene and automated control surface for headless acceptance.
- [ ] 4.2 Add the documented XOR draw, glitch observation, chip package, and delayed-device connection flow.
<!-- covers: spatial-circuits/runtime-panel-workbench :: Complete workbench practice flow is runnable :: Workbench practice completes -->

## 5. Run Stage 10 gates

- [ ] 5.1 Run formatting, dependency checks, `dotnet build`, pure tool tests, and bounded Godot workbench tests.
- [ ] 5.2 Run the public workbench practice and retain its exact waveform and scene result.
- [ ] 5.3 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
