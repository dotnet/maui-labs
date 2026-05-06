using AppKit;
using CoreGraphics;
using Microsoft.Maui.Controls;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Manages gesture recognizer attachment for macOS NSViews.
/// Called by MacOSViewHandler when GestureRecognizers are added/removed.
/// </summary>
public static class GestureManager
{
    public static void SetupGestures(NSView platformView, IView virtualView)
    {
        if (virtualView is not View mauiView)
            return;

        // Clear existing managed gesture recognizers
        ClearManagedGestures(platformView);

        foreach (var gestureRecognizer in mauiView.GestureRecognizers)
        {
            switch (gestureRecognizer)
            {
                case TapGestureRecognizer tap:
                    AddTapGesture(platformView, tap);
                    break;
                case PanGestureRecognizer pan:
                    AddPanGesture(platformView, pan);
                    break;
                case SwipeGestureRecognizer swipe:
                    AddSwipeGesture(platformView, swipe);
                    break;
                case PinchGestureRecognizer pinch:
                    AddPinchGesture(platformView, pinch);
                    break;
                case PointerGestureRecognizer pointer:
                    AddPointerGesture(platformView, pointer);
                    break;
            }
        }
    }

    static void ClearManagedGestures(NSView view)
    {
        if (view is MacOSContainerView container)
            container.InterceptChildHitTesting = false;

        if (view.GestureRecognizers == null)
            return;

        var toRemove = new List<NSGestureRecognizer>();
        foreach (var gr in view.GestureRecognizers)
        {
            if (gr is MacOSTapGestureRecognizer or MacOSPanGestureRecognizer or MacOSSwipeGestureRecognizer or MacOSPinchGestureRecognizer)
                toRemove.Add(gr);
        }
        foreach (var gr in toRemove)
            view.RemoveGestureRecognizer(gr);

        // Remove tracking areas for pointer gestures
        foreach (var area in view.TrackingAreas())
        {
            if (area is MacOSPointerTrackingArea)
                view.RemoveTrackingArea(area);
        }
    }

    static void AddTapGesture(NSView view, TapGestureRecognizer tap)
    {
        var recognizer = new MacOSTapGestureRecognizer(tap)
        {
            NumberOfClicksRequired = (nint)tap.NumberOfTapsRequired,
        };
        view.AddGestureRecognizer(recognizer);

        // AppKit delivers events only to the hit-test view's own gesture recognizers,
        // not ancestors. Intercept child hit testing so clicks on child views
        // (e.g. Label NSTextFields) still fire this view's gesture recognizer.
        if (view is MacOSContainerView container)
            container.InterceptChildHitTesting = true;
    }

    static void AddPanGesture(NSView view, PanGestureRecognizer pan)
    {
        var recognizer = new MacOSPanGestureRecognizer(pan);
        view.AddGestureRecognizer(recognizer);
    }

    static void AddSwipeGesture(NSView view, SwipeGestureRecognizer swipe)
    {
        // Combine with existing swipe recognizer if present, so we don't have
        // multiple NSPanGestureRecognizers competing on the same view
        MacOSSwipeGestureRecognizer? existing = null;
        if (view.GestureRecognizers != null)
        {
            foreach (var gr in view.GestureRecognizers)
            {
                if (gr is MacOSSwipeGestureRecognizer s)
                { existing = s; break; }
            }
        }

        if (existing != null)
        {
            existing.AddSwipeRecognizer(swipe);
        }
        else
        {
            var recognizer = new MacOSSwipeGestureRecognizer(swipe);
            view.AddGestureRecognizer(recognizer);
        }
    }

    static void AddPinchGesture(NSView view, PinchGestureRecognizer pinch)
    {
        var recognizer = new MacOSPinchGestureRecognizer(pinch);
        view.AddGestureRecognizer(recognizer);
    }

    static void AddPointerGesture(NSView view, PointerGestureRecognizer pointer)
    {
        var area = new MacOSPointerTrackingArea(view, pointer);
        view.AddTrackingArea(area);
    }
}

internal class MacOSTapGestureRecognizer : NSClickGestureRecognizer
{
    readonly TapGestureRecognizer _tapGesture;

    public MacOSTapGestureRecognizer(TapGestureRecognizer tapGesture)
    {
        _tapGesture = tapGesture;
        Action = new ObjCRuntime.Selector("handleTap:");
        Target = this;
    }

