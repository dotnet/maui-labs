using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class MauiDevFlowAgentService
{
    internal static readonly string[] SupportedGestures =
        ["tap", "doubletap", "longpress", "swipe", "pan", "pinch", "rotate"];

    private const int DefaultGestureSteps = 10;
    private int _panGestureId;

    protected override async Task<HttpResponse> HandleGesture(HttpRequest request)
    {
        if (_app == null)
            return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<GestureActionRequest>();
        if (body == null || string.IsNullOrWhiteSpace(body.Type))
            return HttpResponse.Error("type is required");
        if (await PrepareUiMutationAsync(request, body, body.ElementId) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var gestureType = NormalizeGestureType(body.Type);
        var reservedCapture = GetReservedCapture(request);
        var failureStatusCode = 400;
        GestureOutcome outcome;

        if (gestureType == "tap")
        {
            var tapResponse = await HandleTap(new HttpRequest
            {
                Method = "POST",
                MutationState = request.MutationState,
                Body = JsonSerializer.Serialize(new ActionRequest { ElementId = body.ElementId })
            });
            failureStatusCode = tapResponse.StatusCode;
            outcome = tapResponse.StatusCode < 400
                ? GestureOutcome.Handled("action", "DevFlow tap action")
                : GestureOutcome.NotHandled(ExtractResponseError(tapResponse) ?? "Tap failed");
        }
        else
        {
            var windowIndex = ParseWindowIndex(request);
            outcome = gestureType switch
            {
                "pinch" => await PerformPinchAsync(body, windowIndex, reservedCapture),
                "rotate" => await PerformRotateAsync(body, windowIndex, reservedCapture),
                "pan" => await PerformPanAsync(body, windowIndex, reservedCapture),
                "swipe" => await PerformSwipeAsync(body, windowIndex, reservedCapture),
                "doubletap" => await PerformDoubleTapAsync(body, windowIndex, reservedCapture),
                "longpress" => await PerformLongPressAsync(body, windowIndex, reservedCapture),
                _ => GestureOutcome.NotHandled(
                    $"Gesture '{body.Type}' is not supported. Supported types: {string.Join(", ", SupportedGestures)}")
            };
        }

        if (!outcome.Success && gestureType == "swipe")
        {
            var durationMs = GestureDurationMs(body);
            var scrolled = await HandleScroll(new HttpRequest
            {
                Method = "POST",
                MutationState = request.MutationState,
                Body = JsonSerializer.Serialize(new ScrollRequest
                {
                    ElementId = body.ElementId,
                    DeltaX = -SwipeDeltaX(body.Direction, body.Distance),
                    DeltaY = -SwipeDeltaY(body.Direction, body.Distance),
                    Animated = durationMs <= 0 || durationMs < 400
                })
            });

            if (scrolled.StatusCode < 400)
                outcome = GestureOutcome.Handled("scroll", "ScrollView.ScrollToAsync (legacy swipe fallback)");
        }

        PublishUiOperationSpan(
            $"action.gesture.{gestureType}",
            startedAtUtc,
            outcome.Success,
            outcome.Success ? null : outcome.Error,
            body.ElementId,
            new { gesture = gestureType, handledBy = outcome.HandledBy, detail = outcome.Detail });

        var response = HttpResponse.Json(new
        {
            success = outcome.Success,
            type = gestureType,
            elementId = body.ElementId,
            handledBy = outcome.HandledBy,
            platform = DeviceInfo.Platform.ToString(),
            detail = outcome.Detail,
            error = outcome.Error
        });

        if (!outcome.Success)
        {
            response.StatusCode = failureStatusCode;
            response.StatusText = HttpResponse.StatusTextFor(failureStatusCode);
        }

        return response;
    }

    private static string NormalizeGestureType(string type)
    {
        var normalized = type.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
        return normalized switch
        {
            "double" => "doubletap",
            "zoom" => "pinch",
            "drag" => "pan",
            _ => normalized
        };
    }

    private static double SwipeDeltaX(string? direction, double distance) =>
        direction?.Equals("left", StringComparison.OrdinalIgnoreCase) == true ? -distance :
        direction?.Equals("right", StringComparison.OrdinalIgnoreCase) == true ? distance : 0;

    private static double SwipeDeltaY(string? direction, double distance) =>
        direction?.Equals("up", StringComparison.OrdinalIgnoreCase) == true ? -distance :
        direction?.Equals("down", StringComparison.OrdinalIgnoreCase) == true ? distance : 0;

    private sealed record GestureOutcome(bool Success, string HandledBy, string? Detail, string? Error)
    {
        public static GestureOutcome Recognizer(string detail) => new(true, "recognizer", detail, null);
        public static GestureOutcome Native(string detail) => new(true, "native", detail, null);
        public static GestureOutcome Handled(string handledBy, string detail) => new(true, handledBy, detail, null);
        public static GestureOutcome NotHandled(string error) => new(false, "none", null, error);
    }

    private static int NormalizeSteps(int? steps) => Math.Clamp(steps ?? DefaultGestureSteps, 1, 120);

    private static int GestureDurationMs(GestureActionRequest body) => body.DurationMs ?? 200;

    private static int StepDelayMs(int durationMs, int steps) =>
        durationMs <= 0 ? 0 : Math.Clamp(durationMs / Math.Max(1, steps), 0, 1000);

    private static Point GestureOrigin(GestureActionRequest body) =>
        new(Math.Clamp(body.OriginX ?? 0.5, 0, 1), Math.Clamp(body.OriginY ?? 0.5, 0, 1));

    private VisualElement? ResolveGestureTarget(
        string? elementId,
        int? windowIndex,
        UiCaptureContext capture,
        out string? error)
    {
        error = null;

        if (string.IsNullOrEmpty(elementId))
        {
            var page = GetWindow(windowIndex)?.Page;
            if (page == null)
                error = "No element id supplied and the window has no current page";
            return page;
        }

        var resolved = ResolveCapturedElement(
            capture,
            elementId,
            id => _treeWalker.GetElementById(id, _app));
        if (resolved == null)
        {
            error = "Element not found";
            return null;
        }

        if (resolved is VisualElement visualElement)
            return visualElement;

        error = $"Element '{elementId}' is a {resolved.GetType().Name}, which cannot receive gestures";
        return null;
    }

    private static (T Recognizer, View Owner)? FindGestureRecognizer<T>(
        Element? start,
        Func<T, bool>? predicate = null)
        where T : class, IGestureRecognizer
    {
        var current = start;
        while (current != null)
        {
            if (current is View view)
            {
                foreach (var recognizer in view.GestureRecognizers)
                {
                    if (recognizer is T match && (predicate == null || predicate(match)))
                        return (match, view);
                }
            }

            current = current.Parent;
        }

        return null;
    }

    private async Task<GestureOutcome> PerformPinchAsync(
        GestureActionRequest body,
        int? windowIndex,
        UiCaptureContext capture)
    {
        var scale = body.Scale ?? 1.5;
        if (scale <= 0)
            return GestureOutcome.NotHandled("scale must be greater than 0 (2.0 zooms in, 0.5 zooms out)");

        var steps = NormalizeSteps(body.Steps);
        var origin = GestureOrigin(body);
        var durationMs = GestureDurationMs(body);
        var delay = StepDelayMs(durationMs, steps);
        var outcome = await DispatchAsync(async () =>
        {
            var target = ResolveGestureTarget(body.ElementId, windowIndex, capture, out var error);
            if (target == null)
                return GestureOutcome.NotHandled(error!);

            var found = FindGestureRecognizer<PinchGestureRecognizer>(target);
            if (found is { } hit && hit.Recognizer is IPinchGestureController controller)
            {
                var stepScale = Math.Pow(scale, 1.0 / steps);
                controller.SendPinchStarted(hit.Owner, origin);
                for (var i = 0; i < steps; i++)
                {
                    controller.SendPinch(hit.Owner, stepScale, origin);
                    if (delay > 0)
                        await Task.Delay(delay);
                }

                controller.SendPinchEnded(hit.Owner);
                return GestureOutcome.Recognizer($"PinchGestureRecognizer on {hit.Owner.GetType().Name}");
            }

            var native = await TryNativePinch(target, scale, origin, durationMs, steps);
            return native != null
                ? GestureOutcome.Native(native)
                : GestureOutcome.NotHandled(NoHandlerMessage("pinch", target, "PinchGestureRecognizer"));
        });

        return outcome ?? GestureOutcome.NotHandled("Pinch dispatch failed");
    }

    private async Task<GestureOutcome> PerformRotateAsync(
        GestureActionRequest body,
        int? windowIndex,
        UiCaptureContext capture)
    {
        var degrees = body.Rotation ?? 90;
        var steps = NormalizeSteps(body.Steps);
        var origin = GestureOrigin(body);
        var durationMs = GestureDurationMs(body);
        var outcome = await DispatchAsync(async () =>
        {
            var target = ResolveGestureTarget(body.ElementId, windowIndex, capture, out var error);
            if (target == null)
                return GestureOutcome.NotHandled(error!);

            var native = await TryNativeRotate(target, degrees, origin, durationMs, steps);
            return native != null
                ? GestureOutcome.Native(native)
                : GestureOutcome.NotHandled(
                    $"rotate is not available for {target.GetType().Name} on {DeviceInfo.Platform}. " +
                    "MAUI has no RotationGestureRecognizer, so rotate requires a native rotation recognizer on the platform view.");
        });

        return outcome ?? GestureOutcome.NotHandled("Rotate dispatch failed");
    }

    private async Task<GestureOutcome> PerformPanAsync(
        GestureActionRequest body,
        int? windowIndex,
        UiCaptureContext capture)
    {
        var totalX = body.DeltaX ?? SwipeDeltaX(body.Direction, body.Distance);
        var totalY = body.DeltaY ?? SwipeDeltaY(body.Direction, body.Distance);
        if (totalX == 0 && totalY == 0)
            return GestureOutcome.NotHandled("pan requires a non-zero deltaX/deltaY, or a direction with a distance");

        var steps = NormalizeSteps(body.Steps);
        var durationMs = GestureDurationMs(body);
        var delay = StepDelayMs(durationMs, steps);
        var outcome = await DispatchAsync(async () =>
        {
            var target = ResolveGestureTarget(body.ElementId, windowIndex, capture, out var error);
            if (target == null)
                return GestureOutcome.NotHandled(error!);

            var found = FindGestureRecognizer<PanGestureRecognizer>(target);
            if (found is { } hit && hit.Recognizer is IPanGestureController controller)
            {
                var gestureId = Interlocked.Increment(ref _panGestureId);
                controller.SendPanStarted(hit.Owner, gestureId);
                for (var i = 1; i <= steps; i++)
                {
                    controller.SendPan(hit.Owner, totalX * i / steps, totalY * i / steps, gestureId);
                    if (delay > 0)
                        await Task.Delay(delay);
                }

                controller.SendPanCompleted(hit.Owner, gestureId);
                return GestureOutcome.Recognizer($"PanGestureRecognizer on {hit.Owner.GetType().Name}");
            }

            var native = await TryNativePan(target, totalX, totalY, durationMs, steps);
            return native != null
                ? GestureOutcome.Native(native)
                : GestureOutcome.NotHandled(NoHandlerMessage("pan", target, "PanGestureRecognizer"));
        });

        return outcome ?? GestureOutcome.NotHandled("Pan dispatch failed");
    }

    private async Task<GestureOutcome> PerformSwipeAsync(
        GestureActionRequest body,
        int? windowIndex,
        UiCaptureContext capture)
    {
        if (!TryParseSwipeDirection(body.Direction, out var direction))
            return GestureOutcome.NotHandled("swipe requires a direction of 'up', 'down', 'left' or 'right'");

        var outcome = await DispatchAsync(async () =>
        {
            var target = ResolveGestureTarget(body.ElementId, windowIndex, capture, out var error);
            if (target == null)
                return GestureOutcome.NotHandled(error!);

            var found = FindGestureRecognizer<SwipeGestureRecognizer>(
                target,
                recognizer => recognizer.Direction.HasFlag(direction));
            if (found is { } hit
                && hit.Recognizer is ISwipeGestureController controller)
            {
                controller.SendSwipe(
                    hit.Owner,
                    SwipeDeltaX(body.Direction, body.Distance),
                    SwipeDeltaY(body.Direction, body.Distance));
                if (controller.DetectSwipe(hit.Owner, direction))
                    return GestureOutcome.Recognizer($"SwipeGestureRecognizer on {hit.Owner.GetType().Name}");
            }

            var native = await TryNativeSwipe(
                target,
                direction.ToString().ToLowerInvariant(),
                body.Distance,
                GestureDurationMs(body));
            return native != null
                ? GestureOutcome.Native(native)
                : GestureOutcome.NotHandled(NoHandlerMessage("swipe", target, "SwipeGestureRecognizer"));
        });

        return outcome ?? GestureOutcome.NotHandled("Swipe dispatch failed");
    }

    private async Task<GestureOutcome> PerformDoubleTapAsync(
        GestureActionRequest body,
        int? windowIndex,
        UiCaptureContext capture)
    {
        var outcome = await DispatchAsync(async () =>
        {
            var target = ResolveGestureTarget(body.ElementId, windowIndex, capture, out var error);
            if (target == null)
                return GestureOutcome.NotHandled(error!);

            var found = FindGestureRecognizer<TapGestureRecognizer>(
                target,
                recognizer => recognizer.NumberOfTapsRequired == 2);
            if (found is { } hit)
            {
                if (hit.Recognizer.Command != null)
                {
                    hit.Recognizer.Command.Execute(hit.Recognizer.CommandParameter);
                    return GestureOutcome.Recognizer(
                        $"TapGestureRecognizer(2) command on {hit.Owner.GetType().Name}");
                }

                if (TryInvokeTapped(hit.Recognizer, hit.Owner))
                    return GestureOutcome.Recognizer($"TapGestureRecognizer(2) on {hit.Owner.GetType().Name}");
            }

            var native = await TryNativeDoubleTap(target);
            return native != null
                ? GestureOutcome.Native(native)
                : GestureOutcome.NotHandled(
                    NoHandlerMessage(
                        "doubletap",
                        target,
                        "TapGestureRecognizer with NumberOfTapsRequired=2"));
        });

        return outcome ?? GestureOutcome.NotHandled("Double-tap dispatch failed");
    }

    private async Task<GestureOutcome> PerformLongPressAsync(
        GestureActionRequest body,
        int? windowIndex,
        UiCaptureContext capture)
    {
        var durationMs = Math.Max(body.DurationMs ?? 600, 500);
        var outcome = await DispatchAsync(async () =>
        {
            var target = ResolveGestureTarget(body.ElementId, windowIndex, capture, out var error);
            if (target == null)
                return GestureOutcome.NotHandled(error!);

            var native = await TryNativeLongPress(target, durationMs);
            return native != null
                ? GestureOutcome.Native(native)
                : GestureOutcome.NotHandled(
                    $"longpress is not available for {target.GetType().Name} on {DeviceInfo.Platform}. " +
                    "MAUI has no long-press recognizer, so it requires native touch injection. " +
                    "Use the 'tap' gesture if a plain tap is sufficient.");
        });

        return outcome ?? GestureOutcome.NotHandled("Long-press dispatch failed");
    }

    private static string NoHandlerMessage(string gesture, VisualElement target, string recognizerName) =>
        $"No {gesture} handler for {target.GetType().Name}: no {recognizerName} on the element or its " +
        $"ancestors, and native {gesture} injection is not available on {DeviceInfo.Platform}.";

    private static string? ExtractResponseError(HttpResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            return document.RootElement.TryGetProperty("error", out var error)
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseSwipeDirection(string? direction, out SwipeDirection parsed)
    {
        switch (direction?.Trim().ToLowerInvariant())
        {
            case "up":
                parsed = SwipeDirection.Up;
                return true;
            case "down":
                parsed = SwipeDirection.Down;
                return true;
            case "left":
                parsed = SwipeDirection.Left;
                return true;
            case "right":
                parsed = SwipeDirection.Right;
                return true;
            default:
                parsed = SwipeDirection.Up;
                return false;
        }
    }

    protected virtual Task<string?> TryNativePinch(
        VisualElement element,
        double scale,
        Point origin,
        int durationMs,
        int steps)
        => Task.FromResult<string?>(null);

    protected virtual Task<string?> TryNativeRotate(
        VisualElement element,
        double degrees,
        Point origin,
        int durationMs,
        int steps)
        => Task.FromResult<string?>(null);

    protected virtual Task<string?> TryNativePan(
        VisualElement element,
        double deltaX,
        double deltaY,
        int durationMs,
        int steps)
        => Task.FromResult<string?>(null);

    protected virtual Task<string?> TryNativeSwipe(
        VisualElement element,
        string direction,
        double distance,
        int durationMs)
        => Task.FromResult<string?>(null);

    protected virtual Task<string?> TryNativeLongPress(VisualElement element, int durationMs)
        => Task.FromResult<string?>(null);

    protected virtual Task<string?> TryNativeDoubleTap(VisualElement element)
        => Task.FromResult<string?>(null);
}
