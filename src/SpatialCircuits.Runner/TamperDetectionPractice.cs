using System.Collections.Immutable;
using System.Text.Json;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Runner;

public sealed record TamperDetectionPracticeResult(
    bool Succeeded,
    bool OriginalDisconnected,
    bool PlayerResponderInserted,
    bool SaveReplayMatched,
    bool ResponseAccepted,
    bool AlarmLatched,
    ImmutableArray<TamperDetectionTick> Trace,
    ImmutableArray<CableLaneHistoryEntry> CableHistory,
    PanelWorkbenchSession Session)
{
    public ImmutableArray<CableLaneHistoryEntry> ReplacementCableHistory { get; init; } = [];

    public bool ResponderComputedExpected { get; init; }

    public bool TopologyCasesPassed { get; init; }
}

public static class TamperDetectionPractice
{
    private const string OriginalChallengeLane = "lane/challenge/original/0";
    private const string PlayerResponseLane = "lane/return/player/0";

    public static TamperDetectionPracticeResult Run()
    {
        var playerSession = TamperDetectionResponder.BuildThroughPublicEditing();
        var session = BuildOriginalGraph();
        var graph = session.DeviceGraph ?? throw new InvalidOperationException("Tamper practice graph was not created.");

        graph.Step();
        foreach (var laneId in OriginalLaneIds())
        {
            graph.Disconnect(new ComponentId(laneId));
        }

        AddPlayerResponder(session, playerSession.CommittedDefinition.Panel);
        graph = session.DeviceGraph ?? throw new InvalidOperationException("Tamper practice graph was lost.");

        var protocol = new TamperDetectionProtocol();
        protocol.SetTopology(TamperTopology.Intact);
        protocol.DisconnectOriginal();
        TamperResponse graphResponseAtCompute = TamperResponse.Unknown;
        protocol.InsertPlayerResponder(challenge =>
        {
            var response = ReadGraphResponderOutput(graph);
            if (challenge == TamperDetectionProtocol.LfsrSeed)
            {
                graphResponseAtCompute = response;
            }

            return response;
        });

        var trace = new List<TamperDetectionTick>(TamperDetectionProtocol.ChallengePeriod + 100);
        for (var tick = 0; tick < TamperDetectionProtocol.ChallengePeriod + 100; tick++)
        {
            trace.Add(protocol.Step());
            graph.Step();
        }

        var expectedResponse = TamperDetectionProtocol.ExpectedResponse(TamperDetectionProtocol.LfsrSeed);
        var responderComputedExpected = graphResponseAtCompute.ToByte() == expectedResponse;
        var deliveredResponse = ReadGraphResponse(graph);
        var topologyCasesPassed = ExerciseTopologyCases();
        var checkpoint = protocol.CaptureSnapshot();
        var savePath = Path.Combine(Path.GetTempPath(), $"spatial-wires-tamper-{Guid.NewGuid():N}.json");
        TamperDetectionSnapshot loaded;
        try
        {
            File.WriteAllBytes(savePath, JsonSerializer.SerializeToUtf8Bytes(checkpoint));
            loaded = JsonSerializer.Deserialize<TamperDetectionSnapshot>(File.ReadAllBytes(savePath))
                     ?? throw new InvalidOperationException("Tamper practice snapshot could not be reloaded.");
        }
        finally
        {
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }
        }

        var replay = new TamperDetectionProtocol();
        replay.RestoreSnapshot(loaded);
        var liveHashes = Enumerable.Range(0, 32).Select(_ => protocol.Step().Hash).ToArray();
        var replayHashes = Enumerable.Range(0, 32).Select(_ => replay.Step().Hash).ToArray();
        var saveReplayMatched = liveHashes.SequenceEqual(replayHashes);

        var originalHistory = graph.GetLaneHistory(new ComponentId(OriginalChallengeLane));
        var playerHistory = graph.GetLaneHistory(new ComponentId(PlayerResponseLane));
        var originalDisconnected = OriginalLaneIds().All(id => !graph.IsConnected(new ComponentId(id)));
        var playerResponderInserted = session.CommittedDefinition.DevicePlacements.Any(item =>
            item.DeviceId == new ComponentId("device/downstream-player"));
        var accepted = trace.Any(item => item.ResponseAccepted);
        var succeeded = originalDisconnected && playerResponderInserted && accepted &&
                        !protocol.AlarmLatched && saveReplayMatched &&
                        originalHistory.Any(item => item.IsRelease) &&
                        playerHistory.Any(item => item.Status == CableTransitionStatus.Delivered) &&
                        topologyCasesPassed &&
                        deliveredResponse.ToByte() == TamperDetectionProtocol.ExpectedResponse(
                            NextLfsrState(TamperDetectionProtocol.LfsrSeed));

