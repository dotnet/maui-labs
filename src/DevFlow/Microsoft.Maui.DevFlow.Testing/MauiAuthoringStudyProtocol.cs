using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Aggregates exported Test Workbench study sessions into an authoring-time result.
/// </summary>
/// <remarks>
/// The aggregator exists to stop a number being published that cannot mean anything. Authoring
/// time measured only while using the tool is a description of the tool's own session, not
/// evidence that the tool made authoring faster. The comparison requires an unassisted control
/// arm performing the same fixed task. Until both arms carry enough sessions this type reports
/// <c>insufficient-evidence</c> and emits no headline duration.
/// </remarks>
public static class MauiAuthoringStudyProtocol
{
    public const string ProtocolName = "maui-devflow-authoring-time";
    public const int ProtocolVersion = 1;
    public const string AssistedArm = "assisted";
    public const string ControlArm = "unassisted-control";

    /// <summary>
    /// The salt shape documented in docs/DevFlow/authoring-time-protocol.md. Anchored with
    /// <c>\A</c>/<c>\z</c> rather than <c>^</c>/<c>$</c>: in .NET <c>$</c> also matches before a
    /// trailing newline, so a salt round-tripped through a text file would validate yet compare
    /// ordinally unequal to the same salt without it — counting one person as two participants and
    /// silently defeating the cross-arm blocker.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex ParticipantSaltPattern =
        new(@"\Aparticipant-[0-9a-f]{8,64}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Minimum sessions per arm, per task, before a comparison is reported.</summary>
    public const int MinimumSessionsPerArm = 5;

    /// <summary>Minimum distinct participants per arm, per task, before a comparison is reported.</summary>
    public const int MinimumParticipantsPerArm = 3;

    public static readonly IReadOnlyList<string> TaskIds =
    [
        "task-01-first-run-smoke",
        "task-02-form-entry-assertion",
        "task-03-navigation-round-trip",
        "task-04-list-scroll-select",
        "task-05-repair-a-broken-selector",
    ];

    private static readonly HashSet<string> KnownTasks = new(TaskIds, StringComparer.Ordinal);
    private static readonly HashSet<string> KnownArms = new([AssistedArm, ControlArm], StringComparer.Ordinal);

    /// <summary>
    /// Reads one exported session envelope. Returns <see langword="null"/> with a reason when the
    /// export cannot be used as protocol evidence; callers must not substitute a default.
    /// </summary>
    public static MauiAuthoringStudySession? TryReadSession(string json, out string? rejection)
    {
        rejection = null;
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            rejection = "study-session-not-json";
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            rejection = "study-session-not-an-object";
            return null;
        }
        if (!root.TryGetProperty("protocol", out var protocol) || protocol.ValueKind != JsonValueKind.Object)
        {
            rejection = "study-session-missing-protocol";
            return null;
        }
        if (Text(protocol, "name") != ProtocolName)
        {
            rejection = "study-session-protocol-mismatch";
            return null;
        }
        if (!protocol.TryGetProperty("protocolVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number ||
            version.GetInt32() != ProtocolVersion)
        {
            rejection = "study-session-protocol-version-mismatch";
            return null;
        }

        var arm = Text(protocol, "arm");
        if (arm is null || !KnownArms.Contains(arm))
        {
            rejection = "study-session-arm-unassigned";
            return null;
        }
        var taskId = Text(protocol, "taskId");
        if (taskId is null || !KnownTasks.Contains(taskId))
        {
            rejection = "study-session-task-unassigned";
            return null;
        }
        var participantSalt = Text(protocol, "participantSalt");
        if (string.IsNullOrWhiteSpace(participantSalt))
        {
            rejection = "study-session-participant-unlinkable";
            return null;
        }
        // The control arm has no capture path, so control exports are hand-authored and the
        // browser-side format check binds nothing for exactly the arm that decides the result.
        // Two cosmetically different salts for one person inflate the participant count and defeat
        // the cross-arm blocker, so enforce the documented shape here too.
        if (!ParticipantSaltPattern.IsMatch(participantSalt))
        {
            rejection = "study-session-participant-salt-invalid";
            return null;
        }
        var sessionId = root.TryGetProperty("session", out var session) && session.ValueKind == JsonValueKind.Object
            ? Text(session, "id")
            : null;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            rejection = "study-session-missing-session-id";
            return null;
        }
        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
        {
            rejection = "study-session-missing-summary";
            return null;
        }
        if (Strings(summary, "missingFields").Contains("completeEventHistory"))
        {
            rejection = "study-session-event-history-truncated";
            return null;
        }
        // The exporter is the only thing that saw the raw event stream. When it says a session is
        // not fit to aggregate, that judgement is authoritative here too. It writes the verdict in
        // the protocol block; summary is checked as well so a hand-assembled export cannot evade
        // the check by moving the flag.
        if (IsMarkedIneligible(protocol) || IsMarkedIneligible(summary))
        {
            rejection = "study-session-marked-ineligible";
            return null;
        }

        var timeToGoal = Number(summary, "timeToGoalMs");
        var timeToFirstResult = Number(summary, "timeToFirstResultMs");
        var recordingDuration = Number(summary, "recordingDurationMs");
        var reviewToSave = Number(summary, "reviewToSaveDurationMs");
        // A negative or non-finite duration is a broken clock, not a fast participant. Silently
        // averaging one in would drag a median toward whatever the defect happened to produce.
        if (!IsUsableDuration(timeToGoal) || !IsUsableDuration(timeToFirstResult) ||
            !IsUsableDuration(recordingDuration) || !IsUsableDuration(reviewToSave))
        {
            rejection = "study-session-duration-implausible";
            return null;
        }

        return new MauiAuthoringStudySession
        {
            SessionId = sessionId!,
            ParticipantSalt = participantSalt!,
            TaskId = taskId,
            Arm = arm,
            TimeToGoalMs = timeToGoal,
            TimeToFirstResultMs = timeToFirstResult,
            RecordingDurationMs = recordingDuration,
            ReviewToSaveDurationMs = reviewToSave,
            SavedTest = !Strings(summary, "missingFields").Contains("savedTestMetrics"),
        };
    }