    [Foundation.Export("handleTap:")]
    void HandleTap(NSGestureRecognizer recognizer)
    {
        // SendTapped internally calls Command.Execute, so do NOT call it separately.
        var sendTapped = typeof(TapGestureRecognizer).GetMethod(
            "SendTapped", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (sendTapped != null)
        {
            var parent = (_tapGesture as IElement)?.FindParentOfType<View>();
            var parameters = sendTapped.GetParameters();
            if (parameters.Length == 1)
                sendTapped.Invoke(_tapGesture, new object?[] { parent });
            else if (parameters.Length == 2)
                sendTapped.Invoke(_tapGesture, new object?[] { parent, null });
        }
        else
        {
            // Fallback if SendTapped is not found
            _tapGesture.Command?.Execute(_tapGesture.CommandParameter);
        }
    }
}

internal class MacOSPanGestureRecognizer : NSPanGestureRecognizer
{
    readonly PanGestureRecognizer _panGesture;
    CGPoint _startPoint;

    public MacOSPanGestureRecognizer(PanGestureRecognizer panGesture)
    {
        _panGesture = panGesture;
        Action = new ObjCRuntime.Selector("handlePan:");
        Target = this;
    }

    [Foundation.Export("handlePan:")]
    void HandlePan(NSPanGestureRecognizer recognizer)
    {
        var translation = recognizer.TranslationInView(recognizer.View);

        switch (recognizer.State)
        {
            case NSGestureRecognizerState.Began:
                _startPoint = translation;
                ((IPanGestureController)_panGesture).SendPanStarted(
                    recognizer.View?.FindMauiView(), PanGestureRecognizer.CurrentId.Value);
                break;
            case NSGestureRecognizerState.Changed:
                ((IPanGestureController)_panGesture).SendPan(
                    recognizer.View?.FindMauiView(),
                    translation.X - _startPoint.X,
                    translation.Y - _startPoint.Y,
                    PanGestureRecognizer.CurrentId.Value);
                break;
            case NSGestureRecognizerState.Ended:
            case NSGestureRecognizerState.Cancelled:
                ((IPanGestureController)_panGesture).SendPanCompleted(
                    recognizer.View?.FindMauiView(), PanGestureRecognizer.CurrentId.Value);
                break;
        }
    }
}

internal class MacOSPointerTrackingArea : NSTrackingArea
{
    readonly PointerGestureRecognizer _pointerGesture;
    static readonly System.Reflection.MethodInfo? _sendEntered;
    static readonly System.Reflection.MethodInfo? _sendExited;
    static readonly System.Reflection.MethodInfo? _sendMoved;

    static MacOSPointerTrackingArea()
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        _sendEntered = typeof(PointerGestureRecognizer).GetMethod("SendPointerEntered", flags);
        _sendExited = typeof(PointerGestureRecognizer).GetMethod("SendPointerExited", flags);
        _sendMoved = typeof(PointerGestureRecognizer).GetMethod("SendPointerMoved", flags);
    }

    public MacOSPointerTrackingArea(NSView view, PointerGestureRecognizer pointerGesture)
        : base(view.Bounds,
            NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved |
            NSTrackingAreaOptions.ActiveInKeyWindow | NSTrackingAreaOptions.InVisibleRect,
            view, null)
    {
        _pointerGesture = pointerGesture;
    }

    public PointerGestureRecognizer Recognizer => _pointerGesture;

    static Func<IElement?, Point?> CreateGetPosition(NSView view, NSEvent? theEvent)
    {
        return (IElement? relativeTo) =>
        {
            if (theEvent == null) return null;
            // Get mouse location in window coordinates, convert to view
            var locInView = view.ConvertPointFromView(theEvent.LocationInWindow, null);
            // Flip Y for MAUI (top-left origin) if view is not flipped
            if (!view.IsFlipped)
                locInView.Y = view.Bounds.Height - locInView.Y;

            if (relativeTo == null)
                return new Microsoft.Maui.Graphics.Point(locInView.X, locInView.Y);

            // If relativeTo is the same element, return position in that element's coordinate space
            if (relativeTo is View relView && relView.Handler?.PlatformView is NSView relNative)
            {
                var locInRel = relNative.ConvertPointFromView(theEvent.LocationInWindow, null);
                if (!relNative.IsFlipped)
                    locInRel.Y = relNative.Bounds.Height - locInRel.Y;
                return new Microsoft.Maui.Graphics.Point(locInRel.X, locInRel.Y);
            }

            return new Microsoft.Maui.Graphics.Point(locInView.X, locInView.Y);
        };
    }

    public void FireEntered(View? sender, NSEvent? theEvent = null)
    {
        Func<IElement?, Point?>? getPos = theEvent != null && Owner is NSView view ? CreateGetPosition(view, theEvent) : null;
        _sendEntered?.Invoke(_pointerGesture, new object?[] { sender!, getPos, null, ButtonsMask.Primary });
    }

    public void FireExited(View? sender, NSEvent? theEvent = null)
    {
        Func<IElement?, Point?>? getPos = theEvent != null && Owner is NSView view ? CreateGetPosition(view, theEvent) : null;
        _sendExited?.Invoke(_pointerGesture, new object?[] { sender!, getPos, null, ButtonsMask.Primary });
    }

