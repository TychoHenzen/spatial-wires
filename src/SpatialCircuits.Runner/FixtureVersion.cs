namespace SpatialCircuits.Runner;

public readonly record struct FixtureVersion(int Major, int Minor)
{
    public static FixtureVersion Current => new(1, 0);
}
