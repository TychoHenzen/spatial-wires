# Stage 12 tamper detection

The runner fixture exercises the fixed microtick protocol:

```text
dotnet run --project src/SpatialCircuits.Runner -- examples/stage-12/tamper-detection.fixture.json
```

The controller emits an 8-bit LFSR challenge every 512 microticks. The panel
responder computes rotate-left-one(challenge) XOR 0xA7. The graph practice
routes challenge, response, and return links at 16 microticks each. The
protocol evaluates the responder at the compute boundary and accepts a valid
response from offset 48 through 96. Missing, early, late, duplicate,
malformed, Unknown, and HighImpedance responses latch the alarm.

The Godot dock button **Tamper bypass** builds the responder through public
panel editing, disconnects the original downstream lanes through the public
device graph, inserts the player responder, connects its two 16-microtick
links, and verifies a saved and replayed response without an alarm.
