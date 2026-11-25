using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.Cli.DevFlow.Watch;

/// <summary>Persisted identity and process ownership of one <c>dotnet watch</c> session.</summary>
public sealed class WatchSessionInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("project")] public string Project { get; set; } = "";
    [JsonPropertyName("framework")] public string Framework { get; set; } = "";
    [JsonPropertyName("device")] public string? Device { get; set; }
    [JsonPropertyName("properties")] public string[] Properties { get; set; } = [];
    [JsonPropertyName("arguments")] public string[] Arguments { get; set; } = [];
    [JsonPropertyName("hostPid")] public int HostPid { get; set; }
    [JsonPropertyName("watchPid")] public int WatchPid { get; set; }
    [JsonPropertyName("watchStartedAt")] public DateTimeOffset? WatchStartedAt { get; set; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("exitedAt")] public DateTimeOffset? ExitedAt { get; set; }
    [JsonPropertyName("exitCode")] public int? ExitCode { get; set; }
    [JsonPropertyName("hostError")] public string? HostError { get; set; }
    [JsonPropertyName("stopRequestedAt")] public DateTimeOffset? StopRequestedAt { get; set; }
}

public sealed class WatchStatus
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("running")] public bool Running { get; set; }
    [JsonPropertyName("reused")] public bool? Reused { get; set; }
    [JsonPropertyName("project")] public string Project { get; set; } = "";
    [JsonPropertyName("framework")] public string Framework { get; set; } = "";
    [JsonPropertyName("device")] public string? Device { get; set; }
    [JsonPropertyName("watchPid")] public int WatchPid { get; set; }
    [JsonPropertyName("cursor")] public int Cursor { get; set; }
    [JsonPropertyName("appliedCount")] public int AppliedCount { get; set; }
    [JsonPropertyName("lastOutcome")] public WatchEvent? LastOutcome { get; set; }
    [JsonPropertyName("exitCode")] public int? ExitCode { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("logPath")] public string LogPath { get; set; } = "";
}

public sealed class WatchWaitResult
{
    [JsonPropertyName("outcome")] public string Outcome { get; set; } = "";
    [JsonPropertyName("event")] public WatchEvent? Event { get; set; }
    [JsonPropertyName("cursor")] public int Cursor { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("waitedMs")] public long WaitedMs { get; set; }
    [JsonPropertyName("logPath")] public string LogPath { get; set; } = "";
}

/// <summary>
/// Owns <c>dotnet watch</c> sessions for agents: one session per (project, framework, device),
/// run by a detached host process that drains watch output into a log. Status and wait derive
/// events from that log with <see cref="WatchTranscriptParser"/>, so cursors are stable.
/// </summary>
internal static class WatchSessions
{
    const string InfoFileName = "session.json";
    const string LogFileName = "output.log";

    internal static string RootDirectory { get; set; } = Path.Combine(BrokerPaths.ConfigDir, "watch");

    public static string SessionId(string project, string framework, string? device)
    {
        var key = $"{Path.GetFullPath(project)}|{framework}|{device}".ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return $"{Path.GetFileNameWithoutExtension(project).ToLowerInvariant()}-{Convert.ToHexString(hash)[..10].ToLowerInvariant()}";
    }

    static string SessionDirectory(string id) => Path.Combine(RootDirectory, id);
    static string InfoPath(string id) => Path.Combine(SessionDirectory(id), InfoFileName);
    public static string LogPath(string id) => Path.Combine(SessionDirectory(id), LogFileName);

    public static string ResolveProject(string? project)
    {
        if (!string.IsNullOrWhiteSpace(project))
        {
            var full = Path.GetFullPath(project);
            if (Directory.Exists(full))
                return SingleProjectIn(full);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Project not found: {full}");
            return full;
        }

        return SingleProjectIn(Directory.GetCurrentDirectory());
    }

    static string SingleProjectIn(string directory)
    {
        var projects = Directory.GetFiles(directory, "*.csproj");
        return projects.Length switch
        {
            1 => projects[0],
            0 => throw new InvalidOperationException($"No .csproj in {directory}. Pass --project <path>."),
            _ => throw new InvalidOperationException($"Multiple projects in {directory}. Pass --project <path>."),
        };
    }

    public static string[] BuildWatchArguments(string project, string framework, string? device, IEnumerable<string> properties)
    {
        var args = new List<string> { "watch", "--project", project, "-f", framework, "--non-interactive" };
        if (!string.IsNullOrWhiteSpace(device))
        {
            args.Add("--device");
            args.Add(device);
        }
        foreach (var property in properties.Where(p => !string.IsNullOrWhiteSpace(p)))
            args.Add($"--property:{property}");
        return args.ToArray();
    }

