using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

// Platform error shaping is protocol, not policy: reuse the agent service helpers verbatim so a
// native backend produces byte-identical error payloads to the MAUI one.
using static Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// The .NET MAUI Essentials-backed implementations of the DevFlow preferences, secure storage,
/// device, permission, geolocation and sensor endpoints.
///
/// Essentials is usable without MAUI Controls, so this lives in a source file shared by the MAUI
/// agent and the optional add-on for plain .NET apps rather than being duplicated in both. It
/// deliberately holds no reference to the agent service: every member here depends only on
/// Essentials and the framework-neutral HTTP types.
/// </summary>
internal sealed class EssentialsAgentSupport
{
    public SensorManager Sensors { get; } = new();

    // ── Preferences endpoints ──

    private const string PreferencesKeyRegistryKey = "__devflow_known_keys";

    private const string PreferencesKeyRegistrySeparator = "\x1F"; // unit separator

    private HashSet<string> GetKnownPreferenceKeys(string? sharedName)
    {
        try
        {
            var raw = sharedName != null
                ? Preferences.Get(PreferencesKeyRegistryKey, "", sharedName)
                : Preferences.Get(PreferencesKeyRegistryKey, "");
            if (string.IsNullOrEmpty(raw)) return new HashSet<string>();
            return new HashSet<string>(raw.Split(PreferencesKeyRegistrySeparator, StringSplitOptions.RemoveEmptyEntries));
        }
        catch { return new HashSet<string>(); }
    }

    private void SaveKnownPreferenceKeys(HashSet<string> keys, string? sharedName)
    {
        var raw = string.Join(PreferencesKeyRegistrySeparator, keys);
        if (sharedName != null)
            Preferences.Set(PreferencesKeyRegistryKey, raw, sharedName);
        else
            Preferences.Set(PreferencesKeyRegistryKey, raw);
    }

    private void TrackPreferenceKey(string key, string? sharedName)
    {
        var keys = GetKnownPreferenceKeys(sharedName);
        if (keys.Add(key))
            SaveKnownPreferenceKeys(keys, sharedName);
    }

    private void UntrackPreferenceKey(string key, string? sharedName)
    {
        var keys = GetKnownPreferenceKeys(sharedName);
        if (keys.Remove(key))
            SaveKnownPreferenceKeys(keys, sharedName);
    }

