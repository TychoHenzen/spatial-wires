using Godot;

namespace SpatialCircuits.GodotAdapter;

[Tool]
public partial class SpatialCircuitsDock : EditorDock
{
    public SpatialCircuitsDock()
    {
        Name = SpatialCircuitsEditorPlugin.DockName;
        Title = SpatialCircuitsEditorPlugin.DockTitle;
        LayoutKey = SpatialCircuitsEditorPlugin.PluginId;
        DefaultSlot = DockSlot.LeftUr;
        Global = true;

        AddChild(new Label
        {
            Name = "Title",
            Text = SpatialCircuitsEditorPlugin.DockTitle,
        });
    }
}
