using System.Collections.Immutable;

namespace SpatialCircuits.Runner;

public sealed class ResolutionCase
{
    public ResolutionCase(string caseId, IEnumerable<string> drives, string expected)
    {
        ArgumentNullException.ThrowIfNull(drives);

        CaseId = caseId ?? string.Empty;
        Drives = drives.ToImmutableArray();
        Expected = expected ?? string.Empty;
    }

    public string CaseId { get; }

    public ImmutableArray<string> Drives { get; }

    public string Expected { get; }
}
