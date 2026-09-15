# Sparse Node-backed cells

Node-backed cells use a stable binding ID and positive version in a
`NodeCellBindingDefinition`. The definition records the owning panel and the
Node device ports. `NodeCellBindingRegistry.Resolve` returns an inert
placeholder with a stable diagnostic when the ID, version, or owner is absent.

Place a definition with `PanelWorkbenchSession.TryPlaceNodeCell`, commit the
edit, then bind its `SpatialCircuitNode` through
`SpatialCircuitNodeBinding(session, definition, node)`. The binding receives
committed input transitions and due timers at the shared main-thread barrier.
Its callback-scoped capability accepts only future output, timer, and
presentation-event ticks. Presentation events use the scheduler event log,
emit at their due tick, and can be restored with the binding snapshot.

Active Node bindings make worker execution ineligible. Dispose the binding or
let the Node leave the scene tree to invalidate its incarnation. The graph
then releases its output drives using the configured cable latency. The
`SpatialCircuitNodeMonitorPractice` Godot test exercises a future `alarm`
presentation event without advancing the current callback tick.
