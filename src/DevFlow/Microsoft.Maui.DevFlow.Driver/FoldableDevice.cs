using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Host-side fold controls for explicitly selected emulators/simulators. No app agent is required.
/// iOS uses private simulator interfaces and requires explicit experimental opt-in.
/// </summary>
public sealed partial class FoldableDevice
{
    private readonly Func<string, string[], CancellationToken, Task<string>> _run;
    private readonly string _adbPath;
    private readonly bool _allowExperimentalIos;
    private readonly Func<CancellationToken, Task<string>> _prepareIosHelper;

    public string Platform { get; }
    public string DeviceId { get; }

    public FoldableDevice(string platform, string deviceId, bool allowExperimentalIos = false, string? adbPath = null)
        : this(platform, deviceId, allowExperimentalIos, adbPath, RunProcessAsync)
    {
        if (Platform == "ios" && !OperatingSystem.IsMacOS())
            throw new NotSupportedException("iOS Simulator fold controls require macOS.");
    }

    internal FoldableDevice(string platform, string deviceId, bool allowExperimentalIos, string? adbPath,
        Func<string, string[], CancellationToken, Task<string>> run,
        Func<CancellationToken, Task<string>>? prepareIosHelper = null)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        Platform = platform?.ToLowerInvariant() switch
        {
            "android" => "android",
            "ios" => "ios",
            _ => throw new ArgumentException("Fold controls support android and ios simulators only.", nameof(platform)),
        };
        if (Platform == "android")
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(deviceId, @"\Aemulator-[0-9]+\z"))
                throw new ArgumentException("Select an Android emulator serial such as emulator-5554. Physical devices are not supported.", nameof(deviceId));
            DeviceId = deviceId;
        }
        else
        {
            if (!Guid.TryParseExact(deviceId, "D", out var udid))
                throw new ArgumentException("Select an iOS Simulator using its explicit UDID.", nameof(deviceId));
            DeviceId = udid.ToString("D").ToUpperInvariant();
        }
        _allowExperimentalIos = allowExperimentalIos;
        _adbPath = adbPath ?? "adb";
        _run = run;
        _prepareIosHelper = prepareIosHelper ?? BuildIosHelperAsync;
    }

    public Task<FoldCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        => ReadWithTimeoutAsync(ReadCapabilitiesAsync, cancellationToken);

    public Task<FoldState> GetStateAsync(CancellationToken cancellationToken = default)
        => ReadWithTimeoutAsync(async token =>
        {
            var capabilities = await ReadCapabilitiesAsync(token).ConfigureAwait(false);
            RequireSupported(capabilities);
            return await ReadStateAsync(capabilities.HingeIndices, token).ConfigureAwait(false);
        }, cancellationToken);

    private async Task<T> ReadWithTimeoutAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken token)
    {
        using var timeout = CreateTimeout(30, token);
        try { return await read(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"Fold discovery/readback on {DeviceId} did not complete within 30 seconds.");
        }
    }

    /// <summary>
    /// Sets a single inward-folding hinge in the supported 0..180 degree range.
    /// Mode presets use 0, 90, and 180 degrees. An explicit angle is valid only with partially-folded.
    /// Success requires angle readback and, on iOS endpoints, the expected active display.
    /// </summary>
    public async Task<FoldSetResult> SetAsync(FoldMode mode, double? angleDegrees = null, int hingeIndex = 0,
        double durationSeconds = 0, int timeoutSeconds = 30, CancellationToken cancellationToken = default)
    {
        var angle = ValidateRequest(mode, angleDegrees, hingeIndex, durationSeconds, timeoutSeconds);
        using var timeout = CreateTimeout(timeoutSeconds, cancellationToken);
        FoldState? observed = null;
        try
        {
            using var deviceLock = AcquireDeviceLock();
            var capabilities = await ReadCapabilitiesAsync(timeout.Token).ConfigureAwait(false);
            RequireSupported(capabilities);
            if (!capabilities.HingeIndices.Contains(hingeIndex))
                throw new ArgumentException($"Hinge {hingeIndex} is unavailable on {DeviceId}.", nameof(hingeIndex));

            // Read before sending: never invent an animation starting angle.
            var before = await ReadStateAsync(capabilities.HingeIndices, timeout.Token).ConfigureAwait(false);
            var start = before.Hinges.Single(hinge => hinge.Index == hingeIndex).AngleDegrees;
            if (!double.IsFinite(start) || start < 0 || start > 180)
                throw new NotSupportedException("This command supports inward-folding hinges in the 0..180 degree range only.");
            await SetAngleAsync(start, angle, hingeIndex, durationSeconds, timeout.Token).ConfigureAwait(false);

            string? previousGeometry = null;
            while (true)
            {
                observed = await ReadStateAsync(capabilities.HingeIndices, timeout.Token).ConfigureAwait(false);
                var hinge = observed.Hinges.Single(value => value.Index == hingeIndex);
                var angleMatches = Math.Abs(hinge.AngleDegrees - angle) <= 1;
                var displayMatches = Platform != "ios" || observed.Displays.Any(display =>
                    display.Active && (mode == FoldMode.PartiallyFolded ||
                        display.Primary == (mode == FoldMode.Folded)));
                var geometry = string.Join("|", observed.Displays.Where(display => display.Active)
                    .OrderBy(display => display.Id)
                    .Select(display => $"{display.Id}:{display.NativeWidth}:{display.NativeHeight}:{display.Rotation}:{display.Scale}"));
                if (angleMatches && displayMatches && previousGeometry == geometry)
                    return new FoldSetResult
                    {
                        RequestedMode = mode, RequestedAngleDegrees = angle, HingeIndex = hingeIndex, State = observed,
                    };
                previousGeometry = angleMatches && displayMatches ? geometry : null;
                await Task.Delay(200, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var detail = observed is null ? "No final state was observed." :
                $"Last observed hinges: {string.Join(", ", observed.Hinges.Select(hinge => $"{hinge.Index}={hinge.AngleDegrees.ToString(CultureInfo.InvariantCulture)} degrees"))}; active displays: {string.Join(", ", observed.Displays.Where(display => display.Active).Select(display => display.Id))}.";
            throw new TimeoutException($"Fold change on {DeviceId} was not confirmed within {timeoutSeconds}s. The device may have changed partially. {detail}");
        }
    }

    /// <summary>Validates a fold request without device access and returns its target angle in degrees.</summary>
    public static double ValidateRequest(FoldMode mode, double? angleDegrees, int hingeIndex,
        double durationSeconds, int timeoutSeconds)
    {
        if (mode is not (FoldMode.Folded or FoldMode.PartiallyFolded or FoldMode.Unfolded))
            throw new ArgumentException("Use folded, partially-folded, or unfolded.", nameof(mode));
        ArgumentOutOfRangeException.ThrowIfNegative(hingeIndex);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0 || durationSeconds > 30)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be finite and between 0 and 30 seconds.");
        if (timeoutSeconds is < 1 or > 120 || durationSeconds >= timeoutSeconds)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must be 1..120 seconds and exceed the duration.");
        if (angleDegrees.HasValue && (mode != FoldMode.PartiallyFolded ||
            !double.IsFinite(angleDegrees.Value) || angleDegrees.Value <= 0 || angleDegrees.Value >= 180))
            throw new ArgumentOutOfRangeException(nameof(angleDegrees), "An explicit angle must be finite, between 0 and 180 (exclusive), and use partially-folded.");
        return mode switch
        {
            FoldMode.Folded => 0,
            FoldMode.Unfolded => 180,
            _ => angleDegrees ?? 90,
        };
    }

    private string Backend => Platform == "android" ? "android-emulator-console" : "ios-simulator-hid";

    private async Task<FoldCapabilities> ReadCapabilitiesAsync(CancellationToken token)
    {
        var indices = Platform == "android"
            ? await GetAndroidHingesAsync(token).ConfigureAwait(false)
            : await GetIosHingesAsync(token).ConfigureAwait(false);
        var supported = indices.Length != 0;
        return new FoldCapabilities
        {
            DeviceId = DeviceId, Platform = Platform, Backend = Backend, Experimental = Platform == "ios",
            CanReadAngle = supported, CanSetAngle = supported, CanSetMode = supported,
            HingeIndices = indices,
            SupportedModes = supported ? [FoldMode.Folded, FoldMode.PartiallyFolded, FoldMode.Unfolded] : [],
            Reason = supported ? null : Platform == "ios" && !_allowExperimentalIos
                ? "iOS hinge access uses private simulator interfaces. Explicit experimental opt-in is required."
                : "The selected device does not expose an enabled hinge sensor.",
        };
    }

    private static void RequireSupported(FoldCapabilities capabilities)
    {
        if (!capabilities.CanReadAngle)
            throw new NotSupportedException(capabilities.Reason);
    }

    private async Task<FoldState> ReadStateAsync(int[] indices, CancellationToken token)
    {
        var hinges = Platform == "android" ? await ReadAndroidAnglesAsync(indices, token).ConfigureAwait(false)
            : await ReadIosAnglesAsync(token).ConfigureAwait(false);
        return new FoldState
        {
            DeviceId = DeviceId, Platform = Platform, Backend = Backend, Experimental = Platform == "ios",
            Mode = GetMode(hinges),
            Hinges = hinges,
            Displays = Platform == "ios" ? await ReadIosDisplaysAsync(token).ConfigureAwait(false) : [],
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    internal static FoldMode GetMode(FoldHinge[] hinges)
        => hinges.Length == 0 || hinges.Any(hinge => !double.IsFinite(hinge.AngleDegrees) || hinge.AngleDegrees < 0 || hinge.AngleDegrees > 180)
            ? FoldMode.Unknown
            : hinges.All(hinge => hinge.AngleDegrees <= 1) ? FoldMode.Folded
            : hinges.All(hinge => hinge.AngleDegrees >= 179) ? FoldMode.Unfolded : FoldMode.PartiallyFolded;

    private Task SetAngleAsync(double start, double angle, int hingeIndex, double duration, CancellationToken token)
        => Platform == "android" ? SetAndroidAngleAsync(start, angle, hingeIndex, duration, token)
            : SetIosAngleAsync(angle, duration, token);

    private FileStream AcquireDeviceLock()
    {
        Directory.CreateDirectory(CacheDirectory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Platform}:{DeviceId}")));
        try
        {
            return new FileStream(Path.Combine(CacheDirectory, $"device-{key}.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new IOException($"Could not acquire exclusive fold control for {DeviceId}. Another fold operation may be running. {ex.Message}", ex);
        }
    }

    private static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft.Maui.DevFlow", "foldable");

    private static CancellationTokenSource CreateTimeout(int seconds, CancellationToken token)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        source.CancelAfter(TimeSpan.FromSeconds(seconds));
        return source;
    }

    internal static async Task<string> RunProcessAsync(string executable, string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var stdout = await output.ConfigureAwait(false);
            var stderr = await error.ConfigureAwait(false);
            if (process.ExitCode != 0 || (stdout + "\n" + stderr).Split('\n')
                .Any(line => line.TrimStart().StartsWith("KO:", StringComparison.Ordinal)))
                throw new InvalidOperationException($"{executable} {string.Join(" ", arguments)} failed ({process.ExitCode}): {stderr.Trim()} {stdout.Trim()}");
            return stdout;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException ex) { Trace.WriteLine($"Fold process exited during cancellation: {ex.Message}"); }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException ex) { Trace.WriteLine($"Fold process did not exit after cancellation: {ex.Message}"); }
            // Observe both readers even when cancellation interrupted WaitForExitAsync.
            try { await Task.WhenAll(output, error).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            throw;
        }
    }
}
