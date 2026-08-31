using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

/// <summary>
/// Asks Android, over adb rather than over the DevFlow agent channel, whether the app under test
/// is still alive and how it died.
/// </summary>
/// <remarks>
/// <para>
/// The agent channel is exactly what a crash destroys, so a crashed app and a wedged agent look
/// identical from inside DevFlow. This probe reaches the same device through a different transport
/// and reads what the platform itself recorded.
/// </para>
/// <para>
/// Every observation is bound to the process id this run launched. <c>ApplicationExitInfo</c> is a
/// historical list, so an unbound read could attribute yesterday's crash to today's run. The pid
/// binding removes almost all of that, and because Linux recycles pids the record must also be no
/// older than this run — an age computed entirely on the device's own clock, so no host and device
/// clock are ever compared. When the launched pid is unknown, or the device clock cannot be read,
/// the probe reports that it could not bind evidence rather than guessing — an unbound record is
/// not evidence.
/// </para>
/// </remarks>
internal sealed partial class AndroidAppProcessProbe
{
    internal const string ProbeSource = "android-adb";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Crash-buffer lines read from the device before any redaction or line cap.</summary>
    private const int MaxCrashLogLines = 200;
    private const int MaxCrashLogCharacters = 32 * 1024;
    private const int MaxExitInfoCharacters = 64 * 1024;

    /// <summary>
    /// Grace added to the run's elapsed time before an exit record is judged too old to belong to
    /// it, covering second-granularity device clocks and modest drift between the two clocks.
    /// </summary>
    private static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(2);

    /// <summary>
    /// <c>date</c> format used to read the device clock. It must contain no space: <c>adb shell</c>
    /// re-splits its arguments on the device, so a space arrives as a second argument to
    /// <c>date</c> and the command fails.
    /// </summary>
    internal const string DeviceClockFormat = "+%Y-%m-%dT%H:%M:%S";

    private readonly IAndroidProvider _androidProvider;
    private readonly IExecutionProcessRunner _processRunner;

