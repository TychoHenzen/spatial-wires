using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Runner;

public sealed record DeviceExchangeScenarioPlan(
    int Microticks,
    int LinkLatency,
    ImmutableArray<DeviceExchangeExpectation> Expectations,
    ImmutableArray<DeviceExchangeDeliveryExpectation> DeliveryExpectations);

public sealed record DeviceExchangeExpectation(int Tick, string Challenge, string Response);

public sealed record DeviceExchangeDeliveryExpectation(int Tick, string LaneId, string Signal);

public sealed record DeviceExchangeDeliveryTrace(string LaneId, int Tick, string Signal);

public sealed record DeviceExchangeTraceRecord(
    TraceVersion TraceSchema,
    string FixtureId,
    int Sequence,
    int Microtick,
    string Challenge,
    string ExpectedChallenge,
    string Response,
    string ExpectedResponse,
    ImmutableArray<DeviceExchangeDeliveryTrace> Deliveries,
    ImmutableArray<DeviceExchangeDeliveryExpectation> ExpectedDeliveries,
    bool Passed);

public static class DeviceExchangeScenarioExecutor
{
    public static IReadOnlyList<DeviceExchangeTraceRecord> Execute(RunnerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var plan = fixture.DeviceExchangeScenario
            ?? throw new ArgumentException("Fixture does not contain a device-exchange scenario.", nameof(fixture));
        var runtime = CreateRuntime(plan.LinkLatency);
        var controller = new ComponentId("controller");
        var responder = new ComponentId("responder");
        runtime.SetInput(controller, "challenge-drive", DeviceSignal.Scalar(LogicValue.High));
        runtime.SetInput(responder, "enable", DeviceSignal.Scalar(LogicValue.High));
        var expectedByTick = plan.Expectations.ToDictionary(item => item.Tick);
        var deliveriesByTick = plan.DeliveryExpectations.GroupBy(item => item.Tick)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.LaneId, StringComparer.Ordinal)
                .ThenBy(item => item.Signal, StringComparer.Ordinal).ToArray());
        var trace = new List<DeviceExchangeTraceRecord>(plan.Microticks);

        for (var tick = 0; tick < plan.Microticks; tick++)
        {
            var result = runtime.Step();
            var challenge = runtime.GetOutput(controller, "challenge").Bits[0].ToString();
            var response = runtime.GetOutput(controller, "response").Bits[0].ToString();
            var deliveries = result.CableDeliveries
                .Select(item => new DeviceExchangeDeliveryTrace(
                    item.LaneId.Value, checked((int)item.DeliveredTick), item.Signal.Bits[0].ToString()))
                .OrderBy(item => item.LaneId, StringComparer.Ordinal)
                .ThenBy(item => item.Signal, StringComparer.Ordinal)
                .ToImmutableArray();
            expectedByTick.TryGetValue(tick, out var expected);
            var expectedDeliveries = deliveriesByTick.TryGetValue(tick, out var atTick)
                ? atTick.ToImmutableArray()
                : ImmutableArray<DeviceExchangeDeliveryExpectation>.Empty;
            var passed = expected is not null &&
                         string.Equals(challenge, expected.Challenge, StringComparison.Ordinal) &&
                         string.Equals(response, expected.Response, StringComparison.Ordinal) &&
                         deliveries.Select(item => (item.LaneId, item.Tick, item.Signal)).SequenceEqual(
                             expectedDeliveries.Select(item => (item.LaneId, item.Tick, item.Signal)));
            trace.Add(new DeviceExchangeTraceRecord(
                fixture.TraceSchema,
                fixture.FixtureId,
                tick,
                tick,
                challenge,
                expected?.Challenge ?? string.Empty,
                response,
                expected?.Response ?? string.Empty,
                deliveries,
                expectedDeliveries,
                passed));
        }

        return trace;
    }

    private static DeviceGraphInstance CreateRuntime(int latency)
    {
        var controllerPanel = PanelDefinition.Create(
            new CircuitId("panel/controller"),
            2,
            3,
            [
                PortCell("challenge-source", 0, 0, CellKind.InputPort, CardinalDirection.East, "challenge-drive"),
                PortCell("challenge-output", 1, 0, CellKind.OutputPort, CardinalDirection.East, "challenge"),
                PortCell("response-source", 0, 2, CellKind.InputPort, CardinalDirection.East, "response-in"),
                PortCell("response-output", 1, 2, CellKind.OutputPort, CardinalDirection.East, "response")
            ]);
        var responderPanel = PanelDefinition.Create(
            new CircuitId("panel/responder"),
            3,
            3,
            [
                PortCell("challenge-source", 1, 0, CellKind.InputPort, CardinalDirection.South, "challenge"),
                PortCell("enable-source", 1, 2, CellKind.InputPort, CardinalDirection.North, "enable"),
                PanelCellDefinition.Create(new ComponentId("nand"), new GridCoordinate(1, 1),
                    CellKind.Nand, CardinalDirection.East,
                    parameters: [new KeyValuePair<string, string>("delay", "1")]),
                PortCell("response-output", 2, 1, CellKind.OutputPort, CardinalDirection.East, "response")
            ]);
        var controller = DeviceDefinition.Create(new ComponentId("controller"),
            PanelDeviceBackendDefinition.Create(controllerPanel));
        var responder = DeviceDefinition.Create(new ComponentId("responder"),
            PanelDeviceBackendDefinition.Create(responderPanel));
        return new DeviceGraphInstance(DeviceGraphDefinition.Create(
            new DefinitionId("graph/exchange"),
            [controller, responder],
            [
                CableLaneDefinition.Create(new ComponentId("lane/challenge"),
                    DevicePortEndpoint.Create(controller.Id, "challenge"),
                    DevicePortEndpoint.Create(responder.Id, "challenge"), latency),
                CableLaneDefinition.Create(new ComponentId("lane/response"),
                    DevicePortEndpoint.Create(responder.Id, "response"),
                    DevicePortEndpoint.Create(controller.Id, "response-in"), latency)
            ]));
    }

    private static PanelCellDefinition PortCell(
        string id,
        int x,
        int y,
        CellKind kind,
        CardinalDirection direction,
        string port) => PanelCellDefinition.Create(
        new ComponentId(id),
        new GridCoordinate(x, y),
        kind,
        direction,
        new PortId(port));
}
