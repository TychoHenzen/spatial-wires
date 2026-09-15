using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Runner;

public enum TamperTopology
{
    Intact,
    Disconnected,
    PassiveTap,
    ActiveSplice,
    PlayerBuiltResponder
}

public enum TamperResponseKind
{
    Correct,
    Missing,
    Early,
    Late,
    Duplicate,
    Malformed,
    Unknown,
    HighImpedance,
    Incorrect
}

public enum TamperLinkDirection
{
    ControllerToResponder,
    ResponderToDownstream
}

public sealed record TamperTopologyRegistration(
    TamperTopology Topology,
    string SourceId,
    string DownstreamPanelId,
    string ResponderInsertionPoint,
    string ChallengeLinkId,
    string ResponseLinkId,
    TamperLinkDirection ChallengeLinkDirection,
    TamperLinkDirection ResponseLinkDirection,
    bool OriginalConnected,
    bool PlayerResponderInserted,
    bool PassiveTapObserving,
    bool ActiveSpliceDriving,
    int ChallengeLinkLatency,
    int ResponseLinkLatency)
{
    public static TamperTopologyRegistration For(TamperTopology topology) => topology switch
    {
        TamperTopology.Intact => Create(topology, "controller", "downstream-original", "downstream-original", true, false, false, false),
        TamperTopology.Disconnected => Create(topology, "controller", "downstream-original", "downstream-original", false, false, false, false),
        TamperTopology.PassiveTap => Create(topology, "controller", "downstream-original", "downstream-original", true, false, true, false),
        TamperTopology.ActiveSplice => Create(topology, "controller", "downstream-original", "downstream-original", true, false, false, true),
        TamperTopology.PlayerBuiltResponder => Create(topology, "controller", "downstream-original", "downstream-player", false, true, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(topology))
    };

    private static TamperTopologyRegistration Create(
        TamperTopology topology,
        string sourceId,
        string downstreamPanelId,
        string responderInsertionPoint,
        bool originalConnected,
        bool playerResponderInserted,
        bool passiveTapObserving,
        bool activeSpliceDriving) => new(
        topology,
        sourceId,
        downstreamPanelId,
        responderInsertionPoint,
        "link/challenge",
        "link/response",
        TamperLinkDirection.ControllerToResponder,
        TamperLinkDirection.ResponderToDownstream,
        originalConnected,
        playerResponderInserted,
        passiveTapObserving,
        activeSpliceDriving,
        16,
        16);
}

public static class TamperDetectionController
{
    public static TimedDeviceBackendDefinition CreateTimedBackend(int challengeCount = 2)
    {
        if (challengeCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(challengeCount));
        }

        var ports = Enumerable.Range(0, 8)
            .Select(bit => DevicePortDefinition.Create($"challenge-{bit}", DevicePortDirection.Output))
            .ToArray();
        var changes = new List<TimedOutputChange>(challengeCount * ports.Length);
        var state = TamperDetectionProtocol.LfsrSeed;
        for (var challengeIndex = 0; challengeIndex < challengeCount; challengeIndex++)
        {
            var tick = checked(1 + challengeIndex * TamperDetectionProtocol.ChallengePeriod);
            for (var bit = 0; bit < 8; bit++)
            {
                changes.Add(new TimedOutputChange(
                    tick,
                    $"challenge-{bit}",
                    DeviceSignal.Scalar((state & (1 << bit)) == 0 ? LogicValue.Low : LogicValue.High)));
            }

            state = NextState(state);
        }

        return TimedDeviceBackendDefinition.Create(ports, changes);
    }

    private static byte NextState(byte state)
    {
        var feedback = (byte)(((state >> 7) ^ (state >> 5) ^ (state >> 4) ^ (state >> 3)) & 1);
        var next = (byte)((state << 1) | feedback);
        return next == 0 ? TamperDetectionProtocol.LfsrSeed : next;
    }
}

