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
                case LongPressGestureRecognizer longPress:
                    AddLongPressGesture(platformView, longPress);
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
            if (gr is MacOSTapGestureRecognizer or MacOSPanGestureRecognizer or MacOSSwipeGestureRecognizer or MacOSPinchGestureRecognizer or MacOSLongPressGestureRecognizer)
                toRemove.Add(gr);
        }
        foreach (var gr in toRemove)
        {
            if (gr is MacOSTapGestureRecognizer tap)
                tap.Disconnect();
            else if (gr is MacOSLongPressGestureRecognizer longPress)
                longPress.Disconnect();
            view.RemoveGestureRecognizer(gr);
        }

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
            ButtonMask = (nuint)tap.Buttons,
        };
        view.AddGestureRecognizer(recognizer);

        // Intercept child hit testing so clicks on non-interactive child views
        // (e.g. Label NSTextFields) still fire this view's gesture recognizer.
        // Interactive children (buttons, entries) keep their own clicks.
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

    static void AddLongPressGesture(NSView view, LongPressGestureRecognizer longPress)
    {
        view.AddGestureRecognizer(new MacOSLongPressGestureRecognizer(longPress));

        // Same as taps: a press on a non-interactive child view (e.g. a Label's
        // NSTextField) must still reach this view's recognizer, while child controls
        // such as a Button or Entry keep handling their own clicks.
        if (view is MacOSContainerView container)
            container.InterceptChildHitTesting = true;
    }

    /// <summary>
    /// Builds the <c>getPosition</c> callback MAUI gesture event args expose from the
    /// recognizer's current location. See <see cref="ResolvePosition"/> for the contract.
    /// </summary>
    internal static Func<IElement?, Point?> CreateGetPosition(NSGestureRecognizer recognizer)
    {
        // Snapshot the location now: the args can be queried after the recognizer has moved on.
        var locationInWindow = recognizer.LocationInView(null);
        var window = recognizer.View?.Window;
        return relativeTo => ResolvePosition(locationInWindow, window, relativeTo);
    }

    /// <summary>
    /// Builds the <c>getPosition</c> callback for an AppKit mouse event.
    /// </summary>
    internal static Func<IElement?, Point?> CreateGetPosition(NSEvent theEvent, NSWindow? fallbackWindow)
    {
        var locationInWindow = theEvent.LocationInWindow;
        var window = theEvent.Window ?? fallbackWindow;
        return relativeTo => ResolvePosition(locationInWindow, window, relativeTo);
    }

    /// <summary>
    /// Converts a location in <paramref name="window"/>'s base coordinates into MAUI's
    /// top-left origin coordinate space.
    /// </summary>
    /// <remarks>
    /// <para>A <see langword="null"/> <paramref name="relativeTo"/> yields window coordinates
    /// (relative to the window's content view) for every gesture type. This is what the MAUI 11
    /// iOS/Mac Catalyst backend (<c>UIGestureRecognizer.LocationInView(null)</c>) and the Windows
    /// backend (<c>TappedRoutedEventArgs.GetPosition(null)</c>) return, and it is the documented
    /// contract of <see cref="LongPressedEventArgs.GetPosition"/>.</para>
    /// <para>Any other element is resolved through its handler's platform view (a page, layout
    /// or view; a <see cref="Window"/> maps to its content view). An element without a
    /// connected AppKit view yields <see langword="null"/>.</para>
    /// </remarks>
    internal static Point? ResolvePosition(CGPoint locationInWindow, NSWindow? window, IElement? relativeTo)
    {
        if (window is null)
            return null;

        NSView? target = relativeTo is null
            ? window.ContentView
            : relativeTo.Handler?.PlatformView switch
            {
                NSView view => view,
                NSWindow platformWindow => platformWindow.ContentView,
                _ => null,
            };

        var targetWindow = target?.Window;
        if (target is null || targetWindow is null)
            return null;

        // An element in another window (e.g. a modal sheet) is reached through screen space.
        if (targetWindow != window)
        {
            var onScreen = window.ConvertPointToScreen(locationInWindow);
            locationInWindow = targetWindow.ConvertPointFromScreen(onScreen);
        }

        var location = target.ConvertPointFromView(locationInWindow, null);
        if (!target.IsFlipped)
            location.Y = target.Bounds.Height - location.Y;

        return new Point(location.X, location.Y);
    }
}

internal class MacOSTapGestureRecognizer : NSClickGestureRecognizer
{
    readonly TapGestureRecognizer _tapGesture;
    bool _isConnected;

    public MacOSTapGestureRecognizer(TapGestureRecognizer tapGesture)
    {
        _tapGesture = tapGesture;
        _tapGesture.PropertyChanged += OnTapGesturePropertyChanged;
        _isConnected = true;
        Action = new ObjCRuntime.Selector("handleTap:");
        Target = this;
    }