    private static bool IsMarkedIneligible(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty("eligibleForAggregation", out var eligible) &&
        eligible.ValueKind == JsonValueKind.False;

    private static bool IsUsableDuration(double? value) =>
        value is null || (double.IsFinite(value.Value) && value.Value >= 0);

    /// <summary>Aggregates sessions per task and refuses to report without both arms.</summary>
    public static MauiAuthoringStudyReport Aggregate(IEnumerable<MauiAuthoringStudySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var materialized = sessions.Where(static session => session is not null).ToList();
        var report = new MauiAuthoringStudyReport
        {
            Protocol = ProtocolName,
            ProtocolVersion = ProtocolVersion,
            TotalSessions = materialized.Count,
            AssistedSessions = materialized.Count(static session => session.Arm == AssistedArm),
            ControlSessions = materialized.Count(static session => session.Arm == ControlArm),
        };

        foreach (var taskId in TaskIds)
        {
            var forTask = materialized.Where(session => session.TaskId == taskId).ToList();
            if (forTask.Count == 0) continue;
            report.Tasks.Add(BuildTask(taskId, forTask));
        }

        report.ComparableTasks = report.Tasks.Count(static task => task.Status == "comparable");
        report.Status = report.ComparableTasks > 0 ? "comparable" : "insufficient-evidence";
        report.Statement = report.ControlSessions == 0
            ? "No unassisted-control sessions were supplied. Assisted-arm durations describe how long " +
              "sessions took while using DevFlow; they are not evidence that DevFlow reduced authoring time."
            : report.ComparableTasks > 0
                ? "Per-task comparisons below are descriptive differences between arms. They are not " +
                  "significance-tested and the sample is small."
                : "Both arms are present but no task yet meets the minimum session and participant counts, " +
                  "so no authoring-time comparison is reported.";
        return report;
    }

    private static MauiAuthoringStudyTaskResult BuildTask(string taskId, List<MauiAuthoringStudySession> sessions)
    {
        var assisted = BuildArm(AssistedArm, sessions.Where(static session => session.Arm == AssistedArm));
        var control = BuildArm(ControlArm, sessions.Where(static session => session.Arm == ControlArm));
        var blockers = new List<string>();
        if (assisted.Sessions < MinimumSessionsPerArm) blockers.Add("assisted-session-count-insufficient");
        if (control.Sessions < MinimumSessionsPerArm) blockers.Add("control-session-count-insufficient");
        if (assisted.Participants < MinimumParticipantsPerArm) blockers.Add("assisted-participant-count-insufficient");
        if (control.Participants < MinimumParticipantsPerArm) blockers.Add("control-participant-count-insufficient");
        // The protocol forbids one participant appearing in both arms for the same task: they
        // would carry knowledge of the task across, and the difference would measure learning.
        var crossArm = sessions.Where(static session => session.Arm == AssistedArm)
            .Select(static session => session.ParticipantSalt)
            .Intersect(
                sessions.Where(static session => session.Arm == ControlArm).Select(static session => session.ParticipantSalt),
                StringComparer.Ordinal)
            .Any();
        if (crossArm) blockers.Add("participant-in-both-arms");
        // Time to first result is the primary comparison: it is the interval the tool actually
        // acts on. Time to goal includes the participant reading the result, which no tooling
        // change can shorten.
        if (assisted.MedianTimeToFirstResultMs is null) blockers.Add("assisted-time-to-first-result-missing");
        if (control.MedianTimeToFirstResultMs is null) blockers.Add("control-time-to-first-result-missing");
        // A five-session arm whose median rests on one observation is not five observations. The
        // primary endpoint has to clear the session minimum on its own, not on the arm's behalf.
        if (assisted.TimeToFirstResultSampleCount < MinimumSessionsPerArm)
            blockers.Add("assisted-time-to-first-result-sample-insufficient");
        if (control.TimeToFirstResultSampleCount < MinimumSessionsPerArm)
            blockers.Add("control-time-to-first-result-sample-insufficient");

        var result = new MauiAuthoringStudyTaskResult
        {
            TaskId = taskId,
            Assisted = assisted,
            Control = control,
            Status = blockers.Count == 0 ? "comparable" : "insufficient-evidence",
            Blockers = blockers,
        };
        if (blockers.Count == 0)
        {
            result.MedianTimeToFirstResultDifferenceMs =
                assisted.MedianTimeToFirstResultMs - control.MedianTimeToFirstResultMs;
            result.MedianTimeToGoalDifferenceMs = assisted.MedianTimeToGoalMs - control.MedianTimeToGoalMs;
        }
        return result;
    }

    private static MauiAuthoringStudyArmSummary BuildArm(string arm, IEnumerable<MauiAuthoringStudySession> sessions)
    {
        var materialized = sessions.ToList();
        return new MauiAuthoringStudyArmSummary
        {
            Arm = arm,
            Sessions = materialized.Count,
            Participants = materialized.Select(static session => session.ParticipantSalt)
                .Distinct(StringComparer.Ordinal).Count(),
            CompletedTasks = materialized.Count(static session => session.SavedTest),
            MedianTimeToGoalMs = Median(materialized.Select(static session => session.TimeToGoalMs)),
            MedianTimeToFirstResultMs = Median(materialized.Select(static session => session.TimeToFirstResultMs)),
            MedianRecordingDurationMs = Median(materialized.Select(static session => session.RecordingDurationMs)),
            TimeToFirstResultSampleCount = materialized.Count(static session => session.TimeToFirstResultMs.HasValue),
            TimeToGoalSampleCount = materialized.Count(static session => session.TimeToGoalMs.HasValue),
        };
    }

    private static double? Median(IEnumerable<double?> values)
    {
        var ordered = values.Where(static value => value.HasValue).Select(static value => value!.Value).Order().ToList();
        if (ordered.Count == 0) return null;
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2d;
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            // Trimmed before any comparison: participant salts are compared ordinally, so
            // whitespace picked up from a file round-trip would split one person into two.
            ? value.GetString()?.Trim()
            : null;

    private static double? Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static HashSet<string> Strings(JsonElement element, string name)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text) result.Add(text);
        }
        return result;
    }
}

