using Godot;

namespace SpatialCircuits.GodotAdapter;

[GlobalClass]
public partial class SpatialCircuitCellResource : Resource
{
    [Export]
    public string CellId { get; set; } = string.Empty;

    [Export]
    public int X { get; set; }

    [Export]
    public int Y { get; set; }

    [Export(PropertyHint.Enum,
        "empty,wire,junction,crossing,constant,input-port,output-port,probe,nand,clock,d-flip-flop,stability-filter,custom")]
    public string Kind { get; set; } = "empty";

    [Export(PropertyHint.Enum, "north,east,south,west")]
    public string Orientation { get; set; } = "east";

    [Export]
    public string PortId { get; set; } = string.Empty;

    [Export]
    public string BehaviorId { get; set; } = string.Empty;

    [Export]
    public Godot.Collections.Dictionary<string, string> Parameters { get; set; } = new();
}
