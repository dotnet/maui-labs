using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
#if ANDROID
using Android.Views;
#endif
#if IOS || MACCATALYST
using System.Runtime.InteropServices;
using ObjCRuntime;
using UIKit;
#endif
#if MACOS
using AppKit;
#endif

namespace Microsoft.Maui.DevFlow.Agent;

/// <summary>
/// Native gesture injection — the second tier behind the managed MAUI gesture recognizers
/// in <see cref="Core.MauiDevFlowAgentService"/>. This is what makes pinch-to-zoom work on
/// controls that own their gestures internally (Map, WebView, SKCanvasView, native scroll views)
/// and therefore expose no <c>PinchGestureRecognizer</c> to walk.
///
/// Fidelity varies by platform, and each method reports how it was handled so callers can tell:
/// <list type="bullet">
/// <item>Android — genuine multi-pointer <c>MotionEvent</c>s dispatched through the activity,
/// so the whole hit-test and <c>GestureDetector</c> pipeline runs. Fully faithful.</item>
/// <item>iOS / Mac Catalyst — drives the real native zoom/pan surfaces (MKMapView camera and
/// centre, UIScrollView zoom/offset), then the attached UIGestureRecognizers, and finally —
/// when <c>AgentOptions.EnableSyntheticTouch</c> is on — synthesised <c>UITouch</c>es delivered
/// to raw-touch views such as SKCanvasView and GraphicsView, which own no recognizer to
/// drive. That last tier needs private UIKit ivars, hence the opt-in.</item>
/// <item>Windows — ScrollViewer zoom/offset. Input injection needs the restricted
/// <c>inputInjectionBrokered</c> capability and is not usable from a normal app package.</item>
/// <item>macOS AppKit — NSScrollView magnification and content offset.</item>
/// </list>
/// Every method returns a short description of what serviced the gesture, or null when unhandled.
/// </summary>
public partial class PlatformAgentService
{
    protected override async Task<string?> TryNativePinch(VisualElement element, double scale, Point origin, int durationMs, int steps)
    {
#if ANDROID
        return await AndroidPinchAsync(element, scale, origin, durationMs, steps);
#elif IOS || MACCATALYST
        return await ApplePinchAsync(element, scale, origin, durationMs, steps, _options.EnableSyntheticTouch);
#elif WINDOWS
        return await Task.FromResult(WindowsPinch(element, scale));
#elif MACOS
        return await Task.FromResult(MacPinch(element, scale));
#else
        return await base.TryNativePinch(element, scale, origin, durationMs, steps);
#endif
    }

    protected override async Task<string?> TryNativeRotate(VisualElement element, double degrees, Point origin, int durationMs, int steps)
    {
#if ANDROID
        return await AndroidRotateAsync(element, degrees, origin, durationMs, steps);
#elif IOS || MACCATALYST
        return await AppleRotateAsync(element, degrees, durationMs, steps);
#else
        return await base.TryNativeRotate(element, degrees, origin, durationMs, steps);
#endif
    }

    protected override async Task<string?> TryNativePan(VisualElement element, double deltaX, double deltaY, int durationMs, int steps)
    {
#if ANDROID
        return await AndroidPanAsync(element, deltaX, deltaY, durationMs, steps, "pan");
#elif IOS || MACCATALYST
        return await ApplePanAsync(element, deltaX, deltaY, durationMs, steps, _options.EnableSyntheticTouch);
#elif WINDOWS
        return await Task.FromResult(WindowsPan(element, deltaX, deltaY));
#elif MACOS
        return await Task.FromResult(MacPan(element, deltaX, deltaY));
#else
        return await base.TryNativePan(element, deltaX, deltaY, durationMs, steps);
#endif
    }

    protected override async Task<string?> TryNativeSwipe(VisualElement element, string direction, double distance, int durationMs)
    {
#if ANDROID
        // A swipe is a fast pan; the shorter duration is what makes Android's
        // VelocityTracker read it as a fling rather than a drag.
        var dx = direction switch { "left" => -distance, "right" => distance, _ => 0 };
        var dy = direction switch { "up" => -distance, "down" => distance, _ => 0 };
        var swipeDuration = durationMs > 0 ? Math.Min(durationMs, 150) : 100;
        return await AndroidPanAsync(element, dx, dy, swipeDuration, 8, "swipe");
#elif IOS || MACCATALYST
        return await AppleSwipeAsync(element, direction, distance, durationMs, _options.EnableSyntheticTouch);
#else
        return await base.TryNativeSwipe(element, direction, distance, durationMs);
#endif
    }

    protected override async Task<string?> TryNativeLongPress(VisualElement element, int durationMs)
    {
#if ANDROID
        return await AndroidLongPressAsync(element, durationMs);
#elif IOS || MACCATALYST
        return await AppleLongPressAsync(element, durationMs);
#else
        return await base.TryNativeLongPress(element, durationMs);
#endif
    }