public readonly record struct TamperResponse(ImmutableArray<LogicValue> Bits)
{
    public static TamperResponse FromByte(byte value) => new(Enumerable.Range(0, 8)
        .Select(index => (value & (1 << index)) == 0 ? LogicValue.Low : LogicValue.High)
        .ToImmutableArray());

    public static TamperResponse Unknown => new(Enumerable.Repeat(LogicValue.Unknown, 8).ToImmutableArray());

    public static TamperResponse HighImpedance => new(Enumerable.Repeat(LogicValue.HighImpedance, 8).ToImmutableArray());

    public static TamperResponse Malformed => new(Enumerable.Repeat(LogicValue.Low, 7).ToImmutableArray());

    public bool IsEightBit => !Bits.IsDefaultOrEmpty && Bits.Length == 8;

    public byte? ToByte()
    {
        if (!IsEightBit || Bits.Any(bit => bit is not (LogicValue.Low or LogicValue.High)))
        {
            return null;
        }

        byte value = 0;
        for (var index = 0; index < Bits.Length; index++)
        {
            if (Bits[index] == LogicValue.High)
            {
                value |= (byte)(1 << index);
            }
        }

        return value;
    }

    public override string ToString() => Bits.IsDefault
        ? string.Empty
        : string.Concat(Bits.Select(bit => bit switch
        {
            LogicValue.Low => "0",
            LogicValue.High => "1",
            LogicValue.Unknown => "X",
            LogicValue.HighImpedance => "Z",
            _ => "?"
        }));
}

public sealed record TamperPendingResponse(
    int RequestTick,
    int ArrivalTick,
    TamperResponse Response,
    bool FromActiveSplice);

public sealed record TamperResponderRequest(int RequestTick, int ComputeTick, byte Challenge);

public sealed record TamperDecision(
    int Tick,
    int RequestTick,
    string Code,
    bool Accepted,
    bool AlarmLatched);

public sealed record TamperDetectionSnapshot(
    int CurrentTick,
    byte LfsrState,
    TamperTopology Topology,
    bool AlarmLatched,
    byte? ActiveChallenge,
    int ActiveRequestTick,
    ImmutableArray<TamperPendingResponse> PendingResponses,
    ImmutableArray<TamperDecision> Decisions)
{
    public string StateIntegrityHash { get; init; } = string.Empty;

    public bool ResponderAttached { get; init; }

    public ImmutableArray<TamperResponderRequest> PendingResponderRequests { get; init; } = [];

    public ImmutableArray<byte> PassiveTapObservations { get; init; } = [];

    public TamperTopologyRegistration? TopologyRegistration { get; init; }

    public ImmutableArray<TamperPendingResponse> EvaluatedResponses { get; init; } = [];
}

public sealed record TamperDetectionTick(
    int Tick,
    byte? Challenge,
    byte? ExpectedResponse,
    string Response,
    string Decision,
    bool ResponseAccepted,
    bool AlarmLatched,
    string Hash);

/// <summary>Runs the fixed-tick gameplay diagnostic without frame-time or engine state.</summary>
public sealed class TamperDetectionProtocol
{
    public const int ChallengePeriod = 512;
    public const int LinkLatency = 16;
    public const int ResponderComputeDelay = 16;
    public const int ResponseWindowStart = 48;
    public const int ResponseWindowEnd = 96;
    public const int MissingDecisionOffset = ResponseWindowEnd + 1;
    public const byte LfsrSeed = 0x1D;
    public const byte ResponseConstant = 0xA7;

    private readonly List<TamperPendingResponse> _pendingResponses = [];
    private readonly List<TamperPendingResponse> _evaluatedResponses = [];
    private readonly List<TamperResponderRequest> _pendingResponderRequests = [];
    private readonly List<TamperDecision> _decisions = [];
    private byte _lfsrState = LfsrSeed;
    private byte? _activeChallenge;
    private int _activeRequestTick = -1;
    private TamperTopologyRegistration _topologyRegistration =
        TamperTopologyRegistration.For(TamperTopology.Intact);
    private Func<byte, TamperResponse>? _responder;
    private readonly List<byte> _passiveTapObservations = [];

