using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// CLI adapter that turns downloaded Test Workbench study exports into an aggregated
/// authoring-time report. Read-only over the inputs; writes only explicit output paths.
/// </summary>
internal static class FlowStudyCommands
{
    private const int MaxStudySessionFileBytes = 4_194_304;

    internal static Command Create(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output,
        Action markError)
    {
        var command = new Command(
            "study",
            "Aggregate exported Test Workbench authoring-time study sessions into a two-arm report.");
        var sessionsOption = new Option<string[]>("--session")
        {
            Description = "One or more exported study session JSON files. Repeat or pass several paths.",
            AllowMultipleArgumentsPerToken = true,
        };
        var sessionDirOption = new Option<string?>("--session-dir")
        {
            Description = "Directory of exported study session JSON files. Read non-recursively.",
        };
        var studyOutOption = new Option<string?>("--study-out")
        {
            Description = "Path to write the aggregated authoring-time report. The command writes only this explicit path.",
        };
        var failOnInsufficientOption = new Option<bool>("--fail-on-insufficient")
        {
            Description = "Return a nonzero exit code when no task has both arms at the required session and participant counts.",
        };
        command.Add(sessionsOption);
        command.Add(sessionDirOption);
        command.Add(studyOutOption);
        command.Add(failOnInsufficientOption);
        command.SetAction(async (ctx, ct) =>
        {
            var json = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var files = new List<string>(ctx.GetValue(sessionsOption) ?? []);
            var directory = ctx.GetValue(sessionDirOption);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                if (!Directory.Exists(directory))
                {
                    markError();
                    output.WriteError($"Study session directory not found: {directory}", json, "StudySessionDirMissing");
                    return 1;
                }
                files.AddRange(Directory.GetFiles(directory, "*.json").OrderBy(static path => path, StringComparer.Ordinal));
            }

            var sessions = new List<MauiAuthoringStudySession>();
            var rejected = new List<StudyRejection>();
            foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string content;
                try
                {
                    // Bounded like every other evidence reader here: an unbounded export would
                    // otherwise let one file exhaust memory before it is even validated.
                    var info = new FileInfo(file);
                    if (info.Length > MaxStudySessionFileBytes)
                    {
                        rejected.Add(new StudyRejection(file, "study-session-file-too-large"));
                        continue;
                    }
                    content = await File.ReadAllTextAsync(file, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    rejected.Add(new StudyRejection(file, "study-session-unreadable"));
                    continue;
                }
                var session = MauiAuthoringStudyProtocol.TryReadSession(content, out var rejection);
                if (session is null)
                {
                    rejected.Add(new StudyRejection(file, rejection ?? "study-session-rejected"));
                    continue;
                }
                sessions.Add(session);
            }

            // Two exports of the same browser session are one measurement.
            var deduped = sessions
                .GroupBy(static session => session.SessionId, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToList();
            var duplicates = sessions.Count - deduped.Count;

            var report = MauiAuthoringStudyProtocol.Aggregate(deduped);
            var studyOut = ctx.GetValue(studyOutOption);
            string? writtenTo = null;
            if (!string.IsNullOrWhiteSpace(studyOut))
            {
                try
                {
                    var full = Path.GetFullPath(studyOut);
                    var parent = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    await File.WriteAllTextAsync(
                        full,
                        JsonSerializer.Serialize(report, MauiTestingJsonContext.Default.MauiAuthoringStudyReport),
                        ct);
                    writtenTo = full;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    markError();
                    output.WriteError("Study report could not be written to the explicit output path.", json, "StudyReportWriteError");
                    return 1;
                }
            }

            // `ok` must mean the same thing as exit 0, or a JSON consumer and a shell consumer of
            // the same invocation disagree about whether the run succeeded.
            var insufficient = ctx.GetValue(failOnInsufficientOption) && report.Status != "comparable";
            var ok = rejected.Count == 0 && !insufficient;

            if (json)
            {
                output.WriteResult(new StudyCommandResult(
                    ok,
                    report,
                    files.Count,
                    deduped.Count,
                    duplicates,
                    rejected.Select(static item => new StudyRejectionResult(Path.GetFileName(item.File), item.Reason)).ToArray(),
                    writtenTo), json: true);
            }
            else
            {
                Console.WriteLine($"Authoring-time study: {report.Status}");
                Console.WriteLine($"  sessions accepted     : {deduped.Count} (assisted {report.AssistedSessions}, unassisted-control {report.ControlSessions})");
                if (duplicates > 0) Console.WriteLine($"  duplicate exports     : {duplicates} (counted once)");
                if (rejected.Count > 0)
                {
                    Console.WriteLine($"  rejected exports      : {rejected.Count}");
                    foreach (var item in rejected.Take(10))
                        Console.WriteLine($"    - {Path.GetFileName(item.File)}: {item.Reason}");
                }
                foreach (var task in report.Tasks)
                {
                    Console.WriteLine($"  {task.TaskId}: {task.Status}");
                    Console.WriteLine($"    assisted           : n={task.Assisted.Sessions} participants={task.Assisted.Participants} medianTimeToFirstResultMs={Format(task.Assisted.MedianTimeToFirstResultMs)} medianTimeToGoalMs={Format(task.Assisted.MedianTimeToGoalMs)}");
                    Console.WriteLine($"    unassisted-control : n={task.Control.Sessions} participants={task.Control.Participants} medianTimeToFirstResultMs={Format(task.Control.MedianTimeToFirstResultMs)} medianTimeToGoalMs={Format(task.Control.MedianTimeToGoalMs)}");
                    if (task.MedianTimeToFirstResultDifferenceMs is { } primary)
                        Console.WriteLine($"    time-to-first-result difference : {primary:0} ms (assisted minus control, primary comparison)");
                    if (task.MedianTimeToGoalDifferenceMs is { } secondary)
                        Console.WriteLine($"    time-to-goal difference         : {secondary:0} ms (includes participant reading time)");
                    if (task.Blockers.Count > 0)
                        Console.WriteLine($"    blockers           : {string.Join(", ", task.Blockers)}");
                }
                Console.WriteLine($"  {report.Statement}");
                if (writtenTo is not null) Console.WriteLine($"  report written to     : {writtenTo}");
            }

            if (insufficient)
            {
                markError();
                return 1;
            }
            // A run that silently discarded exports is not a clean run. The report still stands on
            // what survived and is still written, but the exit code says plainly that not every
            // session was counted — an aggregation job must not report success on partial data.
            if (rejected.Count > 0)
            {
                markError();
                return 1;
            }
            return 0;
        });
        return command;
    }

    private static string Format(double? value) => value is { } number ? number.ToString("0") : "not-recorded";

    private readonly record struct StudyRejection(string File, string Reason);
}

internal sealed record StudyRejectionResult(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("reason")] string Reason);

internal sealed record StudyCommandResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("report")] MauiAuthoringStudyReport Report,
    [property: JsonPropertyName("consideredFiles")] int ConsideredFiles,
    [property: JsonPropertyName("acceptedSessions")] int AcceptedSessions,
    [property: JsonPropertyName("duplicateSessions")] int DuplicateSessions,
    [property: JsonPropertyName("rejectedSessions")] StudyRejectionResult[] RejectedSessions,
    [property: JsonPropertyName("writtenTo")] string? WrittenTo);