    protected override async Task<string?> TryNativeDoubleTap(VisualElement element)
    {
#if ANDROID
        return await AndroidDoubleTapAsync(element);
#elif IOS || MACCATALYST
        return await AppleDoubleTapAsync(element);
#else
        return await base.TryNativeDoubleTap(element);
#endif
    }

#if ANDROID
    // MotionEvent.ACTION_POINTER_INDEX_SHIFT — the pointer index is packed into the
    // high bits of the action for ACTION_POINTER_DOWN/UP.
    private const int PointerIndexShift = 8;

    private static global::Android.Views.View? GetAndroidView(VisualElement element)
    {
        if (element.Handler?.PlatformView is global::Android.Views.View view)
            return view;

        // Pages and Shell content often have no directly usable platform view — fall back
        // to the activity's content view so a page-level gesture still lands somewhere real.
        return global::Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?
            .Window?.DecorView?.FindViewById(global::Android.Resource.Id.Content);
    }

    /// <summary>
    /// Element centre and half-extent in window pixels, plus the display density.
    /// Returns null when the view has no measured size to aim at.
    /// </summary>
    private static (PointF Center, float Radius, float Density)? GetAndroidTouchGeometry(
        global::Android.Views.View view, Point origin)
    {
        if (view.Width <= 0 || view.Height <= 0)
            return null;

        var location = new int[2];
        view.GetLocationInWindow(location);

        var center = new PointF(
            location[0] + (float)(view.Width * origin.X),
            location[1] + (float)(view.Height * origin.Y));

        // Keep both pinch pointers comfortably inside the view.
        var radius = Math.Min(view.Width, view.Height) * 0.4f;
        var density = view.Resources?.DisplayMetrics?.Density ?? 1f;
        return (center, radius, density);
    }

