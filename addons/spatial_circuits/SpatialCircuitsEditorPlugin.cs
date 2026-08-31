using Godot;

namespace SpatialCircuits.GodotAdapter;

[Tool]
public partial class SpatialCircuitsEditorPlugin : EditorPlugin
{
    public const string PluginId = "spatial_circuits";
    public const string DockName = "SpatialCircuitsDock";
    public const string DockTitle = "Spatial Circuits";

    private SpatialCircuitsDock? _dock;
    private bool _dockAdded;
    private bool _dockTreeExitedConnected;

    public override void _EnterTree()
    {
        CleanupOwnedEditorState();

        try
        {
            _dock = new SpatialCircuitsDock();
            _dock.TreeExited += OnDockTreeExited;
            _dockTreeExitedConnected = true;

            AddDock(_dock);
            _dockAdded = true;
        }
        catch
        {
            CleanupOwnedEditorState();
            throw;
        }
    }

    public override void _ExitTree()
    {
        CleanupOwnedEditorState();
    }

    private void CleanupOwnedEditorState()
    {
        var dock = _dock;
        var dockAdded = _dockAdded;
        var dockTreeExitedConnected = _dockTreeExitedConnected;
        _dock = null;
        _dockAdded = false;
        _dockTreeExitedConnected = false;

        if (dock is null || !GodotObject.IsInstanceValid(dock))
        {
            return;
        }

        try
        {
            if (dockTreeExitedConnected)
            {
                dock.TreeExited -= OnDockTreeExited;
            }
        }
        finally
        {
            try
            {
                if (dockAdded && GodotObject.IsInstanceValid(dock))
                {
                    RemoveDock(dock);
                }
            }
            finally
            {
                if (GodotObject.IsInstanceValid(dock) && !dock.IsQueuedForDeletion())
                {
                    dock.QueueFree();
                }
            }
        }
    }

    private void OnDockTreeExited()
    {
        _dock = null;
        _dockAdded = false;
        _dockTreeExitedConnected = false;
    }
}
