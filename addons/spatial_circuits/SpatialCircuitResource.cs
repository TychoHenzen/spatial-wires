using Godot;

namespace SpatialCircuits.GodotAdapter;

[GlobalClass]
public partial class SpatialCircuitResource : Resource
{
    [Export]
    public string DefinitionId { get; set; } = string.Empty;

    [Export]
    public SpatialCircuitPanelResource SourcePanel { get; set; } = new();

    [Export]
    public Godot.Collections.Array<SpatialCircuitChipPortResource> Ports { get; set; } = new();

    [Export]
    public int SchemaVersion { get; set; } = 1;

    [Export]
    public int BehaviorVersion { get; set; } = 1;

    [Export]
    public string Symbol { get; set; } = string.Empty;

    [Export]
    public Godot.Collections.Dictionary<string, string> Parameters { get; set; } = new();
}