    /// <summary>
    /// Dispatches a full touch sequence (down → moves → up) through the activity so the
    /// real hit-test and gesture-detection pipeline runs. <paramref name="positionsAt"/>
    /// is sampled with t in 0..1 and must return one point per pointer, in window pixels.
    /// </summary>
    private static async Task<bool> InjectAndroidTouchAsync(
        global::Android.Views.View targetView,
        int pointerCount,
        Func<double, PointF[]> positionsAt,
        int durationMs,
        int steps,
        int holdMs = 0,
        int settleMs = 0)
    {
        var activity = global::Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var targetLocation = new int[2];
        targetView.GetLocationInWindow(targetLocation);

        var properties = new MotionEvent.PointerProperties[pointerCount];
        var coords = new MotionEvent.PointerCoords[pointerCount];
        for (var i = 0; i < pointerCount; i++)
        {
            properties[i] = new MotionEvent.PointerProperties { Id = i, ToolType = MotionEventToolType.Finger };
            coords[i] = new MotionEvent.PointerCoords { Pressure = 1f, Size = 1f };
        }

        var downTime = global::Android.OS.SystemClock.UptimeMillis();
        var stepDelay = steps > 0 && durationMs > 0 ? Math.Max(1, durationMs / steps) : 0;
        var eventTime = downTime;

        void Apply(PointF[] points)
        {
            for (var i = 0; i < pointerCount; i++)
            {
                coords[i].X = points[i].X;
                coords[i].Y = points[i].Y;
            }
        }

        bool Send(MotionEventActions action, int activePointers)
        {
            static MotionEvent? CreateEvent(
                long downTime,
                long eventTime,
                MotionEventActions action,
                int activePointers,
                MotionEvent.PointerProperties[] properties,
                MotionEvent.PointerCoords[] coords)
                => MotionEvent.Obtain(
                    downTime, eventTime, action, activePointers,
                    properties[..activePointers], coords[..activePointers],
                    0, (MotionEventButtonState)0, 1f, 1f, 0, (Edge)0,
                    InputSourceType.Touchscreen, (MotionEventFlags)0);

            if (activity != null)
            {
                var windowEvent = CreateEvent(
                    downTime, eventTime, action, activePointers, properties, coords);
                if (windowEvent != null)
                {
                    try
                    {
                        if (activity.DispatchTouchEvent(windowEvent))
                            return true;
                    }
                    finally
                    {
                        windowEvent.Recycle();
                    }
                }
            }

            var localCoords = new MotionEvent.PointerCoords[activePointers];
            for (var i = 0; i < activePointers; i++)
            {
                localCoords[i] = new MotionEvent.PointerCoords
                {
                    X = coords[i].X - targetLocation[0],
                    Y = coords[i].Y - targetLocation[1],
                    Pressure = coords[i].Pressure,
                    Size = coords[i].Size
                };
            }

            var localEvent = CreateEvent(
                downTime, eventTime, action, activePointers, properties, localCoords);
            if (localEvent == null)
                return false;
            try
            {
                return targetView.DispatchTouchEvent(localEvent);
            }
            finally
            {
                localEvent.Recycle();
            }
        }

        try
        {
            Apply(positionsAt(0));

            var handled = Send(MotionEventActions.Down, 1);
            for (var i = 1; i < pointerCount; i++)
                handled |= Send(
                    (MotionEventActions)((int)MotionEventActions.PointerDown | (i << PointerIndexShift)),
                    i + 1);

            // Keep the gesture on the element the caller named. Without this an enclosing
            // ScrollView steals a vertical drag once it passes the touch slop — correct for a
            // real finger, wrong for "pan this canvas". Ancestors only: the target view itself
            // still handles the gesture normally. Must follow the down, which resets the flag.
            targetView.Parent?.RequestDisallowInterceptTouchEvent(true);

            if (holdMs > 0)
            {
                await Task.Delay(holdMs);
                eventTime += holdMs;
            }

            for (var step = 1; step <= steps; step++)
            {
                eventTime += Math.Max(1, stepDelay);
                Apply(positionsAt((double)step / steps));
                handled |= Send(MotionEventActions.Move, pointerCount);
                if (stepDelay > 0) await Task.Delay(stepDelay);
            }

            // A pan should stop where it was asked to. Lifting straight after the last move
            // leaves velocity on the VelocityTracker and the view flings past the target, so
            // hold still for a moment first; a swipe skips this precisely to keep its fling.
            if (settleMs > 0)
            {
                await Task.Delay(settleMs);
                eventTime += settleMs;
                handled |= Send(MotionEventActions.Move, pointerCount);
            }

            eventTime += 1;
            for (var i = pointerCount - 1; i >= 1; i--)
                Send((MotionEventActions)((int)MotionEventActions.PointerUp | (i << PointerIndexShift)), i + 1);
            Send(MotionEventActions.Up, 1);
            return handled;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] Android touch injection failed: {ex.GetBaseException().Message}");
            return false;
        }
        finally
        {
            targetView.Parent?.RequestDisallowInterceptTouchEvent(false);
        }
    }

    private static async Task<string?> AndroidPinchAsync(VisualElement element, double scale, Point origin, int durationMs, int steps)
    {
        var view = GetAndroidView(element);
        if (view == null) return null;
        if (GetAndroidTouchGeometry(view, origin) is not { } geometry) return null;

        var (center, maxRadius, _) = geometry;

        // Pick start/end radii so both ends of the pinch stay inside the view:
        // zooming in starts close together, zooming out starts far apart.
        var (startRadius, endRadius) = scale >= 1
            ? ((float)(maxRadius / scale), maxRadius)
            : (maxRadius, (float)(maxRadius * scale));

        var handled = await InjectAndroidTouchAsync(
            view,
            pointerCount: 2,
            positionsAt: t =>
            {
                var r = startRadius + (endRadius - startRadius) * (float)t;
                return
                [
                    new PointF(center.X - r, center.Y),
                    new PointF(center.X + r, center.Y)
                ];
            },
            durationMs: durationMs > 0 ? durationMs : 200,
            steps: steps);

        return handled ? $"MotionEvent 2-pointer pinch x{scale:0.##}" : null;
    }

    private static async Task<string?> AndroidRotateAsync(VisualElement element, double degrees, Point origin, int durationMs, int steps)
    {
        var view = GetAndroidView(element);
        if (view == null) return null;
        if (GetAndroidTouchGeometry(view, origin) is not { } geometry) return null;

        var (center, radius, _) = geometry;
        var totalRadians = degrees * Math.PI / 180.0;

        var handled = await InjectAndroidTouchAsync(
            view,
            pointerCount: 2,
            positionsAt: t =>
            {
                var angle = totalRadians * t;
                var dx = (float)(Math.Cos(angle) * radius);
                var dy = (float)(Math.Sin(angle) * radius);
                return
                [
                    new PointF(center.X - dx, center.Y - dy),
                    new PointF(center.X + dx, center.Y + dy)
                ];
            },
            durationMs: durationMs > 0 ? durationMs : 300,
            steps: steps);

        return handled ? $"MotionEvent 2-pointer rotate {degrees:0.#}°" : null;
    }

    private static async Task<string?> AndroidPanAsync(VisualElement element, double deltaX, double deltaY, int durationMs, int steps, string label)
    {
        var view = GetAndroidView(element);
        if (view == null) return null;
        if (GetAndroidTouchGeometry(view, new Point(0.5, 0.5)) is not { } geometry) return null;

        var (center, _, density) = geometry;
        // Request deltas are device-independent pixels; MotionEvent works in physical pixels.
        var pixelX = (float)(deltaX * density);
        var pixelY = (float)(deltaY * density);

        // Start offset against the travel direction so the whole gesture stays on the view.
        var start = new PointF(center.X - pixelX / 2f, center.Y - pixelY / 2f);

        var handled = await InjectAndroidTouchAsync(
            view,
            pointerCount: 1,
            positionsAt: t => [new PointF(start.X + pixelX * (float)t, start.Y + pixelY * (float)t)],
            durationMs: durationMs > 0 ? durationMs : 200,
            steps: steps,
            settleMs: label == "pan" ? 60 : 0);

        return handled ? $"MotionEvent {label} ({deltaX:0.#}, {deltaY:0.#})" : null;
    }

    private static async Task<string?> AndroidLongPressAsync(VisualElement element, int durationMs)
    {
        var view = GetAndroidView(element);
        if (view == null) return null;
        if (GetAndroidTouchGeometry(view, new Point(0.5, 0.5)) is not { } geometry) return null;

        var center = geometry.Center;
        var handled = await InjectAndroidTouchAsync(
            view,
            pointerCount: 1,
            positionsAt: _ => [center],
            durationMs: 0,
            steps: 0,
            holdMs: durationMs);

        return handled ? $"MotionEvent long press ({durationMs}ms)" : null;
    }

    private static async Task<string?> AndroidDoubleTapAsync(VisualElement element)
    {
        var view = GetAndroidView(element);
        if (view == null) return null;
        if (GetAndroidTouchGeometry(view, new Point(0.5, 0.5)) is not { } geometry) return null;

        var center = geometry.Center;
        Func<double, PointF[]> at = _ => [center];

        if (!await InjectAndroidTouchAsync(view, 1, at, 0, 0)) return null;
        // Comfortably inside Android's ~300ms double-tap timeout.
        await Task.Delay(80);
        if (!await InjectAndroidTouchAsync(view, 1, at, 0, 0)) return null;

        return "MotionEvent double tap";
    }
