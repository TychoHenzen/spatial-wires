using SpatialCircuits.Core;
using SpatialCircuits.Cells;
using SpatialCircuits.Runner;
using SpatialCircuits.Workbench;
using Xunit;

namespace SpatialCircuits.Runner.Tests;

public sealed class TamperDetectionProtocolTests
{
    [Fact]
    public void PanelResponderComputesRotateLeftXorForEveryByte()
    {
        for (var challenge = 0; challenge <= byte.MaxValue; challenge++)
        {
            Assert.Equal(
                TamperDetectionProtocol.ExpectedResponse((byte)challenge),
                TamperDetectionResponder.ComputeResponse((byte)challenge));
        }
    }

    [Fact]
    public void IntactResponseIsAcceptedAtTheStartOfTheWindow()
    {
        var protocol = new TamperDetectionProtocol();
        protocol.QueueInjectedResponse(
            0,
            TamperDetectionProtocol.ResponseWindowStart,
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(TamperDetectionProtocol.LfsrSeed)));

        TamperDetectionTick result = default!;
        for (var tick = 0; tick <= TamperDetectionProtocol.ResponseWindowStart; tick++)
        {
            result = protocol.Step();
        }

        Assert.True(result.ResponseAccepted);
        Assert.False(result.AlarmLatched);
    }

    [Fact]
    public void AutomaticResponderUsesBothSixteenMicrotickLinks()
    {
        var protocol = new TamperDetectionProtocol();
        protocol.SetTopology(TamperTopology.Intact);
        protocol.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));

        TamperDetectionTick result = default!;
        for (var tick = 0; tick <= TamperDetectionProtocol.ResponseWindowStart; tick++)
        {
            result = protocol.Step();
        }

        Assert.Equal(TamperLinkDirection.ControllerToResponder,
            protocol.TopologyRegistration.ChallengeLinkDirection);
        Assert.Equal(TamperLinkDirection.ResponderToDownstream,
            protocol.TopologyRegistration.ResponseLinkDirection);
        Assert.Equal(TamperDetectionProtocol.LinkLatency,
            protocol.TopologyRegistration.ChallengeLinkLatency);
        Assert.Equal("accepted", result.Decision);
        Assert.True(result.ResponseAccepted);
    }

    [Fact]
    public void DisconnectedOriginalWaitsForTheMissingDecision()
    {
        var protocol = new TamperDetectionProtocol();
        protocol.SetTopology(TamperTopology.Disconnected);
        protocol.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));

        TamperDetectionTick result = default!;
        for (var tick = 0; tick <= TamperDetectionProtocol.MissingDecisionOffset; tick++)
        {
            result = protocol.Step();
        }

        Assert.Equal("missing", result.Decision);
        Assert.True(result.AlarmLatched);
        Assert.DoesNotContain(protocol.Decisions, decision => decision.Accepted);
    }

    [Fact]
    public void ReconnectedOriginalCanUseTheConfiguredRouteAgain()
    {
        var protocol = new TamperDetectionProtocol();
        protocol.SetTopology(TamperTopology.Disconnected);
        protocol.ReconnectOriginal();
        protocol.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));

        TamperDetectionTick result = default!;
        for (var tick = 0; tick <= TamperDetectionProtocol.ResponseWindowStart; tick++)
        {
            result = protocol.Step();
        }

        Assert.True(result.ResponseAccepted);
        Assert.False(result.AlarmLatched);
    }

    [Fact]
    public void PassiveTapObservesWithoutChangingTheResponse()
    {
        var protocol = new TamperDetectionProtocol();
        protocol.SetTopology(TamperTopology.PassiveTap);
        protocol.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));

        for (var tick = 0; tick <= TamperDetectionProtocol.ResponseWindowStart; tick++)
        {
            protocol.Step();
        }

        Assert.Contains(TamperDetectionProtocol.LfsrSeed, protocol.PassiveTapObservations);
        Assert.False(protocol.AlarmLatched);
    }

    [Fact]
    public void ActiveSpliceResolvesTheAdditionalDriver()
    {
        var protocol = new TamperDetectionProtocol();
        protocol.SetTopology(TamperTopology.ActiveSplice);
        protocol.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));

        TamperDetectionTick result = default!;
        for (var tick = 0; tick <= TamperDetectionProtocol.ResponseWindowStart; tick++)
        {
            result = protocol.Step();
        }

        Assert.Equal("unknown", result.Decision);
        Assert.True(result.AlarmLatched);
        Assert.True(protocol.TopologyRegistration.ActiveSpliceDriving);
    }

    [Fact]
    public void CorrectResponseAtEndOfWindowIsAccepted()
    {
        var protocol = new TamperDetectionProtocol();
        protocol.QueueInjectedResponse(
            0,
            TamperDetectionProtocol.ResponseWindowEnd,
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(TamperDetectionProtocol.LfsrSeed)));

        TamperDetectionTick result = default!;
        for (var tick = 0; tick <= TamperDetectionProtocol.ResponseWindowEnd; tick++)
        {
            result = protocol.Step();
        }

        Assert.True(result.ResponseAccepted);
        Assert.False(result.AlarmLatched);
    }

    [Fact]
    public void TopologyRegistrationRejectsUnstableIdentifiers()
    {
        var protocol = new TamperDetectionProtocol();
        var invalid = TamperTopologyRegistration.For(TamperTopology.Intact) with
        {
            SourceId = "bad id"
        };

        Assert.Throws<ArgumentException>(() => protocol.RegisterTopology(invalid));
    }

    [Theory]
    [InlineData(TamperResponseKind.Early, "early", 32)]
    [InlineData(TamperResponseKind.Late, "late", 97)]
    [InlineData(TamperResponseKind.Duplicate, "duplicate", 49)]
    [InlineData(TamperResponseKind.Malformed, "malformed", 48)]
    [InlineData(TamperResponseKind.Unknown, "unknown", 48)]
    [InlineData(TamperResponseKind.HighImpedance, "high-impedance", 48)]
    public void InvalidResponsesLatchTheAlarm(
        TamperResponseKind kind,
        string expectedDecision,
        int arrivalTick)
    {
        var protocol = new TamperDetectionProtocol();
        var expected = TamperDetectionProtocol.ExpectedResponse(TamperDetectionProtocol.LfsrSeed);
        var response = kind switch
        {
            TamperResponseKind.Malformed => TamperResponse.Malformed,
            TamperResponseKind.Unknown => TamperResponse.Unknown,
            TamperResponseKind.HighImpedance => TamperResponse.HighImpedance,
            _ => TamperResponse.FromByte(expected)
        };
        protocol.QueueInjectedResponse(0, arrivalTick, response);
        if (kind == TamperResponseKind.Duplicate)
        {
            protocol.QueueInjectedResponse(0, arrivalTick + 1, response);
        }

        TamperDetectionTick result = default!;
        for (var tick = 0; tick <= arrivalTick + (kind == TamperResponseKind.Duplicate ? 1 : 0); tick++)
        {
            result = protocol.Step();
        }

        Assert.Equal(expectedDecision, result.Decision);
        Assert.True(result.AlarmLatched);
    }

    [Fact]
    public void SnapshotRestoreKeepsTheAlarmDecisionAndFutureHash()
    {
        var live = new TamperDetectionProtocol();
        live.QueueInjectedResponse(
            0,
            TamperDetectionProtocol.ResponseWindowStart,
            TamperResponse.Unknown);
        for (var tick = 0; tick <= TamperDetectionProtocol.ResponseWindowStart; tick++)
        {
            live.Step();
        }

        var snapshot = live.CaptureSnapshot();
        Assert.Contains(snapshot.Decisions, decision =>
            decision.Tick == TamperDetectionProtocol.ResponseWindowStart &&
            decision.Code == "unknown" && !decision.Accepted);
        var expected = Enumerable.Range(0, 90).Select(_ => live.Step()).ToArray();
        var restored = new TamperDetectionProtocol();
        restored.RestoreSnapshot(snapshot);
        var actual = Enumerable.Range(0, 90).Select(_ => restored.Step()).ToArray();

        Assert.Equal(expected.Select(item => item.Hash), actual.Select(item => item.Hash));
        Assert.Equal(expected[^1].AlarmLatched, actual[^1].AlarmLatched);
    }

    [Fact]
    public void SnapshotRestoreKeepsAResponderRequestAtTheComputeBoundary()
    {
        var live = new TamperDetectionProtocol();
        live.SetTopology(TamperTopology.PlayerBuiltResponder);
        live.AttachResponder(challenge =>
            TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));
        live.Step();

        var snapshot = live.CaptureSnapshot();
        Assert.Single(snapshot.PendingResponderRequests);
        var restored = new TamperDetectionProtocol();
        restored.RestoreSnapshot(snapshot);

        var expected = Enumerable.Range(0, 64).Select(_ => live.Step().Hash);
        var actual = Enumerable.Range(0, 64).Select(_ => restored.Step().Hash);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SnapshotRestoreKeepsResetRequestsAndTopologyMetadata()
    {
        var live = new TamperDetectionProtocol();
        live.RegisterTopology(TamperTopologyRegistration.For(TamperTopology.Intact) with
        {
            SourceId = "custom/controller",
            DownstreamPanelId = "custom/downstream"
        });
        live.QueueInjectedResponse(0, TamperDetectionProtocol.ResponseWindowStart, TamperResponse.Unknown);
        for (var tick = 0; tick <= TamperDetectionProtocol.ResponseWindowStart; tick++)
        {
            live.Step();
        }

        live.RequestReset(live.CurrentTick);
        var snapshot = live.CaptureSnapshot();
        var restored = new TamperDetectionProtocol();
        restored.RestoreSnapshot(snapshot);

        Assert.Equal(live.TopologyRegistration, restored.TopologyRegistration);
        Assert.Contains(snapshot.Decisions, decision => decision.Code == "reset-requested");
        var result = restored.Step();
        Assert.Equal("reset", result.Decision);
        Assert.False(result.AlarmLatched);
    }

    [Fact]
    public void SnapshotRestoreKeepsAFutureResetRequest()
    {
        var live = new TamperDetectionProtocol();
        for (var tick = 0; tick < 49; tick++)
        {
            live.Step();
        }

        live.RequestReset(60);
        var snapshot = live.CaptureSnapshot();
        var restored = new TamperDetectionProtocol();
        restored.RestoreSnapshot(snapshot);

        while (restored.CurrentTick < 60)
        {
            restored.Step();
        }

        Assert.Equal("reset", restored.Step().Decision);
        Assert.False(restored.AlarmLatched);
    }

    [Fact]
    public void ResetClearsALatchedAlarmAtItsRequestedMicrotick()
    {
        var protocol = new TamperDetectionProtocol();
        protocol.QueueInjectedResponse(0, 48, TamperResponse.Unknown);
        for (var tick = 0; tick <= 48; tick++)
        {
            protocol.Step();
        }

        Assert.True(protocol.AlarmLatched);
        protocol.RequestReset(protocol.CurrentTick);
        var result = protocol.Step();

        Assert.Equal("reset", result.Decision);
        Assert.False(result.AlarmLatched);
    }

    [Fact]
    public void RepeatedChallengeIsAcceptedInItsCurrentWindow()
    {
        var protocol = new TamperDetectionProtocol();
        var repeatedRequest = TamperDetectionProtocol.ChallengePeriod * 255;
        var state = TamperDetectionProtocol.LfsrSeed;
        for (var index = 0; index <= 255; index++)
        {
            var request = index * TamperDetectionProtocol.ChallengePeriod;
            protocol.QueueInjectedResponse(
                request,
                request + TamperDetectionProtocol.ResponseWindowStart,
                TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(state)));
            var feedback = (byte)(((state >> 7) ^ (state >> 5) ^ (state >> 4) ^ (state >> 3)) & 1);
            state = (byte)((state << 1) | feedback);
            if (state == 0)
            {
                state = TamperDetectionProtocol.LfsrSeed;
            }
        }

        TamperDetectionTick result = default!;
        for (var tick = 0; tick <= repeatedRequest + 48; tick++)
        {
            result = protocol.Step();
        }

        Assert.True(result.ResponseAccepted);
        Assert.False(result.AlarmLatched);
    }

    [Fact]
    public void PublicEditingBuildsTheResponderPanel()
    {
        var session = TamperDetectionResponder.BuildThroughPublicEditing();

        Assert.Equal(29, session.CommittedDefinition.Panel.Cells.Count(cell => cell is not null));
        Assert.Equal(13, session.CommittedDefinition.Panel.Cells.Count(cell => cell?.Kind == CellKind.InputPort));
        Assert.Equal(8, session.CommittedDefinition.Panel.Cells.Count(cell => cell?.Kind == CellKind.OutputPort));
        Assert.Empty(session.CommandLog.Where(command => command.Status == WorkbenchCommandStatus.Pending));
    }

    [Fact]
    public void PublicBypassPracticeDisconnectsAndReplacesTheDownstreamResponder()
    {
        var result = TamperDetectionPractice.Run();

        Assert.True(result.Succeeded);
        Assert.True(result.OriginalDisconnected);
        Assert.True(result.PlayerResponderInserted);
        Assert.True(result.SaveReplayMatched);
        Assert.True(result.ResponseAccepted);
        Assert.False(result.AlarmLatched);
        Assert.True(result.ResponderComputedExpected);
        Assert.Contains(result.Session.CommittedDefinition.DeviceGraph!.Lanes,
            lane => lane.Latency == TamperDetectionProtocol.LinkLatency &&
                    lane.Id.Value.StartsWith("lane/challenge/player/", StringComparison.Ordinal));
    }
}