    public int CurrentTick { get; private set; }

    public TamperTopology Topology { get; private set; } = TamperTopology.Intact;

    public TamperTopologyRegistration TopologyRegistration => _topologyRegistration;

    public IReadOnlyList<byte> PassiveTapObservations => _passiveTapObservations;

    public bool AlarmLatched { get; private set; }

    public IReadOnlyList<TamperDecision> Decisions => _decisions;

    public static byte ExpectedResponse(byte challenge) =>
        (byte)(((challenge << 1) | (challenge >> 7)) ^ ResponseConstant);

    public void SetTopology(TamperTopology topology)
    {
        if (!Enum.IsDefined(topology))
        {
            throw new ArgumentOutOfRangeException(nameof(topology));
        }

        RegisterTopology(TamperTopologyRegistration.For(topology));
    }

    public void RegisterTopology(TamperTopologyRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateTopologyRegistration(registration);

        _topologyRegistration = registration;
        Topology = registration.Topology;
    }

    public void AttachResponder(Func<byte, TamperResponse> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _responder = responder;
    }

    public void DetachResponder() => _responder = null;

    public void DisconnectOriginal()
    {
        RegisterTopology(_topologyRegistration with
        {
            Topology = TamperTopology.Disconnected,
            OriginalConnected = false,
            PlayerResponderInserted = false,
            PassiveTapObserving = false,
            ActiveSpliceDriving = false
        });
    }

    public void ReconnectOriginal()
    {
        RegisterTopology(_topologyRegistration with
        {
            Topology = TamperTopology.Intact,
            OriginalConnected = true,
            PlayerResponderInserted = false,
            PassiveTapObserving = false,
            ActiveSpliceDriving = false
        });
    }

    public void AddPassiveTap()
    {
        RegisterTopology(_topologyRegistration with
        {
            Topology = TamperTopology.PassiveTap,
            OriginalConnected = true,
            PlayerResponderInserted = false,
            PassiveTapObserving = true,
            ActiveSpliceDriving = false
        });
    }

    public void AddActiveSplice()
    {
        RegisterTopology(_topologyRegistration with
        {
            Topology = TamperTopology.ActiveSplice,
            OriginalConnected = true,
            PlayerResponderInserted = false,
            PassiveTapObserving = false,
            ActiveSpliceDriving = true
        });
    }

    public void InsertPlayerResponder(Func<byte, TamperResponse> responder)
    {
        AttachResponder(responder);
        RegisterTopology(_topologyRegistration with
        {
            Topology = TamperTopology.PlayerBuiltResponder,
            ResponderInsertionPoint = "downstream-player",
            OriginalConnected = false,
            PlayerResponderInserted = true,
            PassiveTapObserving = false,
            ActiveSpliceDriving = false
        });
    }

    /// <summary>Queues a fixture fault at a chosen tick. Normal exchanges use the two-link route.</summary>
    public void QueueInjectedResponse(
        int requestTick,
        int arrivalTick,
        TamperResponse response,
        bool fromActiveSplice = false)
    {
        if (requestTick < 0 || requestTick % ChallengePeriod != 0 ||
            arrivalTick < requestTick || response.Bits.IsDefault)
        {
            throw new ArgumentException("Tamper response schedule is invalid.", nameof(arrivalTick));
        }

        if (Topology == TamperTopology.Disconnected && !TopologyRegistration.PlayerResponderInserted)
        {
            return;
        }

        _pendingResponses.Add(new TamperPendingResponse(requestTick, arrivalTick, response, fromActiveSplice));
    }

