## Context

See `proposal.md` and both capability specs. Stage 13 reuses the recorded Node bridge at panel-cell scope. Stage 14 wraps the proven runtime workbench in the existing editor addon shell. Both are Godot-facing and can proceed in parallel after shared asset and lifecycle contracts are fixed.

## Goals / Non-Goals

**Goals:**

- Add sparse Node cell behavior without changing ordinary flat-cell execution.
- Preserve documents when a Node binding is missing.
- Reuse one authoring model across runtime and editor surfaces.

**Non-Goals:**

- Allow one Node per ordinary cell.
- Let editor Resources become runtime authority.
- Qualify unpinned Godot editor versions.

## Decisions

### Resolve cell bindings through the owning panel adapter

Portable cells store only stable binding IDs and behavior versions. The panel's Godot adapter maintains the live binding map and converts a valid binding into a bridge endpoint carrying panel, coordinate, and incarnation identity.

Storing scene paths or Node object IDs in portable documents was rejected because they are not stable across projects or reloads.

### Reuse the device bridge protocol with cell-scoped capabilities

Cell endpoints receive committed neighbor values and due timers in the same canonical main-thread barrier. Their capability limits outputs to the bound cell's declared ports and future ticks. Recording and replay use the existing command and captured-state envelopes.

Creating a second callback scheduler for cells was rejected because re-entry and replay rules would diverge.

### Preserve missing bindings as inert placeholders

If binding resolution fails, keep the portable cell record, expose diagnostics, invalidate live callback capabilities, and release drives through ordinary incarnation rules. Authoring can later repair the binding without reconstructing the document.

Deleting the cell data was rejected because a missing plugin should not destroy saved layouts.

### Make worker eligibility a world invariant

Track active recorded device and cell endpoints in causal topology state. Worker-mode admission and topology edits check that count before stepping or publishing a change.

Checking only at startup was rejected because a live edit could add a Node endpoint after worker execution begins.

### Share one workbench model between runtime and editor

Extract tool commands, validation previews, document revisions, selection, and trace projection from scene-specific controls. The editor dock hosts that model through a thin Godot 4.7 adapter and editor services for import and open-definition actions.

Copying runtime tools into editor-only implementations was rejected because behavior and saved hashes would drift.

### Keep plugin ownership explicit

The editor plugin owns the dock adapter, registrations, imported Resource references, and signal subscriptions. Shutdown is idempotent and reverses partial initialization in one place.

Relying on scene-tree cleanup alone was rejected because editor docks and handlers can outlive their controls.

## Risks / Trade-offs

- [Many Node bindings harm frame time] -> Document sparse use, expose active counts, and benchmark before changing the barrier.
- [Missing-binding placeholders are executed accidentally] -> Make them inert and diagnostic until explicit resolution succeeds.
- [Experimental dock APIs change] -> Isolate calls and keep enable-disable-re-enable acceptance in the pinned editor.
- [Shared workbench model accumulates editor conditionals] -> Keep editor services behind small ports and test the model outside editor lifecycle code.

## Migration Plan

1. Freeze shared binding, placeholder, and workbench-host contracts.
2. Add Node cell resolution, recording, replay, deletion, and worker checks.
3. Extract and host the workbench model in the editor dock adapter.
4. Add import, validation, hash, and open-definition tools.
5. Run Node monitor, editor-to-runtime, lifecycle, and blank-consumer practices.

Rollback replaces Node-bound cells with preserved inert placeholders and disables editor integration. Runtime authoring and portable documents remain available.
