using System.Net.Http.Json;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>
/// An <see cref="IDeviceSurface"/> backed by a locally installed Mobile Canvas host.
/// <para>
/// The host is a separate product with its own lifecycle, so this adapter treats it as an
/// untrusted, optional peer: every call tolerates it being absent, restarted, or a version whose
/// payloads do not deserialise. Anything unexpected degrades to "unavailable" rather than
/// propagating, because a DevFlow session must never fail because an optional device layer
/// hiccupped.
/// </para>
/// </summary>
public sealed class MobileCanvasDeviceSurface : IDeviceSurface, IDeviceRecordingPathAuthority, IDisposable
{
    private const string ApiPrefix = "/api/v1";
    private const int MaxScreenshotBytes = 64 * 1024 * 1024;
    private const int MaxUiSnapshotBytes = 4 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Func<MobileCanvasHostState?> _stateProvider;

    public MobileCanvasDeviceSurface(HttpClient? httpClient = null, Func<MobileCanvasHostState?>? stateProvider = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _stateProvider = stateProvider ?? MobileCanvasHost.TryRead;
    }

    public async Task<DeviceHostHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var state = _stateProvider();
        if (state is null)
            return DeviceHostHealth.Unavailable;

        if (!MobileCanvasHost.IsTrustedForControl(state))
            return DeviceHostHealth.Incompatible(state.SchemaVersion, state.Version);

        try
        {
            using var request = CreateRequest(HttpMethod.Get, state, "/status");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return DeviceHostHealth.Unauthorized();
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // The route we depend on is gone, which means this host is not one we know how to
                // drive. Saying so beats reporting an empty device list forever.
                return DeviceHostHealth.Incompatible(state.SchemaVersion, state.Version);
            }

            if (!response.IsSuccessStatusCode)
                return DeviceHostHealth.NotResponding($"The device host answered with {(int)response.StatusCode}.");