#endif

#if IOS || MACCATALYST
    // UIGestureRecognizer.state is readonly in the public API. Driving it is how
    // synthetic gestures reach recognizers we do not own; UIKit dispatches the
    // recognizer's target actions on each state change.
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetGestureRecognizerState(IntPtr receiver, IntPtr selector, nint state);

    private static bool TrySetState(UIGestureRecognizer recognizer, UIGestureRecognizerState state)
    {
        try
        {
            SetGestureRecognizerState(recognizer.Handle, Selector.GetHandle("setState:"), (nint)(long)state);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] setState: failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static UIView? GetAppleView(VisualElement element) => element.Handler?.PlatformView as UIView;

    /// <summary>
    /// Breadth-ish search for a recognizer of the given kind on the view, its subviews and
    /// its ancestors. Native controls such as MKMapView keep their recognizers on internal subviews.
    /// Callers that want the element's own handling to win pass <paramref name="includeAncestors"/>
    /// as false, try the closer tiers, and only then widen the search.
    /// </summary>
    private static T? FindRecognizer<T>(
        UIView? view, Func<T, bool>? predicate = null, bool includeAncestors = true)
        where T : UIGestureRecognizer
    {
        if (view == null) return null;

        static T? OnView(UIView v, Func<T, bool>? p)
        {
            if (v.GestureRecognizers == null) return null;
            foreach (var recognizer in v.GestureRecognizers)
            {
                if (recognizer is T match && recognizer.Enabled && (p == null || p(match)))
                    return match;
            }
            return null;
        }

        static T? Descend(UIView v, Func<T, bool>? p)
        {
            var hit = OnView(v, p);
            if (hit != null) return hit;
            foreach (var sub in v.Subviews)
            {
                var found = Descend(sub, p);
                if (found != null) return found;
            }
            return null;
        }

        var descendant = Descend(view, predicate);
        if (descendant != null) return descendant;
        if (!includeAncestors) return null;

        var ancestor = view.Superview;
        while (ancestor != null)
        {
            var hit = OnView(ancestor, predicate);
            if (hit != null) return hit;
            ancestor = ancestor.Superview;
        }
        return null;
    }

    private static T? FindNativeSubview<T>(UIView? view) where T : UIView
    {
        if (view == null) return null;
        if (view is T match) return match;
        foreach (var sub in view.Subviews)
        {
            var found = FindNativeSubview<T>(sub);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Locates an MKMapView by type name so the agent does not link MapKit into every
    /// consuming app just to support the maps scenario.
    /// </summary>
    private static UIView? FindMapView(UIView? view)
    {
        if (view == null) return null;
        if (view.GetType().Name.Contains("MKMapView", StringComparison.Ordinal)) return view;
        foreach (var sub in view.Subviews)
        {
            var found = FindMapView(sub);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Zooms an MKMapView through its camera. MKMapCamera is a reference type with a
    /// plain double CenterCoordinateDistance, which keeps this reachable by reflection —
    /// unlike MKCoordinateRegion, whose nested structs cannot be updated in place.
    /// </summary>
    private static string? TryZoomMapView(UIView mapView, double scale)
    {
        try
        {
            var mapType = mapView.GetType();
            var cameraProperty = mapType.GetProperty("Camera");
            var camera = cameraProperty?.GetValue(mapView);
            if (camera == null) return null;

            var distanceProperty = camera.GetType().GetProperty("CenterCoordinateDistance");
            if (distanceProperty?.GetValue(camera) is not double distance || distance <= 0) return null;

            // Halving the camera distance doubles the apparent zoom.
            distanceProperty.SetValue(camera, distance / scale);

            var setCamera = mapType.GetMethod("SetCamera", [camera.GetType(), typeof(bool)]);
            if (setCamera != null)
                setCamera.Invoke(mapView, [camera, true]);
            else
                cameraProperty!.SetValue(mapView, camera);

            return $"MKMapView.Camera.CenterCoordinateDistance /{scale:0.##}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] MKMapView zoom failed: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private static async Task<string?> ApplePinchAsync(
        VisualElement element, double scale, Point origin, int durationMs, int steps, bool allowSyntheticTouch)
    {
        var view = GetAppleView(element);
        if (view == null) return null;

        // 1. Maps — the scenario this feature exists for.
        if (FindMapView(view) is { } mapView && TryZoomMapView(mapView, scale) is { } mapDetail)
            return mapDetail;

        // 2. Any zoomable UIScrollView: WKWebView's inner scroll view, ScrollView, CollectionView.
        var scrollView = view as UIScrollView ?? FindNativeSubview<UIScrollView>(view);
        if (scrollView != null && scrollView.MaximumZoomScale > scrollView.MinimumZoomScale)
        {
            var target = (nfloat)Math.Clamp(
                (double)scrollView.ZoomScale * scale,
                (double)scrollView.MinimumZoomScale,
                (double)scrollView.MaximumZoomScale);
            if (Math.Abs(target - scrollView.ZoomScale) < 0.001)
                return null;
            scrollView.SetZoomScale(target, animated: true);
            return $"UIScrollView.ZoomScale → {target:0.##}";
        }

        // 3. Drive whatever pinch recognizer the control installed.
        if (FindRecognizer<UIPinchGestureRecognizer>(view) is { } recognizer)
        {
            var stepDelay = steps > 0 && durationMs > 0 ? Math.Max(1, durationMs / steps) : 0;
            recognizer.Scale = 1;
            if (!TrySetState(recognizer, UIGestureRecognizerState.Began)) return null;
            for (var i = 1; i <= steps; i++)
            {
                recognizer.Scale = (nfloat)(1 + (scale - 1) * i / steps);
                TrySetState(recognizer, UIGestureRecognizerState.Changed);
                if (stepDelay > 0) await Task.Delay(stepDelay);
            }
            TrySetState(recognizer, UIGestureRecognizerState.Ended);
            return $"UIPinchGestureRecognizer x{scale:0.##}";
        }

        // 4. Raw-touch surfaces — SKCanvasView, GraphicsView — own no recognizer at all.
        if (!allowSyntheticTouch) return null;
        return await AppleSyntheticPinchAsync(view, scale, origin, durationMs, steps);
    }

    /// <summary>
    /// Element centre in window coordinates and the largest pinch radius that keeps both
    /// fingers on the view. Null when the view is unmeasured or off-window.
    /// </summary>
    private static (CoreGraphics.CGPoint Center, double Radius)? GetAppleTouchGeometry(UIView view, Point origin)
    {
        var bounds = view.Bounds;
        if (view.Window == null || bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        var pointInView = new CoreGraphics.CGPoint(
            (double)bounds.X + (double)bounds.Width * origin.X,
            (double)bounds.Y + (double)bounds.Height * origin.Y);

        return (
            view.ConvertPointToView(pointInView, null),
            Math.Min((double)bounds.Width, (double)bounds.Height) * 0.4);
    }

    /// <summary>
    /// Resolves the raw-touch view a gesture at <paramref name="pointInWindow"/> would land on,
    /// or null when the hit view takes its input through recognizers (which the callers above
    /// have already tried) and would therefore ignore synthesised touches.
    /// </summary>
    private static UIView? ResolveRawTouchTarget(UIView view, CoreGraphics.CGPoint pointInWindow)
        => AppleTouchInjector.HitTest(view, pointInWindow) is { } hit && AppleTouchInjector.IsRawTouchView(hit)
            ? hit
            : null;

    private static async Task<string?> AppleSyntheticPinchAsync(
        UIView view, double scale, Point origin, int durationMs, int steps)
    {
        if (GetAppleTouchGeometry(view, origin) is not { } geometry) return null;

        var (center, maxRadius) = geometry;
        if (ResolveRawTouchTarget(view, center) is not { } target) return null;

        // Both ends of the pinch stay on the view: zooming in starts with the fingers
        // together, zooming out starts with them apart.
        var (startRadius, endRadius) = scale >= 1
            ? (maxRadius / scale, maxRadius)
            : (maxRadius, maxRadius * scale);

        var handled = await AppleTouchInjector.InjectAsync(
            view,
            t =>
            {
                var r = startRadius + (endRadius - startRadius) * t;
                return
                [
                    new CoreGraphics.CGPoint((double)center.X - r, (double)center.Y),
                    new CoreGraphics.CGPoint((double)center.X + r, (double)center.Y)
                ];
            },
            durationMs > 0 ? durationMs : 200,
            steps);

        return handled ? $"Synthetic UITouch pinch x{scale:0.##} on {target.GetType().Name}" : null;
    }

    private static async Task<string?> AppleSyntheticDragAsync(
        UIView view, double deltaX, double deltaY, int durationMs, int steps, string label)
    {
        if (GetAppleTouchGeometry(view, new Point(0.5, 0.5)) is not { } geometry) return null;

        // Start offset against the travel direction so the whole drag stays on the view.
        var start = new CoreGraphics.CGPoint(
            (double)geometry.Center.X - deltaX / 2,
            (double)geometry.Center.Y - deltaY / 2);
        if (ResolveRawTouchTarget(view, start) is not { } target) return null;

        var handled = await AppleTouchInjector.InjectAsync(
            view,
            t => [new CoreGraphics.CGPoint((double)start.X + deltaX * t, (double)start.Y + deltaY * t)],
            durationMs > 0 ? durationMs : 200,
            steps);

        return handled
            ? $"Synthetic UITouch {label} ({deltaX:0.#}, {deltaY:0.#}) on {target.GetType().Name}"
            : null;
    }

    private static async Task<string?> AppleRotateAsync(VisualElement element, double degrees, int durationMs, int steps)
    {
        var recognizer = FindRecognizer<UIRotationGestureRecognizer>(GetAppleView(element));
        if (recognizer == null) return null;

        var totalRadians = degrees * Math.PI / 180.0;
        var stepDelay = steps > 0 && durationMs > 0 ? Math.Max(1, durationMs / steps) : 0;

        recognizer.Rotation = 0;
        if (!TrySetState(recognizer, UIGestureRecognizerState.Began)) return null;
        for (var i = 1; i <= steps; i++)
        {
            recognizer.Rotation = (nfloat)(totalRadians * i / steps);
            TrySetState(recognizer, UIGestureRecognizerState.Changed);
            if (stepDelay > 0) await Task.Delay(stepDelay);
        }
        TrySetState(recognizer, UIGestureRecognizerState.Ended);
        return $"UIRotationGestureRecognizer {degrees:0.#}°";
    }

    private static async Task<string?> ApplePanAsync(
        VisualElement element, double deltaX, double deltaY, int durationMs, int steps, bool allowSyntheticTouch)
    {
        var view = GetAppleView(element);
        if (view == null) return null;

        // 1. Maps pan themselves and expose no scroll view to nudge.
        if (FindMapView(view) is { } mapView && TryPanMapView(mapView, deltaX, deltaY) is { } mapDetail)
            return mapDetail;

        // 2. Scroll views — including WKWebView's inner one — move honestly by content offset,
        // but only when there is somewhere to move; a page that fits falls through.
        var scrollView = view as UIScrollView ?? FindNativeSubview<UIScrollView>(view);
        if (scrollView != null)
        {
            var offset = scrollView.ContentOffset;
            // A pan that drags content right moves the viewport left, hence the negation.
            var x = Math.Max(0, Math.Min(offset.X - deltaX, Math.Max(0, scrollView.ContentSize.Width - scrollView.Bounds.Width)));
            var y = Math.Max(0, Math.Min(offset.Y - deltaY, Math.Max(0, scrollView.ContentSize.Height - scrollView.Bounds.Height)));
            if (Math.Abs(x - offset.X) > 0.5 || Math.Abs(y - offset.Y) > 0.5)
            {
                scrollView.SetContentOffset(new CoreGraphics.CGPoint(x, y), animated: true);
                return $"UIScrollView.ContentOffset → ({x:0.#}, {y:0.#})";
            }
        }

        // 3. A real pan recognizer on the element itself: MKMapView's own, a custom drag
        // handler. Translation is public API; velocity stays zero, so a handler that flings
        // off velocity sees a drag that simply stops.
        if (FindRecognizer<UIPanGestureRecognizer>(view, IsDrivablePan, includeAncestors: false) is { } own)
            return await DriveApplePanRecognizerAsync(own, deltaX, deltaY, durationMs, steps);

        // 4. Raw-touch surfaces — SKCanvasView, GraphicsView — own no recognizer at all.
        if (allowSyntheticTouch
            && await AppleSyntheticDragAsync(view, deltaX, deltaY, durationMs, steps, "pan") is { } synthetic)
            return synthetic;

        // 5. Only now an enclosing pan: a real finger would have been consumed by the element
        // above, so an ancestor's recognizer is the weakest reading of the request.
        return FindRecognizer<UIPanGestureRecognizer>(view, IsDrivablePan) is { } ancestor
            ? await DriveApplePanRecognizerAsync(ancestor, deltaX, deltaY, durationMs, steps)
            : null;
    }

    /// <summary>
    /// Whether a pan recognizer is one we can honestly drive. A scroll view's built-in
    /// recognizer is not — UIScrollView scrolls from its own touch tracking, so driving it
    /// reports success and moves nothing — and neither is a screen-edge recognizer, which
    /// belongs to the navigation controller's back swipe rather than to the element.
    /// </summary>
    private static bool IsDrivablePan(UIPanGestureRecognizer recognizer)
        => recognizer is not UIScreenEdgePanGestureRecognizer && recognizer.View is not UIScrollView;

    private static async Task<string?> DriveApplePanRecognizerAsync(
        UIPanGestureRecognizer recognizer, double deltaX, double deltaY, int durationMs, int steps)
    {
        var reference = recognizer.View;
        var stepDelay = steps > 0 && durationMs > 0 ? Math.Max(1, durationMs / steps) : 0;

        recognizer.SetTranslation(CoreGraphics.CGPoint.Empty, reference);
        if (!TrySetState(recognizer, UIGestureRecognizerState.Began)) return null;
        for (var i = 1; i <= steps; i++)
        {
            recognizer.SetTranslation(
                new CoreGraphics.CGPoint(deltaX * i / steps, deltaY * i / steps), reference);
            TrySetState(recognizer, UIGestureRecognizerState.Changed);
            if (stepDelay > 0) await Task.Delay(stepDelay);
        }
        TrySetState(recognizer, UIGestureRecognizerState.Ended);
        return $"UIPanGestureRecognizer ({deltaX:0.#}, {deltaY:0.#})";
    }

    private static async Task<string?> AppleSwipeAsync(
        VisualElement element, string direction, double distance, int durationMs, bool allowSyntheticTouch)
    {
        var view = GetAppleView(element);
        if (view == null) return null;

        // A UISwipeGestureRecognizer is discrete — it only ever reports the completed swipe,
        // so driving it to Ended is the entire gesture.
        var swipeDirection = direction switch
        {
            "left" => UISwipeGestureRecognizerDirection.Left,
            "right" => UISwipeGestureRecognizerDirection.Right,
            "up" => UISwipeGestureRecognizerDirection.Up,
            "down" => UISwipeGestureRecognizerDirection.Down,
            _ => default
        };

        if (swipeDirection != default
            && FindRecognizer<UISwipeGestureRecognizer>(view, r => r.Direction.HasFlag(swipeDirection)) is { } swipe
            && TrySetState(swipe, UIGestureRecognizerState.Ended))
            return $"UISwipeGestureRecognizer {direction}";

        // Otherwise a swipe is a fast pan over the same surfaces.
        var travel = distance > 0 ? distance : 120;
        var dx = direction switch { "left" => -travel, "right" => travel, _ => 0d };
        var dy = direction switch { "up" => -travel, "down" => travel, _ => 0d };
        var swipeDuration = durationMs > 0 ? Math.Min(durationMs, 150) : 100;

        var detail = await ApplePanAsync(element, dx, dy, swipeDuration, 6, allowSyntheticTouch);
        return detail == null ? null : $"{detail} (swipe {direction})";
    }

    /// <summary>
    /// Pans an MKMapView by re-centring on the coordinate that the drag would bring to the
    /// middle. Both calls deal in CLLocationCoordinate2D, which stays reachable by reflection
    /// because the value is only ever handed straight back to MapKit, never picked apart.
    /// </summary>
    private static string? TryPanMapView(UIView mapView, double deltaX, double deltaY)
    {
        try
        {
            var mapType = mapView.GetType();
            var convert = mapType.GetMethod(
                "ConvertPoint", [typeof(CoreGraphics.CGPoint), typeof(UIView)]);
            var setCenter = mapType.GetMethods().FirstOrDefault(
                m => m.Name == "SetCenterCoordinate" && m.GetParameters().Length == 2);
            if (convert == null || setCenter == null) return null;

            var bounds = mapView.Bounds;
            // Dragging content right reveals the map further left, hence the negation.
            var point = new CoreGraphics.CGPoint(
                (double)bounds.X + (double)bounds.Width / 2 - deltaX,
                (double)bounds.Y + (double)bounds.Height / 2 - deltaY);

            var coordinate = convert.Invoke(mapView, [point, mapView]);
            if (coordinate == null) return null;

            setCenter.Invoke(mapView, [coordinate, true]);
            return $"MKMapView.SetCenterCoordinate ({deltaX:0.#}, {deltaY:0.#})";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Microsoft.Maui.DevFlow] MKMapView pan failed: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private static async Task<string?> AppleLongPressAsync(VisualElement element, int durationMs)
    {
        var recognizer = FindRecognizer<UILongPressGestureRecognizer>(GetAppleView(element));
        if (recognizer == null) return null;

        if (!TrySetState(recognizer, UIGestureRecognizerState.Began)) return null;
        await Task.Delay(durationMs);
        TrySetState(recognizer, UIGestureRecognizerState.Ended);
        return $"UILongPressGestureRecognizer ({durationMs}ms)";
    }

    private static Task<string?> AppleDoubleTapAsync(VisualElement element)
    {
        var recognizer = FindRecognizer<UITapGestureRecognizer>(
            GetAppleView(element), r => r.NumberOfTapsRequired == 2);
        if (recognizer == null) return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(
            TrySetState(recognizer, UIGestureRecognizerState.Ended) ? "UITapGestureRecognizer(2)" : null);
    }
#endif

#if WINDOWS
    private static string? WindowsPinch(VisualElement element, double scale)
    {
        var scrollViewer = FindWindowsScrollViewer(element);
        if (scrollViewer == null) return null;

        var target = (float)Math.Clamp(
            scrollViewer.ZoomFactor * scale,
            scrollViewer.MinZoomFactor,
            scrollViewer.MaxZoomFactor);

        if (Math.Abs(target - scrollViewer.ZoomFactor) < 0.001)
            return null;

        return scrollViewer.ChangeView(null, null, target)
            ? $"ScrollViewer.ZoomFactor → {target:0.##}"
            : null;
    }

    private static string? WindowsPan(VisualElement element, double deltaX, double deltaY)
    {
        var scrollViewer = FindWindowsScrollViewer(element);
        if (scrollViewer == null) return null;

        // A pan that drags content right moves the viewport left, hence the negation.
        return scrollViewer.ChangeView(
            scrollViewer.HorizontalOffset - deltaX,
            scrollViewer.VerticalOffset - deltaY,
            null)
            ? $"ScrollViewer.ChangeView ({-deltaX:0.#}, {-deltaY:0.#})"
            : null;
    }

    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindWindowsScrollViewer(VisualElement element)
    {
        if (element.Handler?.PlatformView is not Microsoft.UI.Xaml.DependencyObject platformView)
            return null;
        return platformView as Microsoft.UI.Xaml.Controls.ScrollViewer
            ?? FindWinUIDescendant<Microsoft.UI.Xaml.Controls.ScrollViewer>(platformView)
            ?? FindWinUIScrollViewer(platformView);
    }
#endif

#if MACOS
    private static string? MacPinch(VisualElement element, double scale)
    {
        var scrollView = FindMacScrollView(element);
        if (scrollView is not { AllowsMagnification: true }) return null;

        var target = Math.Clamp(
            scrollView.Magnification * scale,
            scrollView.MinMagnification,
            scrollView.MaxMagnification);
        if (Math.Abs(target - scrollView.Magnification) < 0.001)
            return null;
        scrollView.Magnification = (nfloat)target;
        return $"NSScrollView.Magnification → {target:0.##}";
    }

    private static string? MacPan(VisualElement element, double deltaX, double deltaY)
    {
        var scrollView = FindMacScrollView(element);
        if (scrollView?.ContentView == null) return null;

        var origin = scrollView.ContentView.Bounds.Location;
        // A pan that drags content right moves the viewport left, hence the negation.
        var point = new CoreGraphics.CGPoint(origin.X - deltaX, origin.Y - deltaY);
        scrollView.ContentView.ScrollToPoint(point);
        scrollView.ReflectScrolledClipView(scrollView.ContentView);
        return $"NSScrollView.ScrollToPoint ({point.X:0.#}, {point.Y:0.#})";
    }

    private static NSScrollView? FindMacScrollView(VisualElement element)
    {
        if (element.Handler?.PlatformView is not NSView view) return null;
        if (view is NSScrollView direct) return direct;

        static NSScrollView? Descend(NSView v)
        {
            if (v is NSScrollView match) return match;
            foreach (var sub in v.Subviews)
            {
                var found = Descend(sub);
                if (found != null) return found;
            }
            return null;
        }

        var descendant = Descend(view);
        if (descendant != null) return descendant;

        var ancestor = view.Superview;
        while (ancestor != null)
        {
            if (ancestor is NSScrollView match) return match;
            ancestor = ancestor.Superview;
        }
        return null;
    }
#endif
}