    public static WatchSessionInfo? Load(string id)
    {
        try
        {
            var path = InfoPath(id);
            return File.Exists(path) ? CliJson.Deserialize<WatchSessionInfo>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    static void Save(WatchSessionInfo info)
    {
        var dir = SessionDirectory(info.Id);
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $"{InfoFileName}.{Environment.ProcessId}.tmp");
        File.WriteAllText(tmp, CliJson.SerializeUntyped(info));
        File.Move(tmp, InfoPath(info.Id), overwrite: true);
    }

    public static bool IsWatchRunning(WatchSessionInfo info)
    {
        if (info.WatchPid <= 0 || info.ExitedAt is not null)
            return false;
        try
        {
            using var process = Process.GetProcessById(info.WatchPid);
            if (process.HasExited)
                return false;
            // Guard against PID reuse by a later, unrelated process.
            return info.WatchStartedAt is null
                || Math.Abs((process.StartTime.ToUniversalTime() - info.WatchStartedAt.Value.UtcDateTime).TotalSeconds) < 5;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static IReadOnlyList<WatchEvent> ReadEvents(string id, bool watchRunning)
    {
        var events = new List<WatchEvent>();
        var path = LogPath(id);
        if (File.Exists(path))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                lines.Add(line);
            events = WatchTranscriptParser.Parse(lines);
        }

        if (!watchRunning && Load(id) is { WatchPid: > 0 } info)
        {
            events.Add(new WatchEvent
            {
                Seq = events.Count + 1,
                Kind = WatchEventKinds.WatchExited,
                Message = info.HostError
                    ?? (info.StopRequestedAt is not null ? "Stopped by maui devflow watch stop."
                    : info.ExitCode is { } code ? $"dotnet watch exited with code {code}." : "dotnet watch is not running."),
            });
        }

        return events;
    }

    public static WatchStatus GetStatus(WatchSessionInfo info)
    {
        var running = IsWatchRunning(info);
        var events = ReadEvents(info.Id, running);
        return new WatchStatus
        {
            Id = info.Id,
            State = info.WatchPid == 0 && info.HostError is null && HostAlive(info) ? "starting" : WatchTranscriptParser.DeriveState(events, running),
            Running = running,
            Project = info.Project,
            Framework = info.Framework,
            Device = info.Device,
            WatchPid = info.WatchPid,
            Cursor = events.Count,
            AppliedCount = events.Count(e => e.Kind == WatchEventKinds.Applied),
            LastOutcome = events.LastOrDefault(e => WatchEventKinds.OutcomeKinds.Contains(e.Kind)),
            ExitCode = info.ExitCode,
            Error = info.HostError,
            LogPath = LogPath(info.Id),
        };
    }

    static bool HostAlive(WatchSessionInfo info)
    {
        if (info.HostPid <= 0)
            return false;
        try
        {
            using var process = Process.GetProcessById(info.HostPid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static IReadOnlyList<WatchStatus> List()
    {
        if (!Directory.Exists(RootDirectory))
            return [];
        return Directory.GetDirectories(RootDirectory)
            .Select(dir => Load(Path.GetFileName(dir)))
            .OfType<WatchSessionInfo>()
            .Select(GetStatus)
            .OrderByDescending(s => s.Running)
            .ToList();
    }

    /// <summary>Finds a session by id, or the only running session when no id is given.</summary>
    public static WatchSessionInfo Find(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            return Load(id) ?? throw new InvalidOperationException($"No watch session '{id}'. Run 'maui devflow watch list'.");

        var all = Directory.Exists(RootDirectory)
            ? Directory.GetDirectories(RootDirectory).Select(d => Load(Path.GetFileName(d))).OfType<WatchSessionInfo>().ToList()
            : [];
        var running = all.Where(IsWatchRunning).ToList();
        return running.Count switch
        {
            1 => running[0],
            0 when all.Count == 1 => all[0],
            0 => throw new InvalidOperationException("No running watch session. Start one with 'maui devflow watch start'."),
            _ => throw new InvalidOperationException($"Multiple watch sessions are running ({string.Join(", ", running.Select(r => r.Id))}). Pass --session <id>."),
        };
    }

    public static async Task<WatchStatus> StartAsync(string project, string framework, string? device, string[] properties,
        bool waitForReady, TimeSpan timeout, CancellationToken ct)
    {
        project = ResolveProject(project);
        var id = SessionId(project, framework, device);
        var existing = Load(id);
        if (existing is not null && IsWatchRunning(existing))
        {
            if (!existing.Properties.SequenceEqual(properties, StringComparer.Ordinal))
                throw new InvalidOperationException($"Watch session '{id}' is already running with different properties. Use 'maui devflow watch restart' or stop it first.");
            var reused = GetStatus(existing);
            reused.Reused = true;
            return reused;
        }

        var dir = SessionDirectory(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        var info = new WatchSessionInfo
        {
            Id = id,
            Project = project,
            Framework = framework,
            Device = device,
            Properties = properties,
            Arguments = BuildWatchArguments(project, framework, device, properties),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Save(info);

        // The host records its own PID; writing it here could overwrite the host's watch PID.
        var hostPid = SpawnHost(id);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var current = Load(id) ?? info;
            if (current.HostPid == 0)
                current.HostPid = hostPid;
            if (current.HostError is not null || current.ExitedAt is not null)
                return GetStatus(current);
            if (current.WatchPid > 0)
            {
                if (!waitForReady)
                    return GetStatus(current);
                var status = GetStatus(current);
                if (status.State is not ("starting" or "applying"))
                    return status;
            }
            else if (!HostAlive(current) && DateTime.UtcNow - info.CreatedAt.UtcDateTime > TimeSpan.FromSeconds(10))
            {
                current.HostError ??= "Watch host exited before starting dotnet watch.";
                Save(current);
                return GetStatus(current);
            }
            await Task.Delay(500, ct);
        }

        var timedOut = GetStatus(Load(id) ?? info);
        timedOut.Error ??= $"Timed out after {timeout.TotalSeconds:F0}s waiting for the app to be ready; the session keeps running.";
        return timedOut;
    }

    /// <summary>
    /// How long a cursor-less wait lets the file watcher notice a save that just happened, so
    /// "edit and wait" in one command does not report the previous edit's outcome.
    /// </summary>
    internal static TimeSpan SaveSettleDelay { get; set; } = TimeSpan.FromMilliseconds(1500);

    public static async Task<WatchWaitResult> WaitAsync(WatchSessionInfo info, int? since, TimeSpan timeout, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        int cursor;
        if (since is { } explicitCursor)
        {
            cursor = explicitCursor;
        }
        else
        {
            if (SaveSettleDelay < timeout)
                await Task.Delay(SaveSettleDelay, ct);
            cursor = DefaultCursor(ReadEvents(info.Id, IsWatchRunning(Load(info.Id) ?? info)));
        }

        while (true)
        {
            var current = Load(info.Id) ?? info;
            var running = IsWatchRunning(current);
            var events = ReadEvents(info.Id, running);
            var outcome = FindOutcome(events, cursor);
            if (outcome is not null || stopwatch.Elapsed >= timeout)
            {
                return new WatchWaitResult
                {
                    Outcome = outcome?.Kind ?? "timeout",
                    Event = outcome,
                    Cursor = outcome?.Seq ?? events.Count,
                    State = WatchTranscriptParser.DeriveState(events, running),
                    WaitedMs = stopwatch.ElapsedMilliseconds,
                    LogPath = LogPath(info.Id),
                };
            }
            await Task.Delay(200, ct);
        }
    }

    /// <summary>
    /// The outcome of the latest file change after <paramref name="cursor"/>; outcomes of earlier
    /// saves that a newer save superseded are skipped. Without file changes, the first outcome
    /// (for example the app exiting) is returned. Returns null while the latest change is pending.
    /// </summary>
    internal static WatchEvent? FindOutcome(IReadOnlyList<WatchEvent> events, int cursor)
    {
        var after = events.Where(e => e.Seq > cursor).ToList();
        var from = after.LastOrDefault(e => e.Kind == WatchEventKinds.FileChanged)?.Seq ?? cursor;
        return after.FirstOrDefault(e => e.Seq > from && WatchEventKinds.OutcomeKinds.Contains(e.Kind))
            // The watcher stopping always ends a wait, even with a change still pending.
            ?? after.FirstOrDefault(e => e.Kind == WatchEventKinds.WatchExited);
    }

    /// <summary>
    /// Without an explicit cursor, report the result of the most recent file change the watcher saw.
    /// </summary>
    internal static int DefaultCursor(IReadOnlyList<WatchEvent> events)
    {
        var lastChange = events.LastOrDefault(e => e.Kind == WatchEventKinds.FileChanged);
        return lastChange is not null ? lastChange.Seq - 1 : events.Count;
    }

    public static async Task<WatchStatus> StopAsync(WatchSessionInfo info, TimeSpan grace)
    {
        if (IsWatchRunning(info))
        {
            info = Load(info.Id) ?? info;
            info.StopRequestedAt = DateTimeOffset.UtcNow;
            Save(info);
            if (OperatingSystem.IsWindows())
            {
                KillTree(info.WatchPid);
            }
            else
            {
                // With redirected stdin, dotnet-watch ignores SIGINT/SIGTERM (it only handles Ctrl+C read from a
                // console), so emulate a terminal Ctrl+C: interrupt the app launchers below it (for example
                // `dotnet run` and Microsoft.Android.Run, which stops the app on the device), then end the watcher.
                var watcherPids = ProcessTree(info.WatchPid, maxDepth: 1);
                var appPids = ProcessTree(info.WatchPid, maxDepth: int.MaxValue).Except(watcherPids).ToList();
                foreach (var pid in appPids)
                    _ = NativeMethods.kill(pid, NativeMethods.SIGINT);
                var deadline = DateTime.UtcNow + grace;
                while (DateTime.UtcNow < deadline && appPids.Any(ProcessAlive))
                    await Task.Delay(250);
                KillTree(info.WatchPid);
            }
        }

        var deadlineHost = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadlineHost && HostAlive(info))
            await Task.Delay(100);
        if (HostAlive(info))
            KillTree(info.HostPid);

        var current = Load(info.Id) ?? info;
        if (current.ExitedAt is null)
        {
            current.ExitedAt = DateTimeOffset.UtcNow;
            Save(current);
        }
        return GetStatus(current);
    }

    /// <summary>The process and its descendants up to <paramref name="maxDepth"/> levels (Unix), discovered through <c>ps</c>.</summary>
    static List<int> ProcessTree(int root, int maxDepth)
    {
        var result = new List<int> { root };
        try
        {
            using var ps = Process.Start(new ProcessStartInfo("ps", "-A -o pid=,ppid=")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (ps is null)
                return result;
            var output = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit(5000);
            var children = new Dictionary<int, List<int>>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var ppid))
                {
                    if (!children.TryGetValue(ppid, out var list))
                        children[ppid] = list = [];
                    list.Add(pid);
                }
            }
            var level = new List<int> { root };
            for (var depth = 0; depth < maxDepth && level.Count > 0; depth++)
            {
                level = level.SelectMany(p => children.TryGetValue(p, out var list) ? list : []).Where(c => !result.Contains(c)).ToList();
                result.AddRange(level);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        return result;
    }

    static bool ProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    static void KillTree(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    static int SpawnHost(string id)
    {
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the CLI executable path.");
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (Path.GetFileNameWithoutExtension(exePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entry = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(entry))
                throw new InvalidOperationException("Cannot resolve the CLI entry assembly path.");
            startInfo.ArgumentList.Add(entry);
        }
        foreach (var arg in new[] { "devflow", "watch", "host", "--session", id })
            startInfo.ArgumentList.Add(arg);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the watch host.");
        // The host never writes to these pipes; closing them keeps the caller's shell from waiting on it.
        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();
        return process.Id;
    }

    /// <summary>
    /// Runs in the detached host process: starts <c>dotnet watch</c>, drains its output to the
    /// session log, and records the exit. Never writes to the console.
    /// </summary>
    public static async Task<int> RunHostAsync(string id)
    {
        if (!OperatingSystem.IsWindows())
            _ = NativeMethods.setsid(); // Leave the caller's session so its shell teardown cannot kill the watcher.

        var info = Load(id);
        if (info is null)
            return 1;
        info.HostPid = Environment.ProcessId;
        Save(info);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ResolveDotnet(),
                WorkingDirectory = Path.GetDirectoryName(info.Project)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in info.Arguments)
                startInfo.ArgumentList.Add(arg);

            using var log = new StreamWriter(new FileStream(LogPath(id), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
            var gate = new object();
            void Write(string? line)
            {
                if (line is null)
                    return;
                lock (gate)
                    log.WriteLine(line);
            }

            using var watch = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet watch.");
            watch.OutputDataReceived += (_, e) => Write(e.Data);
            watch.ErrorDataReceived += (_, e) => Write(e.Data);
            watch.BeginOutputReadLine();
            watch.BeginErrorReadLine();

            info.WatchPid = watch.Id;
            info.WatchStartedAt = new DateTimeOffset(watch.StartTime.ToUniversalTime(), TimeSpan.Zero);
            Save(info);

            await watch.WaitForExitAsync();
            watch.WaitForExit(); // drain redirected output

            info = Load(id) ?? info;
            info.ExitCode = watch.ExitCode;
            info.ExitedAt = DateTimeOffset.UtcNow;
            Save(info);
            return 0;
        }
        catch (Exception ex)
        {
            info.HostError = ex.Message;
            info.ExitedAt = DateTimeOffset.UtcNow;
            Save(info);
            return 1;
        }
    }

    static string ResolveDotnet()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath))
            return hostPath;
        var processPath = Environment.ProcessPath;
        if (processPath is not null && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return processPath;
        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    static class NativeMethods
    {
        public const int SIGINT = 2;

        [DllImport("libc", SetLastError = true)]
        public static extern int kill(int pid, int sig);

        [DllImport("libc", SetLastError = true)]
        public static extern int setsid();
    }
}
