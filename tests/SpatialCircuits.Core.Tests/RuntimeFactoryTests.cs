using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Core.Tests;

public sealed class RuntimeFactoryTests
{
    [Fact]
    public void UnsupportedSchemaIsRejectedBeforeInstantiation()
    {
        var document = TestDocuments.ValidDocument(new SchemaVersion(2, 0));
        var factory = new ProbeRuntimeFactory();

        var result = factory.Create(document);

        Assert.Null(result.Instance);
        Assert.Equal(0, factory.ActivationCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.UnsupportedSchema, diagnostic.Code);
        Assert.Equal("$.schema", diagnostic.DocumentPath);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void TwoInstancesDivergeIndependently()
    {
        var document = TestDocuments.ValidDocument();
        var factory = new CircuitRuntimeFactory();

        var firstResult = factory.Create(document);
        var secondResult = factory.Create(document);

        var first = Assert.IsType<CircuitRuntimeInstance>(firstResult.Instance);
        var second = Assert.IsType<CircuitRuntimeInstance>(secondResult.Instance);
        var componentId = new ComponentId("component-a");
        var firstComponent = first.GetComponent(componentId);
        var secondComponent = second.GetComponent(componentId);

        firstComponent.SetParameter("delay", "9");

        Assert.NotSame(firstComponent, secondComponent);
        Assert.Equal("9", firstComponent.Parameters["delay"]);
        Assert.Equal("1", secondComponent.Parameters["delay"]);
        Assert.Equal("1", Assert.Single(document.Components).Parameters["delay"]);
        Assert.Empty(firstResult.Diagnostics);
        Assert.Empty(secondResult.Diagnostics);
    }

    private sealed class ProbeRuntimeFactory : CircuitRuntimeFactory
    {
        public int ActivationCount { get; private set; }

        protected override CircuitRuntimeInstance CreateInstance(CircuitDocument document)
        {
            ActivationCount++;
            return base.CreateInstance(document);
        }
    }
}