/// <summary>One accepted study session, reduced to protocol fields and durations.</summary>
public sealed class MauiAuthoringStudySession
{
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = string.Empty;
    [JsonPropertyName("participantSalt")] public string ParticipantSalt { get; set; } = string.Empty;
    [JsonPropertyName("taskId")] public string TaskId { get; set; } = string.Empty;
    [JsonPropertyName("arm")] public string Arm { get; set; } = string.Empty;
    [JsonPropertyName("timeToGoalMs")] public double? TimeToGoalMs { get; set; }
    [JsonPropertyName("timeToFirstResultMs")] public double? TimeToFirstResultMs { get; set; }
    [JsonPropertyName("recordingDurationMs")] public double? RecordingDurationMs { get; set; }
    [JsonPropertyName("reviewToSaveDurationMs")] public double? ReviewToSaveDurationMs { get; set; }
    [JsonPropertyName("savedTest")] public bool SavedTest { get; set; }
}

/// <summary>Per-arm descriptive summary for a single task.</summary>
public sealed class MauiAuthoringStudyArmSummary
{
    [JsonPropertyName("arm")] public string Arm { get; set; } = string.Empty;
    [JsonPropertyName("sessions")] public int Sessions { get; set; }
    [JsonPropertyName("participants")] public int Participants { get; set; }
    [JsonPropertyName("completedTasks")] public int CompletedTasks { get; set; }
    [JsonPropertyName("medianTimeToGoalMs")] public double? MedianTimeToGoalMs { get; set; }
    [JsonPropertyName("medianTimeToFirstResultMs")] public double? MedianTimeToFirstResultMs { get; set; }
    [JsonPropertyName("medianRecordingDurationMs")] public double? MedianRecordingDurationMs { get; set; }

