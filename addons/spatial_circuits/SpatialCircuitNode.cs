using Godot;
using System;

namespace SpatialCircuits.GodotAdapter;

[GlobalClass]
public partial class SpatialCircuitNode : Node
{
    public event Action<SpatialCircuitNodeStepContext>? DeviceStep;

    internal void DispatchDeviceStep(SpatialCircuitNodeStepContext context) => DeviceStep?.Invoke(context);
}
