using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.MacOS.Handlers;
using AppKit;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

public class MacOSContainerView : NSView
{
    public Func<double, double, Graphics.Size>? CrossPlatformMeasure { get; set; }
    public Func<Graphics.Rect, Graphics.Size>? CrossPlatformArrange { get; set; }

    /// When true, HitTest returns this view for clicks on non-interactive children
    /// (e.g. a Label's NSTextField) so that NSGestureRecognizers attached here receive
    /// the events. Interactive descendants (buttons, editable or selectable text,
    /// other enabled controls, nested gesture owners) keep their hits, the way UIKit
    /// lets a child UIControl handle its own touches under an ancestor's recognizer.
    public bool InterceptChildHitTesting { get; set; }

    /// <summary>
    /// When true, safe area insets are never applied regardless of ISafeAreaView.
    /// Used for modal pages that are already positioned within safe bounds.
    /// </summary>
    public bool IgnorePlatformSafeArea { get; set; }

    /// <summary>
    /// When true, PlatformArrange won't override the frame set externally.
    /// Used for modal pages whose frame is managed by MacOSModalManager.
    /// </summary>
    public bool ExternalFrameManagement { get; set; }

    WeakReference<IView>? _viewReference;

    /// <summary>
    /// The cross-platform IView this container represents, used for safe area checks.
    /// </summary>
    public IView? View
    {
        get => _viewReference != null && _viewReference.TryGetTarget(out var v) ? v : null;
        set => _viewReference = value == null ? null : new(value);
    }

    public MacOSContainerView()
    {
        WantsLayer = true;
    }

    // NSView defaults to bottom-left origin; MAUI needs top-left
    public override bool IsFlipped => true;

    public override NSView? HitTest(CGPoint point)
    {
        if (InterceptChildHitTesting)
        {
            // Let the normal hit test find the deepest child first
            var child = base.HitTest(point);

            // If the hit lands inside an NSScrollView that is a descendant of this
            // container, let it through so scroll wheel events work correctly.
            // Don't bypass interception if the NSScrollView is an ancestor (e.g.
            // CollectionView cells inside a scroll view still need tap interception).
            if (child != null && child != this && IsInsideDescendantScrollView(child, this))
                return child;

            // A press on a child control (Button, Entry, Switch, ...) belongs to that
            // control, or to a nested view that owns its own gesture recognizers.
            if (child != null && child != this && IsInsideInteractiveDescendant(child, this))
                return child;

            // Otherwise intercept: return this view so our gesture recognizers
            // receive the event
            var local = ConvertPointFromView(point, Superview);
            if (Bounds.Contains(local))
                return this;
        }

        // Check if this view itself is InputTransparent — pass through to views behind
        if (View is IView v && v.InputTransparent)
            return null;

        return base.HitTest(point);
    }

    /// <summary>
    /// Check if child is inside an NSScrollView that is a descendant of container.
    /// Walks from child up to container; returns true only if an NSScrollView is found
    /// between them (not above container).
    /// </summary>
    static bool IsInsideDescendantScrollView(NSView child, NSView container)
    {
        var current = child;
        while (current != null && current != container)
        {
            if (current is NSScrollView)
                return true;
            current = current.Superview;
        }
        return false;
    }

    /// <summary>
    /// Check if child, or one of its ancestors below container, handles mouse input itself.
    /// </summary>
    internal static bool IsInsideInteractiveDescendant(NSView child, NSView container)
    {
        var current = child;
        while (current != null && current != container)
        {
            if (IsInteractive(current))
                return true;
            current = current.Superview;
        }
        return false;
    }

    static bool IsInteractive(NSView view) => view switch
    {
        // Labels are non-editable, non-selectable text fields and stay interceptable.
        NSTextField textField => textField.Enabled && (textField.Editable || textField.Selectable),
        NSImageView imageView => imageView.Enabled && imageView.Editable,
        NSControl control => control.Enabled,
        // Covers the field editor of a focused Entry as well as Editor.
        NSTextView textView => textView.Editable || textView.Selectable,
        _ => view.GestureRecognizers is { Length: > 0 },
    };

