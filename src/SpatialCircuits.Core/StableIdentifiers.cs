namespace SpatialCircuits.Core;

public readonly record struct CircuitId
{
    public CircuitId(string value) => Value = value ?? string.Empty;

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct DefinitionId
{
    public DefinitionId(string value) => Value = value ?? string.Empty;

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct ComponentId
{
    public ComponentId(string value) => Value = value ?? string.Empty;

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct PortId
{
    public PortId(string value) => Value = value ?? string.Empty;

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct BehaviorId
{
    public BehaviorId(string value) => Value = value ?? string.Empty;

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct OwnerId
{
    public OwnerId(string value) => Value = value ?? string.Empty;

    public string Value { get; }

    public override string ToString() => Value;
}