        return new TamperDetectionPracticeResult(
            succeeded,
            originalDisconnected,
            playerResponderInserted,
            saveReplayMatched,
            accepted,
            protocol.AlarmLatched,
            trace.ToImmutableArray(),
            originalHistory.Concat(playerHistory).ToImmutableArray(),
            session)
        {
            ReplacementCableHistory = playerHistory,
            ResponderComputedExpected = responderComputedExpected,
            TopologyCasesPassed = topologyCasesPassed
        };
    }

    private static PanelWorkbenchSession BuildOriginalGraph()
    {
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/tamper-bypass"),
            24,
            40,
            []));
        var source = DeviceDefinition.Create(
            new ComponentId("device/controller"),
            TamperDetectionController.CreateTimedBackend());
        var original = DeviceDefinition.Create(
            new ComponentId("device/downstream-original"),
            PanelDeviceBackendDefinition.Create(TamperDetectionResponder.CreatePanelDefinition(
                "panel/downstream-original")));
        var originalRelay = DeviceDefinition.Create(
            new ComponentId("device/relay-original"),
            PanelDeviceBackendDefinition.Create(CreateResponseRelayPanel("original")));
        var sink = DeviceDefinition.Create(
            new ComponentId("device/downstream-sink"),
            PanelDeviceBackendDefinition.Create(CreateSinkPanel()));

        Place(session, source, new GridCoordinate(1, 1));
        Place(session, original, new GridCoordinate(6, 1));
        Place(session, originalRelay, new GridCoordinate(9, 1));
        Place(session, sink, new GridCoordinate(12, 1));
        Connect(session, BuildLanes(source, original, originalRelay, sink, "original"), "bundle/original");
        return session;
    }

    private static void AddPlayerResponder(PanelWorkbenchSession session, PanelDefinition panel)
    {
        var player = DeviceDefinition.Create(
            new ComponentId("device/downstream-player"),
            PanelDeviceBackendDefinition.Create(panel));
        var playerRelay = DeviceDefinition.Create(
            new ComponentId("device/relay-player"),
            PanelDeviceBackendDefinition.Create(CreateResponseRelayPanel("player")));
        Place(session, player, new GridCoordinate(6, 30));
        Place(session, playerRelay, new GridCoordinate(9, 30));
        var graph = session.CommittedDefinition.DeviceGraph ??
                    throw new InvalidOperationException("Tamper practice device graph is missing.");
        var source = graph.Devices.First(device => device.Id == new ComponentId("device/controller"));
        var sink = graph.Devices.First(device => device.Id == new ComponentId("device/downstream-sink"));
        Connect(session, BuildLanes(source, player, playerRelay, sink, "player"), "bundle/player");
    }

    private static void Place(PanelWorkbenchSession session, DeviceDefinition device, GridCoordinate location)
    {
        if (!session.TryPlaceDevice(device, location, out var diagnostic))
        {
            throw new InvalidOperationException(diagnostic?.Message ?? "Tamper practice device could not be placed.");
        }

        if (!session.CommitStaged(out diagnostic))
        {
            throw new InvalidOperationException(diagnostic?.Message ?? "Tamper practice device could not be committed.");
        }
    }

    private static void Connect(
        PanelWorkbenchSession session,
        IReadOnlyList<CableLaneDefinition> lanes,
        string bundleId)
    {
        var bundle = CableBundleDefinition.Create(new ComponentId(bundleId), lanes.Select(lane => lane.Id));
        if (!session.TryAddCableBundle(bundle, lanes, out var diagnostic))
        {
            throw new InvalidOperationException(diagnostic?.Message ?? "Tamper practice links could not be connected.");
        }

        if (!session.CommitStaged(out diagnostic))
        {
            throw new InvalidOperationException(diagnostic?.Message ?? "Tamper practice links could not be committed.");
        }
    }

    private static IReadOnlyList<CableLaneDefinition> BuildLanes(
        DeviceDefinition source,
        DeviceDefinition responder,
        DeviceDefinition relay,
        DeviceDefinition sink,
        string path)
    {
        var lanes = new List<CableLaneDefinition>(16);
        for (var bit = 0; bit < 8; bit++)
        {
            lanes.Add(CableLaneDefinition.Create(
                new ComponentId($"lane/challenge/{path}/{bit}"),
                DevicePortEndpoint.Create(source.Id, $"challenge-{bit}"),
                DevicePortEndpoint.Create(responder.Id, $"challenge-{bit}"),
                TamperDetectionProtocol.LinkLatency));
            lanes.Add(CableLaneDefinition.Create(
                new ComponentId($"lane/response/{path}/{bit}"),
                DevicePortEndpoint.Create(responder.Id, $"response-{bit}"),
                DevicePortEndpoint.Create(relay.Id, $"{path}-response-in-{bit}"),
                TamperDetectionProtocol.LinkLatency));
            lanes.Add(CableLaneDefinition.Create(
                new ComponentId($"lane/return/{path}/{bit}"),
                DevicePortEndpoint.Create(relay.Id, $"{path}-response-out-{bit}"),
                DevicePortEndpoint.Create(sink.Id, $"{path}-response-{bit}"),
                TamperDetectionProtocol.LinkLatency));
        }

        return lanes;
    }

    private static IEnumerable<string> OriginalLaneIds() =>
        Enumerable.Range(0, 8).SelectMany(bit => new[]
        {
            $"lane/challenge/original/{bit}",
            $"lane/response/original/{bit}",
            $"lane/return/original/{bit}"
        });

    private static TamperResponse ReadGraphResponse(DeviceGraphInstance graph)
    {
        var bits = new LogicValue[8];
        for (var bit = 0; bit < 8; bit++)
        {
            var history = graph.GetLaneHistory(new ComponentId($"lane/return/player/{bit}"));
            var delivered = history
                .Where(item => item.Status == CableTransitionStatus.Delivered && !item.IsRelease)
                .OrderBy(item => item.DeliveredTick)
                .LastOrDefault();
            bits[bit] = delivered?.Signal.Bits is { IsDefaultOrEmpty: false } signal
                ? signal[0]
                : LogicValue.Unknown;
        }

        return new TamperResponse(bits.ToImmutableArray());
    }

    private static TamperResponse ReadGraphResponderOutput(DeviceGraphInstance graph)
    {
        var bits = Enumerable.Range(0, 8)
            .Select(bit => graph.GetOutput(
                new ComponentId("device/downstream-player"),
                $"response-{bit}").Bits[0])
            .ToImmutableArray();
        return new TamperResponse(bits);
    }

    private static bool ExerciseTopologyCases()
    {
        var passive = new TamperDetectionProtocol();
        passive.SetTopology(TamperTopology.PassiveTap);
        passive.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));
        var passiveResult = StepTo(passive, TamperDetectionProtocol.ResponseWindowStart);

        var active = new TamperDetectionProtocol();
        active.SetTopology(TamperTopology.ActiveSplice);
        active.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));
        var activeResult = StepTo(active, TamperDetectionProtocol.ResponseWindowStart);

        var disconnected = new TamperDetectionProtocol();
        disconnected.SetTopology(TamperTopology.Disconnected);
        disconnected.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));
        var missing = StepTo(disconnected, TamperDetectionProtocol.MissingDecisionOffset);

        var reconnected = new TamperDetectionProtocol();
        reconnected.SetTopology(TamperTopology.Disconnected);
        reconnected.ReconnectOriginal();
        reconnected.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));
        var restored = StepTo(reconnected, TamperDetectionProtocol.ResponseWindowStart);

        return passiveResult.ResponseAccepted && !passiveResult.AlarmLatched &&
               activeResult.Decision == "unknown" && activeResult.AlarmLatched &&
               missing.Decision == "missing" && missing.AlarmLatched &&
               restored.ResponseAccepted && !restored.AlarmLatched;
    }

    private static TamperDetectionTick StepTo(TamperDetectionProtocol protocol, int tick)
    {
        TamperDetectionTick result = default!;
        for (var index = 0; index <= tick; index++)
        {
            result = protocol.Step();
        }

        return result;
    }

    private static byte NextLfsrState(byte state)
    {
        var feedback = (byte)(((state >> 7) ^ (state >> 5) ^ (state >> 4) ^ (state >> 3)) & 1);
        var next = (byte)((state << 1) | feedback);
        return next == 0 ? TamperDetectionProtocol.LfsrSeed : next;
    }

    private static PanelDefinition CreateResponseRelayPanel(string path)
    {
        var cells = new List<PanelCellDefinition>();
        for (var bit = 0; bit < 8; bit++)
        {
            var y = bit * 2;
            cells.Add(Cell($"{path}-response-in-{bit}", 0, y, CellKind.InputPort,
                CardinalDirection.East, new PortId($"{path}-response-in-{bit}")));
            cells.Add(Cell($"{path}-response-wire-{bit}", 1, y, CellKind.Wire, CardinalDirection.East));
            cells.Add(Cell($"{path}-response-out-{bit}", 2, y, CellKind.OutputPort,
                CardinalDirection.East, new PortId($"{path}-response-out-{bit}")));
        }

        return PanelDefinition.Create(new CircuitId($"panel/relay-{path}"), 3, 16, cells);
    }

    private static PanelDefinition CreateSinkPanel()
    {
        var cells = new List<PanelCellDefinition>();
        for (var bit = 0; bit < 8; bit++)
        {
            cells.Add(Cell($"original-response-{bit}", 0, bit, CellKind.InputPort,
                CardinalDirection.East, new PortId($"original-response-{bit}")));
            cells.Add(Cell($"player-response-{bit}", 0, bit + 8, CellKind.InputPort,
                CardinalDirection.East, new PortId($"player-response-{bit}")));
        }

        return PanelDefinition.Create(new CircuitId("panel/tamper-sink"), 1, 16, cells);
    }

    private static PanelCellDefinition Cell(
        string id,
        int x,
        int y,
        CellKind kind,
        CardinalDirection orientation,
        PortId? portId = null,
        IEnumerable<KeyValuePair<string, string>>? parameters = null) =>
        PanelCellDefinition.Create(new ComponentId(id), new GridCoordinate(x, y), kind, orientation, portId, parameters);
}
