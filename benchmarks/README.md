# Performance evidence

Run the versioned temporal fixture with the Release benchmark project:

```powershell
dotnet run --project benchmarks/SpatialCircuits.Benchmarks.csproj --configuration Release -- benchmarks/fixtures/temporal-frontier.fixture.json benchmarks/results/temporal-frontier.json
```

The report records the root fixture identity and raw fixture hash, cell count, active
frontier, throughput, allocated bytes per microtick, per-step frame time,
execution mode, build configuration, timestamp, and machine context. It also
contains both traces and the first differing tick and snapshot path when the
reference and optimized modes diverge.

The optimized mode reuses a precomputed active-cell frontier. Reference mode
still evaluates the complete panel index range. Both modes remain selectable,
and the comparison fails before a benchmark report is accepted.

The frame-time value is the pure simulation step time. A Godot render-frame
measurement belongs to the Godot adapter qualification path and is not inferred
from this headless core benchmark.
