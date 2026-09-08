using SpatialCircuits.Core;
using Xunit;

namespace SpatialCircuits.Core.Tests;

public sealed class RuntimeFactoryTests
{
    [Fact]
    public void InvalidCircuitIsRejectedBeforeInstantiation()
    {
        var source = TestCircuits.ValidCircuit();
        var circuit = Circuit.Create(
            new CircuitId("Bad Circuit"),
            source.Definitions,
            source.Components,
            source.Ownerships);
        var factory = new ProbeRuntimeFactory();

        var result = factory.Create(circuit);

        Assert.Null(result.Instance);
        Assert.Equal(0, factory.ActivationCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.InvalidCircuitId, diagnostic.Code);
        Assert.Equal("$.circuitId", diagnostic.Path);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void TwoInstancesDivergeIndependently()
    {
        var circuit = TestCircuits.ValidCircuit();
        var factory = new CircuitRuntimeFactory();

        var firstResult = factory.Create(circuit);
        var secondResult = factory.Create(circuit);

        var first = Assert.IsType<CircuitRuntimeInstance>(firstResult.Instance);
        var second = Assert.IsType<CircuitRuntimeInstance>(secondResult.Instance);
        var componentId = new ComponentId("component-a");
        var firstComponent = first.GetComponent(componentId);
        var secondComponent = second.GetComponent(componentId);

        firstComponent.SetParameter("delay", "9");

        Assert.NotSame(firstComponent, secondComponent);
        Assert.Equal("9", firstComponent.Parameters["delay"]);
        Assert.Equal("1", secondComponent.Parameters["delay"]);
        Assert.Equal("1", Assert.Single(circuit.Components).Parameters["delay"]);
        Assert.NotSame(first.Scheduler, second.Scheduler);
        Assert.Equal(
            1,
            first.Scheduler.GetTarget("component-a").Incarnation);
        Assert.Empty(firstResult.Diagnostics);
        Assert.Empty(secondResult.Diagnostics);
    }

    private sealed class ProbeRuntimeFactory : CircuitRuntimeFactory
    {
        public int ActivationCount { get; private set; }

        protected override CircuitRuntimeInstance CreateInstance(Circuit circuit)
        {
            ActivationCount++;
            return base.CreateInstance(circuit);
        }
    }
}
