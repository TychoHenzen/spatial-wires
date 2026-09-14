using Godot;

namespace SpatialCircuits.GodotAdapter;

[GlobalClass]
public partial class SpatialCircuitPanelResource : Resource
{
    [Export]
    public string PanelId { get; set; } = string.Empty;

    [Export]
    public int Width { get; set; } = 1;

    [Export]
    public int Height { get; set; } = 1;

    [Export]
    public Godot.Collections.Array<SpatialCircuitCellResource> Cells { get; set; } = new();
}
