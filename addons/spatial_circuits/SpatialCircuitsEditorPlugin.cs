using Godot;

namespace SpatialCircuits.GodotAdapter;

[Tool]
public partial class SpatialCircuitsEditorPlugin : EditorPlugin
{
    public const string PluginId = "spatial_circuits";
    public const string DockName = "SpatialCircuitsDock";
    public const string DockTitle = "Spatial Circuits";

    private SpatialCircuitsDock? _dock;

    public override void _EnterTree()
    {
        _dock = new SpatialCircuitsDock();
        AddDock(_dock);
    }

    public override void _ExitTree()
    {
        if (_dock is null)
        {
            return;
        }

        RemoveDock(_dock);
        _dock.QueueFree();
        _dock = null;
    }
}
