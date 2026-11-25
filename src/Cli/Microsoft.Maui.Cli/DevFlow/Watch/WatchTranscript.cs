using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.Cli.DevFlow.Watch;

/// <summary>
/// Event kinds derived from <c>dotnet watch</c> output. Kinds in <see cref="OutcomeKinds"/>
/// end an edit: an agent waiting after saving a file stops at the first one.
/// </summary>
internal static class WatchEventKinds
{
    public const string Loading = "loading";
    public const string Launched = "launched";
    public const string Ready = "ready";
    public const string FileChanged = "file-changed";
    public const string Applied = "applied";
    public const string NoChanges = "no-changes";
    public const string CompileError = "compile-error";
    public const string RestartRequired = "restart-required";
    public const string Restarting = "restarting";
    public const string UpdateFailed = "update-failed";
    public const string Suspended = "suspended";
    public const string ProcessExited = "process-exited";
    public const string WaitingForRestart = "waiting-for-restart";
    public const string Failed = "failed";
    public const string Shutdown = "shutdown";
    public const string WatchExited = "watch-exited";

    public static readonly IReadOnlySet<string> OutcomeKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        Applied, NoChanges, CompileError, RestartRequired, Restarting, UpdateFailed,
        Suspended, ProcessExited, WaitingForRestart, Failed, WatchExited,
    };
}

public sealed class WatchEvent
{
    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("project")] public string? Project { get; set; }
    [JsonPropertyName("elapsedMs")] public int? ElapsedMs { get; set; }
    [JsonPropertyName("files")] public string[]? Files { get; set; }
    [JsonPropertyName("diagnostics")] public string[]? Diagnostics { get; set; }
}

/// <summary>
/// Classifies <c>dotnet watch</c> transcript lines into <see cref="WatchEvent"/>s.
/// Matching uses the message text after the <c>dotnet watch &lt;emoji|:&gt;</c> prefix, so it works
/// for terminal (emoji) and redirected (<c>:</c>) output. Unrecognized lines are ignored and
/// never count as success.
/// </summary>
internal static partial class WatchTranscriptParser
{
    const int MaxDiagnostics = 20;

    [GeneratedRegex(@"^dotnet watch\s+\S+\s(?<msg>.*)$")]
    private static partial Regex WatchLine();

    [GeneratedRegex(@"^\[(?<project>[^\]]+)\]\s+(?<msg>.*)$")]
    private static partial Regex ProjectPrefix();

    [GeneratedRegex(@":\s+error\s+[A-Za-z]+\d+\s*:")]
    private static partial Regex DiagnosticLine();

    [GeneratedRegex(@"applied in (?<ms>\d+)\s*ms")]
    private static partial Regex AppliedIn();

    public static List<WatchEvent> Parse(IEnumerable<string> lines)
    {
        var events = new List<WatchEvent>();
        var diagnostics = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var match = WatchLine().Match(line);
            if (!match.Success)
            {
                if (DiagnosticLine().IsMatch(line))
                    AddDiagnostic(events, diagnostics, line.Trim());
                continue;
            }

            var message = match.Groups["msg"].Value.Trim();
            string? project = null;
            var projectMatch = ProjectPrefix().Match(message);
            if (projectMatch.Success)
            {
                project = projectMatch.Groups["project"].Value;
                message = projectMatch.Groups["msg"].Value.Trim();
            }

            if (DiagnosticLine().IsMatch(message))
            {
                AddDiagnostic(events, diagnostics, message);
                continue;
            }

            var evt = Classify(message, project);
            if (evt is null)
                continue;

            switch (evt.Kind)
            {
                case WatchEventKinds.FileChanged:
                case WatchEventKinds.Applied:
                    diagnostics.Clear();
                    break;
                case WatchEventKinds.CompileError:
                case WatchEventKinds.Failed:
                case WatchEventKinds.WaitingForRestart:
                    if (diagnostics.Count > 0)
                    {
                        evt.Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray();
                        diagnostics.Clear();
                    }
                    break;
            }

            evt.Seq = events.Count + 1;
            events.Add(evt);
        }