    public AndroidAppProcessProbe(IAndroidProvider androidProvider, IExecutionProcessRunner processRunner)
    {
        _androidProvider = androidProvider ?? throw new ArgumentNullException(nameof(androidProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<MauiFlowAppProcessEvidence?> ProbeAsync(
        FlowExecutionAppProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = request.Session;
        if (!IsValidSerial(session.DeviceSerial) ||
            string.IsNullOrEmpty(session.PackageId) ||
            !PackagePattern().IsMatch(session.PackageId))
        {
            return NotProbed("The Android device serial or package identity is not a safe adb argument.");
        }

        var adbPath = ResolveAdbPath();
        if (adbPath is null)
            return NotProbed("ADB was not found in the configured Android SDK, so app liveness was not observed.");

        var liveness = await ReadLivenessAsync(adbPath, session, cancellationToken).ConfigureAwait(false);
        if (liveness.Error is not null)
            return NotProbed(liveness.Error);

        var evidence = new MauiFlowAppProcessEvidence
        {
            Probed = true,
            Source = ProbeSource,
            ProcessExited = liveness.Exited,
        };
        if (liveness.Exited != true)
            return evidence;

        if (session.ProcessId is not { } processId || processId <= 0)
        {
            evidence.ProbeError =
                "The launched process id is unknown, so platform exit records could not be bound to this run.";
            return evidence;
        }

        var reason = await ReadExitReasonAsync(
            adbPath,
            session,
            processId,
            request.RunStartedAt,
            cancellationToken).ConfigureAwait(false);
        if (reason.Error is not null)
        {
            evidence.ProbeError = reason.Error;
            return evidence;
        }
        evidence.ExitReason = reason.Reason;

        var crashLog = await ReadCrashLogAsync(adbPath, session, processId, cancellationToken).ConfigureAwait(false);
        if (crashLog is not null)
        {
            evidence.CrashLogPresent = crashLog.Count > 0;
            if (crashLog.Count > 0)
            {
                evidence.CrashExcerpt = crashLog;
                evidence.CrashSignature = SelectSignature(crashLog);
            }
        }
        return evidence;
    }

    private static MauiFlowAppProcessEvidence NotProbed(string reason) => new()
    {
        Probed = false,
        Source = ProbeSource,
        ProbeError = reason,
    };

    /// <summary>
    /// Reads the live process ids for the package. When the launched pid is known, liveness means
    /// <em>that</em> process: an app the platform restarted after a crash is a new process and
    /// must not mask the death of the one under test.
    /// </summary>
    private async Task<LivenessRead> ReadLivenessAsync(
        string adbPath,
        FlowExecutionPlatformSession session,
        CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                adbPath,
                ["-s", session.DeviceSerial, "shell", "pidof", session.PackageId],
                timeout: ProbeTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LivenessRead.Failed("ADB could not be started to observe app liveness.");
        }

        // `pidof` is silent and exits 0 on a hit, 1 on a miss. adb reuses exit 1 for its own
        // transport errors (`device 'x' not found`) but writes them to stderr, which a reachable
        // device never does for this command. Anything outside those two exact shapes is a channel
        // failure, not an observation: reporting "the app exited" because adb could not answer
        // would invent the very fact the crash rule depends on.
        var pids = ParsePids(result.StandardOutput);
        var quiet = string.IsNullOrWhiteSpace(result.StandardError);
        var observed = result.ExitCode switch
        {
            0 => quiet && pids.Length > 0,
            1 => quiet && pids.Length == 0 && string.IsNullOrWhiteSpace(result.StandardOutput),
            _ => false,
        };
        if (!observed)
            return LivenessRead.Failed("ADB could not reach the device to observe app liveness.");

        return session.ProcessId is { } launched && launched > 0
            ? LivenessRead.Observed(!pids.Contains(launched))
            : LivenessRead.Observed(pids.Length == 0);
    }

    private static int[] ParsePids(string? standardOutput) =>
        (standardOutput ?? string.Empty)
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => int.TryParse(value, out var pid) ? pid : -1)
            .Where(static pid => pid > 0)
            .ToArray();

    /// <summary>
    /// Maps the platform's own <c>ApplicationExitInfo</c> record for the launched pid onto a
    /// neutral exit reason. Returns null when no record for that pid exists, so a missing record is
    /// never read as a crash.
    /// </summary>
    private async Task<ExitReasonRead> ReadExitReasonAsync(
        string adbPath,
        FlowExecutionPlatformSession session,
        int processId,
        DateTimeOffset runStartedAt,
        CancellationToken cancellationToken)
    {
        // Exit records outlive the run and pids are recycled, so the record must be shown to belong
        // to this run before it can be believed. Ages are compared on the device's own clock so no
        // host/device timezone or offset comparison is involved.
        var deviceNow = await ReadDeviceClockAsync(adbPath, session, cancellationToken).ConfigureAwait(false);
        if (deviceNow is null)
        {
            return ExitReasonRead.Failed(
                "The device clock could not be read, so platform exit records could not be shown to belong to this run.");
        }

        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                adbPath,
                ["-s", session.DeviceSerial, "shell", "dumpsys", "activity", "exit-info", session.PackageId],
                timeout: ProbeTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExitReasonRead.None;
        }

        if (!result.Success)
            return ExitReasonRead.None;

        var maximumAge = DateTimeOffset.UtcNow - runStartedAt + ClockSkewAllowance;
        if (maximumAge < ClockSkewAllowance)
            maximumAge = ClockSkewAllowance;
        return ExitReasonRead.Read(ParseExitReason(
            Truncate(result.StandardOutput, MaxExitInfoCharacters),
            processId,
            deviceNow,
            maximumAge));
    }

