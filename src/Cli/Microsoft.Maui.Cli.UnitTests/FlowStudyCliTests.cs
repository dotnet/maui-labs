using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class FlowStudyCliTests : IDisposable
{
    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory,
        "flow-study-cli-tests",
        Guid.NewGuid().ToString("N"));

    public FlowStudyCliTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task FlowStudy_WithNoSessions_ReportsInsufficientEvidenceRatherThanZero()
    {
        var cli = new CliTestHarness(mockAgentPort: 1);

        var result = await cli.InvokeRawAsync("devflow", "flow", "study", "--json");

        Assert.Equal(0, result.ExitCode);
        var report = result.ParseJsonOutput().GetProperty("report");
        Assert.Equal("insufficient-evidence", report.GetProperty("status").GetString());
        Assert.Equal(0, report.GetProperty("totalSessions").GetInt32());
        Assert.Empty(report.GetProperty("tasks").EnumerateArray());
        Assert.Contains(
            "not evidence that DevFlow reduced authoring time",
            report.GetProperty("statement").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlowStudy_WithAssistedArmOnly_RefusesToReportAndFailsClosedOnRequest()
    {
        var sessions = Path.Combine(_root, "assisted-only");
        Directory.CreateDirectory(sessions);
        for (var index = 0; index < 6; index++)
            await WriteSessionAsync(sessions, $"a{index}", $"participant-a{index:x7}", "assisted", 100_000 + index);
        var cli = new CliTestHarness(mockAgentPort: 1);

        var result = await cli.InvokeRawAsync(
            "devflow", "flow", "study", "--session-dir", sessions, "--fail-on-insufficient", "--json");

        Assert.Equal(1, result.ExitCode);
        var root = result.ParseJsonOutput();
        // `ok` has to agree with the exit code, or a JSON consumer reads success from an
        // invocation a shell consumer reads as failure.
        Assert.False(root.GetProperty("ok").GetBoolean());
        var report = root.GetProperty("report");
        Assert.Equal("insufficient-evidence", report.GetProperty("status").GetString());
        Assert.Equal(6, report.GetProperty("assistedSessions").GetInt32());
        Assert.Equal(0, report.GetProperty("controlSessions").GetInt32());
        var task = Assert.Single(report.GetProperty("tasks").EnumerateArray().ToList());

        // The assisted median exists but no difference is published without the control arm.
        Assert.True(task.GetProperty("assisted").GetProperty("medianTimeToGoalMs").GetDouble() > 0);
        Assert.False(task.TryGetProperty("medianTimeToFirstResultDifferenceMs", out _));
        Assert.False(task.TryGetProperty("medianTimeToGoalDifferenceMs", out _));
        Assert.False(task.GetProperty("control").TryGetProperty("medianTimeToGoalMs", out _));
        var blockers = task.GetProperty("blockers").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("control-session-count-insufficient", blockers);
    }

    [Fact]
    public async Task FlowStudy_WithBothArms_WritesAReportAndRejectsUnusableExports()
    {
        var sessions = Path.Combine(_root, "both-arms");
        Directory.CreateDirectory(sessions);
        for (var index = 0; index < 5; index++)
        {
            await WriteSessionAsync(sessions, $"a{index}", $"participant-a{index:x7}", "assisted", 100_000);
            await WriteSessionAsync(sessions, $"c{index}", $"participant-c{index:x7}", "unassisted-control", 160_000);
        }
        // An export whose event history was truncated is rejected, not silently pooled.
        await WriteSessionAsync(sessions, "truncated", "participant-dddddddd", "assisted", 1_000, truncated: true);
        var studyOut = Path.Combine(_root, "study-report.json");
        var cli = new CliTestHarness(mockAgentPort: 1);

        var result = await cli.InvokeRawAsync(
            "devflow", "flow", "study", "--session-dir", sessions, "--study-out", studyOut, "--json");

        // A rejected export is a nonzero exit: an aggregation job that silently drops sessions and
        // still reports success is how a study number stops meaning anything. The report is still
        // written so the operator can see exactly what was dropped.
        Assert.Equal(1, result.ExitCode);
        Assert.True(File.Exists(studyOut));
        var root = result.ParseJsonOutput();
        Assert.Equal(10, root.GetProperty("acceptedSessions").GetInt32());
        var rejected = root.GetProperty("rejectedSessions").EnumerateArray().ToList();
        Assert.Single(rejected);
        Assert.Equal("study-session-event-history-truncated", rejected[0].GetProperty("reason").GetString());

        var report = root.GetProperty("report");
        Assert.Equal("comparable", report.GetProperty("status").GetString());
        var task = Assert.Single(report.GetProperty("tasks").EnumerateArray().ToList());

        // The primary endpoint is time-to-first-result; time-to-goal is reported beside it but is
        // not the number the protocol is powered for.
        Assert.Equal(-60_000d, task.GetProperty("medianTimeToFirstResultDifferenceMs").GetDouble());
        Assert.Equal(-60_000d, task.GetProperty("medianTimeToGoalDifferenceMs").GetDouble());
        Assert.False(task.TryGetProperty("medianDifferenceMs", out _));
    }

    [Fact]
    public async Task FlowStudy_WithTheSameSessionExportedTwice_CountsItOnce()
    {
        var sessions = Path.Combine(_root, "duplicates");
        Directory.CreateDirectory(sessions);
        await WriteSessionAsync(sessions, "only", "participant-aaaaaaa1", "assisted", 100_000, fileName: "first");
        await WriteSessionAsync(sessions, "only", "participant-aaaaaaa1", "assisted", 100_000, fileName: "second");
        var cli = new CliTestHarness(mockAgentPort: 1);

        var result = await cli.InvokeRawAsync(
            "devflow", "flow", "study", "--session-dir", sessions, "--json");

        var root = result.ParseJsonOutput();
        Assert.Equal(1, root.GetProperty("acceptedSessions").GetInt32());
        Assert.Equal(1, root.GetProperty("duplicateSessions").GetInt32());
        Assert.Equal(1, root.GetProperty("report").GetProperty("assistedSessions").GetInt32());
    }

    private static async Task WriteSessionAsync(
        string directory,
        string sessionId,
        string participantSalt,
        string arm,
        double timeToGoalMs,
        bool truncated = false,
        string? fileName = null)
    {
        var missingFields = truncated ? "[\"completeEventHistory\"]" : "[]";
        var json = $$"""
        {
          "schema": "maui-devflow-prototype-study",
          "protocol": {
            "name": "maui-devflow-authoring-time",
            "protocolVersion": 1,
            "taskId": "task-02-form-entry-assertion",
            "arm": "{{arm}}",
            "participantSalt": "{{participantSalt}}"
          },
          "session": { "id": "local-{{sessionId}}" },
          "summary": {
            "timeToGoalMs": {{timeToGoalMs}},
            "timeToFirstResultMs": {{timeToGoalMs + 5000}},
            "recordingDurationMs": 21000,
            "missingFields": {{missingFields}}
          }
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(directory, $"{fileName ?? sessionId}.json"), json);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
