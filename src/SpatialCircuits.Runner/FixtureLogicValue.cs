using SpatialCircuits.Core;

namespace SpatialCircuits.Runner;

internal static class FixtureLogicValue
{
    internal static bool TryParse(string value, out LogicValue logicValue)
    {
        logicValue = value switch
        {
            "Low" => LogicValue.Low,
            "High" => LogicValue.High,
            "Unknown" => LogicValue.Unknown,
            "HighImpedance" => LogicValue.HighImpedance,
            _ => default
        };
        return value is "Low" or "High" or "Unknown" or "HighImpedance";
    }

    internal static LogicValue Parse(string value) => TryParse(value, out var parsed)
        ? parsed
        : throw new ArgumentException("Value is not a four-state logic value.", nameof(value));
}
