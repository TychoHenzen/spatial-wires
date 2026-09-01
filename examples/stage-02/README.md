# Stage 2 drive-resolution practice

Run this command from the repository root:

```powershell
dotnet run --project src/SpatialCircuits.Runner/SpatialCircuits.Runner.csproj -- examples/stage-02/drive-resolution.fixture.json
```

The fixture declares fixture schema `1.0` and trace schema `1.0`.
It selects the `resolveDrives` action and lists its inputs and expected observations.

The command prints one JSON record per case. The records preserve fixture order.
This practice emits `HighImpedance`, then `Low`, then `Unknown` contention.
Each record includes the trace schema, sequence, observation, expectation, and pass result.
The command exits with code `0` when every observed value matches the expected value.
Invalid JSON or fixture data produces JSON diagnostic records and a nonzero exit code.