    bool ShouldApplySafeArea()
    {
        if (IgnorePlatformSafeArea)
            return false;
        if (View is ISafeAreaView sav)
            return !sav.IgnoreSafeArea;
        return false;
    }

    NSEdgeInsets GetSafeAreaInsets()
    {
        return SafeAreaInsets;
    }

    /// <summary>
    /// NSView has no SizeThatFits — we provide our own for the base handler to call.
    /// </summary>
    public CGSize SizeThatFits(CGSize size)
    {
        if (CrossPlatformMeasure == null)
            return IntrinsicContentSize;

        var width = double.IsNaN(size.Width) || double.IsInfinity(size.Width)
            ? double.PositiveInfinity
            : (double)size.Width;
        var height = double.IsNaN(size.Height) || double.IsInfinity(size.Height)
            ? double.PositiveInfinity
            : (double)size.Height;

        if (ShouldApplySafeArea())
        {
            var insets = GetSafeAreaInsets();
            width -= (double)(insets.Left + insets.Right);
            height -= (double)(insets.Top + insets.Bottom);
        }

        var result = CrossPlatformMeasure(width, height);

        if (ShouldApplySafeArea())
        {
            var insets = GetSafeAreaInsets();
            result = new Graphics.Size(
                result.Width + (double)(insets.Left + insets.Right),
                result.Height + (double)(insets.Top + insets.Bottom));
        }

        return new CGSize(result.Width, result.Height);
    }

    public override void Layout()
    {
        base.Layout();

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        try
        {
            var applySafeArea = ShouldApplySafeArea();
            var measureWidth = (double)bounds.Width;
            var measureHeight = (double)bounds.Height;

            if (applySafeArea)
            {
                var insets = GetSafeAreaInsets();
                measureWidth -= (double)(insets.Left + insets.Right);
                measureHeight -= (double)(insets.Top + insets.Bottom);
            }

            // Measure pass must happen before arrange — MAUI's layout engine
            // requires IView.Measure() to be called (which sets DesiredSize) before
            // IView.Arrange() can produce correct results.
            CrossPlatformMeasure?.Invoke(measureWidth, measureHeight);

            if (applySafeArea)
            {
                var insets = GetSafeAreaInsets();
                CrossPlatformArrange?.Invoke(new Graphics.Rect(
                    (double)insets.Left, (double)insets.Top,
                    measureWidth, measureHeight));
            }
            else
            {
                CrossPlatformArrange?.Invoke(new Graphics.Rect(
                    0, 0,
                    bounds.Width,
                    bounds.Height));
            }
        }
        catch (Exception ex)
        {
            // AppKit retries Layout() infinitely when exceptions propagate
            Console.Error.WriteLine($"[MacOSContainerView.Layout] {ex}");
        }
    }

    public override CGSize IntrinsicContentSize => new CGSize(NSView.NoIntrinsicMetric, NSView.NoIntrinsicMetric);

    public override void MouseEntered(NSEvent theEvent)
    {
        foreach (var area in TrackingAreas())
        {
            if (area is MacOSPointerTrackingArea pointerArea)
            {
                var parent = (pointerArea.Recognizer as IElement)?.FindParentOfType<View>();
                pointerArea.FireEntered(parent, theEvent);
            }
        }
        base.MouseEntered(theEvent);
    }

    public override void MouseExited(NSEvent theEvent)
    {
        foreach (var area in TrackingAreas())
        {
            if (area is MacOSPointerTrackingArea pointerArea)
            {
                var parent = (pointerArea.Recognizer as IElement)?.FindParentOfType<View>();
                pointerArea.FireExited(parent, theEvent);
            }
        }
        base.MouseExited(theEvent);
    }

    public override void MouseMoved(NSEvent theEvent)
    {
        foreach (var area in TrackingAreas())
        {
            if (area is MacOSPointerTrackingArea pointerArea)
            {
                var parent = (pointerArea.Recognizer as IElement)?.FindParentOfType<View>();
                pointerArea.FireMoved(parent, theEvent);
            }
        }
        base.MouseMoved(theEvent);
    }
}
