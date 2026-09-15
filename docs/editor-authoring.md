# Editor authoring

The Spatial Circuits dock uses SpatialCircuitEditorAuthoringSession for
validated panel edits, portable Resource conversion, imports, identity display,
and pinned reference opening. The session delegates edits to
PanelWorkbenchSession, so editor definitions and runtime definitions share
the same validation and canonical data.

SpatialCircuitResourceAdapter.ToResource writes panel and chip Resources,
including hash-pinned child networks. SpatialCircuitEditorIdentity.HashPanel
uses the durable definition codec, and chip ContentHash identifies the saved
asset. Reference targets register an identity provider, so opening recomputes
the target identity and rejects a changed resource. Device references use the
same catalog and the durable HashDevice identity.

Import publishes a new revision only after conversion and validation succeeds.
A failed import returns the stable editor.import.invalid diagnostic and leaves
the last published revision unchanged. Imported chips seed the workbench
catalog with the imported pin and known child definitions. Missing child pins
are rejected before the session changes. Failed saves register no new target.
The editor displays the schema version and current identity, and the editor
practice paints, packages, saves, reloads, opens, and runs a hash-pinned chip
through the hidden-grid workbench.

The plugin targets Godot 4.7.1. Its dock and handlers clean up on disable and
can be enabled again without duplicate registrations.
