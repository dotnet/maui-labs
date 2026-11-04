using System.Globalization;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class DevFlowAgentService
{
    private const double MaxPublishedActionCoordinate = 100_000;
    private const int MaxPublishedActionDurationMs = 10 * 60 * 1000;
    protected const int MaxUiBatchActions = 1000;

    protected sealed class UiActionDescriptor
    {
        public required string Kind { get; init; }
        public string? ElementId { get; init; }
        public string? ToElementId { get; init; }
        public double? X { get; init; }
        public double? Y { get; init; }
        public double? ToX { get; init; }
        public double? ToY { get; init; }
        public double? DeltaX { get; init; }
        public double? DeltaY { get; init; }
        public double? Distance { get; init; }
        public double? OriginX { get; init; }
        public double? OriginY { get; init; }
        public string? Direction { get; init; }
        public int? DurationMs { get; init; }
        public int? HoldMs { get; init; }
        public long? CaptureEpoch { get; init; }
        public long? RegistryGeneration { get; init; }
        public int? WindowId { get; init; }
        public string? DiagnosticInputsRevision { get; init; }
    }

    protected virtual string? GetUiActionDiagnosticInputsRevision(
        long? captureEpoch,
        long? registryGeneration,
        int? windowId)
        => null;

    protected Task<HttpResponse> ExecuteUiActionMutationAsync(
        HttpRequest request,
        string kind,
        Func<HttpRequest, Task<HttpResponse>> handler)
        => ExecuteUiMutationAsync(
            request,
            currentRequest => ExecuteUiActionLifecycleAsync(
                currentRequest,
                CreateUiActionDescriptor(kind, currentRequest),
                () => handler(currentRequest)));

    private Task<HttpResponse> ExecutePointerTerminatingActionMutationAsync(
        HttpRequest request,
        string kind,
        Func<HttpRequest, Task<HttpResponse>> handler)
        => ExecutePointerTerminatingMutationAsync(
            request,
            currentRequest => ExecuteUiActionLifecycleAsync(
                currentRequest,
                CreateUiActionDescriptor(kind, currentRequest),
                () => handler(currentRequest)));

    protected async Task<HttpResponse> ExecuteUiActionLifecycleAsync(
        HttpRequest request,
        UiActionDescriptor action,
        Func<Task<HttpResponse>> handler)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var correlation = CreateUiActionCorrelation(request);
        PublishUiEvent(
            "ui-action-started",
            CreateUiActionEventData(action, correlation));

        try
        {
            var response = await handler().ConfigureAwait(false);
            var success = response.StatusCode is >= 200 and < 300;
            PublishUiEvent(
                "ui-action-completed",
                CreateUiActionEventData(
                    action,
                    correlation,
                    success,
                    success ? "completed" : "failed",
                    success ? null : GetUiActionFailureReason(response),
                    response.StatusCode,
                    startedAt));
            return response;
        }
        catch (OperationCanceledException)
        {
            PublishUiEvent(
                "ui-action-completed",
                CreateUiActionEventData(
                    action,
                    correlation,
                    success: false,
                    outcome: "cancelled",
                    reason: "cancelled",
                    statusCode: null,
                    startedAt));
            throw;
        }
        catch
        {
            PublishUiEvent(
                "ui-action-completed",
                CreateUiActionEventData(
                    action,
                    correlation,
                    success: false,
                    outcome: "failed",
                    reason: "action-exception",
                    statusCode: null,
                    startedAt));
            throw;
        }
    }

    protected async Task<HttpResponse> ExecuteBatchActionsWithLifecycleAsync(
        HttpRequest request,
        BatchRequest body,
        Func<BatchActionRequest, Task<HttpResponse>> executeAction)
    {
        var results = new List<object>(body.Actions.Count);
        var allSucceeded = true;

        foreach (var action in body.Actions)
        {
            var actionName = NormalizeUiActionKind(action.Action ?? action.Type);
            var descriptor = CreateUiActionDescriptor(
                actionName,
                action,
                body.CaptureEpoch,
                body.RegistryGeneration,
                ReadWindowId(request, default));
            var lifecycleRequest = new HttpRequest
            {
                Method = request.Method,
                Path = request.Path,
                Headers = new Dictionary<string, string>(
                    request.Headers,
                    StringComparer.OrdinalIgnoreCase),
                MutationLease = request.MutationLease,
                CancellationToken = request.CancellationToken
            };
            // A batch request is only a container. Each logical action gets its own identity.
            lifecycleRequest.Headers.Remove("X-DevFlow-Action-Id");
            var response = await ExecuteUiActionLifecycleAsync(
                lifecycleRequest,
                descriptor,
                () => executeAction(action));

            var succeeded = response.StatusCode < 400;
            allSucceeded &= succeeded;
            results.Add(new Dictionary<string, object?>
            {
                ["action"] = actionName,
                ["success"] = succeeded,
                ["statusCode"] = response.StatusCode,
                ["response"] = response.Body
            });

            if (!succeeded && !body.ContinueOnError)
                break;
        }

        return HttpResponse.Json(new Dictionary<string, object?>
        {
            ["success"] = allSucceeded,
            ["results"] = results
        });
    }

    protected Task<HttpResponse> ExecuteBatchActionHandlerAsync(
        BatchActionRequest action,
        object? mutationState = null)
    {
        var actionName = NormalizeUiActionKind(action.Action ?? action.Type);
        return actionName switch
        {
            "tap" => HandleTap(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                Body = AgentJson.Serialize(new TapRequest
                {
                    ElementId = action.ElementId,
                    X = action.X,
                    Y = action.Y
                })
            }),
            "fill" => HandleFill(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                Body = AgentJson.Serialize(new FillRequest
                {
                    ElementId = action.ElementId,
                    Text = action.Text ?? string.Empty
                })
            }),
            "clear" => HandleClear(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                Body = AgentJson.Serialize(new ActionRequest { ElementId = action.ElementId })
            }),
            "focus" => HandleFocus(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                Body = AgentJson.Serialize(new ActionRequest { ElementId = action.ElementId })
            }),
            "navigate" => HandleNavigate(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                Body = AgentJson.Serialize(new NavigateRequest { Route = action.Route ?? string.Empty })
            }),
            "resize" => HandleResize(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                Body = AgentJson.Serialize(new ResizeRequest(action.Width, action.Height))
            }),
            "scroll" => HandleScroll(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                Body = AgentJson.Serialize(new ScrollRequest
                {
                    ElementId = action.ElementId,
                    DeltaX = action.DeltaX ?? 0,
                    DeltaY = action.DeltaY ?? 0,
                    ItemIndex = action.ItemIndex,
                    GroupIndex = action.GroupIndex,
                    ScrollToPosition = action.ScrollToPosition,
                    Animated = action.Animated
                })
            }),
            "back" => HandleBack(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState
            }),
            "key" => HandleKey(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                Body = AgentJson.Serialize(new KeyActionRequest
                {
                    ElementId = action.ElementId,
                    Key = action.Key,
                    Text = action.Text
                })
            }),
            "gesture" => HandleGesture(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                Body = AgentJson.Serialize(new GestureActionRequest
                {
                    ElementId = action.ElementId,
                    Type = action.Type ?? action.Action,
                    Direction = action.Direction,
                    Distance = action.Distance,
                    DurationMs = action.DurationMs,
                    Scale = action.Scale,
                    Rotation = action.Rotation,
                    DeltaX = action.DeltaX,
                    DeltaY = action.DeltaY,
                    OriginX = action.OriginX,
                    OriginY = action.OriginY,
                    X = action.X,
                    Y = action.Y,
                    ToX = action.ToX,
                    ToY = action.ToY,
                    ToElementId = action.ToElementId,
                    HoldMs = action.HoldMs,
                    Steps = action.Steps
                })
            }),
            "set-property" or "set_property" => HandleSetProperty(new HttpRequest
            {
                Method = "PUT",
                MutationState = mutationState,
                RouteParams = new Dictionary<string, string>
                {
                    ["id"] = action.ElementId ?? string.Empty,
                    ["name"] = action.Property ?? string.Empty
                },
                Body = AgentJson.Serialize(new SetPropertyRequest
                {
                    Value = action.Value ?? string.Empty
                })
            }),
            "invoke-action" or "invoke_action" => HandleInvokeAction(new HttpRequest
            {
                Method = "POST",
                MutationState = mutationState,
                RouteParams = new Dictionary<string, string>
                {
                    ["name"] = action.Name ?? string.Empty
                },
                Body = AgentJson.Serialize(new InvokeActionRequest { Args = action.Args })
            }),
            _ => Task.FromResult(HttpResponse.Error($"Unsupported batch action '{actionName}'"))
        };
    }

    private UiActionDescriptor CreateUiActionDescriptor(
        string kind,
        HttpRequest request)
    {
        JsonElement body = default;
        JsonDocument? document = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(request.Body))
            {
                document = JsonDocument.Parse(request.Body);
                body = document.RootElement;
            }

            var normalizedKind = NormalizeUiActionKind(kind);
            if (normalizedKind == "gesture")
            {
                var gestureKind = ReadJsonString(body, "type");
                if (!string.IsNullOrWhiteSpace(gestureKind))
                    normalizedKind = NormalizeUiActionKind(gestureKind);
            }

            var captureEpoch = ReadPositiveInt64(body, "captureEpoch");
            var registryGeneration = ReadNonNegativeInt64(body, "registryGeneration");
            var windowId = ReadWindowId(request, body);
            return new UiActionDescriptor
            {
                Kind = normalizedKind,
                ElementId = ReadBoundedIdentifier(
                    ReadJsonString(body, "elementId")
                    ?? request.RouteParams.GetValueOrDefault("id")),
                ToElementId = ReadBoundedIdentifier(
                    ReadJsonString(body, "toElementId")),
                X = ReadActionNumber(request, body, "X", "x"),
                Y = ReadActionNumber(request, body, "Y", "y"),
                ToX = ReadActionNumber(request, body, "To-X", "toX"),
                ToY = ReadActionNumber(request, body, "To-Y", "toY"),
                DeltaX = ReadActionNumber(request, body, "Delta-X", "deltaX"),
                DeltaY = ReadActionNumber(request, body, "Delta-Y", "deltaY"),
                Distance = ReadBoundedDouble(body, "distance"),
                OriginX = ReadBoundedDouble(body, "originX"),
                OriginY = ReadBoundedDouble(body, "originY"),
                Direction = ReadSafeDirection(body),
                DurationMs = ReadBoundedInt(body, "durationMs"),
                HoldMs = ReadBoundedInt(body, "holdMs"),
                CaptureEpoch = captureEpoch,
                RegistryGeneration = registryGeneration,
                WindowId = windowId,
                DiagnosticInputsRevision = ReadSafeToken(
                    GetUiActionDiagnosticInputsRevision(
                        captureEpoch,
                        registryGeneration,
                        windowId),
                    128)
            };
        }
        catch (JsonException)
        {
            return new UiActionDescriptor { Kind = NormalizeUiActionKind(kind) };
        }
        finally
        {
            document?.Dispose();
        }
    }

    private UiActionDescriptor CreateUiActionDescriptor(
        string actionName,
        BatchActionRequest action,
        long? captureEpoch,
        long? registryGeneration,
        int? windowId)
    {
        var kind = actionName == "gesture"
            ? NormalizeUiActionKind(action.Type)
            : actionName;
        return new UiActionDescriptor
        {
            Kind = kind,
            ElementId = ReadBoundedIdentifier(action.ElementId),
            ToElementId = ReadBoundedIdentifier(action.ToElementId),
            X = Bounded(action.X),
            Y = Bounded(action.Y),
            ToX = Bounded(action.ToX),
            ToY = Bounded(action.ToY),
            DeltaX = Bounded(action.DeltaX),
            DeltaY = Bounded(action.DeltaY),
            Distance = Bounded(action.Distance),
            OriginX = Bounded(action.OriginX),
            OriginY = Bounded(action.OriginY),
            Direction = NormalizeDirection(action.Direction),
            DurationMs = BoundedDuration(action.DurationMs),
            HoldMs = BoundedDuration(action.HoldMs),
            CaptureEpoch = captureEpoch is > 0 ? captureEpoch : null,
            RegistryGeneration = registryGeneration is >= 0 ? registryGeneration : null,
            WindowId = windowId,
            DiagnosticInputsRevision = ReadSafeToken(
                GetUiActionDiagnosticInputsRevision(
                    captureEpoch,
                    registryGeneration,
                    windowId),
                128)
        };
    }

    private static Dictionary<string, object?> CreateUiActionCorrelation(HttpRequest request)
    {
        var source = ResolveUiActionSource(request);
        var actionId = ReadSafeHeader(request, "X-DevFlow-Action-Id")
            ?? Guid.NewGuid().ToString("N");
        return new Dictionary<string, object?>
        {
            ["actionId"] = actionId,
            ["source"] = source,
            ["streamId"] = ReadSafeHeader(request, "X-DevFlow-Action-Stream-Id"),
            ["frameId"] = ReadSafeHeader(request, "X-DevFlow-Action-Frame-Id"),
            ["frameRevision"] = ReadPositiveHeaderInt64(
                request,
                "X-DevFlow-Action-Frame-Revision"),
            ["coordinateSpace"] = ReadCoordinateSpace(request)
        }.Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static Dictionary<string, object?> CreatePointerActionCorrelation(
        HttpRequest request,
        PointerActionCorrelation? pointerCorrelation)
    {
        var correlation = CreateUiActionCorrelation(request);
        if (pointerCorrelation is null)
            return correlation;

        var actionId = ReadSafeToken(pointerCorrelation.ActionId, 128);
        var source = pointerCorrelation.Source?.Trim().ToLowerInvariant() switch
        {
            "inspector" => "inspector",
            "cli" => "cli",
            "mcp" => "mcp",
            "replay" => "replay",
            _ => null
        };
        var streamId = ReadSafeToken(pointerCorrelation.StreamId, 128);
        var frameId = ReadSafeToken(pointerCorrelation.FrameId, 128);
        var coordinateSpace = pointerCorrelation.CoordinateSpace switch
        {
            "appLogical" => "appLogical",
            "windowLogical" => "windowLogical",
            "screenshotPixel" => "screenshotPixel",
            "inspectorCss" => "inspectorCss",
            "transformedViewport" => "transformedViewport",
            _ => null
        };

        if (actionId is not null) correlation["actionId"] = actionId;
        if (source is not null) correlation["source"] = source;
        if (streamId is not null) correlation["streamId"] = streamId;
        if (frameId is not null) correlation["frameId"] = frameId;
        if (pointerCorrelation.FrameRevision is > 0)
            correlation["frameRevision"] = pointerCorrelation.FrameRevision;
        if (coordinateSpace is not null)
            correlation["coordinateSpace"] = coordinateSpace;
        return correlation;
    }

    private static Dictionary<string, object?> CreateUiActionEventData(
        UiActionDescriptor action,
        Dictionary<string, object?> correlation,
        bool? success = null,
        string? outcome = null,
        string? reason = null,
        int? statusCode = null,
        DateTimeOffset? startedAt = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["correlation"] = correlation,
            ["kind"] = action.Kind,
            ["elementId"] = action.ElementId,
            ["toElementId"] = action.ToElementId,
            ["x"] = action.X,
            ["y"] = action.Y,
            ["toX"] = action.ToX,
            ["toY"] = action.ToY,
            ["deltaX"] = action.DeltaX,
            ["deltaY"] = action.DeltaY,
            ["distance"] = action.Distance,
            ["originX"] = action.OriginX,
            ["originY"] = action.OriginY,
            ["direction"] = action.Direction,
            ["durationMs"] = action.DurationMs,
            ["holdMs"] = action.HoldMs,
            ["captureEpoch"] = action.CaptureEpoch,
            ["registryGeneration"] = action.RegistryGeneration,
            ["windowId"] = action.WindowId,
            ["diagnosticInputsRevision"] = action.DiagnosticInputsRevision,
            ["success"] = success,
            ["outcome"] = outcome,
            ["reason"] = reason,
            ["statusCode"] = statusCode,
            ["elapsedMs"] = startedAt.HasValue
                ? BoundedElapsedMilliseconds(startedAt.Value)
                : null
        };
        return data.Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static int BoundedElapsedMilliseconds(DateTimeOffset startedAt)
    {
        var elapsed = Math.Max(
            0,
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        return elapsed >= int.MaxValue
            ? int.MaxValue
            : (int)Math.Round(elapsed);
    }

    private static string ResolveUiActionSource(HttpRequest request)
    {
        var holder = request.MutationLease?.HolderKind;
        if (string.IsNullOrWhiteSpace(holder))
            request.Headers.TryGetValue("X-DevFlow-Holder", out holder);

        var source = holder?.Trim().ToLowerInvariant() switch
        {
            "mcp" => "mcp",
            "cli" => "cli",
            "web" or "web-inspector" or "inspector" or "vscode" or "canvas" => "inspector",
            _ => "cli"
        };

        // Replay is a presentation distinction, never an authorization signal. The admitted
        // mutation holder remains authoritative for lease ownership.
        if (request.Headers.TryGetValue("X-DevFlow-Action-Source", out var requestedSource)
            && string.Equals(requestedSource?.Trim(), "replay", StringComparison.OrdinalIgnoreCase))
        {
            return "replay";
        }

        return source;
    }

    private static string GetUiActionFailureReason(HttpResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Body))
        {
            try
            {
                using var document = JsonDocument.Parse(response.Body);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String
                    && ReadSafeToken(reason.GetString(), 80) is { } safeReason)
                {
                    return safeReason;
                }
            }
            catch (JsonException)
            {
            }
        }

        return response.StatusCode switch
        {
            408 => "timeout",
            409 => "conflict",
            429 => "rate-limited",
            >= 500 => "agent-error",
            _ => "action-rejected"
        };
    }

    private static string NormalizeUiActionKind(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace('_', '-');
        return string.IsNullOrWhiteSpace(normalized)
            ? "unknown"
            : ReadSafeToken(normalized, 40) ?? "unknown";
    }

    private static string? ReadSafeHeader(HttpRequest request, string name)
        => request.Headers.TryGetValue(name, out var value)
            ? ReadSafeToken(value, 128)
            : null;

    private static string? ReadBoundedIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            return null;
        foreach (var character in value)
        {
            if (char.IsControl(character))
                return null;
        }
        return value;
    }

    private static string? ReadSafeToken(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            return null;
        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.' and not ':')
            {
                return null;
            }
        }
        return trimmed;
    }

    private static string? ReadCoordinateSpace(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(
            "X-DevFlow-Action-Coordinate-Space",
            out var value))
        {
            return null;
        }

        return value?.Trim() switch
        {
            "appLogical" => "appLogical",
            "windowLogical" => "windowLogical",
            "screenshotPixel" => "screenshotPixel",
            "inspectorCss" => "inspectorCss",
            "transformedViewport" => "transformedViewport",
            _ => null
        };
    }

    private static double? ReadActionNumber(
        HttpRequest request,
        JsonElement body,
        string headerSuffix,
        string propertyName)
    {
        if (request.Headers.TryGetValue(
            $"X-DevFlow-Action-{headerSuffix}",
            out var headerValue)
            && double.TryParse(
                headerValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var headerNumber))
        {
            return Bounded(headerNumber);
        }

        return ReadBoundedDouble(body, propertyName);
    }

    private static double? ReadBoundedDouble(JsonElement body, string propertyName)
        => body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            ? Bounded(number)
            : null;

    private static double? Bounded(double? value)
        => value is { } number
            && double.IsFinite(number)
            && Math.Abs(number) <= MaxPublishedActionCoordinate
                ? number
                : null;

    private static int? ReadBoundedInt(JsonElement body, string propertyName)
        => body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            ? BoundedDuration(number)
            : null;

    private static int? BoundedDuration(int? value)
        => value is >= 0 and <= MaxPublishedActionDurationMs ? value : null;

    private static long? ReadPositiveInt64(JsonElement body, string propertyName)
        => body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            && number > 0
                ? number
                : null;

    private static long? ReadNonNegativeInt64(JsonElement body, string propertyName)
        => body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            && number >= 0
                ? number
                : null;

    private static long? ReadPositiveHeaderInt64(HttpRequest request, string name)
        => request.Headers.TryGetValue(name, out var value)
            && long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number)
            && number > 0
                ? number
                : null;

    private static int? ReadWindowId(HttpRequest request, JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("windowId", out var bodyValue)
            && bodyValue.ValueKind == JsonValueKind.Number
            && bodyValue.TryGetInt32(out var bodyWindow)
            && bodyWindow >= 0)
        {
            return bodyWindow;
        }

        return request.QueryParams.TryGetValue("window", out var queryValue)
            && int.TryParse(
                queryValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var queryWindow)
            && queryWindow >= 0
                ? queryWindow
                : null;
    }

    private static string? ReadSafeDirection(JsonElement body)
        => NormalizeDirection(ReadJsonString(body, "direction"));

    private static string? NormalizeDirection(string? direction)
        => direction?.Trim().ToLowerInvariant() switch
        {
            "up" => "up",
            "down" => "down",
            "left" => "left",
            "right" => "right",
            _ => null
        };
}