    public Task<HttpResponse> HandlePreferencesList(HttpRequest request)
    {
        try
        {
            request.QueryParams.TryGetValue("sharedName", out var sharedName);
            var keys = GetKnownPreferenceKeys(sharedName);
            var entries = new List<object>();
            foreach (var key in keys.OrderBy(k => k))
            {
                var (value, type) = ReadPreferenceValue(key, sharedName);
                entries.Add(new { key, value, type, sharedName });
            }
            return Task.FromResult(HttpResponse.Json(new { keys = entries }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HttpResponse.Error($"Failed to list preferences: {ex.Message}"));
        }
    }

    /// <summary>
    /// Read a preference value trying all supported types.
    /// Android SharedPreferences stores typed values and throws ClassCastException
    /// if you read with the wrong type, so we try each in turn.
    /// </summary>
    private (object? Value, string Type) ReadPreferenceValue(string key, string? sharedName)
    {
        // Try string first (most common)
        try
        {
            var s = sharedName != null
                ? Preferences.Get(key, (string?)null, sharedName)
                : Preferences.Get(key, (string?)null);
            return (s, "string");
        }
        catch { }

        // Try int
        try
        {
            var i = sharedName != null
                ? Preferences.Get(key, int.MinValue, sharedName)
                : Preferences.Get(key, int.MinValue);
            return (i, "int");
        }
        catch { }

        // Try bool
        try
        {
            var b = sharedName != null
                ? Preferences.Get(key, false, sharedName)
                : Preferences.Get(key, false);
            return (b, "bool");
        }
        catch { }

        // Try double
        try
        {
            var d = sharedName != null
                ? Preferences.Get(key, double.NaN, sharedName)
                : Preferences.Get(key, double.NaN);
            if (!double.IsNaN(d)) return (d, "double");
        }
        catch { }

        // Try long
        try
        {
            var l = sharedName != null
                ? Preferences.Get(key, long.MinValue, sharedName)
                : Preferences.Get(key, long.MinValue);
            return (l, "long");
        }
        catch { }

        // Try float
        try
        {
            var f = sharedName != null
                ? Preferences.Get(key, float.NaN, sharedName)
                : Preferences.Get(key, float.NaN);
            if (!float.IsNaN(f)) return (f, "float");
        }
        catch { }

        return (null, "unknown");
    }

    public Task<HttpResponse> HandlePreferencesGet(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("key", out var key))
                return Task.FromResult(HttpResponse.Error("key is required"));

            request.QueryParams.TryGetValue("sharedName", out var sharedName);
            var requestedType = request.QueryParams.TryGetValue("type", out var typeVal) ? typeVal : null;

            object? value;
            string type;
            if (requestedType != null)
            {
                type = requestedType;
                value = type.ToLowerInvariant() switch
                {
                    "int" or "integer" => sharedName != null ? Preferences.Get(key, 0, sharedName) : Preferences.Get(key, 0),
                    "bool" or "boolean" => sharedName != null ? Preferences.Get(key, false, sharedName) : Preferences.Get(key, false),
                    "double" => sharedName != null ? Preferences.Get(key, 0.0, sharedName) : Preferences.Get(key, 0.0),
                    "float" => sharedName != null ? Preferences.Get(key, 0f, sharedName) : Preferences.Get(key, 0f),
                    "long" => sharedName != null ? Preferences.Get(key, 0L, sharedName) : Preferences.Get(key, 0L),
                    "datetime" => sharedName != null ? Preferences.Get(key, DateTime.MinValue, sharedName) : Preferences.Get(key, DateTime.MinValue),
                    _ => sharedName != null ? Preferences.Get(key, (string?)null, sharedName) : Preferences.Get(key, (string?)null),
                };
            }
            else
            {
                (value, type) = ReadPreferenceValue(key, sharedName);
            }

            var exists = sharedName != null ? Preferences.ContainsKey(key, sharedName) : Preferences.ContainsKey(key);
            return Task.FromResult(HttpResponse.Json(new { key, value, type, exists, sharedName }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HttpResponse.Error($"Failed to get preference: {ex.Message}"));
        }
    }

    public Task<HttpResponse> HandlePreferencesSet(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("key", out var key))
                return Task.FromResult(HttpResponse.Error("key is required"));

            var body = request.BodyAs<PreferenceSetRequest>();
            if (body == null)
                return Task.FromResult(HttpResponse.Error("Request body is required"));

            var type = body.Type ?? "string";
            var sharedName = body.SharedName;

            // STJ deserializes object? properties as JsonElement — extract the raw string for parsing
            var rawValue = body.Value is JsonElement je ? je.ToString() : body.Value?.ToString() ?? "";

            switch (type.ToLowerInvariant())
            {
                case "int" or "integer":
                    var intVal = int.Parse(rawValue);
                    if (sharedName != null) Preferences.Set(key, intVal, sharedName);
                    else Preferences.Set(key, intVal);
                    break;
                case "bool" or "boolean":
                    var boolVal = bool.Parse(rawValue);
                    if (sharedName != null) Preferences.Set(key, boolVal, sharedName);
                    else Preferences.Set(key, boolVal);
                    break;
                case "double":
                    var doubleVal = double.Parse(rawValue);
                    if (sharedName != null) Preferences.Set(key, doubleVal, sharedName);
                    else Preferences.Set(key, doubleVal);
                    break;
                case "float":
                    var floatVal = float.Parse(rawValue);
                    if (sharedName != null) Preferences.Set(key, floatVal, sharedName);
                    else Preferences.Set(key, floatVal);
                    break;
                case "long":
                    var longVal = long.Parse(rawValue);
                    if (sharedName != null) Preferences.Set(key, longVal, sharedName);
                    else Preferences.Set(key, longVal);
                    break;
                case "datetime":
                    var dtVal = DateTime.Parse(rawValue);
                    if (sharedName != null) Preferences.Set(key, dtVal, sharedName);
                    else Preferences.Set(key, dtVal);
                    break;
                default:
                    var strVal = rawValue;
                    if (sharedName != null) Preferences.Set(key, strVal, sharedName);
                    else Preferences.Set(key, strVal);
                    break;
            }

