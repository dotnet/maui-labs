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
/// in <see cref="Core.DevFlowAgentService"/>. This is what makes pinch-to-zoom work on
/// controls that own their gestures internally (Map, WebView, SKCanvasView, native scroll views)
/// and therefore expose no <c>PinchGestureRecognizer</c> to walk.
///
/// Fidelity varies by platform, and each method reports how it was handled so callers can tell:
/// <list type="bullet">
/// <item>Android — genuine multi-pointer <c>MotionEvent</c>s dispatched through the activity,
/// so the whole hit-test and <c>GestureDetector</c> pipeline runs. Fully faithful.</item>
/// <item>iOS / Mac Catalyst — drives the real native zoom/pan surfaces (MKMapView camera,
/// UIScrollView zoom/offset) and, failing those, the attached UIGestureRecognizers.
/// In-process synthetic <c>UITouch</c> needs private API and is deliberately not attempted.</item>
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
        return await ApplePinchAsync(element, scale, origin, durationMs, steps);
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
        return await ApplePanAsync(element, deltaX, deltaY);
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
        int pointerCount,
        Func<double, PointF[]> positionsAt,
        int durationMs,
        int steps,
        int holdMs = 0)
    {
        var activity = global::Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity == null) return false;

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
            var ev = MotionEvent.Obtain(
                downTime, eventTime, action, activePointers,
                properties[..activePointers], coords[..activePointers],
                0, (MotionEventButtonState)0, 1f, 1f, 0, (Edge)0,
                InputSourceType.Touchscreen, (MotionEventFlags)0);
            if (ev == null) return false;
            try { return activity.DispatchTouchEvent(ev); }
            finally { ev.Recycle(); }
        }

        try
        {
            Apply(positionsAt(0));

            Send(MotionEventActions.Down, 1);
            for (var i = 1; i < pointerCount; i++)
                Send((MotionEventActions)((int)MotionEventActions.PointerDown | (i << PointerIndexShift)), i + 1);

            if (holdMs > 0)
            {
                await Task.Delay(holdMs);
                eventTime += holdMs;
            }

            for (var step = 1; step <= steps; step++)
            {
                eventTime += Math.Max(1, stepDelay);
                Apply(positionsAt((double)step / steps));
                Send(MotionEventActions.Move, pointerCount);
                if (stepDelay > 0) await Task.Delay(stepDelay);
            }

            eventTime += 1;
            for (var i = pointerCount - 1; i >= 1; i--)
                Send((MotionEventActions)((int)MotionEventActions.PointerUp | (i << PointerIndexShift)), i + 1);
            Send(MotionEventActions.Up, 1);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] Android touch injection failed: {ex.GetBaseException().Message}");
            return false;
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
            pointerCount: 1,
            positionsAt: t => [new PointF(start.X + pixelX * (float)t, start.Y + pixelY * (float)t)],
            durationMs: durationMs > 0 ? durationMs : 200,
            steps: steps);

        return handled ? $"MotionEvent {label} ({deltaX:0.#}, {deltaY:0.#})" : null;
    }

    private static async Task<string?> AndroidLongPressAsync(VisualElement element, int durationMs)
    {
        var view = GetAndroidView(element);
        if (view == null) return null;
        if (GetAndroidTouchGeometry(view, new Point(0.5, 0.5)) is not { } geometry) return null;

        var center = geometry.Center;
        var handled = await InjectAndroidTouchAsync(
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

        if (!await InjectAndroidTouchAsync(1, at, 0, 0)) return null;
        // Comfortably inside Android's ~300ms double-tap timeout.
        await Task.Delay(80);
        if (!await InjectAndroidTouchAsync(1, at, 0, 0)) return null;

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
    /// </summary>
    private static T? FindRecognizer<T>(UIView? view, Func<T, bool>? predicate = null) where T : UIGestureRecognizer
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

    private static async Task<string?> ApplePinchAsync(VisualElement element, double scale, Point origin, int durationMs, int steps)
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
            scrollView.SetZoomScale(target, animated: true);
            return $"UIScrollView.ZoomScale → {target:0.##}";
        }

        // 3. Fall back to driving whatever pinch recognizer the control installed.
        var recognizer = FindRecognizer<UIPinchGestureRecognizer>(view);
        if (recognizer == null) return null;

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

    private static Task<string?> ApplePanAsync(VisualElement element, double deltaX, double deltaY)
    {
        var view = GetAppleView(element);
        if (view == null) return Task.FromResult<string?>(null);

        var scrollView = view as UIScrollView ?? FindNativeSubview<UIScrollView>(view);
        if (scrollView != null)
        {
            var offset = scrollView.ContentOffset;
            // A pan that drags content right moves the viewport left, hence the negation.
            var x = Math.Max(0, Math.Min(offset.X - deltaX, Math.Max(0, scrollView.ContentSize.Width - scrollView.Bounds.Width)));
            var y = Math.Max(0, Math.Min(offset.Y - deltaY, Math.Max(0, scrollView.ContentSize.Height - scrollView.Bounds.Height)));
            scrollView.SetContentOffset(new CoreGraphics.CGPoint(x, y), animated: true);
            return Task.FromResult<string?>($"UIScrollView.ContentOffset → ({x:0.#}, {y:0.#})");
        }

        return Task.FromResult<string?>(null);
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
        scrollView.Magnification = target;
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
