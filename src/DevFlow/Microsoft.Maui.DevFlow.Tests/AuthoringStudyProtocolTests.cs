using System.Text.Json;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class AuthoringStudyProtocolTests
{
    [Fact]
    public void Aggregate_WithNoSessions_ReportsInsufficientEvidenceAndSaysAssistedAloneIsNotEvidence()
    {
        var report = MauiAuthoringStudyProtocol.Aggregate([]);

        Assert.Equal("insufficient-evidence", report.Status);
        Assert.Equal(0, report.TotalSessions);
        Assert.Equal(0, report.ControlSessions);
        Assert.Empty(report.Tasks);
        Assert.Contains("not evidence that DevFlow reduced authoring time", report.Statement, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregate_WithAssistedArmOnly_RefusesToReportADifference()
    {
        var sessions = Enumerable.Range(0, 12)
            .Select(index => Session($"s{index}", $"participant-{index:x8}", MauiAuthoringStudyProtocol.AssistedArm, 60_000 + index))
            .ToList();

        var report = MauiAuthoringStudyProtocol.Aggregate(sessions);

        Assert.Equal("insufficient-evidence", report.Status);
        Assert.Equal(12, report.AssistedSessions);
        Assert.Equal(0, report.ControlSessions);
        var task = Assert.Single(report.Tasks);
        Assert.Equal("insufficient-evidence", task.Status);
        Assert.Null(task.MedianTimeToFirstResultDifferenceMs);
        Assert.Null(task.MedianTimeToGoalDifferenceMs);
        Assert.Contains("control-session-count-insufficient", task.Blockers);
        Assert.Contains("control-participant-count-insufficient", task.Blockers);
    }

    [Fact]
    public void Aggregate_WithBothArms_ReportsAMedianDifferenceOnly()
    {
        var sessions = new List<MauiAuthoringStudySession>();
        for (var index = 0; index < 5; index++)
        {
            sessions.Add(Session($"a{index}", $"participant-a{index:x7}", MauiAuthoringStudyProtocol.AssistedArm, 100_000));
            sessions.Add(Session($"c{index}", $"participant-c{index:x7}", MauiAuthoringStudyProtocol.ControlArm, 160_000));
        }

        var report = MauiAuthoringStudyProtocol.Aggregate(sessions);

        Assert.Equal("comparable", report.Status);
        var task = Assert.Single(report.Tasks);
        Assert.Equal("comparable", task.Status);
        Assert.Empty(task.Blockers);
        // Time to first result is the primary comparison; the goal-time delta is reported beside
        // it but includes the participant reading the result.
        Assert.Equal(-60_000d, task.MedianTimeToFirstResultDifferenceMs);
        Assert.Equal(-60_000d, task.MedianTimeToGoalDifferenceMs);
        Assert.Equal(5, task.Assisted.Participants);
        Assert.Equal(5, task.Control.Participants);
        Assert.DoesNotContain("significant", report.Statement, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aggregate_CountsParticipantsSeparatelyFromSessions()
    {
        var sessions = new List<MauiAuthoringStudySession>();
        for (var index = 0; index < 6; index++)
        {
            sessions.Add(Session($"a{index}", "participant-aaaaaaa1", MauiAuthoringStudyProtocol.AssistedArm, 100_000));
            sessions.Add(Session($"c{index}", $"participant-c{index:x7}", MauiAuthoringStudyProtocol.ControlArm, 160_000));
        }

        var report = MauiAuthoringStudyProtocol.Aggregate(sessions);

        var task = Assert.Single(report.Tasks);
        Assert.Equal(6, task.Assisted.Sessions);
        Assert.Equal(1, task.Assisted.Participants);
        Assert.Equal("insufficient-evidence", task.Status);
        Assert.Contains("assisted-participant-count-insufficient", task.Blockers);
    }

    [Theory]
    [InlineData("arm", "study-session-arm-unassigned")]
    [InlineData("taskId", "study-session-task-unassigned")]
    [InlineData("participantSalt", "study-session-participant-unlinkable")]
    public void TryReadSession_WithMissingProtocolField_FailsClosed(string field, string expectedRejection)
    {
        var node = JsonSerializer.Deserialize<JsonElement>(ExportJson());
        var mutated = RemoveProtocolField(node, field);

        var session = MauiAuthoringStudyProtocol.TryReadSession(mutated, out var rejection);

        Assert.Null(session);
        Assert.Equal(expectedRejection, rejection);
    }

    [Fact]
    public void TryReadSession_WithTruncatedEventHistory_IsRejectedRatherThanUsed()
    {
        var json = ExportJson(missingFields: """["completeEventHistory"]""");

        var session = MauiAuthoringStudyProtocol.TryReadSession(json, out var rejection);

        Assert.Null(session);
        Assert.Equal("study-session-event-history-truncated", rejection);
    }

    [Fact]
    public void TryReadSession_WithAValidExport_ReadsProtocolAndDurations()
    {
        var session = MauiAuthoringStudyProtocol.TryReadSession(ExportJson(), out var rejection);

        Assert.Null(rejection);
        Assert.NotNull(session);
        Assert.Equal("local-abc123", session!.SessionId);
        Assert.Equal("participant-abcdef01", session.ParticipantSalt);
        Assert.Equal("task-02-form-entry-assertion", session.TaskId);
        Assert.Equal(MauiAuthoringStudyProtocol.AssistedArm, session.Arm);
        Assert.Equal(42_000d, session.TimeToGoalMs);
        Assert.True(session.SavedTest);
    }

    [Fact]
    public void TryReadSession_WithAWrongProtocolVersion_IsRejected()
    {
        var json = ExportJson().Replace("\"protocolVersion\": 1", "\"protocolVersion\": 2", StringComparison.Ordinal);

        var session = MauiAuthoringStudyProtocol.TryReadSession(json, out var rejection);

        Assert.Null(session);
        Assert.Equal("study-session-protocol-version-mismatch", rejection);
    }

    [Fact]
    public void TryReadSession_TrimsAParticipantSaltInsteadOfSplittingOnePersonInTwo()
    {
        // .NET's `$` also matches before a trailing newline, so a salt round-tripped through a
        // text file would validate yet compare ordinally unequal to the same salt without it —
        // counting one participant as two and emptying the cross-arm intersection.
        var json = ExportJson().Replace(
            "\"participantSalt\": \"participant-abcdef01\"",
            "\"participantSalt\": \"participant-abcdef01\\n\"",
            StringComparison.Ordinal);

        var session = MauiAuthoringStudyProtocol.TryReadSession(json, out var rejection);

        Assert.Null(rejection);
        Assert.NotNull(session);
        Assert.Equal("participant-abcdef01", session!.ParticipantSalt);
    }

    [Fact]
    public void TryReadSession_RejectsAParticipantSaltThatIsNotTheDocumentedShape()
    {
        var json = ExportJson().Replace(
            "\"participantSalt\": \"participant-abcdef01\"",
            "\"participantSalt\": \"alice@example.com\"",
            StringComparison.Ordinal);

        var session = MauiAuthoringStudyProtocol.TryReadSession(json, out var rejection);

        Assert.Null(session);
        Assert.Equal("study-session-participant-salt-invalid", rejection);
    }

    [Fact]
    public void TryReadSession_HonoursTheExportersOwnIneligibilityVerdict()
    {
        var json = ExportJson().Replace(
            "\"missingFields\": []",
            "\"missingFields\": [], \"eligibleForAggregation\": false",
            StringComparison.Ordinal);

        var session = MauiAuthoringStudyProtocol.TryReadSession(json, out var rejection);

        Assert.Null(session);
        Assert.Equal("study-session-marked-ineligible", rejection);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1e999")]
    public void TryReadSession_WithAnImplausibleDuration_IsRejected(string value)
    {
        var json = ExportJson().Replace("\"timeToGoalMs\": 42000", $"\"timeToGoalMs\": {value}", StringComparison.Ordinal);

        var session = MauiAuthoringStudyProtocol.TryReadSession(json, out var rejection);

        Assert.Null(session);
        Assert.Equal("study-session-duration-implausible", rejection);
    }

    [Fact]
    public void Aggregate_WithAParticipantInBothArms_RefusesToCompare()
    {
        // A participant who has already done the task in one arm carries that knowledge into the
        // other, so the difference would measure learning rather than tooling.
        var sessions = new List<MauiAuthoringStudySession>();
        for (var index = 0; index < 5; index++)
        {
            sessions.Add(Session($"a{index}", $"participant-x{index:x7}", MauiAuthoringStudyProtocol.AssistedArm, 100_000));
            sessions.Add(Session($"c{index}", $"participant-x{index:x7}", MauiAuthoringStudyProtocol.ControlArm, 160_000));
        }

        var report = MauiAuthoringStudyProtocol.Aggregate(sessions);

        var task = Assert.Single(report.Tasks);
        Assert.Equal("insufficient-evidence", task.Status);
        Assert.Contains("participant-in-both-arms", task.Blockers);
        Assert.Null(task.MedianTimeToFirstResultDifferenceMs);
    }

    private static MauiAuthoringStudySession Session(string id, string participant, string arm, double timeToGoalMs)
        => new()
        {
            SessionId = id,
            ParticipantSalt = participant,
            TaskId = "task-02-form-entry-assertion",
            Arm = arm,
            TimeToGoalMs = timeToGoalMs,
            TimeToFirstResultMs = timeToGoalMs + 5_000,
            RecordingDurationMs = 20_000,
            SavedTest = true,
        };

    private static string ExportJson(string missingFields = "[]") => $$"""
    {
      "schema": "maui-devflow-prototype-study",
      "protocol": {
        "name": "maui-devflow-authoring-time",
        "protocolVersion": 1,
        "taskId": "task-02-form-entry-assertion",
        "arm": "assisted",
        "participantSalt": "participant-abcdef01"
      },
      "session": { "id": "local-abc123" },
      "summary": {
        "timeToGoalMs": 42000,
        "timeToFirstResultMs": 68000,
        "recordingDurationMs": 21000,
        "missingFields": {{missingFields}}
      }
    }
    """;

    private static string RemoveProtocolField(JsonElement root, string field)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name != "protocol")
                {
                    property.WriteTo(writer);
                    continue;
                }
                writer.WriteStartObject("protocol");
                foreach (var inner in property.Value.EnumerateObject())
                {
                    if (inner.Name != field) inner.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