            TrackPreferenceKey(key, sharedName);
            return Task.FromResult(HttpResponse.Json(new { key, value = body.Value, type, sharedName }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HttpResponse.Error($"Failed to set preference: {ex.Message}"));
        }
    }

    public Task<HttpResponse> HandlePreferencesDelete(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("key", out var key))
                return Task.FromResult(HttpResponse.Error("key is required"));

            request.QueryParams.TryGetValue("sharedName", out var sharedName);

            if (sharedName != null)
                Preferences.Remove(key, sharedName);
            else
                Preferences.Remove(key);

            UntrackPreferenceKey(key, sharedName);
            return Task.FromResult(HttpResponse.Ok($"Preference '{key}' removed"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HttpResponse.Error($"Failed to remove preference: {ex.Message}"));
        }
    }

    public Task<HttpResponse> HandlePreferencesClear(HttpRequest request)
    {
        try
        {
            request.QueryParams.TryGetValue("sharedName", out var sharedName);

            if (sharedName != null)
                Preferences.Clear(sharedName);
            else
                Preferences.Clear();

            // Clear the key registry too
            SaveKnownPreferenceKeys(new HashSet<string>(), sharedName);
            return Task.FromResult(HttpResponse.Ok("All preferences cleared"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HttpResponse.Error($"Failed to clear preferences: {ex.Message}"));
        }
    }

    // ── Secure Storage endpoints ──

    public async Task<HttpResponse> HandleSecureStorageGet(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("key", out var key))
                return HttpResponse.Error("key is required");

            var value = await SecureStorage.GetAsync(key);
            return HttpResponse.Json(new { key, value, exists = value != null });
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"Failed to get secure storage value: {ex.Message}");
        }
    }

    public async Task<HttpResponse> HandleSecureStorageSet(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("key", out var key))
                return HttpResponse.Error("key is required");

            var body = request.BodyAs<SecureStorageSetRequest>();
            if (body?.Value == null)
                return HttpResponse.Error("value is required");

            await SecureStorage.SetAsync(key, body.Value);
            return HttpResponse.Json(new { key, value = body.Value });
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"Failed to set secure storage value: {ex.Message}");
        }
    }

    public Task<HttpResponse> HandleSecureStorageDelete(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("key", out var key))
                return Task.FromResult(HttpResponse.Error("key is required"));

            var removed = SecureStorage.Remove(key);
            return Task.FromResult(HttpResponse.Json(new { key, removed }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HttpResponse.Error($"Failed to remove secure storage value: {ex.Message}"));
        }
    }

    public Task<HttpResponse> HandleSecureStorageClear(HttpRequest request)
    {
        try
        {
            SecureStorage.RemoveAll();
            return Task.FromResult(HttpResponse.Ok("All secure storage entries cleared"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HttpResponse.Error($"Failed to clear secure storage: {ex.Message}"));
        }
    }

    public string GetAppDataBasePath()
        => FileSystem.AppDataDirectory;


    public Task<HttpResponse> HandlePlatformDeviceInfo(HttpRequest request)
    {
        try
        {
            var info = DeviceInfo.Current;
            return Task.FromResult(HttpResponse.Json(new
            {
                manufacturer = info.Manufacturer,
                model = info.Model,
                name = info.Name,
                platform = info.Platform.ToString(),
                idiom = info.Idiom.ToString(),
                deviceType = info.DeviceType.ToString(),
                osVersion = info.VersionString,
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreatePlatformError($"Failed to get device info: {ex.Message}", ex));
        }
    }

    public async Task<HttpResponse> HandlePlatformDeviceDisplay(HttpRequest request)
    {
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var display = DeviceDisplay.MainDisplayInfo;
                return HttpResponse.Json(new
                {
                    width = display.Width,
                    height = display.Height,
                    density = display.Density,
                    orientation = display.Orientation.ToString(),
                    rotation = display.Rotation.ToString(),
                    refreshRate = display.RefreshRate,
                });
            });
        }
        catch (Exception ex)
        {
            return CreatePlatformError($"Failed to get display info: {ex.Message}", ex);
        }
    }

    public Task<HttpResponse> HandlePlatformBattery(HttpRequest request)
    {
        try
        {
            var battery = Battery.Default;
            return Task.FromResult(HttpResponse.Json(new
            {
                chargeLevel = battery.ChargeLevel,
                state = battery.State.ToString(),
                powerSource = battery.PowerSource.ToString(),
                energySaverStatus = battery.EnergySaverStatus.ToString(),
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreatePlatformError($"Failed to get battery info: {ex.Message}", ex));
        }
    }

    public Task<HttpResponse> HandlePlatformConnectivity(HttpRequest request)
    {
        try
        {
            var connectivity = Connectivity.Current;
            return Task.FromResult(HttpResponse.Json(new
            {
                networkAccess = connectivity.NetworkAccess.ToString(),
                connectionProfiles = connectivity.ConnectionProfiles.Select(p => p.ToString()).ToList(),
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreatePlatformError($"Failed to get connectivity info: {ex.Message}", ex));
        }
    }

    public Task<HttpResponse> HandlePlatformVersionTracking(HttpRequest request)
    {
        try
        {
            var vt = VersionTracking.Default;
            return Task.FromResult(HttpResponse.Json(new
            {
                currentVersion = vt.CurrentVersion,
                currentBuild = vt.CurrentBuild,
                previousVersion = vt.PreviousVersion,
                previousBuild = vt.PreviousBuild,
                firstInstalledVersion = vt.FirstInstalledVersion,
                firstInstalledBuild = vt.FirstInstalledBuild,
                isFirstLaunchEver = vt.IsFirstLaunchEver,
                isFirstLaunchForCurrentVersion = vt.IsFirstLaunchForCurrentVersion,
                isFirstLaunchForCurrentBuild = vt.IsFirstLaunchForCurrentBuild,
                versionHistory = vt.VersionHistory.ToList(),
                buildHistory = vt.BuildHistory.ToList(),
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreatePlatformError($"Failed to get version tracking info: {ex.Message}", ex));
        }
    }

    private static readonly Dictionary<string, Func<Permissions.BasePermission>> KnownPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["camera"] = () => new Permissions.Camera(),
        ["locationWhenInUse"] = () => new Permissions.LocationWhenInUse(),
        ["locationAlways"] = () => new Permissions.LocationAlways(),
        ["microphone"] = () => new Permissions.Microphone(),
        ["photos"] = () => new Permissions.Photos(),
        ["sensors"] = () => new Permissions.Sensors(),
        ["speech"] = () => new Permissions.Speech(),
        ["storageRead"] = () => new Permissions.StorageRead(),
        ["storageWrite"] = () => new Permissions.StorageWrite(),
        ["calendar"] = () => new Permissions.CalendarRead(),
        ["calendarRead"] = () => new Permissions.CalendarRead(),
        ["calendarWrite"] = () => new Permissions.CalendarWrite(),
        ["contacts"] = () => new Permissions.ContactsRead(),
        ["contactsRead"] = () => new Permissions.ContactsRead(),
        ["contactsWrite"] = () => new Permissions.ContactsWrite(),
        ["flashlight"] = () => new Permissions.Flashlight(),
        ["networkState"] = () => new Permissions.NetworkState(),
        ["battery"] = () => new Permissions.Battery(),
        ["vibrate"] = () => new Permissions.Vibrate(),
    };

    public async Task<HttpResponse> HandlePlatformPermissions(HttpRequest request)
    {
        try
        {
            var results = new List<object>();
            foreach (var (name, factory) in KnownPermissions)
            {
                try
                {
                    var perm = factory();
                    var status = await perm.CheckStatusAsync();
                    results.Add(new { permission = name, status = status.ToString() });
                }
                catch
                {
                    results.Add(new { permission = name, status = "unavailable" });
                }
            }
            return HttpResponse.Json(new { permissions = results });
        }
        catch (Exception ex)
        {
            return CreatePlatformError($"Failed to check permissions: {ex.Message}", ex);
        }
    }

    public async Task<HttpResponse> HandlePlatformPermissionCheck(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("permission", out var permName))
                return HttpResponse.Error(
                    "permission name is required",
                    reason: PlatformErrorReasonInvalidRequest,
                    details: new Dictionary<string, object?> { ["parameter"] = "permission" });

            if (!KnownPermissions.TryGetValue(permName, out var factory))
                return HttpResponse.Error(
                    $"Unknown permission: {permName}. Valid: {string.Join(", ", KnownPermissions.Keys)}",
                    reason: PlatformErrorReasonInvalidRequest,
                    details: new Dictionary<string, object?> { ["parameter"] = "permission" });

            var perm = factory();
            var status = await perm.CheckStatusAsync();
            return HttpResponse.Json(new { permission = permName, status = status.ToString() });
        }
        catch (Exception ex)
        {
            return CreatePlatformError($"Failed to check permission: {ex.Message}", ex);
        }
    }

    public async Task<HttpResponse> HandlePlatformGeolocation(HttpRequest request)
    {
        try
        {
            var accuracyStr = request.QueryParams.GetValueOrDefault("accuracy", "Medium");
            var accuracy = accuracyStr.ToLowerInvariant() switch
            {
                "lowest" => GeolocationAccuracy.Lowest,
                "low" => GeolocationAccuracy.Low,
                "high" => GeolocationAccuracy.High,
                "best" => GeolocationAccuracy.Best,
                _ => GeolocationAccuracy.Medium,
            };

            var timeoutStr = request.QueryParams.GetValueOrDefault("timeout", "10");
            if (!int.TryParse(timeoutStr, out var timeoutSec)) timeoutSec = 10;

            var location = await Geolocation.GetLocationAsync(new GeolocationRequest(accuracy, TimeSpan.FromSeconds(timeoutSec)));

            if (location == null)
                return CreatePlatformError("Could not determine location", PlatformErrorReasonUnknown);

            return HttpResponse.Json(new
            {
                latitude = location.Latitude,
                longitude = location.Longitude,
                altitude = location.Altitude,
                accuracy = location.Accuracy,
                speed = location.Speed,
                course = location.Course,
                timestamp = location.Timestamp,
                isFromMockProvider = location.IsFromMockProvider,
            });
        }
        catch (PermissionException)
        {
            return CreatePlatformError("Location permission not granted", PlatformErrorReasonMissingPermission, 403);
        }
        catch (FeatureNotEnabledException)
        {
            return CreatePlatformError(
                "Location services not enabled on device",
                PlatformErrorReasonNotSupported,
                details: new Dictionary<string, object?>
                {
                    ["feature"] = "geolocation",
                    ["enabled"] = false
                });
        }
        catch (Exception ex)
        {
            return CreatePlatformError($"Failed to get location: {ex.Message}", ex);
        }
    }

    // ── Sensor endpoints ──

    public Task<HttpResponse> HandleSensorsList(HttpRequest request)
    {
        return Task.FromResult(HttpResponse.Json(Sensors.GetStatus()));
    }

    public Task<HttpResponse> HandleSensorStart(HttpRequest request)
    {
        if (!request.RouteParams.TryGetValue("sensor", out var sensorName))
            return Task.FromResult(HttpResponse.Error("sensor name is required"));

        var speedStr = request.QueryParams.GetValueOrDefault("speed", "UI");
        var speed = SensorManager.ParseSpeed(speedStr);

        var error = Sensors.Start(sensorName, speed);
        return Task.FromResult(error != null
            ? HttpResponse.Error(error)
            : HttpResponse.Ok($"Sensor '{sensorName}' started with speed {speed}"));
    }

    public Task<HttpResponse> HandleSensorStop(HttpRequest request)
    {
        if (!request.RouteParams.TryGetValue("sensor", out var sensorName))
            return Task.FromResult(HttpResponse.Error("sensor name is required"));

        var error = Sensors.Stop(sensorName);
        return Task.FromResult(error != null
            ? HttpResponse.Error(error)
            : HttpResponse.Ok($"Sensor '{sensorName}' stopped"));
    }

    public async Task HandleSensorWebSocket(
        System.Net.Sockets.TcpClient client,
        System.Net.Sockets.NetworkStream stream,
        HttpRequest request,
        CancellationToken ct)
    {
        // Parse sensor name from query param since WS routes don't support path params
        var sensorName = request.QueryParams.GetValueOrDefault("sensor");
        if (string.IsNullOrEmpty(sensorName))
        {
            await AgentHttpServer.WebSocketSendTextAsync(stream,
                JsonSerializer.Serialize(new
                {
                    type = "error",
                    timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    error = "sensor query parameter is required (e.g., ?sensor=accelerometer)"
                }), ct);
            return;
        }

        sensorName = sensorName.ToLowerInvariant();

        // Auto-start the sensor if not already running
        var speedStr = request.QueryParams.GetValueOrDefault("speed", "UI");
        var speed = SensorManager.ParseSpeed(speedStr);

        // Allow clients to override throttle interval (default 100ms)
        if (request.QueryParams.TryGetValue("throttleMs", out var throttleStr) &&
            int.TryParse(throttleStr, out var throttleMs) && throttleMs >= 0)
        {
            Sensors.ThrottleMs = throttleMs;
        }

        var startError = Sensors.Start(sensorName, speed);
        if (startError != null)
        {
            await AgentHttpServer.WebSocketSendTextAsync(stream,
                JsonSerializer.Serialize(new
                {
                    type = "error",
                    timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    error = startError
                }), ct);
            return;
        }

        // Subscribe to sensor readings
        var queue = Sensors.Subscribe(sensorName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            // Confirm subscription
            var subscribedAt = DateTimeOffset.UtcNow.ToString("O");
            await AgentHttpServer.WebSocketSendTextAsync(stream,
                JsonSerializer.Serialize(new
                {
                    type = "subscribed",
                    timestamp = subscribedAt,
                    sensor = new
                    {
                        name = sensorName,
                        available = Sensors.SupportedSensors.Contains(sensorName),
                        active = true
                    },
                    sensorName = sensorName,
                    speed = speed.ToString(),
                    throttleMs = Sensors.ThrottleMs
                }), ct);

            // Read loop (detects disconnection)
            var readTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var msg = await AgentHttpServer.WebSocketReadTextAsync(stream, cts.Token);
                    if (msg == null) { await cts.CancelAsync(); break; }
                }
            }, cts.Token);

            // Send loop — drain queue and send pings
            var lastPing = DateTime.UtcNow;
            while (!cts.Token.IsCancellationRequested)
            {
                while (queue.TryDequeue(out var reading))
                {
                    try
                    {
                        await AgentHttpServer.WebSocketSendTextAsync(stream, reading, cts.Token);
                    }
                    catch { await cts.CancelAsync(); break; }
                }

                if ((DateTime.UtcNow - lastPing).TotalSeconds >= 15)
                {
                    try
                    {
                        await AgentHttpServer.WebSocketSendPingAsync(stream, cts.Token);
                        lastPing = DateTime.UtcNow;
                    }
                    catch { await cts.CancelAsync(); break; }
                }

                try { await Task.Delay(20, cts.Token); }
                catch { break; }
            }

            await readTask;
        }
        finally
        {
            Sensors.Unsubscribe(sensorName, queue);
        }
    }
}
