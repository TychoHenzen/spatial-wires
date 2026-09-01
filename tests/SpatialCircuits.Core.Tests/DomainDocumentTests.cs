using System.Security.Cryptography;
using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Core.Tests;

public sealed class DomainDocumentTests
{
    // covers: spatial-circuits/domain-documents-and-runner :: Portable definitions use stable identities and ownership :: Document identity survives process boundaries
    [Fact]
    public void DocumentIdentitySurvivesProcessBoundaries()
    {
        var document = CircuitDocument.Create(new("doc-1"), [new("def-b"), new("def-a")], [new(new("owner"), new("def-a"))], [new("behavior-and")]);
        var loaded = CircuitDocumentCodec.Read(CircuitDocumentCodec.Write(document));
        Assert.Equal(document.DocumentId, loaded.DocumentId);
        Assert.Equal(document.Definitions, loaded.Definitions);
        Assert.Equal(document.Ownerships, loaded.Ownerships);
        Assert.Equal(document.Behaviors, loaded.Behaviors);
    }

    // covers: spatial-circuits/domain-documents-and-runner :: Portable definitions use stable identities and ownership :: CLR type name is used as persisted behavior identity
    [Fact]
    public void ClrTypeNameIsRejectedAsBehaviorIdentity()
    {
        var document = CircuitDocument.Create(new("doc"), [], [], [new("System.String")]);
        Assert.Contains("SCHEMA_BEHAVIOR_ID:behaviors", document.Validate());
    }

    // covers: spatial-circuits/domain-documents-and-runner :: Canonical documents round-trip exactly :: Canonical round trip is byte stable
    [Fact]
    public void CanonicalRoundTripIsByteStable()
    {
        var bytes = CircuitDocumentCodec.Write(CircuitDocument.Create(new("doc"), [new("b"), new("a")], [], [new("z"), new("a")]));
        var roundTrip = CircuitDocumentCodec.Write(CircuitDocumentCodec.Read(bytes));
        Assert.Equal(bytes, roundTrip);
        Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(roundTrip));
    }

    // covers: spatial-circuits/domain-documents-and-runner :: Canonical documents round-trip exactly :: Equivalent input ordering is normalized
    [Fact]
    public void EquivalentInputOrderingIsNormalized()
    {
        var first = CircuitDocumentCodec.Write(CircuitDocument.Create(new("doc"), [new("a"), new("b")], [new(new("o"), new("b")), new(new("o"), new("a"))], [new("x"), new("a")]));
        var second = CircuitDocumentCodec.Write(CircuitDocument.Create(new("doc"), [new("b"), new("a")], [new(new("o"), new("a")), new(new("o"), new("b"))], [new("a"), new("x")]));
        Assert.Equal(first, second);
        Assert.Equal(SHA256.HashData(first), SHA256.HashData(second));
    }
}