    public void RequestReset(int tick)
    {
        if (tick < CurrentTick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), "Alarm reset cannot target a past microtick.");
        }

        _decisions.Add(new TamperDecision(tick, -1, "reset-requested", false, AlarmLatched));
    }

    public TamperDetectionTick Step()
    {
        var challenge = CurrentTick % ChallengePeriod == 0
            ? EmitChallenge()
            : (byte?)null;
        var decision = string.Empty;
        var accepted = false;

        var responderRequests = _pendingResponderRequests
            .Where(request => request.ComputeTick == CurrentTick)
            .OrderBy(request => request.RequestTick)
            .ToArray();
        foreach (var request in responderRequests)
        {
            _pendingResponderRequests.Remove(request);
            ScheduleRoutedResponse(request);
        }

        var reset = _decisions.LastOrDefault(item => item.Tick == CurrentTick && item.Code == "reset-requested");
        if (reset is not null)
        {
            AlarmLatched = false;
            decision = "reset";
        }

        var responses = _pendingResponses
            .Where(response => response.ArrivalTick == CurrentTick)
            .OrderBy(response => response.RequestTick)
            .ThenBy(response => response.FromActiveSplice)
            .ToArray();
        foreach (var group in responses.GroupBy(response => (response.RequestTick, response.ArrivalTick)))
        {
            foreach (var pending in group)
            {
                _pendingResponses.Remove(pending);
            }

            var pendingResponses = group.Any(item => item.FromActiveSplice)
                ? [group.First() with { Response = ResolveSplice(group), FromActiveSplice = true }]
                : group.ToArray();
            foreach (var pending in pendingResponses)
            {
                _evaluatedResponses.Add(pending with { ArrivalTick = CurrentTick });
                var result = EvaluateResponse(pending);
                decision = result.Code;
                accepted |= result.Accepted;
                if (!result.Accepted && result.Code.Length > 0)
                {
                    AlarmLatched = true;
                }

                _decisions.Add(new TamperDecision(
                    CurrentTick,
                    pending.RequestTick,
                    result.Code,
                    result.Accepted,
                    AlarmLatched));
            }
        }

        if (_activeChallenge is not null && CurrentTick == _activeRequestTick + MissingDecisionOffset &&
            responses.Length == 0 &&
            !_decisions.Any(item => item.RequestTick == _activeRequestTick && item.Accepted))
        {
            AlarmLatched = true;
            decision = "missing";
            _decisions.Add(new TamperDecision(CurrentTick, _activeRequestTick, decision, false, true));
        }

        if (challenge is not null)
        {
            _activeChallenge = challenge;
            _activeRequestTick = CurrentTick;
            if (_responder is not null &&
                (_topologyRegistration.OriginalConnected || _topologyRegistration.PlayerResponderInserted))
            {
                _pendingResponderRequests.Add(new TamperResponderRequest(
                    CurrentTick,
                    checked(CurrentTick + _topologyRegistration.ChallengeLinkLatency + ResponderComputeDelay),
                    challenge.Value));
            }
        }

        var expected = _activeChallenge is { } active ? ExpectedResponse(active) : (byte?)null;
        var responseText = responses.Length == 0 ? "missing" : string.Join(",", responses.Select(item => item.Response.ToString()));
        var hash = ComputeHash(challenge, expected, responseText, decision, accepted);
        CurrentTick++;
        return new TamperDetectionTick(
            CurrentTick - 1,
            challenge,
            expected,
            responseText,
            decision,
            accepted,
            AlarmLatched,
            hash);
    }

    public TamperDetectionSnapshot CaptureSnapshot()
    {
        var snapshot = new TamperDetectionSnapshot(
            CurrentTick,
            _lfsrState,
            Topology,
            AlarmLatched,
            _activeChallenge,
            _activeRequestTick,
            _pendingResponses.OrderBy(item => item.ArrivalTick).ThenBy(item => item.RequestTick).ToImmutableArray(),
            _decisions.ToImmutableArray());
        return snapshot with
        {
            ResponderAttached = _responder is not null,
            PendingResponderRequests = _pendingResponderRequests.ToImmutableArray(),
            PassiveTapObservations = _passiveTapObservations.ToImmutableArray(),
            TopologyRegistration = _topologyRegistration,
            EvaluatedResponses = _evaluatedResponses.ToImmutableArray(),
            StateIntegrityHash = ComputeStateIntegrityHash(snapshot with
            {
                ResponderAttached = _responder is not null,
                PendingResponderRequests = _pendingResponderRequests.ToImmutableArray(),
                PassiveTapObservations = _passiveTapObservations.ToImmutableArray(),
                TopologyRegistration = _topologyRegistration,
                EvaluatedResponses = _evaluatedResponses.ToImmutableArray()
            })
        };
    }

    public void RestoreSnapshot(TamperDetectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.CurrentTick < 0 || snapshot.ActiveRequestTick < -1 ||
            snapshot.LfsrState == 0 || snapshot.ActiveChallenge is null && snapshot.ActiveRequestTick != -1 ||
            snapshot.ActiveChallenge is not null && snapshot.ActiveRequestTick < 0 ||
            !Enum.IsDefined(snapshot.Topology) || snapshot.PendingResponses.IsDefault ||
            snapshot.Decisions.IsDefault || !IsSha256(snapshot.StateIntegrityHash) ||
            snapshot.ResponderAttached && snapshot.Topology == TamperTopology.Disconnected ||
            snapshot.PendingResponderRequests.IsDefault ||
            snapshot.PendingResponderRequests.Any(request => request.RequestTick < 0 ||
                                                             request.ComputeTick < snapshot.CurrentTick ||
                                                             request.ComputeTick < request.RequestTick ||
                                                             request.RequestTick > int.MaxValue - LinkLatency - ResponderComputeDelay ||
                                                             request.ComputeTick != request.RequestTick +
                                                                 LinkLatency +
                                                                 ResponderComputeDelay ||
                                                             request.Challenge == 0 ||
                                                             snapshot.ActiveRequestTick != request.RequestTick ||
                                                             snapshot.ActiveChallenge != request.Challenge) ||
            snapshot.PassiveTapObservations.IsDefault ||
            snapshot.PassiveTapObservations.Any(observation => observation == 0) ||
            snapshot.TopologyRegistration is null ||
            snapshot.TopologyRegistration.Topology != snapshot.Topology ||
            !IsValidTopologyRegistration(snapshot.TopologyRegistration) ||
            snapshot.EvaluatedResponses.IsDefault ||
            snapshot.EvaluatedResponses.Any(response => response.RequestTick < 0 ||
                                                        response.RequestTick % ChallengePeriod != 0 ||
                                                        response.ArrivalTick < response.RequestTick ||
                                                        response.ArrivalTick > snapshot.CurrentTick ||
                                                        response.Response.Bits.IsDefault ||
                                                        response.Response.Bits.Any(bit => !Enum.IsDefined(bit))) ||
            snapshot.PendingResponses.Any(response => response.RequestTick < 0 ||
                                                      response.RequestTick % ChallengePeriod != 0 ||
                                                      response.ArrivalTick < snapshot.CurrentTick ||
                                                      response.ArrivalTick < response.RequestTick ||
                                                      response.Response.Bits.IsDefault ||
                                                      response.Response.Bits.Any(bit => !Enum.IsDefined(bit))) ||
            snapshot.Decisions.Any(decision => !IsValidDecision(decision, snapshot)) ||
            !string.Equals(snapshot.StateIntegrityHash, ComputeStateIntegrityHash(snapshot), StringComparison.Ordinal))
        {
            throw new ArgumentException("Tamper detection snapshot is invalid.", nameof(snapshot));
        }

        CurrentTick = snapshot.CurrentTick;
        _lfsrState = snapshot.LfsrState;
        RegisterTopology(snapshot.TopologyRegistration!);
        _responder = snapshot.ResponderAttached ? DefaultResponder : null;
        AlarmLatched = snapshot.AlarmLatched;
        _activeChallenge = snapshot.ActiveChallenge;
        _activeRequestTick = snapshot.ActiveRequestTick;
        _pendingResponses.Clear();
        _pendingResponses.AddRange(snapshot.PendingResponses);
        _pendingResponderRequests.Clear();
        _pendingResponderRequests.AddRange(snapshot.PendingResponderRequests);
        _evaluatedResponses.Clear();
        _evaluatedResponses.AddRange(snapshot.EvaluatedResponses);
        _decisions.Clear();
        _decisions.AddRange(snapshot.Decisions);
        _passiveTapObservations.Clear();
        _passiveTapObservations.AddRange(snapshot.PassiveTapObservations);
    }

    private byte EmitChallenge()
    {
        var challenge = _lfsrState;
        var feedback = (byte)(((_lfsrState >> 7) ^ (_lfsrState >> 5) ^ (_lfsrState >> 4) ^ (_lfsrState >> 3)) & 1);
        _lfsrState = (byte)((_lfsrState << 1) | feedback);
        if (_lfsrState == 0)
        {
            _lfsrState = LfsrSeed;
        }

        return challenge;
    }

    private void ScheduleRoutedResponse(TamperResponderRequest request)
    {
        if (_responder is null ||
            !_topologyRegistration.OriginalConnected && !_topologyRegistration.PlayerResponderInserted)
        {
            return;
        }

        var response = _responder(request.Challenge);
        var arrivalTick = checked(CurrentTick + _topologyRegistration.ResponseLinkLatency);
        _pendingResponses.Add(new TamperPendingResponse(request.RequestTick, arrivalTick, response, false));
        if (_topologyRegistration.PassiveTapObserving)
        {
            _passiveTapObservations.Add(request.Challenge);
        }

        if (_topologyRegistration.ActiveSpliceDriving)
        {
            _pendingResponses.Add(new TamperPendingResponse(
                request.RequestTick,
                arrivalTick,
                TamperResponse.Unknown,
                true));
        }
    }

    private (string Code, bool Accepted) EvaluateResponse(TamperPendingResponse pending)
    {
        if (_activeChallenge is null || pending.RequestTick != _activeRequestTick)
        {
            return ("late", false);
        }

        var offset = CurrentTick - pending.RequestTick;
        if (offset < ResponseWindowStart)
        {
            return ("early", false);
        }

        if (offset > ResponseWindowEnd)
        {
            return ("late", false);
        }

        if (_decisions.Any(item => item.RequestTick == pending.RequestTick && item.Accepted))
        {
            return ("duplicate", false);
        }

        if (pending.Response.Bits.Any(bit => bit == LogicValue.Unknown))
        {
            return ("unknown", false);
        }

        if (pending.Response.Bits.Any(bit => bit == LogicValue.HighImpedance))
        {
            return ("high-impedance", false);
        }

        var expected = TamperResponse.FromByte(ExpectedResponse(_activeChallenge.Value));
        if (!pending.Response.IsEightBit ||
            !pending.Response.Bits.AsSpan().SequenceEqual(expected.Bits.AsSpan()))
        {
            return (pending.Response.IsEightBit ? "incorrect" : "malformed", false);
        }

        return ("accepted", true);
    }

    private static TamperResponse ResolveSplice(IEnumerable<TamperPendingResponse> responses)
    {
        var candidates = responses.Select(item => item.Response).ToArray();
        if (candidates.Any(response => !response.IsEightBit))
        {
            return TamperResponse.Malformed;
        }

        return new TamperResponse(Enumerable.Range(0, 8)
            .Select(index => DriveResolver.Resolve(candidates.Select(response => response.Bits[index])))
            .ToImmutableArray());
    }

    private string ComputeHash(
        byte? challenge,
        byte? expected,
        string response,
        string decision,
        bool accepted)
    {
        var builder = new StringBuilder();
        builder.Append(CurrentTick).Append('|')
            .Append(_lfsrState).Append('|')
            .Append((int)Topology).Append('|')
            .Append(AlarmLatched).Append('|')
            .Append(_activeChallenge?.ToString() ?? string.Empty).Append('|')
            .Append(_activeRequestTick).Append('|')
            .Append(_responder is not null).Append('|')
            .Append(challenge?.ToString() ?? string.Empty).Append('|')
            .Append(expected?.ToString() ?? string.Empty).Append('|')
            .Append(response).Append('|')
            .Append(decision).Append('|')
            .Append(accepted);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static string ComputeStateIntegrityHash(TamperDetectionSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.CurrentTick).Append('|')
            .Append(snapshot.LfsrState).Append('|')
            .Append((int)snapshot.Topology).Append('|')
            .Append(snapshot.AlarmLatched).Append('|')
            .Append(snapshot.ActiveChallenge?.ToString() ?? string.Empty).Append('|')
            .Append(snapshot.ActiveRequestTick).Append('|')
            .Append(snapshot.ResponderAttached).Append('|');
        AppendTopologyRegistration(builder, snapshot.TopologyRegistration);
        foreach (var pending in snapshot.PendingResponses)
        {
            builder.Append('|').Append(pending.RequestTick).Append('|').Append(pending.ArrivalTick)
                .Append('|').Append(pending.Response).Append('|').Append(pending.FromActiveSplice);
        }

        foreach (var request in snapshot.PendingResponderRequests)
        {
            builder.Append('|').Append(request.RequestTick).Append('|').Append(request.ComputeTick)
                .Append('|').Append(request.Challenge);
        }

        foreach (var observation in snapshot.PassiveTapObservations)
        {
            builder.Append('|').Append(observation);
        }

        foreach (var response in snapshot.EvaluatedResponses)
        {
            builder.Append('|').Append(response.RequestTick).Append('|').Append(response.ArrivalTick)
                .Append('|').Append(response.Response).Append('|').Append(response.FromActiveSplice);
        }

        foreach (var decision in snapshot.Decisions)
        {
            builder.Append('|').Append(decision.Tick).Append('|').Append(decision.RequestTick)
                .Append('|').Append(decision.Code).Append('|').Append(decision.Accepted)
                .Append('|').Append(decision.AlarmLatched);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsStableId(string? value) =>
        value is not null && Regex.IsMatch(
            value,
            "^[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?(?::[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?)?$",
            RegexOptions.CultureInvariant);

    private static bool IsValidTopologyRegistration(TamperTopologyRegistration? registration)
    {
        if (registration is null || !Enum.IsDefined(registration.Topology) ||
            !Enum.IsDefined(registration.ChallengeLinkDirection) ||
            !Enum.IsDefined(registration.ResponseLinkDirection) ||
            !IsStableId(registration.SourceId) ||
            !IsStableId(registration.DownstreamPanelId) ||
            !IsStableId(registration.ResponderInsertionPoint) ||
            !IsStableId(registration.ChallengeLinkId) ||
            !IsStableId(registration.ResponseLinkId) ||
            registration.ChallengeLinkLatency != LinkLatency ||
            registration.ResponseLinkLatency != LinkLatency ||
            registration.ChallengeLinkDirection != TamperLinkDirection.ControllerToResponder ||
            registration.ResponseLinkDirection != TamperLinkDirection.ResponderToDownstream ||
            registration.Topology == TamperTopology.Disconnected && registration.OriginalConnected ||
            registration.Topology == TamperTopology.PlayerBuiltResponder &&
            (!registration.PlayerResponderInserted || registration.OriginalConnected) ||
            registration.Topology != TamperTopology.PlayerBuiltResponder && registration.PlayerResponderInserted ||
            registration.Topology == TamperTopology.PassiveTap && !registration.PassiveTapObserving ||
            registration.Topology != TamperTopology.PassiveTap && registration.PassiveTapObserving ||
            registration.Topology == TamperTopology.ActiveSplice && !registration.ActiveSpliceDriving ||
            registration.Topology != TamperTopology.ActiveSplice && registration.ActiveSpliceDriving)
        {
            return false;
        }

        return true;
    }

    private static void ValidateTopologyRegistration(TamperTopologyRegistration registration)
    {
        if (!IsValidTopologyRegistration(registration))
        {
            throw new ArgumentException("Tamper topology registration is invalid.", nameof(registration));
        }
    }

    private static void AppendTopologyRegistration(
        StringBuilder builder,
        TamperTopologyRegistration? registration)
    {
        if (registration is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append((int)registration.Topology).Append('|')
            .Append(registration.SourceId).Append('|')
            .Append(registration.DownstreamPanelId).Append('|')
            .Append(registration.ResponderInsertionPoint).Append('|')
            .Append(registration.ChallengeLinkId).Append('|')
            .Append(registration.ResponseLinkId).Append('|')
            .Append((int)registration.ChallengeLinkDirection).Append('|')
            .Append((int)registration.ResponseLinkDirection).Append('|')
            .Append(registration.OriginalConnected).Append('|')
            .Append(registration.PlayerResponderInserted).Append('|')
            .Append(registration.PassiveTapObserving).Append('|')
            .Append(registration.ActiveSpliceDriving).Append('|')
            .Append(registration.ChallengeLinkLatency).Append('|')
            .Append(registration.ResponseLinkLatency);
    }

    private static bool IsValidDecision(TamperDecision decision, TamperDetectionSnapshot snapshot)
    {
        var known = decision.Code is "reset-requested" or "accepted" or "early" or "late" or
            "duplicate" or "malformed" or "unknown" or "high-impedance" or "incorrect" or "missing";
        if (!known || decision.Tick < 0 ||
            decision.Accepted != (decision.Code == "accepted"))
        {
            return false;
        }

        if (decision.Code == "reset-requested")
        {
            return decision.RequestTick == -1;
        }

        if (decision.Tick > snapshot.CurrentTick)
        {
            return false;
        }

        if (decision.RequestTick < 0 || decision.RequestTick > decision.Tick ||
            decision.RequestTick % ChallengePeriod != 0)
        {
            return false;
        }

        var offset = decision.Tick - decision.RequestTick;
        if (decision.Code == "accepted" && !snapshot.EvaluatedResponses.Any(response =>
                !response.FromActiveSplice &&
                response.RequestTick == decision.RequestTick &&
                response.ArrivalTick == decision.Tick &&
                response.Response.ToByte() == ExpectedResponse(ChallengeAt(decision.RequestTick))))
        {
            return false;
        }

        return decision.Code switch
        {
            "accepted" or "duplicate" or "malformed" or "unknown" or "high-impedance" or "incorrect" =>
                offset is >= ResponseWindowStart and <= ResponseWindowEnd,
            "early" => offset < ResponseWindowStart,
            "late" => offset > ResponseWindowEnd,
            "missing" => offset == MissingDecisionOffset,
            _ => false
        };
    }

    private static byte ChallengeAt(int requestTick)
    {
        var state = LfsrSeed;
        for (var index = 0; index < requestTick / ChallengePeriod; index++)
        {
            var feedback = (byte)(((state >> 7) ^ (state >> 5) ^ (state >> 4) ^ (state >> 3)) & 1);
            state = (byte)((state << 1) | feedback);
            if (state == 0)
            {
                state = LfsrSeed;
            }
        }

        return state;
    }

    private static readonly Func<byte, TamperResponse> DefaultResponder =
        challenge => TamperResponse.FromByte(TamperDetectionResponder.ComputeResponse(challenge));
}
