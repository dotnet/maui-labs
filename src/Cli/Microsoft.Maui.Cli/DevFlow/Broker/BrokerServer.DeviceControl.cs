using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

public partial class BrokerServer
{
    private const int MaxDeviceControlBodyChars = 64 * 1024;
    private static readonly HashSet<string> SupportedDeviceButtons = new(StringComparer.Ordinal)
    {
        "home", "lock", "side-button", "siri", "apple-pay",
        "back", "apps", "power", "volume-up", "volume-down", "menu",
    };
    private static readonly HashSet<string> SupportedDeviceOrientations = new(StringComparer.Ordinal)
    {
        "portrait", "portrait-upside-down", "landscape-left", "landscape-right",
        DeviceOrientations.Portrait, DeviceOrientations.PortraitUpsideDown,
        DeviceOrientations.LandscapeLeft, DeviceOrientations.LandscapeRight,
    };

    private async Task<(int, string)> BuildDeviceCatalogResponse()
    {
        var health = await _devices.GetHealthAsync().ConfigureAwait(false);
        var catalog = await _devices.GetCatalogAsync().ConfigureAwait(false);
        var agents = _agents.Values.Select(connection => connection.Registration).ToArray();
        var paired = await _devices.ListPairedAsync(agents, forceRefresh: true).ConfigureAwait(false);
        var catalogNode = catalog is null
            ? null
            : JsonSerializer.SerializeToNode(catalog, DevFlowCliJsonContext.Default.DeviceCatalog)?.AsObject();

        return JsonResult(200, new JsonObject
        {
            ["available"] = health.Available,
            ["reason"] = health.Reason,
            ["schemaVersion"] = catalog?.SchemaVersion,
            ["devices"] = new JsonArray(paired.Select(BuildPairedDeviceNode).ToArray()),
            ["runtimes"] = catalogNode?["runtimes"]?.DeepClone() ?? new JsonArray(),
            ["deviceTypes"] = catalogNode?["deviceTypes"]?.DeepClone() ?? new JsonArray(),
            ["diagnostics"] = catalog is null
                ? new JsonArray()
                : new JsonArray(catalog.Diagnostics.Select(BuildSafeDiagnosticsNode).ToArray()),
        });
    }