            return new DeviceHostHealth
            {
                Availability = DeviceHostAvailability.Available,
                Version = state.Version,
                ProtocolVersion = state.SchemaVersion,
            };
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            // A stale host.json outlives a crashed host; that is a normal state, not a fault.
            return DeviceHostHealth.NotResponding("The device host is not responding.");
        }
    }

    public async Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken cancellationToken = default)
    {
        // A null result distinguishes "could not enumerate" from "enumerated, none present".
        // Callers cache the latter and fall back on the former.
        return await GetJsonAsync(
            "/devices",
            MobileCanvasJsonContext.Default.DeviceTargetArray,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<DeviceCatalog?> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync(
            "/catalog",
            MobileCanvasJsonContext.Default.DeviceCatalog,
            cancellationToken);

    public async Task<DeviceTarget?> GetAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        return await GetJsonAsync(
            $"/devices/{Uri.EscapeDataString(deviceId)}",
            MobileCanvasJsonContext.Default.DeviceTarget,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeviceOperationResult> CreateAsync(
        DeviceCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.RuntimeId) ||
            string.IsNullOrWhiteSpace(request.DeviceTypeId) ||
            request.Platform is not (DevicePlatforms.Android or DevicePlatforms.Ios))
        {
            return DeviceOperationResult.Failed(
                "Platform, name, runtime, and device type are required.");
        }

        var response = await SendJsonAtPathAsync(
            HttpMethod.Post,
            "/devices",
            JsonContent.Create(request, MobileCanvasJsonContext.Default.DeviceCreateRequest),
            MobileCanvasJsonContext.Default.DeviceTarget,
            cancellationToken).ConfigureAwait(false);
        return response.Value is null
            ? DeviceOperationResult.Failed(
                response.Error ?? "The device host did not return the created device.",
                response.FailureKind)
            : DeviceOperationResult.Ok(response.Value);
    }

    public Task<DeviceOperationResult> BootAsync(string deviceId, CancellationToken cancellationToken = default) =>
        PostAsync(deviceId, "boot", content: null, cancellationToken);

    public Task<DeviceOperationResult> ShutdownAsync(string deviceId, CancellationToken cancellationToken = default) =>
        PostAsync(deviceId, "shutdown", content: null, cancellationToken);

    public Task<DeviceOperationResult> RestartAsync(string deviceId, CancellationToken cancellationToken = default) =>
        PostAsync(deviceId, "restart", content: null, cancellationToken);

    public Task<DeviceOperationResult> RevealAsync(string deviceId, CancellationToken cancellationToken = default) =>
        PostAsync(deviceId, "reveal", content: null, cancellationToken);

    public Task<DeviceOperationResult> EraseAsync(
        string deviceId,
        bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
            return Task.FromResult(DeviceOperationResult.Failed("Device erasure requires explicit confirmation."));

        return PostAsync(
            deviceId,
            "erase",
            JsonContent.Create(
                new ConfirmedDeviceOperationPayload(true),
                MobileCanvasJsonContext.Default.ConfirmedDeviceOperationPayload),
            cancellationToken);
    }

    public Task<DeviceOperationResult> DeleteAsync(
        string deviceId,
        bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
            return Task.FromResult(DeviceOperationResult.Failed("Device deletion requires explicit confirmation."));

        return SendOperationAtPathAsync(
            HttpMethod.Delete,
            DevicePath(deviceId),
            JsonContent.Create(
                new ConfirmedDeviceOperationPayload(true),
                MobileCanvasJsonContext.Default.ConfirmedDeviceOperationPayload),
            cancellationToken);
    }

    public Task<DeviceOperationResult> TapAsync(string deviceId, DevicePoint point, CancellationToken cancellationToken = default) =>
        PostAsync(
            deviceId,
            "input/tap",
            JsonContent.Create(new DevicePointPayload(point.X, point.Y), MobileCanvasJsonContext.Default.DevicePointPayload),
            cancellationToken);

    public Task<DeviceOperationResult> LongPressAsync(
        string deviceId,
        DevicePoint point,
        double duration = 1,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(duration) || duration is < 0.1 or > 60)
            return Task.FromResult(DeviceOperationResult.Failed("Long-press duration must be between 0.1 and 60 seconds."));

        return PostAsync(
            deviceId,
            "input/tap",
            JsonContent.Create(
                new DevicePointPayload(point.X, point.Y, duration),
                MobileCanvasJsonContext.Default.DevicePointPayload),
            cancellationToken);
    }

    public Task<DeviceOperationResult> SwipeAsync(
        string deviceId,
        DeviceSwipe swipe,
        CancellationToken cancellationToken = default)
    {
        if (swipe is null ||
            !double.IsFinite(swipe.StartX) ||
            !double.IsFinite(swipe.StartY) ||
            !double.IsFinite(swipe.EndX) ||
            !double.IsFinite(swipe.EndY) ||
            !double.IsFinite(swipe.Duration) ||
            swipe.Duration is < 0.01 or > 60)
        {
            return Task.FromResult(DeviceOperationResult.Failed(
                "Swipe coordinates and a duration between 0.01 and 60 seconds are required."));
        }

        return PostAsync(
            deviceId,
            "input/swipe",
            JsonContent.Create(swipe, MobileCanvasJsonContext.Default.DeviceSwipe),
            cancellationToken);
    }

    public Task<DeviceOperationResult> TypeTextAsync(
        string deviceId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (text is null || text.Length > 8192)
            return Task.FromResult(DeviceOperationResult.Failed("Text input must not exceed 8192 characters."));

        return PostAsync(
            deviceId,
            "input/text",
            JsonContent.Create(
                new DeviceTextInputPayload(text),
                MobileCanvasJsonContext.Default.DeviceTextInputPayload),
            cancellationToken);
    }

    public Task<DeviceOperationResult> PressKeyAsync(
        string deviceId,
        ulong keyCode,
        CancellationToken cancellationToken = default)
    {
        if (keyCode > 65535)
            return Task.FromResult(DeviceOperationResult.Failed("Key code must be between 0 and 65535."));

        return PostAsync(
            deviceId,
            "input/key",
            JsonContent.Create(
                new DeviceKeyInputPayload(keyCode),
                MobileCanvasJsonContext.Default.DeviceKeyInputPayload),
            cancellationToken);
    }

    public Task<DeviceOperationResult> PressButtonAsync(
        string deviceId,
        string button,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(button) || button.Length > 64)
            return Task.FromResult(DeviceOperationResult.Failed("A bounded device button name is required."));

        return PostAsync(
            deviceId,
            "input/button",
            JsonContent.Create(
                new DeviceButtonInputPayload(button),
                MobileCanvasJsonContext.Default.DeviceButtonInputPayload),
            cancellationToken);
    }

    public async Task<byte[]?> ScreenshotAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var state = GetCompatibleState();
        if (state is null || string.IsNullOrWhiteSpace(deviceId))
            return null;

        try
        {
            using var request = CreateRequest(HttpMethod.Get, state, $"/devices/{Uri.EscapeDataString(deviceId)}/screenshot");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;
            if (response.Content.Headers.ContentLength is > MaxScreenshotBytes)
                return null;

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (output.Length + read > MaxScreenshotBytes)
                    return null;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return output.ToArray();
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return null;
        }
    }

    public async Task<DeviceUiSnapshot?> CaptureUiAsync(
        string deviceId,
        string? appPackageId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;
        var query = string.IsNullOrWhiteSpace(appPackageId)
            ? ""
            : $"?appPackageId={Uri.EscapeDataString(appPackageId)}";
        var state = GetCompatibleState();
        if (state is null)
            return null;
        try
        {
            using var request = CreateRequest(
                HttpMethod.Get,
                state,
                $"/devices/{Uri.EscapeDataString(deviceId)}/ui/snapshot{query}");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentLength is > MaxUiSnapshotBytes)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var block = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(block, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (buffer.Length + read > MaxUiSnapshotBytes)
                    return null;
                await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            var snapshot = JsonSerializer.Deserialize(
                buffer.ToArray(),
                MobileCanvasJsonContext.Default.DeviceUiSnapshot);
            return IsValidUiSnapshot(snapshot) ? snapshot : null;
        }
        catch (Exception ex) when (IsTransport(ex) || ex is JsonException)
        {
            return null;
        }
    }

    public async Task<string?> DescribeForegroundAsync(
        string deviceId,
        string? appPackageId = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureUiAsync(deviceId, appPackageId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null ||
            string.IsNullOrWhiteSpace(snapshot.ForegroundOwner) ||
            snapshot.ForegroundOwner.Equals(appPackageId, StringComparison.OrdinalIgnoreCase))
            return null;
        return snapshot.ForegroundOwner;
    }

    private static bool IsValidUiSnapshot(DeviceUiSnapshot? snapshot)
    {
        if (snapshot is null ||
            string.IsNullOrWhiteSpace(snapshot.DeviceId) ||
            snapshot.DeviceId.Length > 256 ||
            snapshot.Elements is null ||
            snapshot.Elements.Length > 1000 ||
            snapshot.Limitations is null ||
            snapshot.Limitations.Length > 128 ||
            snapshot.Limitations.Any(item => item is null || item.Length > 512))
            return false;
        return snapshot.Elements.All(element =>
            element is not null &&
            element.Id.Length is > 0 and <= 256 &&
            (element.ParentId?.Length ?? 0) <= 256 &&
            (element.Role?.Length ?? 0) <= 128 &&
            (element.Type?.Length ?? 0) <= 128 &&
            (element.PackageId?.Length ?? 0) <= 256 &&
            (element.Bounds is null ||
             (double.IsFinite(element.Bounds.Value.X) &&
              double.IsFinite(element.Bounds.Value.Y) &&
              double.IsFinite(element.Bounds.Value.Width) &&
              double.IsFinite(element.Bounds.Value.Height) &&
              element.Bounds.Value.Width >= 0 &&
              element.Bounds.Value.Height >= 0)));
    }

    public async Task<DeviceOperationResult> SetPermissionAsync(
        string deviceId,
        string appPackageId,
        string permission,
        string state,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appPackageId) || string.IsNullOrWhiteSpace(permission))
        {
            return DeviceOperationResult.Failed(
                "An app package and permission are required.",
                DeviceOperationFailureKind.InvalidRequest);
        }

        var action = state?.Trim().ToLowerInvariant() switch
        {
            "granted" => "grant",
            "denied" => "revoke",
            _ => null,
        };
        if (action is null)
        {
            return DeviceOperationResult.Failed(
                "Permission state must be 'granted' or 'denied'.",
                DeviceOperationFailureKind.InvalidRequest);
        }

        var response = await SendJsonAsync(
            HttpMethod.Post,
            deviceId,
            "permissions",
            JsonContent.Create(
                new PermissionChangePayload(appPackageId, permission, action),
                MobileCanvasJsonContext.Default.PermissionChangePayload),
            MobileCanvasJsonContext.Default.PermissionChangeResponse,
            cancellationToken).ConfigureAwait(false);

        return response.Value?.Success == true
            ? DeviceOperationResult.Ok()
            : DeviceOperationResult.Failed(
                response.Error ?? "The permission change could not be confirmed.",
                response.FailureKind);
    }

    public async Task<DeviceOperationResult> SetLocationAsync(
        string deviceId,
        DeviceLocation? location,
        CancellationToken cancellationToken = default)
    {
        if (location is null)
        {
            return await SendOperationAsync(
                HttpMethod.Delete,
                deviceId,
                "hardware/location",
                content: null,
                cancellationToken).ConfigureAwait(false);
        }

        return await SendOperationAsync(
            HttpMethod.Post,
            deviceId,
            "hardware/location",
            JsonContent.Create(
                new DeviceLocationPayload(location.Latitude, location.Longitude),
                MobileCanvasJsonContext.Default.DeviceLocationPayload),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeviceOperationResult> SetNetworkAsync(
        string deviceId,
        string condition,
        CancellationToken cancellationToken = default)
    {
        var profile = condition.Trim().ToLowerInvariant() switch
        {
            "online" => "full",
            "offline" => null,
            var value when value.Length > 0 => value,
            _ => null,
        };
        if (profile is null)
        {
            return DeviceOperationResult.Failed(
                "The companion protocol cannot establish a verifiable offline network state.");
        }

        var response = await SendJsonAsync(
            HttpMethod.Post,
            deviceId,
            "hardware/network",
            JsonContent.Create(
                new NetworkPayload(profile),
                MobileCanvasJsonContext.Default.NetworkPayload),
            MobileCanvasJsonContext.Default.HardwareStateResponse,
            cancellationToken).ConfigureAwait(false);

        if (response.Value is null)
            return DeviceOperationResult.Failed(response.Error ?? "The network change could not be confirmed.");
        if (response.Value.NetworkIsIndicatorOnly)
        {
            return DeviceOperationResult.Failed(
                "The device changed only its network indicator; the app's connection was unchanged.");
        }

        return DeviceOperationResult.Ok();
    }

    public async Task<DeviceOperationResult> SetBatteryAsync(
        string deviceId,
        int percentage,
        CancellationToken cancellationToken = default)
    {
        if (percentage is < 0 or > 100)
            return DeviceOperationResult.Failed("Battery percentage must be between 0 and 100.");

        var response = await SendJsonAsync(
            HttpMethod.Post,
            deviceId,
            "hardware/battery",
            JsonContent.Create(
                new BatteryPayload(percentage),
                MobileCanvasJsonContext.Default.BatteryPayload),
            MobileCanvasJsonContext.Default.HardwareStateResponse,
            cancellationToken).ConfigureAwait(false);

        return response.Value?.BatteryLevel == percentage
            ? DeviceOperationResult.Ok()
            : DeviceOperationResult.Failed(response.Error ?? "The battery level could not be confirmed.");
    }

    public async Task<DeviceOperationResult> RotateAsync(
        string deviceId,
        string orientation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orientation))
            return DeviceOperationResult.Failed("An orientation is required.");
        var hostOrientation = ToHostOrientation(orientation);
        if (hostOrientation is null)
            return DeviceOperationResult.Failed($"Unsupported device orientation '{orientation}'.");

        var sent = await SendOperationAsync(
            HttpMethod.Post,
            deviceId,
            "input/rotate",
            JsonContent.Create(
                new RotatePayload(hostOrientation),
                MobileCanvasJsonContext.Default.RotatePayload),
            cancellationToken).ConfigureAwait(false);
        if (!sent.Success)
            return sent;

        var refreshed = await GetAsync(deviceId, cancellationToken).ConfigureAwait(false);
        return string.Equals(
            NormalizeOrientation(refreshed?.Display?.Orientation),
            NormalizeOrientation(hostOrientation),
            StringComparison.Ordinal)
            ? DeviceOperationResult.Ok(refreshed)
            : DeviceOperationResult.Failed("The requested orientation could not be confirmed.");
    }

    /// <summary>
    /// The directory DevFlow owns for device recordings. The host is told exactly where to write,
    /// and nothing outside this root is ever accepted back — a host that answers <c>stop</c> with
    /// some other path would otherwise have DevFlow copy and serve an arbitrary local file.
    /// </summary>
    public static string RecordingRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "maui-devflow",
        "device-recordings");

    /// <summary>
    /// How long an unclaimed recording may sit in <see cref="RecordingRoot"/> before it is swept.
    /// <para>
    /// DevFlow cannot see when the host finished writing a file it abandoned, so the only safe
    /// signal is age: a recording is bounded to one hour by <c>timeoutSeconds</c>, so anything
    /// whose last write is a day old is finished, whoever stopped caring about it. The sweep never
    /// touches a file written inside that window, so an in-progress recording — including one
    /// belonging to a different DevFlow process on this machine — is never deleted out from under
    /// its writer.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan AbandonedRecordingRetention = TimeSpan.FromHours(24);

    public async Task<DeviceOperationResult> StartRecordingAsync(
        string deviceId,
        int timeoutSeconds = 180,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds is < 1 or > 3600)
            return DeviceOperationResult.Failed("Recording timeout must be between 1 and 3600 seconds.");

        string outputPath;
        try
        {
            Directory.CreateDirectory(RecordingRoot);
            SweepAbandonedRecordings(RecordingRoot, AbandonedRecordingRetention, DateTimeOffset.UtcNow);
            outputPath = Path.Combine(RecordingRoot, $"{Guid.NewGuid():N}.mp4");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DeviceOperationResult.Failed(
                $"A DevFlow recording directory could not be prepared: {exception.Message}");
        }

        var response = await SendJsonAsync(
            HttpMethod.Post,
            deviceId,
            "recording/start",
            JsonContent.Create(
                new RecordingStartPayload(outputPath, timeoutSeconds),
                MobileCanvasJsonContext.Default.RecordingStartPayload),
            MobileCanvasJsonContext.Default.DeviceRecordingStatus,
            cancellationToken).ConfigureAwait(false);

        return response.Value?.IsRecording == true
            ? DeviceOperationResult.Ok()
            : DeviceOperationResult.Failed(response.Error ?? "Device recording did not start.");
    }

    public async Task<DeviceRecordingResult> StopRecordingAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync(
            HttpMethod.Post,
            deviceId,
            "recording/stop",
            null,
            MobileCanvasJsonContext.Default.DeviceRecordingStatus,
            cancellationToken).ConfigureAwait(false);
        if (response.Value is not { IsRecording: false, OutputPath.Length: > 0 })
        {
            return new DeviceRecordingResult(
                false,
                null,
                response.Error ?? "Device recording did not produce an artifact.");
        }

        var contained = ResolveContainedRecordingPath(response.Value.OutputPath);
        return contained is null
            ? new DeviceRecordingResult(
                false,
                null,
                "The device host reported a recording outside the DevFlow recording directory, so it was refused.")
            : new DeviceRecordingResult(true, contained);
    }

    /// <summary>
    /// Accepts a host-reported recording path only when it resolves inside
    /// <see cref="RecordingRoot"/> and carries the expected extension. Returns <c>null</c>
    /// otherwise, which callers must treat as "no recording" rather than as an error to work
    /// around.
    /// <para>
    /// Resolution follows symlinks, junctions, and every other reparse point to the final target
    /// and re-checks the root, and refuses Windows alternate data streams. A lexical check alone
    /// would let a link planted inside the root name any file on the machine, which DevFlow would
    /// then copy, hash, and serve to a browser.
    /// </para>
    /// </summary>
    public static string? ResolveContainedRecordingPath(string? reported) =>
        DeviceRecordingPathGuard.Resolve(reported, RecordingRoot);

    /// <summary>
    /// The same rule reached through <see cref="IDeviceRecordingPathAuthority"/>, so a caller
    /// holding only an <see cref="IDeviceSurface"/> can validate a recording without naming this
    /// implementation or its root.
    /// </summary>
    string? IDeviceRecordingPathAuthority.ResolveContainedRecordingPath(string? reported) =>
        ResolveContainedRecordingPath(reported);

    /// <summary>
    /// Deletes recordings older than <paramref name="retention"/> from a DevFlow-owned root.
    /// <para>
    /// Deliberately narrow: only files this root's own naming scheme produces, only ordinary files
    /// (a reparse point is skipped rather than followed, so a planted link can never make the sweep
    /// delete something outside the root), and never a file younger than the retention window. A
    /// failure to delete is not reported — a recording that cannot be swept is a disk-space
    /// question, not a correctness one.
    /// </para>
    /// </summary>
    internal static void SweepAbandonedRecordings(
        string root,
        TimeSpan retention,
        DateTimeOffset now)
    {
        var cutoff = now - retention;
        IEnumerable<string> candidates;
        try { candidates = Directory.EnumerateFiles(root, "*.mp4", SearchOption.TopDirectoryOnly); }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var info = new FileInfo(candidate);
                if (!info.Exists ||
                    info.LinkTarget is not null ||
                    info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) >= cutoff)
                {
                    continue;
                }
                info.Delete();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
    }

    public Task<DeviceRecordingStatus?> GetRecordingStatusAsync(
        string deviceId,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(deviceId)
            ? Task.FromResult<DeviceRecordingStatus?>(null)
            : GetJsonAsync(
                DevicePath(deviceId, "recording"),
                MobileCanvasJsonContext.Default.DeviceRecordingStatus,
                cancellationToken);

    public async Task<DeviceOperationResult> TapUiAsync(
        string deviceId,
        string? nativeId,
        string? text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nativeId) && string.IsNullOrWhiteSpace(text))
        {
            return DeviceOperationResult.Failed(
                "A native accessibility identifier or text is required.",
                DeviceOperationFailureKind.InvalidRequest);
        }

        var response = await SendJsonAsync(
            HttpMethod.Post,
            deviceId,
            "ui/tap",
            JsonContent.Create(
                new UiQueryPayload(
                    Text: string.IsNullOrWhiteSpace(nativeId) ? text : null,
                    Identifier: nativeId,
                    Role: null,
                    Exact: true,
                    InteractableOnly: true,
                    Limit: 2),
                MobileCanvasJsonContext.Default.UiQueryPayload),
            MobileCanvasJsonContext.Default.UiTapResponse,
            cancellationToken).ConfigureAwait(false);

        if (response.Value is { Success: true, Total: 1 })
            return DeviceOperationResult.Ok();
        if (response.Value is { Total: > 1 })
        {
            return DeviceOperationResult.Ambiguous(
                $"The native accessibility selector is ambiguous: it matched {response.Value.Total} elements.");
        }
        if (response.Value is { Total: 0 })
        {
            return DeviceOperationResult.NotFound(
                "The native accessibility selector did not match an element.");
        }

        return DeviceOperationResult.Failed(
            response.Error ?? "The native accessibility tap could not be confirmed.",
            response.FailureKind);
    }

    /// <summary>
    /// Builds a request carrying the host's control token.
    /// <para>
    /// The token is a bearer credential for a trusted local client, distinct from the single-use
    /// bootstrap secret the host issues to canvas panels. Every call needs it, so it is applied
    /// here rather than at each call site where one could be forgotten.
    /// </para>
    /// </summary>
    private static HttpRequestMessage CreateRequest(HttpMethod method, MobileCanvasHostState state, string path)
    {
        var request = new HttpRequestMessage(method, $"{state.BaseUrl}{ApiPrefix}{path}");
        if (!string.IsNullOrWhiteSpace(state.ControlToken))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", state.ControlToken);
        }

        return request;
    }

    private async Task<T?> GetJsonAsync<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var state = GetCompatibleState();
        if (state is null)
            return default;

        try
        {
            using var request = CreateRequest(HttpMethod.Get, state, path);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return default;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer
                    .DeserializeAsync(stream, typeInfo, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsTransport(ex) || ex is JsonException)
        {
            return default;
        }
    }

    private async Task<DeviceOperationResult> PostAsync(
        string deviceId,
        string action,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var ownedContent = content;
        if (string.IsNullOrWhiteSpace(deviceId))
            return DeviceOperationResult.Failed("A device id is required.");

        var state = GetCompatibleState();
        if (state is null)
            return DeviceOperationResult.NoHost();

        try
        {
            using var request = CreateRequest(HttpMethod.Post, state, $"/devices/{Uri.EscapeDataString(deviceId)}/{action}");
            request.Content = content;
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return DeviceOperationResult.Failed(
                    "The device host rejected DevFlow's control token. It was most likely restarted.");
            }

            if (!response.IsSuccessStatusCode)
                return DeviceOperationResult.Failed($"The device host refused {action} with {(int)response.StatusCode}.");

            return DeviceOperationResult.Ok();
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return DeviceOperationResult.Failed("The device host is not responding.");
        }
        finally
        {
            content?.Dispose();
        }
    }

    private async Task<DeviceOperationResult> SendOperationAsync(
        HttpMethod method,
        string deviceId,
        string action,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var ownedContent = content;
        if (string.IsNullOrWhiteSpace(deviceId))
            return DeviceOperationResult.Failed("A device id is required.");

        var state = GetCompatibleState();
        if (state is null)
            return DeviceOperationResult.NoHost();

        try
        {
            using var request = CreateRequest(
                method,
                state,
                $"/devices/{Uri.EscapeDataString(deviceId)}/{action}");
            request.Content = content;
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return DeviceOperationResult.Failed("The device host rejected DevFlow's control token.");
            return response.IsSuccessStatusCode
                ? DeviceOperationResult.Ok()
                : DeviceOperationResult.Failed($"The device host refused {action} with {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return DeviceOperationResult.Failed("The device host is not responding.");
        }
    }

    private async Task<DeviceOperationResult> SendOperationAtPathAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var ownedContent = content;
        var state = GetCompatibleState();
        if (state is null)
            return DeviceOperationResult.NoHost();

        try
        {
            using var request = CreateRequest(method, state, path);
            request.Content = content;
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return DeviceOperationResult.Failed("The device host rejected DevFlow's control token.");
            return response.IsSuccessStatusCode
                ? DeviceOperationResult.Ok()
                : DeviceOperationResult.Failed($"The device host refused the operation with {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return DeviceOperationResult.Failed("The device host is not responding.");
        }
    }

    private Task<(T? Value, string? Error, DeviceOperationFailureKind FailureKind)> SendJsonAsync<T>(
        HttpMethod method,
        string deviceId,
        string action,
        HttpContent? content,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Task.FromResult<(T?, string?, DeviceOperationFailureKind)>(
                (default, "A device id is required.", DeviceOperationFailureKind.InvalidRequest));
        }

        return SendJsonAtPathAsync(
            method,
            DevicePath(deviceId, action),
            content,
            typeInfo,
            cancellationToken);
    }

    private async Task<(T? Value, string? Error, DeviceOperationFailureKind FailureKind)> SendJsonAtPathAsync<T>(
        HttpMethod method,
        string path,
        HttpContent? content,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var ownedContent = content;
        var state = GetCompatibleState();
        if (state is null)
        {
            return (
                default,
                "No compatible device host is available, so device-level control is unavailable.",
                DeviceOperationFailureKind.Unavailable);
        }

        try
        {
            using var request = CreateRequest(method, state, path);
            request.Content = content;
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return (
                    default,
                    "The device host rejected DevFlow's control token.",
                    DeviceOperationFailureKind.Unauthorized);
            }
            if (!response.IsSuccessStatusCode)
            {
                return (
                    default,
                    $"The device host refused the operation with {(int)response.StatusCode}.",
                    DeviceOperationFailureKind.Rejected);
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var value = await JsonSerializer
                    .DeserializeAsync(stream, typeInfo, cancellationToken)
                    .ConfigureAwait(false);
                return value is null
                    ? (
                        default,
                        "The device host returned an empty response after accepting the operation.",
                        DeviceOperationFailureKind.UnknownCompletion)
                    : (value, null, DeviceOperationFailureKind.None);
            }
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return (
                default,
                "The device host stopped responding before completion could be confirmed.",
                DeviceOperationFailureKind.UnknownCompletion);
        }
        catch (JsonException)
        {
            return (
                default,
                "The device host returned an invalid response after accepting the operation.",
                DeviceOperationFailureKind.UnknownCompletion);
        }
    }

    private static string DevicePath(string deviceId, string? action = null)
    {
        var path = $"/devices/{Uri.EscapeDataString(deviceId)}";
        return string.IsNullOrWhiteSpace(action) ? path : $"{path}/{action}";
    }

    private static bool IsTransport(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException;

    private static string? ToHostOrientation(string value) =>
        NormalizeOrientation(value) switch
        {
            "portrait" => "portrait",
            "portraitupsidedown" => "portrait-upside-down",
            "landscapeleft" => "landscape-left",
            "landscaperight" => "landscape-right",
            _ => null,
        };

    private static string? NormalizeOrientation(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

    private MobileCanvasHostState? GetCompatibleState()
    {
        var state = _stateProvider();
        return MobileCanvasHost.IsTrustedForControl(state)
            ? state
            : null;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
