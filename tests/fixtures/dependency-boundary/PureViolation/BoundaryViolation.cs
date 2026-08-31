using Godot;

namespace SpatialCircuits.DependencyBoundaryFixture;

public sealed class BoundaryViolation
{
    public GodotMarker Marker { get; } = new();
}
