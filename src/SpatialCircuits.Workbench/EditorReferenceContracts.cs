using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace SpatialCircuits.Workbench;

public static class SpatialCircuitEditorDiagnosticCodes
{
    public const string ImportInvalid = "editor.import.invalid";
    public const string SaveInvalid = "editor.save.invalid";
    public const string SaveFailed = "editor.save.failed";
    public const string ReferenceMissing = "editor.reference.missing";
    public const string ReferenceHashMismatch = "editor.reference.hash-mismatch";
}

public enum SpatialCircuitReferenceKind
{
    Panel,
    Chip,
    Device
}

public sealed record SpatialCircuitReference(
    SpatialCircuitReferenceKind Kind,
    string StableId,
    string ContentHash)
{
    public static SpatialCircuitReference Create(
        SpatialCircuitReferenceKind kind,
        string stableId,
        string contentHash)
    {
        if (!Enum.IsDefined(kind) || !IsStableId(stableId) || !IsHash(contentHash))
        {
            throw new ArgumentException("Editor reference identity is invalid.");
        }

        return new SpatialCircuitReference(kind, stableId, contentHash.ToLowerInvariant());
    }

    private static bool IsStableId(string? value) =>
        value is not null && Regex.IsMatch(
            value,
            "^[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?(?::[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?)?$",
            RegexOptions.CultureInvariant);

    private static bool IsHash(string? value) =>
        value is not null && Regex.IsMatch(value, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
}

public sealed class SpatialCircuitReferenceCatalog
{
    private readonly Dictionary<(SpatialCircuitReferenceKind Kind, string Id, string Hash),
        (SpatialCircuitReference Reference, object? Target, Func<SpatialCircuitReference>? IdentityProvider)> _references = [];

    public ImmutableArray<SpatialCircuitReference> References => _references.Values
        .Select(item => item.Reference)
        .OrderBy(reference => reference.Kind)
        .ThenBy(reference => reference.StableId, StringComparer.Ordinal)
        .ThenBy(reference => reference.ContentHash, StringComparer.Ordinal)
        .ToImmutableArray();

    public void Register(
        SpatialCircuitReference reference,
        object? target = null,
        Func<SpatialCircuitReference>? identityProvider = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (target is not null && identityProvider is null)
        {
            throw new ArgumentException(
                "A reference target requires an identity provider.",
                nameof(identityProvider));
        }

        _references[(reference.Kind, reference.StableId, reference.ContentHash)] =
            (reference, target, identityProvider);
    }

    public bool TryOpen(
        SpatialCircuitReference reference,
        out SpatialCircuitReference? opened,
        out object? target,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (_references.TryGetValue((reference.Kind, reference.StableId, reference.ContentHash), out var entry))
        {
            if (entry.IdentityProvider is { } identityProvider)
            {
                try
                {
                    var current = identityProvider();
                    if (current != entry.Reference)
                    {
                        opened = null;
                        target = null;
                        diagnostic =
                            $"{SpatialCircuitEditorDiagnosticCodes.ReferenceHashMismatch}: reference '{reference.StableId}' has a different content hash.";
                        return false;
                    }
                }
                catch (Exception exception)
                    when (exception is ArgumentException or InvalidOperationException)
                {
                    opened = null;
                    target = null;
                    diagnostic =
                        $"{SpatialCircuitEditorDiagnosticCodes.ReferenceHashMismatch}: reference '{reference.StableId}' could not be validated ({exception.Message}).";
                    return false;
                }
            }

            opened = entry.Reference;
            target = entry.Target;
            diagnostic = string.Empty;
            return true;
        }

        var sameIdentity = _references.Keys.Any(key => key.Kind == reference.Kind &&
                                                       string.Equals(key.Id, reference.StableId,
                                                           StringComparison.Ordinal));
        opened = null;
        target = null;
        diagnostic = sameIdentity
            ? $"{SpatialCircuitEditorDiagnosticCodes.ReferenceHashMismatch}: reference '{reference.StableId}' has a different content hash."
            : $"{SpatialCircuitEditorDiagnosticCodes.ReferenceMissing}: reference '{reference.StableId}' is not registered.";
        return false;
    }

    public bool TryOpen(
        SpatialCircuitReference reference,
        out SpatialCircuitReference? opened,
        out string diagnostic)
    {
        var result = TryOpen(reference, out opened, out _, out diagnostic);
        return result;
    }
}
