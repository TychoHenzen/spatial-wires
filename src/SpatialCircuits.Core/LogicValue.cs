namespace SpatialCircuits.Core;

public enum LogicValue
{
    Low,
    High,
    Unknown,
    HighImpedance
}

public static class DriveResolver
{
    public static LogicValue Resolve(IEnumerable<LogicValue> drives)
    {
        ArgumentNullException.ThrowIfNull(drives);

        var active = drives.Where(value => value != LogicValue.HighImpedance).Distinct().ToArray();
        if (active.Length == 0)
        {
            return LogicValue.HighImpedance;
        }

        if (active.Length == 1 && active[0] is LogicValue.Low or LogicValue.High)
        {
            return active[0];
        }

        return LogicValue.Unknown;
    }
}
