using System.Collections.Immutable;
using SpatialCircuits.Core;

namespace SpatialCircuits.Runner;

public sealed record TamperDetectionScenarioPlan(
    int Microticks,
    ImmutableArray<TamperDetectionCasePlan> Cases);

public sealed record TamperDetectionCasePlan(
    string CaseId,
    TamperTopology Topology,
    TamperResponseKind Response,
    int ResponseOffset,
    bool ExpectedAccepted,
    bool ExpectedAlarm,
    int ExpectedAlarmTick);

public sealed record TamperDetectionTraceRecord(
    TraceVersion TraceSchema,
    string FixtureId,
    string CaseId,
    int Sequence,
    int Microtick,
    string? Challenge,
    string? ExpectedResponse,
    string Response,
    string Decision,
    bool ResponseAccepted,
    bool AlarmLatched,
    string Hash,
    bool Passed);

public static class TamperDetectionScenarioExecutor
{
    public static IReadOnlyList<TamperDetectionTraceRecord> Execute(RunnerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var plan = fixture.TamperDetectionScenario
            ?? throw new ArgumentException("Fixture does not contain a tamper-detection scenario.", nameof(fixture));
        var records = new List<TamperDetectionTraceRecord>();
        foreach (var testCase in plan.Cases)
        {
            records.AddRange(ExecuteCase(fixture, plan.Microticks, testCase));
        }

        return records.AsReadOnly();
    }

    private static IEnumerable<TamperDetectionTraceRecord> ExecuteCase(
        RunnerFixture fixture,
        int microticks,
        TamperDetectionCasePlan testCase)
    {
        var protocol = new TamperDetectionProtocol();
        protocol.SetTopology(testCase.Topology);
        ScheduleResponse(protocol, microticks, testCase);
        var records = new List<TamperDetectionTraceRecord>(microticks);
        for (var tick = 0; tick < microticks; tick++)
        {
            var result = protocol.Step();
            var expectedAlarm = testCase.ExpectedAlarm &&
                                testCase.ExpectedAlarmTick >= 0 &&
                                tick >= testCase.ExpectedAlarmTick;
            var passed = result.AlarmLatched == expectedAlarm &&
                         (!result.ResponseAccepted || testCase.ExpectedAccepted);
            records.Add(new TamperDetectionTraceRecord(
                fixture.TraceSchema,
                fixture.FixtureId,
                testCase.CaseId,
                tick,
                tick,
                result.Challenge?.ToString("X2"),
                result.ExpectedResponse?.ToString("X2"),
                result.Response,
                result.Decision,
                result.ResponseAccepted,
                result.AlarmLatched,
                result.Hash,
                passed));
        }

        var final = records[^1];
        var accepted = protocol.Decisions.Any(decision => decision.Accepted);
        if (accepted != testCase.ExpectedAccepted || final.AlarmLatched != testCase.ExpectedAlarm)
        {
            records[^1] = final with { Passed = false };
        }

        return records;
    }

    private static void ScheduleResponse(
        TamperDetectionProtocol protocol,
        int microticks,
        TamperDetectionCasePlan testCase)
    {
        if (testCase.Response == TamperResponseKind.Missing ||
            testCase.Topology == TamperTopology.Disconnected)
        {
            return;
        }

        if (testCase.Response == TamperResponseKind.Correct &&
            testCase.ResponseOffset == TamperDetectionProtocol.ResponseWindowStart)
        {
            protocol.AttachResponder(challenge =>
                TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));
            return;
        }

        if (testCase.Response == TamperResponseKind.Correct &&
            testCase.Topology == TamperTopology.ActiveSplice)
        {
            protocol.AttachResponder(challenge =>
                TamperResponse.FromByte(TamperDetectionProtocol.ExpectedResponse(challenge)));
            return;
        }

        for (var requestTick = 0; requestTick < microticks; requestTick += TamperDetectionProtocol.ChallengePeriod)
        {
            var arrivalTick = checked(requestTick + testCase.ResponseOffset);
            var challenge = LfsrChallenge(requestTick);
            var expected = TamperDetectionProtocol.ExpectedResponse(challenge);
            var response = testCase.Response switch
            {
                TamperResponseKind.Correct => TamperResponse.FromByte(expected),
                TamperResponseKind.Early => TamperResponse.FromByte(expected),
                TamperResponseKind.Late => TamperResponse.FromByte(expected),
                TamperResponseKind.Duplicate => TamperResponse.FromByte(expected),
                TamperResponseKind.Malformed => TamperResponse.Malformed,
                TamperResponseKind.Unknown => TamperResponse.Unknown,
                TamperResponseKind.HighImpedance => TamperResponse.HighImpedance,
                TamperResponseKind.Incorrect => TamperResponse.FromByte((byte)(expected ^ 0x01)),
                _ => throw new ArgumentOutOfRangeException(nameof(testCase.Response))
            };

            protocol.QueueInjectedResponse(requestTick, arrivalTick, response);
            if (testCase.Response == TamperResponseKind.Duplicate)
            {
                protocol.QueueInjectedResponse(requestTick, checked(arrivalTick + 1), response);
            }
        }
    }

    private static byte LfsrChallenge(int requestTick)
    {
        var state = TamperDetectionProtocol.LfsrSeed;
        for (var tick = 0; tick < requestTick / TamperDetectionProtocol.ChallengePeriod; tick++)
        {
            var feedback = (byte)(((state >> 7) ^ (state >> 5) ^ (state >> 4) ^ (state >> 3)) & 1);
            state = (byte)((state << 1) | feedback);
            if (state == 0)
            {
                state = TamperDetectionProtocol.LfsrSeed;
            }
        }

        return state;
    }
}
