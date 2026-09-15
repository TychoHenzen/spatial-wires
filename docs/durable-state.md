# Durable state practice

Run the public Stage 11 practice from the repository root:

```powershell
dotnet run --project src/SpatialCircuits.Runner -- examples/stage-11/durable-replay.fixture.json
```

The practice records a Node-backed exchange, serializes the complete session, reloads it, and replays the accepted Node commands without a live Node. Each output line contains the live and replay causal hashes. A passing run reports matching hashes for all five microticks.

Durable saves use schema version `1`, pin stable definitions and behavior assembly fingerprints, and replace an existing file only after candidate validation. Migration creates a new save identity while preserving stable circuit and component identifiers. Presentation is regenerated from causal traces, so frame timing is not part of causal state.
