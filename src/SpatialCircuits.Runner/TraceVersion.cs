namespace SpatialCircuits.Runner;

public readonly record struct TraceVersion(int Major, int Minor)
{
    public static TraceVersion Current => new(1, 0);
}