    public void FireMoved(View? sender, NSEvent? theEvent = null)
    {
        Func<IElement?, Point?>? getPos = theEvent != null && Owner is NSView view ? CreateGetPosition(view, theEvent) : null;
        _sendMoved?.Invoke(_pointerGesture, new object?[] { sender!, getPos, null, ButtonsMask.Primary });
    }
}

internal static class GestureExtensions
{
    public static View? FindMauiView(this NSView? view)
    {
        // Walk up the view hierarchy to find the MAUI view element
        return null; // Placeholder — requires handler → VirtualView lookup
    }

    public static T? FindParentOfType<T>(this IElement element) where T : class
    {
        var current = element.Parent;
        while (current != null)
        {
            if (current is T result)
                return result;
            current = current.Parent;
        }
        return null;
    }

    public static void SendTapped(this IGestureRecognizer recognizer, View? view)
    {
        if (recognizer is TapGestureRecognizer tap)
            tap.Command?.Execute(tap.CommandParameter);
    }
}

/// <summary>
/// Swipe gesture using a single NSPanGestureRecognizer for all directions.
/// macOS doesn't have a native swipe gesture recognizer for mouse input,
/// so we detect swipe from pan distance and direction at the end of the gesture.
/// All SwipeGestureRecognizers on the same view share one native recognizer.
/// </summary>
internal class MacOSSwipeGestureRecognizer : NSPanGestureRecognizer
{
    readonly List<SwipeGestureRecognizer> _swipeGestures = new();
    CGPoint _startLocation;

    public MacOSSwipeGestureRecognizer(SwipeGestureRecognizer swipeGesture)
    {
        _swipeGestures.Add(swipeGesture);
        Action = new ObjCRuntime.Selector("handleSwipe:");
        Target = this;
    }

    public void AddSwipeRecognizer(SwipeGestureRecognizer swipeGesture)
    {
        _swipeGestures.Add(swipeGesture);
    }

    [Foundation.Export("handleSwipe:")]
    void HandleSwipe(NSPanGestureRecognizer recognizer)
    {
        switch (recognizer.State)
        {
            case NSGestureRecognizerState.Began:
                _startLocation = recognizer.LocationInView(recognizer.View);
                break;
            case NSGestureRecognizerState.Changed:
            case NSGestureRecognizerState.Ended:
                var currentLocation = recognizer.LocationInView(recognizer.View);
                var dx = (double)(currentLocation.X - _startLocation.X);
                var dy = (double)(currentLocation.Y - _startLocation.Y);

                // Accumulate swipe distance on all recognizers
                foreach (var gesture in _swipeGestures)
                {
                    var parent = (gesture as IElement)?.FindParentOfType<View>();
                    ((ISwipeGestureController)gesture).SendSwipe(parent as Element, dx, dy);
                }

                // On end, check if threshold was met and fire the Swiped event
                if (recognizer.State == NSGestureRecognizerState.Ended)
                {
                    foreach (var gesture in _swipeGestures)
                    {
                        var parent = (gesture as IElement)?.FindParentOfType<View>();
                        ((ISwipeGestureController)gesture).DetectSwipe(parent, gesture.Direction);
                    }
                }
                break;
        }
    }
}

/// <summary>
/// Pinch gesture using NSMagnificationGestureRecognizer for trackpad pinch-to-zoom.
/// </summary>
internal class MacOSPinchGestureRecognizer : NSMagnificationGestureRecognizer
{
    readonly PinchGestureRecognizer _pinchGesture;

    public MacOSPinchGestureRecognizer(PinchGestureRecognizer pinchGesture)
    {
        _pinchGesture = pinchGesture;
        Action = new ObjCRuntime.Selector("handlePinch:");
        Target = this;
    }

    [Foundation.Export("handlePinch:")]
    void HandlePinch(NSMagnificationGestureRecognizer recognizer)
    {
        // NSMagnificationGestureRecognizer.Magnification is the delta from 1.0
        var scale = 1.0 + (double)recognizer.Magnification;
        // Use center of view as the pinch origin (normalized 0-1)
        var scalePoint = new Point(0.5, 0.5);

        var controller = (IPinchGestureController)_pinchGesture;
        var parent = (_pinchGesture as IElement)?.FindParentOfType<View>();

        switch (recognizer.State)
        {
            case NSGestureRecognizerState.Began:
                controller.SendPinchStarted(parent!, scalePoint);
                break;
            case NSGestureRecognizerState.Changed:
                controller.SendPinch(parent!, scale, scalePoint);
                break;
            case NSGestureRecognizerState.Ended:
            case NSGestureRecognizerState.Cancelled:
                controller.SendPinchEnded(parent!);
                break;
        }
    }
}
