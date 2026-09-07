## 1. Add sparse Node cell bindings

- [ ] 1.1 Add stable binding IDs, behavior versions, owning-panel resolution, and bound cell incarnations.
<!-- covers: spatial-circuits/node-backed-custom-cells :: Node-backed cells use stable sparse bindings :: Binding resolves in the owning panel -->
- [ ] 1.2 Preserve unavailable binding records as inert placeholders with stable diagnostics.
<!-- covers: spatial-circuits/node-backed-custom-cells :: Node-backed cells use stable sparse bindings :: Binding is missing -->
- [ ] 1.3 Deliver only committed neighbor values and due timers through the shared main-thread barrier.
<!-- covers: spatial-circuits/node-backed-custom-cells :: Node cells receive only committed transitions and due timers :: Input changes during one microtick -->
- [ ] 1.4 Add cell-scoped capabilities and reject same-tick causal mutation.
<!-- covers: spatial-circuits/node-backed-custom-cells :: Node cells receive only committed transitions and due timers :: Node cell requests same-tick mutation -->
- [ ] 1.5 Record cell outputs, timers, state, and presentation policy and replay without the Node.
<!-- covers: spatial-circuits/node-backed-custom-cells :: Node cell state and outputs replay without Node execution :: Node cell is unavailable during replay -->
- [ ] 1.6 Invalidate deleted bindings, cancel stale callbacks, and release drives through ordinary timing.
<!-- covers: spatial-circuits/node-backed-custom-cells :: Deleted bindings release safely :: Bound Node is deleted with a pending output -->
- [ ] 1.7 Reject worker mode and topology changes that would activate a recorded cell binding.
<!-- covers: spatial-circuits/node-backed-custom-cells :: Worker execution refuses active recorded bindings :: Worker mode starts with an active Node cell -->
- [ ] 1.8 Build the protocol monitor practice and recorded alarm animation event.
<!-- covers: spatial-circuits/node-backed-custom-cells :: Node monitor practice preserves causal boundaries :: Protocol monitor emits animation event -->

## 2. Share the workbench model with the editor

- [ ] 2.1 Extract validated tools, revisions, selection, and trace projection into a runtime and editor host model.
- [ ] 2.2 Author a panel in the editor and open it in the runtime workbench with the same canonical hash.
<!-- covers: spatial-circuits/editor-authoring-tools :: Editor authoring wraps the runtime workbench contract :: Panel is authored in the editor -->
- [ ] 2.3 Package a chip in the editor and execute its hidden grid in the runtime workbench.
<!-- covers: spatial-circuits/editor-authoring-tools :: Editor authoring wraps the runtime workbench contract :: Chip is authored in the editor -->

## 3. Add editor import and lifecycle tools

- [ ] 3.1 Add import, validation, schema-version, content-hash, and open-definition editor services.
- [ ] 3.2 Reject invalid imports without publishing a partial Resource revision.
<!-- covers: spatial-circuits/editor-authoring-tools :: Editor tools expose import and validation identity :: Invalid definition is imported -->
- [ ] 3.3 Open references by pinned stable identifier and content hash.
<!-- covers: spatial-circuits/editor-authoring-tools :: Editor tools expose import and validation identity :: User opens a referenced definition -->
- [ ] 3.4 Isolate the Godot 4.7 dock API and remove every plugin-owned dock, handler, and registration on disable.
<!-- covers: spatial-circuits/editor-authoring-tools :: Dock integration has a clean lifecycle :: Plugin is disabled after use -->
- [ ] 3.5 Re-enable the plugin without duplicate integrations.
<!-- covers: spatial-circuits/editor-authoring-tools :: Dock integration has a clean lifecycle :: Plugin is re-enabled -->
- [ ] 3.6 Extend staged-addon and independent-consumer checks to the full editor tools.
<!-- covers: spatial-circuits/editor-authoring-tools :: Final editor addon remains independently installable :: Updated addon is installed in a blank consumer -->
- [ ] 3.7 Add the public editor-to-runtime panel and chip practice.
<!-- covers: spatial-circuits/editor-authoring-tools :: Editor-to-runtime practice is portable :: Authored assets cross surfaces -->

## 4. Run Stage 13 and 14 gates

- [ ] 4.1 Run formatting, dependency checks, `dotnet build`, pure tests, and bounded pinned-Godot tests.
- [ ] 4.2 Run the Node monitor practice and retain its command and presentation traces.
- [ ] 4.3 Run editor lifecycle, independent-consumer, and editor-to-runtime practices and retain process results.
- [ ] 4.4 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