        return events;
    }

    /// <summary>
    /// dotnet watch prints build errors before a failure summary but Hot Reload compile errors
    /// after it, so a diagnostic joins the latest event when that event is a failure; otherwise
    /// it is held for the next failure event.
    /// </summary>
    static void AddDiagnostic(List<WatchEvent> events, List<string> pending, string line)
    {
        if (events.Count > 0 && events[^1].Kind is WatchEventKinds.CompileError or WatchEventKinds.Failed or WatchEventKinds.WaitingForRestart)
        {
            var current = events[^1].Diagnostics ?? [];
            if (current.Length < MaxDiagnostics && !current.Contains(line, StringComparer.Ordinal))
                events[^1].Diagnostics = [.. current, line];
            return;
        }
        if (pending.Count < MaxDiagnostics)
            pending.Add(line);
    }

    static WatchEvent? Classify(string message, string? project)
    {
        WatchEvent Make(string kind) => new() { Kind = kind, Message = message, Project = project };

        // Project-scoped lines: only app lifecycle matters; handler and transport chatter is ignored.
        if (project is not null)
        {
            if (message.StartsWith("Launched '", StringComparison.Ordinal))
                return Make(WatchEventKinds.Launched);
            if (message == "Exited" || message.StartsWith("Exited with error code", StringComparison.Ordinal))
                return Make(WatchEventKinds.ProcessExited);
            if (message.StartsWith("Update batch #", StringComparison.Ordinal) && message.EndsWith("failed", StringComparison.Ordinal))
                return Make(WatchEventKinds.UpdateFailed);
            if (message.StartsWith("Application failed", StringComparison.Ordinal))
                return Make(WatchEventKinds.Failed);
            return null;
        }

        if (message.StartsWith("Loading projects", StringComparison.Ordinal))
            return Make(WatchEventKinds.Loading);
        if (message.StartsWith("Waiting for changes", StringComparison.Ordinal))
            return Make(WatchEventKinds.Ready);
        if (message.StartsWith("File updated: ", StringComparison.Ordinal) || message.StartsWith("Files updated: ", StringComparison.Ordinal))
        {
            var evt = Make(WatchEventKinds.FileChanged);
            evt.Files = message[(message.IndexOf(':') + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return evt;
        }
        if (message.Contains("changes applied", StringComparison.Ordinal))
        {
            var evt = Make(WatchEventKinds.Applied);
            var ms = AppliedIn().Match(message);
            if (ms.Success && int.TryParse(ms.Groups["ms"].Value, out var elapsed))
                evt.ElapsedMs = elapsed;
            return evt;
        }
        if (message.StartsWith("No managed code changes to apply", StringComparison.Ordinal)
            || message.StartsWith("No C# changes to apply", StringComparison.Ordinal))
            return Make(WatchEventKinds.NoChanges);
        if (message.StartsWith("Unable to apply changes due to compilation errors", StringComparison.Ordinal)
            || message.StartsWith("Build failed", StringComparison.Ordinal))
            return Make(WatchEventKinds.CompileError);
        if (message.StartsWith("Restart is needed", StringComparison.Ordinal)
            || message.StartsWith("Unable to apply hot reload", StringComparison.Ordinal))
            return Make(WatchEventKinds.RestartRequired);
        if (message.StartsWith("Restarting", StringComparison.Ordinal)
            || message.StartsWith("Relaunching", StringComparison.Ordinal))
            return Make(WatchEventKinds.Restarting);
        if (message.StartsWith("Waiting for a file to change before restarting", StringComparison.Ordinal))
            return Make(WatchEventKinds.WaitingForRestart);
        if (message.StartsWith("Hot reload suspended", StringComparison.Ordinal))
            return Make(WatchEventKinds.Suspended);
        if (message.StartsWith("Update batch #", StringComparison.Ordinal) && message.EndsWith("failed", StringComparison.Ordinal))
            return Make(WatchEventKinds.UpdateFailed);
        if (message.StartsWith("Exited with error code", StringComparison.Ordinal))
            return Make(WatchEventKinds.ProcessExited);
        if (message.StartsWith("Failed to launch", StringComparison.Ordinal)
            || message.StartsWith("No devices are available", StringComparison.Ordinal)
            || message.StartsWith("Project does not support Hot Reload", StringComparison.Ordinal)
            || message.StartsWith("Application failed", StringComparison.Ordinal)
            || message.StartsWith("Process crashed", StringComparison.Ordinal))
            return Make(WatchEventKinds.Failed);
        if (message.StartsWith("Shutdown requested", StringComparison.Ordinal))
            return Make(WatchEventKinds.Shutdown);

        return null;
    }

    /// <summary>
    /// Summarizes what the watcher is doing right now, from its latest meaningful event.
    /// </summary>
    public static string DeriveState(IReadOnlyList<WatchEvent> events, bool watchRunning)
    {
        if (!watchRunning)
            return "stopped";

        for (var i = events.Count - 1; i >= 0; i--)
        {
            switch (events[i].Kind)
            {
                case WatchEventKinds.Ready:
                case WatchEventKinds.Applied:
                case WatchEventKinds.NoChanges:
                    return "ready";
                case WatchEventKinds.FileChanged:
                    return "applying";
                case WatchEventKinds.CompileError:
                    return "compile-error";
                case WatchEventKinds.RestartRequired:
                case WatchEventKinds.WaitingForRestart:
                case WatchEventKinds.ProcessExited:
                    return "app-exited";
                case WatchEventKinds.Restarting:
                case WatchEventKinds.Loading:
                case WatchEventKinds.Launched:
                    return "starting";
                case WatchEventKinds.Suspended:
                    return "suspended";
                case WatchEventKinds.Failed:
                case WatchEventKinds.UpdateFailed:
                    return "failed";
            }
        }

        return "starting";
    }
}