    /// <summary>
    /// How many sessions actually carried the primary endpoint. This is not always
    /// <see cref="Sessions"/>: a session missing <c>timeToFirstResultMs</c> still counts toward the
    /// arm's session and participant minimums but contributes nothing to the median, so without
    /// this a five-session arm could publish a median resting on one observation.
    /// </summary>
    [JsonPropertyName("timeToFirstResultSampleCount")] public int TimeToFirstResultSampleCount { get; set; }

    /// <summary>How many sessions carried <c>timeToGoalMs</c>.</summary>
    [JsonPropertyName("timeToGoalSampleCount")] public int TimeToGoalSampleCount { get; set; }
}

/// <summary>Result for one fixed task across both arms.</summary>
public sealed class MauiAuthoringStudyTaskResult
{
    [JsonPropertyName("taskId")] public string TaskId { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = "insufficient-evidence";
    [JsonPropertyName("assisted")] public MauiAuthoringStudyArmSummary Assisted { get; set; } = new();
    [JsonPropertyName("control")] public MauiAuthoringStudyArmSummary Control { get; set; } = new();
    [JsonPropertyName("medianTimeToFirstResultDifferenceMs")] public double? MedianTimeToFirstResultDifferenceMs { get; set; }
    [JsonPropertyName("medianTimeToGoalDifferenceMs")] public double? MedianTimeToGoalDifferenceMs { get; set; }
    [JsonPropertyName("blockers")] public List<string> Blockers { get; set; } = [];
}

/// <summary>Aggregated authoring-time evidence across all supplied study sessions.</summary>
public sealed class MauiAuthoringStudyReport
{
    [JsonPropertyName("protocol")] public string Protocol { get; set; } = MauiAuthoringStudyProtocol.ProtocolName;
    [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; } = MauiAuthoringStudyProtocol.ProtocolVersion;
    [JsonPropertyName("status")] public string Status { get; set; } = "insufficient-evidence";
    [JsonPropertyName("totalSessions")] public int TotalSessions { get; set; }
    [JsonPropertyName("assistedSessions")] public int AssistedSessions { get; set; }
    [JsonPropertyName("controlSessions")] public int ControlSessions { get; set; }
    [JsonPropertyName("comparableTasks")] public int ComparableTasks { get; set; }
    [JsonPropertyName("statement")] public string Statement { get; set; } = string.Empty;
    [JsonPropertyName("tasks")] public List<MauiAuthoringStudyTaskResult> Tasks { get; set; } = [];
}