    private async Task<(int, string)> BuildDeviceDetailResponse(string deviceId)
    {
        var agents = _agents.Values.Select(connection => connection.Registration).ToArray();
        var paired = await _devices.ListPairedAsync(agents, forceRefresh: true).ConfigureAwait(false);
        var match = paired.FirstOrDefault(candidate =>
            string.Equals(candidate.Device.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return JsonResult(404, new JsonObject { ["error"] = $"Device '{deviceId}' was not found." });

        return JsonResult(200, new JsonObject
        {
            ["ok"] = true,
            ["device"] = BuildPairedDeviceNode(match),
        });
    }

    private async Task<(int, string)> BuildDeviceRecordingStatusResponse(string deviceId)
    {
        var status = await _devices.GetRecordingStatusAsync(deviceId).ConfigureAwait(false);
        if (status is null)
        {
            return JsonResult(404, new JsonObject
            {
                ["ok"] = false,
                ["error"] = "Device recording status is unavailable.",
            });
        }

        return JsonResult(200, new JsonObject
        {
            ["ok"] = true,
            ["recording"] = BuildRecordingNode(status),
        });
    }

    private async Task<(int, string)> BuildExpandedDeviceControlResponse(HttpListenerContext context)
    {
        JsonObject body;
        try
        {
            var text = await ReadBoundedBodyAsync(
                context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8,
                MaxDeviceControlBodyChars);
            body = string.IsNullOrWhiteSpace(text)
                ? new JsonObject()
                : JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        catch (RequestBodyTooLargeException)
        {
            return JsonResult(413, new JsonObject { ["error"] = "Request body too large." });
        }
        catch (JsonException)
        {
            return JsonResult(400, new JsonObject { ["error"] = "Invalid JSON body." });
        }

        return await BuildExpandedDeviceControlResponse(context, body).ConfigureAwait(false);
    }

    private async Task<(int, string)> BuildExpandedDeviceControlResponse(
        HttpListenerContext context,
        JsonObject body)
    {
        var action = ReadBoundedString(body, "action", 64)?.ToLowerInvariant();
        var deviceId = ReadBoundedString(body, "deviceId", 512);
        var catalogMutation = string.Equals(action, "create", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(action) || (!catalogMutation && string.IsNullOrWhiteSpace(deviceId)))
            return JsonResult(400, new JsonObject { ["error"] = "A bounded action and deviceId are required." });

        var leaseId = context.Request.QueryString["leaseId"]?.Trim();
        if (string.IsNullOrWhiteSpace(leaseId) || leaseId.Length > 256)
            return JsonResult(400, new JsonObject { ["error"] = "A bounded leaseId is required." });

        var holderKind = context.Request.QueryString["holderKind"]?.Trim();
        var holderLabel = context.Request.QueryString["label"]?.Trim();
        var leaseKey = catalogMutation
            ? "device-catalog"
            : await ResolveStableDeviceLeaseKeyAsync(deviceId!, _cts?.Token ?? CancellationToken.None)
                .ConfigureAwait(false);
        if (leaseKey is null)
            return JsonResult(404, new JsonObject { ["error"] = $"Device '{deviceId}' was not found." });

        var effectiveHolderKind = string.IsNullOrWhiteSpace(holderKind) ? "web-inspector" : holderKind;
        var effectiveHolderLabel = string.IsNullOrWhiteSpace(holderLabel) ? "DevFlow Web Inspector" : holderLabel;
        var transactionId = Guid.NewGuid().ToString("N");
        var begun = _mutationLeases.ClaimAndBeginExclusive(
            leaseKey,
            leaseId,
            transactionId,
            effectiveHolderKind,
            effectiveHolderLabel,
            out var claimedForOperation);
        if (!string.Equals(begun.TransactionId, transactionId, StringComparison.Ordinal))
        {
            return JsonResult(409, new JsonObject
            {
                ["success"] = false,
                ["reason"] = begun.HeldByOther
                    ? $"Another session ({begun.Label ?? begun.HolderKind ?? "unknown"}) is driving this device."
                    : "Another device operation is already in progress for this session.",
                ["holderKind"] = begun.HolderKind,
                ["label"] = begun.Label,
                ["expiresInMs"] = begun.ExpiresInMs,
            });
        }

        try
        {
            return await ExecuteExpandedDeviceControlAsync(action, deviceId, body).ConfigureAwait(false);
        }
        finally
        {
            _mutationLeases.Control(
                leaseKey,
                "end",
                leaseId,
                effectiveHolderKind,
                effectiveHolderLabel,
                force: false,
                transactionId);
            if (claimedForOperation)
            {
                _mutationLeases.Control(
                    leaseKey,
                    "release",
                    leaseId,
                    effectiveHolderKind,
                    effectiveHolderLabel,
                    force: false);
            }
        }
    }

    private async Task<(int, string)> ExecuteExpandedDeviceControlAsync(
        string action,
        string? deviceId,
        JsonObject body)
    {
        DeviceOperationResult operation;
        DeviceRecordingResult? stoppedRecording = null;
        switch (action)
        {
            case "create":
                var request = new DeviceCreateRequest
                {
                    Platform = ReadBoundedString(body, "platform", 32) ?? "",
                    Name = ReadBoundedString(body, "name", 128) ?? "",
                    RuntimeId = ReadBoundedString(body, "runtimeId", 512) ?? "",
                    DeviceTypeId = ReadBoundedString(body, "deviceTypeId", 512) ?? "",
                };
                operation = await _devices.CreateAsync(request).ConfigureAwait(false);
                if (operation is { Success: true, Device.IsBooted: false } &&
                    !string.IsNullOrWhiteSpace(operation.Device.Id))
                {
                    var created = operation.Device;
                    var booted = await _devices.BootAsync(created.Id).ConfigureAwait(false);
                    if (!booted.Success)
                    {
                        operation = booted;
                        break;
                    }

                    var refreshed = booted.Device ?? await _devices.GetAsync(created.Id).ConfigureAwait(false);
                    if (refreshed is null ||
                        !string.Equals(refreshed.Id, created.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        refreshed = created with { State = DeviceStates.Booted };
                    }
                    operation = DeviceOperationResult.Ok(refreshed);
                }
                break;
            case "boot":
                operation = await _devices.BootAsync(deviceId!).ConfigureAwait(false);
                break;
            case "shutdown":
                operation = await _devices.ShutdownAsync(deviceId!).ConfigureAwait(false);
                break;
            case "restart":
                operation = await _devices.RestartAsync(deviceId!).ConfigureAwait(false);
                break;
            case "reveal":
                operation = await _devices.RevealAsync(deviceId!).ConfigureAwait(false);
                break;
            case "erase":
            case "delete":
                var confirmed = TryReadBoolean(body, "confirm", out var confirmation) &&
                    confirmation &&
                    string.Equals(
                        ReadBoundedString(body, "confirmedDeviceId", 512),
                        deviceId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        ReadBoundedRawString(body, "confirmationText", 600),
                        $"{action} {deviceId}",
                        StringComparison.Ordinal);
                if (!confirmed)
                {
                    operation = DeviceOperationResult.Failed(
                        $"{action} requires confirmation for this exact device.");
                    break;
                }
                operation = action == "erase"
                    ? await _devices.EraseAsync(deviceId!, confirm: true).ConfigureAwait(false)
                    : await _devices.DeleteAsync(deviceId!, confirm: true).ConfigureAwait(false);
                break;
            case "tap":
                operation = TryReadPoint(body, out var tapPoint, out var pointError)
                    ? await _devices.TapAsync(deviceId!, tapPoint).ConfigureAwait(false)
                    : DeviceOperationResult.Failed(pointError!);
                if (operation.Success)
                    await ObserveDeviceTapAsync(deviceId!, tapPoint).ConfigureAwait(false);
                break;
            case "long-press":
                operation = TryReadPoint(body, out var longPressPoint, out pointError) &&
                    TryReadFiniteDouble(body, "duration", out var holdDuration) &&
                    holdDuration is >= 0.1 and <= 60
                        ? await _devices.LongPressAsync(deviceId!, longPressPoint, holdDuration).ConfigureAwait(false)
                        : DeviceOperationResult.Failed(pointError ?? "Long press requires a duration between 0.1 and 60 seconds.");
                break;
            case "swipe":
                operation = TryReadSwipe(body, out var swipe, out var swipeError)
                    ? await _devices.SwipeAsync(deviceId!, swipe).ConfigureAwait(false)
                    : DeviceOperationResult.Failed(swipeError!);
                break;
            case "type-text":
                var text = ReadBoundedString(body, "text", 8192);
                operation = text is not null
                    ? await _devices.TypeTextAsync(deviceId!, text).ConfigureAwait(false)
                    : DeviceOperationResult.Failed("Text input must be a string of at most 8192 characters.");
                break;
            case "press-key":
                operation = TryReadUnsignedInteger(body, "keyCode", out var keyCode) && keyCode <= 65535
                    ? await _devices.PressKeyAsync(deviceId!, keyCode).ConfigureAwait(false)
                    : DeviceOperationResult.Failed("Key code must be between 0 and 65535.");
                break;
            case "press-button":
                var button = ReadBoundedString(body, "button", 64);
                operation = button is not null && SupportedDeviceButtons.Contains(button)
                    ? await _devices.PressButtonAsync(deviceId!, button).ConfigureAwait(false)
                    : DeviceOperationResult.Failed("The requested device button is not supported.");
                break;
            case "rotate":
                var orientation = ReadBoundedString(body, "orientation", 64);
                operation = orientation is not null && SupportedDeviceOrientations.Contains(orientation)
                    ? await _devices.RotateAsync(deviceId!, orientation).ConfigureAwait(false)
                    : DeviceOperationResult.Failed("The requested device orientation is not supported.");
                break;
            case "recording-start":
                var timeout = TryReadInteger(body, "timeoutSeconds", out var requestedTimeout)
                    ? requestedTimeout
                    : 180;
                operation = timeout is >= 1 and <= 3600
                    ? await _devices.StartRecordingAsync(deviceId!, timeout).ConfigureAwait(false)
                    : DeviceOperationResult.Failed("Recording timeout must be between 1 and 3600 seconds.");
                break;
            case "recording-stop":
                stoppedRecording = await _devices.StopRecordingAsync(deviceId!).ConfigureAwait(false);
                operation = stoppedRecording.Success
                    ? DeviceOperationResult.Ok()
                    : DeviceOperationResult.Failed(stoppedRecording.Reason ?? "Device recording did not stop.");
                break;
            default:
                return JsonResult(404, new JsonObject { ["error"] = $"Unknown device action '{action}'." });
        }

        var response = new JsonObject
        {
            ["success"] = operation.Success,
            ["reason"] = operation.Reason,
        };
        if (operation.Success && action != "delete")
        {
            var device = operation.Device ?? (deviceId is null
                ? null
                : await _devices.GetAsync(deviceId).ConfigureAwait(false));
            if (device is not null)
                response["device"] = BuildDeviceNode(device);
        }
        if (action.StartsWith("recording-", StringComparison.Ordinal) && deviceId is not null)
        {
            var status = await _devices.GetRecordingStatusAsync(deviceId).ConfigureAwait(false);
            if (status is not null)
                response["recording"] = BuildRecordingNode(status);
            if (!string.IsNullOrWhiteSpace(stoppedRecording?.Path))
                response["artifactName"] = Path.GetFileName(stoppedRecording.Path);
        }

        return JsonResult(operation.Success ? 200 : 400, response);
    }

    private async Task ObserveDeviceTapAsync(string deviceId, DevicePoint point)
    {
        var agents = _agents.Values.Select(connection => connection.Registration).ToArray();
        var paired = await _devices.ListPairedAsync(agents).ConfigureAwait(false);
        var target = paired.FirstOrDefault(entry =>
            string.Equals(entry.Device.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(target?.AgentId))
            _flows.ObserveDeviceTap(target.AgentId, point.X, point.Y);
    }

    private static JsonObject BuildPairedDeviceNode(PairedDevice entry)
    {
        var node = BuildDeviceNode(entry.Device);
        node["agentId"] = entry.AgentId;
        node["agentPort"] = entry.AgentPort;
        node["pairing"] = entry.MatchConfidence.ToString().ToLowerInvariant();
        return node;
    }

    private static JsonObject BuildDeviceNode(DeviceTarget device)
    {
        var node = JsonSerializer.SerializeToNode(
            device,
            DevFlowCliJsonContext.Default.DeviceTarget)?.AsObject() ?? new JsonObject();
        node["isBooted"] = device.IsBooted;
        node.Remove("deviceSet");
        node["capabilities"] = DeviceCapabilityProjection.Create(device.Capabilities);
        return node;
    }

    private static JsonObject BuildSafeDiagnosticsNode(DeviceHostDiagnostics diagnostics) =>
        new()
        {
            ["platform"] = diagnostics.Platform,
            ["ready"] = diagnostics.Ready,
            ["checks"] = new JsonArray(diagnostics.Checks.Select(check =>
                new JsonObject
                {
                    ["name"] = check.Name,
                    ["status"] = check.Status,
                    ["message"] = check.Message,
                    ["version"] = check.Version,
                    ["actions"] = new JsonArray(check.Actions.Select(action =>
                        new JsonObject
                        {
                            ["type"] = action.Type,
                            ["target"] = action.Target,
                            ["label"] = action.Label,
                        }).ToArray()),
                }).ToArray()),
        };

    private static JsonObject BuildRecordingNode(DeviceRecordingStatus status) =>
        new()
        {
            ["deviceId"] = status.DeviceId,
            ["isRecording"] = status.IsRecording,
            ["hasArtifact"] = !string.IsNullOrWhiteSpace(status.OutputPath),
            ["artifactName"] = string.IsNullOrWhiteSpace(status.OutputPath)
                ? null
                : Path.GetFileName(status.OutputPath),
            ["startedAt"] = status.StartedAt,
            ["timeoutSeconds"] = status.TimeoutSeconds,
        };

    private static bool TryParseDeviceDetailPath(string path, out string deviceId)
    {
        deviceId = "";
        const string prefix = "/api/devices/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var remainder = path[prefix.Length..];
        if (string.IsNullOrWhiteSpace(remainder) || remainder.Contains('/'))
            return false;
        deviceId = Uri.UnescapeDataString(remainder);
        return deviceId.Length is > 0 and <= 512;
    }

    private static bool TryParseDeviceResourcePath(string path, string resource, out string deviceId)
    {
        deviceId = "";
        var trimmed = path.TrimEnd('/');
        var suffix = "/" + resource;
        if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;
        return TryParseDeviceDetailPath(trimmed[..^suffix.Length], out deviceId);
    }

    private static bool TryReadPoint(JsonObject body, out DevicePoint point, out string? error)
    {
        if (TryReadFiniteDouble(body, "x", out var x) &&
            TryReadFiniteDouble(body, "y", out var y) &&
            x >= 0 &&
            y >= 0)
        {
            point = new DevicePoint(x, y);
            error = null;
            return true;
        }

        point = default;
        error = "Device coordinates must be finite and non-negative.";
        return false;
    }

    private static bool TryReadSwipe(JsonObject body, out DeviceSwipe swipe, out string? error)
    {
        if (TryReadFiniteDouble(body, "startX", out var startX) &&
            TryReadFiniteDouble(body, "startY", out var startY) &&
            TryReadFiniteDouble(body, "endX", out var endX) &&
            TryReadFiniteDouble(body, "endY", out var endY) &&
            TryReadFiniteDouble(body, "duration", out var duration) &&
            startX >= 0 &&
            startY >= 0 &&
            endX >= 0 &&
            endY >= 0 &&
            duration is >= 0.01 and <= 60)
        {
            swipe = new DeviceSwipe
            {
                StartX = startX,
                StartY = startY,
                EndX = endX,
                EndY = endY,
                Duration = duration,
            };
            error = null;
            return true;
        }

        swipe = new DeviceSwipe();
        error = "Swipe coordinates must be finite and non-negative, with a duration between 0.01 and 60 seconds.";
        return false;
    }

    private static bool TryReadFiniteDouble(JsonObject body, string name, out double value)
    {
        value = 0;
        try
        {
            value = body[name]?.GetValue<double>() ?? double.NaN;
            return double.IsFinite(value);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static bool TryReadInteger(JsonObject body, string name, out int value)
    {
        value = 0;
        try
        {
            value = body[name]?.GetValue<int>() ?? 0;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static bool TryReadUnsignedInteger(JsonObject body, string name, out ulong value)
    {
        value = 0;
        try
        {
            value = body[name]?.GetValue<ulong>() ?? 0;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static bool TryReadBoolean(JsonObject body, string name, out bool value)
    {
        value = false;
        try
        {
            value = body[name]?.GetValue<bool>() ?? false;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static string? ReadBoundedString(JsonObject body, string name, int maximumLength)
    {
        try
        {
            var value = body[name]?.GetValue<string>()?.Trim();
            return string.IsNullOrWhiteSpace(value) || value.Length > maximumLength ? null : value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string? ReadBoundedRawString(JsonObject body, string name, int maximumLength)
    {
        try
        {
            var value = body[name]?.GetValue<string>();
            return string.IsNullOrEmpty(value) || value.Length > maximumLength ? null : value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static (int, string) JsonResult(int statusCode, JsonObject body) =>
        (statusCode, CliJson.SerializeUntyped(body, indented: false));
}
