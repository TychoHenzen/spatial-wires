using Godot;

namespace SpatialCircuits.GodotAdapter;

[GlobalClass]
public partial class SpatialCircuitChipPortResource : Resource
{
    [Export]
    public string Name { get; set; } = string.Empty;

    [Export]
    public string PanelPortId { get; set; } = string.Empty;

    [Export(PropertyHint.Enum, "input,output")]
    public string Direction { get; set; } = "input";
}
