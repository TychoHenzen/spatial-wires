using Godot;

namespace SpatialCircuits.GodotAdapter;

[GlobalClass]
public partial class SpatialCircuitChildNetworkResource : Resource
{
    [Export]
    public Godot.Collections.Array<SpatialCircuitChildInstanceResource> Instances { get; set; } = new();

    [Export]
    public Godot.Collections.Array<SpatialCircuitChildConnectionResource> Connections { get; set; } = new();
}

[GlobalClass]
public partial class SpatialCircuitChildInstanceResource : Resource
{
    [Export]
    public string InstanceId { get; set; } = string.Empty;

    [Export]
    public string DefinitionId { get; set; } = string.Empty;

    [Export]
    public string ContentHash { get; set; } = string.Empty;
}

[GlobalClass]
public partial class SpatialCircuitChildConnectionResource : Resource
{
    [Export]
    public string SourceInstanceId { get; set; } = string.Empty;

    [Export]
    public string SourcePortName { get; set; } = string.Empty;

    [Export]
    public string TargetInstanceId { get; set; } = string.Empty;

    [Export]
    public string TargetPortName { get; set; } = string.Empty;
}
