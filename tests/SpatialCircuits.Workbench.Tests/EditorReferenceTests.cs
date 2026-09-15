using SpatialCircuits.Workbench;
using Xunit;

namespace SpatialCircuits.Workbench.Tests;

public sealed class EditorReferenceTests
{
    [Fact]
    public void ReferenceAcceptsRuntimeStableIdentifiers()
    {
        var reference = SpatialCircuitReference.Create(
            SpatialCircuitReferenceKind.Device,
            "panel/foo_bar:v2",
            new string('A', 64));

        Assert.Equal("panel/foo_bar:v2", reference.StableId);
        Assert.Equal(new string('a', 64), reference.ContentHash);
    }

    [Fact]
    public void CatalogOpensOnlyTheExactPinnedIdentityAndReturnsItsTarget()
    {
        var catalog = new SpatialCircuitReferenceCatalog();
        var reference = SpatialCircuitReference.Create(
            SpatialCircuitReferenceKind.Chip,
            "chip/editor",
            new string('a', 64));
        var target = new object();
        var currentIdentity = reference;
        catalog.Register(reference, target, () => currentIdentity);

        Assert.True(catalog.TryOpen(reference, out var opened, out var resolvedTarget, out var diagnostic));
        Assert.Equal(reference, opened);
        Assert.Same(target, resolvedTarget);
        Assert.Empty(diagnostic);

        currentIdentity = reference with { ContentHash = new string('b', 64) };
        Assert.False(catalog.TryOpen(reference, out _, out _, out diagnostic));
        Assert.Contains(SpatialCircuitEditorDiagnosticCodes.ReferenceHashMismatch, diagnostic);

        var wrongHash = reference with { ContentHash = new string('b', 64) };
        Assert.False(catalog.TryOpen(wrongHash, out _, out _, out diagnostic));
        Assert.Contains(SpatialCircuitEditorDiagnosticCodes.ReferenceHashMismatch, diagnostic);
    }
}