    /// <summary>
    /// Reads the device's wall clock from the same clock as the <c>timestamp=</c> field of an exit
    /// record, so record age can be computed without ever comparing a host clock to a device clock.
    /// </summary>
    /// <remarks>
    /// The format deliberately contains no space. <c>adb shell</c> re-splits its arguments on the
    /// device, so a space would reach <c>date</c> as a second argument and the command would fail.
    /// </remarks>
    private async Task<DateTime?> ReadDeviceClockAsync(
        string adbPath,
        FlowExecutionPlatformSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                adbPath,
                ["-s", session.DeviceSerial, "shell", "date", DeviceClockFormat],
                timeout: ProbeTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.Success ? ParseDeviceTimestamp(result.StandardOutput?.Trim()) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a device-local naive timestamp in either the exit-record form (space separated, with
    /// optional milliseconds) or the <c>date</c> form this probe requests. No offset is applied:
    /// these values are only ever subtracted from another reading of the same clock.
    /// </summary>
    internal static DateTime? ParseDeviceTimestamp(string? value) =>
        DateTime.TryParseExact(
            value,
            [
                "yyyy-MM-dd HH:mm:ss.fff",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.fff",
                "yyyy-MM-ddTHH:mm:ss",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Finds the exit record whose <c>pid</c> is the process this run launched and translates its
    /// reason. Exposed for tests so the parser is exercised without a device.
    /// </summary>
    /// <remarks>
    /// Records are emitted newest first and look like this on API 35:
    /// <code>
    ///         ApplicationExitInfo #0:
    ///           timestamp=2026-08-17 14:42:36.633 pid=14277 realUid=10212 ... user=0
    ///           process=com.contoso.app reason=4 (APP CRASH(EXCEPTION)) subreason=0 (UNKNOWN) status=0
    ///           importance=100 pss=0.00 rss=0.00 description=crash state=empty trace=null
    /// </code>
    /// The numeric reason code is read rather than the display name: the display name contains
    /// spaces and nested parentheses, while the code is a documented <c>ApplicationExitInfo.REASON_*</c>
    /// constant. Each field is bound to the line that owns it so a free-text <c>description=</c>
    /// can never be mistaken for a pid, and so an unrecognised layout yields no reason at all
    /// rather than a wrong one.
    /// <para>
    /// A pid match alone is not enough: Linux recycles pids, so a record older than this run is
    /// discarded even when its pid matches. A record with no readable timestamp is discarded for
    /// the same reason.
    /// </para>
    /// </remarks>
    internal static string? ParseExitReason(
        string? dumpsysOutput,
        int processId,
        DateTime? deviceNow = null,
        TimeSpan? maximumAge = null)
    {
        if (string.IsNullOrWhiteSpace(dumpsysOutput))
            return null;

        int? recordPid = null;
        string? recordReason = null;
        DateTime? recordTimestamp = null;
        foreach (var raw in dumpsysOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (ExitRecordHeaderPattern().IsMatch(line))
            {
                if (recordPid == processId &&
                    recordReason is not null &&
                    WithinRun(recordTimestamp, deviceNow, maximumAge))
                {
                    return recordReason;
                }
                recordPid = null;
                recordReason = null;
                recordTimestamp = null;
                continue;
            }
            if (line.Contains("timestamp=", StringComparison.Ordinal) &&
                PidPattern().Match(line) is { Success: true } pid &&
                int.TryParse(pid.Groups[1].Value, out var value))
            {
                recordPid = value;
                recordTimestamp = ParseDeviceTimestamp(
                    TimestampPattern().Match(line) is { Success: true } stamp ? stamp.Groups[1].Value : null);
            }
            if (line.Contains("process=", StringComparison.Ordinal) &&
                ReasonPattern().Match(line) is { Success: true } reason &&
                int.TryParse(reason.Groups[1].Value, out var code))
            {
                recordReason = MapReasonCode(code);
            }
        }
        return recordPid == processId && WithinRun(recordTimestamp, deviceNow, maximumAge)
            ? recordReason
            : null;
    }

    /// <summary>
    /// Decides whether an exit record is recent enough to belong to this run. Both timestamps come
    /// from the device clock, so the subtraction is offset-free. A record with no readable
    /// timestamp is rejected rather than assumed current.
    /// </summary>
    private static bool WithinRun(DateTime? recordTimestamp, DateTime? deviceNow, TimeSpan? maximumAge)
    {
        if (deviceNow is null || maximumAge is null)
            return true;
        if (recordTimestamp is null)
            return false;
        var age = deviceNow.Value - recordTimestamp.Value;
        return age <= maximumAge.Value;
    }

    /// <summary>
    /// Translates a documented <c>android.app.ApplicationExitInfo.REASON_*</c> constant. Anything
    /// unrecognised maps to <see cref="MauiFlowAppExitReasons.Unknown"/>, which does not prove a crash.
    /// </summary>
    private static string MapReasonCode(int reasonCode) => reasonCode switch
    {
        1 => MauiFlowAppExitReasons.ExitSelf,
        2 => MauiFlowAppExitReasons.Signaled,
        3 => MauiFlowAppExitReasons.LowMemory,
        4 => MauiFlowAppExitReasons.Crash,
        5 => MauiFlowAppExitReasons.CrashNative,
        6 => MauiFlowAppExitReasons.Anr,
        10 or 11 => MauiFlowAppExitReasons.UserRequested,
        _ => MauiFlowAppExitReasons.Unknown,
    };

    /// <summary>
    /// Reads the device crash buffer for the launched pid only. Returns null when the read itself
    /// failed, so "no crash log" and "could not look" stay distinguishable.
    /// </summary>
    private async Task<List<string>?> ReadCrashLogAsync(
        string adbPath,
        FlowExecutionPlatformSession session,
        int processId,
        CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                adbPath,
                [
                    "-s", session.DeviceSerial,
                    "logcat", "-b", "crash",
                    "-t", MaxCrashLogLines.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"--pid={processId.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                ],
                timeout: ProbeTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        if (!result.Success)
            return null;

        var output = result.StandardOutput ?? string.Empty;
        if (output.Length > MaxCrashLogCharacters)
            output = output[..MaxCrashLogCharacters];
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith("---------", StringComparison.Ordinal))
            .Take(MaxCrashLogLines)
            .ToList();
    }

    /// <summary>
    /// Picks the single most identifying line from the crash buffer: the exception type and message
    /// that follows <c>FATAL EXCEPTION</c>. The intervening <c>Process:</c> banner and the stack
    /// frames below it are skipped because neither distinguishes one crash from another.
    /// </summary>
    internal static string? SelectSignature(IReadOnlyList<string> crashLog)
    {
        for (var index = 0; index < crashLog.Count; index++)
        {
            if (!crashLog[index].Contains("FATAL EXCEPTION", StringComparison.Ordinal))
                continue;

            for (var candidate = index + 1; candidate < crashLog.Count; candidate++)
            {
                var payload = StripLogcatPrefix(crashLog[candidate]);
                if (payload.Length == 0 ||
                    payload.StartsWith("Process:", StringComparison.Ordinal) ||
                    payload.StartsWith("at ", StringComparison.Ordinal) ||
                    payload.StartsWith("Caused by:", StringComparison.Ordinal))
                {
                    continue;
                }
                return payload;
            }
            return StripLogcatPrefix(crashLog[index]);
        }
        return crashLog.Count > 0 ? StripLogcatPrefix(crashLog[0]) : null;
    }

    /// <summary>Removes the <c>MM-DD HH:MM:SS.mmm pid tid LEVEL TAG:</c> banner logcat prepends.</summary>
    private static string StripLogcatPrefix(string line)
    {
        var match = LogcatPrefixPattern().Match(line);
        return (match.Success ? line[match.Length..] : line).Trim();
    }

    private string? ResolveAdbPath()
    {
        var sdkPath = _androidProvider.SdkPath;
        if (string.IsNullOrWhiteSpace(sdkPath))
            return null;

        var executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var path = Path.Combine(sdkPath, "platform-tools", executable);
        return File.Exists(path) ? path : null;
    }

    private static bool IsValidSerial(string? serial)
        => !string.IsNullOrWhiteSpace(serial) &&
           serial.Length <= 256 &&
           !serial.StartsWith('-') &&
           serial.All(static character =>
               char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':');

    [GeneratedRegex(@"\A[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"\AApplicationExitInfo\s+#\d+", RegexOptions.CultureInvariant)]
    private static partial Regex ExitRecordHeaderPattern();

    /// <summary>Matches the inline <c>pid=</c> field without matching <c>realUid=</c> or similar.</summary>
    [GeneratedRegex(@"(?<![A-Za-z])pid=(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex PidPattern();

    /// <summary>
    /// Matches the inline numeric reason code. The lookbehind is what keeps <c>subreason=</c>,
    /// which uses an unrelated code space, from being read as the exit reason.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z])reason=(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ReasonPattern();

    /// <summary>Matches the device-local record timestamp, with or without milliseconds.</summary>
    [GeneratedRegex(
        @"(?<![A-Za-z])timestamp=(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}(?:\.\d{3})?)",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(
        @"\A\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3}\s+\d+\s+\d+\s+[VDIWEFS]\s+[^:]{0,64}:\s?",
        RegexOptions.CultureInvariant)]
    private static partial Regex LogcatPrefixPattern();

    private static string? Truncate(string? value, int maximumCharacters) =>
        value is not null && value.Length > maximumCharacters ? value[..maximumCharacters] : value;

    private readonly record struct LivenessRead(bool? Exited, string? Error)
    {
        public static LivenessRead Observed(bool exited) => new(exited, null);
        public static LivenessRead Failed(string error) => new(null, error);
    }

    /// <summary>
    /// Separates "the platform recorded no usable exit reason" from "the exit reason could not be
    /// established", so the second case is reported rather than silently read as the first.
    /// </summary>
    private readonly record struct ExitReasonRead(string? Reason, string? Error)
    {
        public static readonly ExitReasonRead None = new(null, null);
        public static ExitReasonRead Read(string? reason) => new(reason, null);
        public static ExitReasonRead Failed(string error) => new(null, error);
    }
}