    void OnTapGesturePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TapGestureRecognizer.Buttons))
            ButtonMask = (nuint)_tapGesture.Buttons;
    }

    public void Disconnect()
    {
        if (!_isConnected)
            return;

        _tapGesture.PropertyChanged -= OnTapGesturePropertyChanged;
        _isConnected = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Disconnect();
        base.Dispose(disposing);
    }

    [Foundation.Export("handleTap:")]
    void HandleTap(NSGestureRecognizer recognizer)
    {
        // SendTapped executes Command and raises Tapped, so do NOT execute the command separately.
        // It requires the owning view, which a detached recognizer no longer has.
        var parent = (_tapGesture as IElement)?.FindParentOfType<View>();
        if (parent is not null)
            _tapGesture.SendTapped(parent, GestureManager.CreateGetPosition(recognizer));
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

    public MacOSPointerTrackingArea(NSView view, PointerGestureRecognizer pointerGesture)
        : base(view.Bounds,
            NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved |
            NSTrackingAreaOptions.ActiveInKeyWindow | NSTrackingAreaOptions.InVisibleRect,
            view, null)
    {
        _pointerGesture = pointerGesture;
    }

    public PointerGestureRecognizer Recognizer => _pointerGesture;

    // The PointerGestureRecognizer Send* methods require the owning view; a recognizer that
    // has been detached from its view has nothing to report against.
    public void FireEntered(View? sender, NSEvent? theEvent = null)
    {
        if (sender is not null)
            _pointerGesture.SendPointerEntered(sender, GetPositionFor(theEvent));
    }

    public void FireExited(View? sender, NSEvent? theEvent = null)
    {
        if (sender is not null)
            _pointerGesture.SendPointerExited(sender, GetPositionFor(theEvent));
    }

    public void FireMoved(View? sender, NSEvent? theEvent = null)
    {
        if (sender is not null)
            _pointerGesture.SendPointerMoved(sender, GetPositionFor(theEvent));
    }

    Func<IElement?, Point?>? GetPositionFor(NSEvent? theEvent)
        => theEvent != null && Owner is NSView view ? GestureManager.CreateGetPosition(theEvent, view.Window) : null;
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

/// <summary>
/// Long press using NSPressGestureRecognizer, mirroring the MAUI iOS/Mac Catalyst mapping of
/// UILongPressGestureRecognizer states onto <see cref="LongPressGestureRecognizer.SendLongPressing"/>
/// and <see cref="LongPressGestureRecognizer.SendLongPressed"/>.
/// </summary>
internal class MacOSLongPressGestureRecognizer : NSPressGestureRecognizer
{
    readonly LongPressGestureRecognizer _longPressGesture;
    bool _isConnected;

    public MacOSLongPressGestureRecognizer(LongPressGestureRecognizer longPressGesture)
    {
        _longPressGesture = longPressGesture;
        _longPressGesture.PropertyChanged += OnLongPressGesturePropertyChanged;
        _isConnected = true;
        UpdateFromGesture();
        Action = new ObjCRuntime.Selector("handleLongPress:");
        Target = this;
    }

    void OnLongPressGesturePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LongPressGestureRecognizer.MinimumPressDuration) ||
            e.PropertyName == nameof(LongPressGestureRecognizer.AllowableMovement))
            UpdateFromGesture();
    }

    // NumberOfTouchesRequired has no mouse equivalent: NSPressGestureRecognizer only counts
    // touches for Touch Bar input, so it is left at AppKit's default.
    void UpdateFromGesture()
    {
        MinimumPressDuration = _longPressGesture.MinimumPressDuration / 1000.0;
        AllowableMovement = (nfloat)_longPressGesture.AllowableMovement;
    }

    public void Disconnect()
    {
        if (!_isConnected)
            return;

        _longPressGesture.PropertyChanged -= OnLongPressGesturePropertyChanged;
        _isConnected = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Disconnect();
        base.Dispose(disposing);
    }

    [Foundation.Export("handleLongPress:")]
    void HandleLongPress(NSPressGestureRecognizer recognizer)
    {
        var parent = (_longPressGesture as IElement)?.FindParentOfType<View>();
        if (parent is null)
            return;

        var getPosition = GestureManager.CreateGetPosition(recognizer);
        switch (recognizer.State)
        {
            case NSGestureRecognizerState.Began:
                _longPressGesture.SendLongPressing(parent, GestureStatus.Started, getPosition);
                break;
            case NSGestureRecognizerState.Changed:
                _longPressGesture.SendLongPressing(parent, GestureStatus.Running, getPosition);
                break;
            case NSGestureRecognizerState.Ended:
                _longPressGesture.SendLongPressed(parent, getPosition);
                _longPressGesture.SendLongPressing(parent, GestureStatus.Completed, getPosition);
                break;
            case NSGestureRecognizerState.Cancelled:
            case NSGestureRecognizerState.Failed:
                _longPressGesture.SendLongPressing(parent, GestureStatus.Canceled, getPosition);
                break;
        }
    }
}
