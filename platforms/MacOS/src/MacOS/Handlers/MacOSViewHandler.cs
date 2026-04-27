using CoreGraphics;
using CoreAnimation;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Base view handler for macOS that provides PlatformArrange and GetDesiredSize implementations.
/// The net10.0 (platform-agnostic) ViewHandler has no-op PlatformArrange and returns Size.Zero from
/// GetDesiredSize, which means NSView frames are never set and all views measure as zero.
/// This base class bridges MAUI's layout system to AppKit by setting NSView Frame in
/// PlatformArrange and using IntrinsicContentSize/FittingSize/SizeThatFits in GetDesiredSize.
/// </summary>
public abstract class MacOSViewHandler<TVirtualView, TPlatformView> : ViewHandler<TVirtualView, TPlatformView>
    where TVirtualView : class, IView
    where TPlatformView : NSView
{
    static MacOSViewHandler()
    {
        try
        {
            if (ViewMapper is PropertyMapper<IView, IViewHandler> mapper)
            {
                mapper[nameof(IView.Shadow)] = MapShadow;
                mapper[nameof(IView.Opacity)] = MapOpacity;
                mapper[nameof(IView.Visibility)] = MapVisibility;
                mapper[nameof(IView.IsEnabled)] = MapIsEnabled;
                mapper[nameof(IView.Background)] = MapBackground;
                mapper[nameof(IView.FlowDirection)] = MapFlowDirection;
                mapper[nameof(IView.AutomationId)] = MapAutomationId;
                mapper[nameof(IView.Clip)] = MapClip;
                mapper[nameof(IView.TranslationX)] = MapTransform;
                mapper[nameof(IView.TranslationY)] = MapTransform;
                mapper[nameof(IView.Rotation)] = MapTransform;
                mapper[nameof(IView.RotationX)] = MapTransform;
                mapper[nameof(IView.RotationY)] = MapTransform;
                mapper[nameof(IView.Scale)] = MapTransform;
                mapper[nameof(IView.ScaleX)] = MapTransform;
                mapper[nameof(IView.ScaleY)] = MapTransform;
                mapper[nameof(IView.AnchorX)] = MapTransform;
                mapper[nameof(IView.AnchorY)] = MapTransform;
                mapper[nameof(IView.InputTransparent)] = MapInputTransparent;
                mapper[nameof(IView.ZIndex)] = MapZIndex;
                mapper["ContextFlyout"] = MapContextFlyout;
                mapper["ToolTipProperties.Text"] = MapToolTip;
                mapper[nameof(IView.Semantics)] = MapSemantics;
            }

            if (ViewCommandMapper is CommandMapper<IView, IViewHandler> cmdMapper)
            {
                cmdMapper[nameof(IView.Focus)] = MapFocus;
            }
        }
        catch
        {
            // Mapping registration failed — non-fatal
        }
    }

    protected MacOSViewHandler(IPropertyMapper mapper) : base(mapper)
    {
    }

    protected MacOSViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper) : base(mapper, commandMapper)
    {
    }

    protected override void ConnectHandler(TPlatformView platformView)
    {
        base.ConnectHandler(platformView);
        SetupGestures(platformView);

        if (VirtualView is Microsoft.Maui.Controls.View mauiView)
        {
            ((System.Collections.Specialized.INotifyCollectionChanged)mauiView.GestureRecognizers)
                .CollectionChanged += OnGestureRecognizersChanged;
        }
    }

    protected override void DisconnectHandler(TPlatformView platformView)
    {
        if (VirtualView is Microsoft.Maui.Controls.View mauiView)
        {
            ((System.Collections.Specialized.INotifyCollectionChanged)mauiView.GestureRecognizers)
                .CollectionChanged -= OnGestureRecognizersChanged;
        }
        base.DisconnectHandler(platformView);
    }

    void OnGestureRecognizersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (PlatformView != null)
            SetupGestures(PlatformView);
    }

    void SetupGestures(NSView platformView)
    {
        if (VirtualView is IView view)
            GestureManager.SetupGestures(platformView, view);
    }

    public static new void MapShadow(IViewHandler handler, IView view)
    {
        var platformView = handler.PlatformView as NSView;
        if (platformView == null)
            return;

        // Ensure the view is layer-backed before accessing layer properties
        platformView.WantsLayer = true;
        if (platformView.Layer == null)
            return;

        var shadow = view.Shadow;
        if (shadow == null)
        {
            platformView.Layer.ShadowOpacity = 0;
            return;
        }

        platformView.Layer.ShadowOpacity = shadow.Opacity;
        platformView.Layer.ShadowRadius = shadow.Radius;
        platformView.Layer.ShadowOffset = new CGSize((float)shadow.Offset.X, (float)shadow.Offset.Y);

        if (shadow.Paint is SolidPaint solidPaint && solidPaint.Color is not null)
            platformView.Layer.ShadowColor = solidPaint.Color.ToPlatformColor().CGColor;
        else
            platformView.Layer.ShadowColor = CoreGraphics.CGColor.CreateSrgb(0, 0, 0, 1);
    }

    public static new void MapOpacity(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is NSView platformView)
            platformView.AlphaValue = (nfloat)view.Opacity;
    }

    public static new void MapVisibility(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is NSView platformView)
        {
            var wasHidden = platformView.Hidden;
            platformView.Hidden = view.Visibility != Visibility.Visible;

            // When a view transitions from hidden to visible, AppKit may not
            // trigger layout automatically — invalidate so it gets measured.
            if (wasHidden && !platformView.Hidden)
            {
                platformView.InvalidateIntrinsicContentSize();
                platformView.NeedsLayout = true;

                // Walk up the native view tree to ensure all ancestors re-layout
                var ancestor = platformView.Superview;
                while (ancestor != null)
                {
                    ancestor.NeedsLayout = true;
                    ancestor = ancestor.Superview;
                }

                // Invalidate MAUI measure on the view and all ancestors so the
                // layout engine re-measures the entire chain (e.g. Grid → StackLayout → Button)
                view.InvalidateMeasure();
                if (view is IElement element)
                {
                    var parent = element.Parent;
                    while (parent is IView parentView)
                    {
                        parentView.InvalidateMeasure();
                        parent = (parent as IElement)?.Parent;
                    }
                }
            }
        }
    }

    public static new void MapIsEnabled(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is NSControl control)
            control.Enabled = view.IsEnabled;
    }

    public static new void MapBackground(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is not NSView platformView)
            return;

        platformView.WantsLayer = true;
        if (platformView.Layer == null)
            return;

        // Remove any existing gradient sublayer
        RemoveGradientLayer(platformView);

        switch (view.Background)
        {
            case LinearGradientPaint linear:
                ApplyLinearGradient(platformView, linear);
                break;
            case RadialGradientPaint radial:
                ApplyRadialGradient(platformView, radial);
                break;
            case SolidPaint solidPaint when solidPaint.Color != null:
                platformView.Layer.BackgroundColor = solidPaint.Color.ToPlatformColor().CGColor;
                break;
            default:
                platformView.Layer.BackgroundColor = null;
                break;
        }
    }

    const string GradientLayerName = "MauiGradientLayer";

    static void RemoveGradientLayer(NSView view)
    {
        if (view.Layer?.Sublayers != null)
        {
            foreach (var sub in view.Layer.Sublayers)
            {
                if (sub.Name == GradientLayerName)
                {
                    sub.RemoveFromSuperLayer();
                    break;
                }
            }
        }
        view.Layer!.BackgroundColor = null;
    }

    static void ApplyLinearGradient(NSView view, LinearGradientPaint paint)
    {
        var gradient = new CoreAnimation.CAGradientLayer
        {
            Name = GradientLayerName,
            Frame = view.Layer!.Bounds,
            StartPoint = new CGPoint(paint.StartPoint.X, paint.StartPoint.Y),
            EndPoint = new CGPoint(paint.EndPoint.X, paint.EndPoint.Y),
        };
        SetGradientStops(gradient, paint.GradientStops);
        gradient.AutoresizingMask = CoreAnimation.CAAutoresizingMask.WidthSizable
            | CoreAnimation.CAAutoresizingMask.HeightSizable;
        view.Layer.InsertSublayer(gradient, 0);
    }

    static void ApplyRadialGradient(NSView view, RadialGradientPaint paint)
    {
        var gradient = new CoreAnimation.CAGradientLayer
        {
            Name = GradientLayerName,
            Frame = view.Layer!.Bounds,
            CornerRadius = 0,
            StartPoint = new CGPoint(paint.Center.X, paint.Center.Y),
            EndPoint = new CGPoint(1, 1),
        };
        SetGradientStops(gradient, paint.GradientStops);
        gradient.AutoresizingMask = CoreAnimation.CAAutoresizingMask.WidthSizable
            | CoreAnimation.CAAutoresizingMask.HeightSizable;
        view.Layer.InsertSublayer(gradient, 0);
    }

    static void SetGradientStops(CoreAnimation.CAGradientLayer gradient, PaintGradientStop[]? stops)
    {
        if (stops == null || stops.Length == 0)
            return;

        var colors = new CGColor[stops.Length];
        var locations = new Foundation.NSNumber[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            colors[i] = stops[i].Color.ToPlatformColor().CGColor;
            locations[i] = new Foundation.NSNumber(stops[i].Offset);
        }
        gradient.Colors = colors;
        gradient.Locations = locations;
    }

    public static new void MapFlowDirection(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is NSView platformView)
        {
            platformView.UserInterfaceLayoutDirection = view.FlowDirection == FlowDirection.RightToLeft
                ? NSUserInterfaceLayoutDirection.RightToLeft
                : NSUserInterfaceLayoutDirection.LeftToRight;
        }
    }

    public static new void MapAutomationId(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is NSView platformView)
            platformView.AccessibilityIdentifier = view.AutomationId;
    }

    public static void MapSemantics(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is not NSView platformView)
            return;

        var semantics = view.Semantics;
        if (semantics == null)
            return;

        if (!string.IsNullOrEmpty(semantics.Description))
            platformView.AccessibilityLabel = semantics.Description;

        if (!string.IsNullOrEmpty(semantics.Hint))
            platformView.AccessibilityHelp = semantics.Hint;

        if (semantics.HeadingLevel != SemanticHeadingLevel.None)
        {
            // Mark as heading for VoiceOver
            platformView.AccessibilityRole = NSAccessibilityRoles.GroupRole;
        }
    }

    public static new void MapClip(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is not NSView platformView)
            return;

        platformView.WantsLayer = true;
        if (platformView.Layer == null)
            return;

        if (view.Clip == null)
        {
            platformView.Layer.Mask = null;
            return;
        }

        var bounds = platformView.Bounds;
        var pathF = view.Clip.PathForBounds(new Rect(0, 0, bounds.Width, bounds.Height));
        if (pathF == null)
        {
            platformView.Layer.Mask = null;
            return;
        }

        var maskLayer = new CAShapeLayer();
        maskLayer.Frame = platformView.Bounds;
        maskLayer.Path = PathFToCGPath(pathF);
        platformView.Layer.Mask = maskLayer;
    }

    public static void MapTransform(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is not NSView platformView)
            return;

        platformView.WantsLayer = true;
        if (platformView.Layer == null)
            return;

        // Adjust position when changing anchorPoint to prevent frame from shifting.
        // AppKit flipped views default anchorPoint to (0,0), not (0.5,0.5) like iOS.
        var newAnchor = new CGPoint(view.AnchorX, view.AnchorY);
        var oldAnchor = platformView.Layer.AnchorPoint;
        if (oldAnchor != newAnchor)
        {
            var bounds = platformView.Layer.Bounds;
            platformView.Layer.Position = new CGPoint(
                platformView.Layer.Position.X + (newAnchor.X - oldAnchor.X) * bounds.Width,
                platformView.Layer.Position.Y + (newAnchor.Y - oldAnchor.Y) * bounds.Height);
            platformView.Layer.AnchorPoint = newAnchor;
        }

        var transform = CATransform3D.Identity;

        var scaleX = view.ScaleX * view.Scale;
        var scaleY = view.ScaleY * view.Scale;
        if (scaleX != 1 || scaleY != 1)
            transform = transform.Scale((nfloat)scaleX, (nfloat)scaleY, 1);

        if (view.Rotation != 0)
            transform = transform.Rotate((nfloat)(view.Rotation * Math.PI / 180.0), 0, 0, 1);
        if (view.RotationX != 0)
            transform = transform.Rotate((nfloat)(view.RotationX * Math.PI / 180.0), 1, 0, 0);
        if (view.RotationY != 0)
            transform = transform.Rotate((nfloat)(view.RotationY * Math.PI / 180.0), 0, 1, 0);

        if (view.TranslationX != 0 || view.TranslationY != 0)
            transform = transform.Translate((nfloat)view.TranslationX, (nfloat)view.TranslationY, 0);

        platformView.Layer.Transform = transform;
    }

    public static void MapInputTransparent(IViewHandler handler, IView view)
    {
        // When InputTransparent is true, the view should not receive any hit-testing events.
        // On macOS, we can achieve this by overriding HitTest in a custom NSView subclass,
        // but for native controls, we disable user interaction via alphaValue trick or
        // by simply marking the control as not a mouse target.
        // The simplest approach: use the AccessibilityElement flag to skip hit testing.
        // More robust: NSView doesn't have a direct "user interaction enabled" property.
        // We rely on the container view's HitTest override to skip InputTransparent children.
    }

    public static void MapToolTip(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is NSView platformView && view is Microsoft.Maui.Controls.View mauiView)
        {
            var tooltip = Microsoft.Maui.Controls.ToolTipProperties.GetText(mauiView);
            platformView.ToolTip = tooltip?.ToString() ?? string.Empty;
        }
    }

    public static void MapZIndex(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is NSView platformView)
        {
            platformView.WantsLayer = true;
            if (platformView.Layer != null)
                platformView.Layer.ZPosition = view.ZIndex;
        }
    }

    public static void MapContextFlyout(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is not NSView platformView || view is not Microsoft.Maui.Controls.View mauiView)
            return;

        var flyout = Microsoft.Maui.Controls.FlyoutBase.GetContextFlyout(mauiView);
        if (flyout is Microsoft.Maui.Controls.MenuFlyout menuFlyout)
        {
            var menu = new NSMenu();
            foreach (var item in menuFlyout)
            {
                if (item is Microsoft.Maui.Controls.MenuFlyoutItem menuItem)
                {
                    var nsItem = new NSMenuItem(menuItem.Text ?? string.Empty, (s, e) =>
                    {
                        menuItem.Command?.Execute(menuItem.CommandParameter);
                    });
                    nsItem.Enabled = menuItem.IsEnabled;
                    menu.AddItem(nsItem);
                }
                else if (item is Microsoft.Maui.Controls.MenuFlyoutSeparator)
                {
                    menu.AddItem(NSMenuItem.SeparatorItem);
                }
            }
            platformView.Menu = menu;
        }
        else
        {
            platformView.Menu = null;
        }
    }

    static void MapFocus(IViewHandler handler, IView view, object? args)
    {
        if (args is not FocusRequest request)
            return;

        var platformView = handler.PlatformView as NSView;
        if (platformView == null)
        {
            request.TrySetResult(false);
            return;
        }

        var window = platformView.Window;
        if (window == null)
        {
            request.TrySetResult(false);
            return;
        }

        // EditorNSView wraps an NSTextView inside an NSScrollView —
        // focus must go to the inner TextView for keyboard input
        if (platformView is EditorNSView editorView)
            platformView = editorView.TextView;

        var result = window.MakeFirstResponder(platformView);
        request.TrySetResult(result);
    }

    public override void PlatformArrange(Rect rect)
    {
        var platformView = PlatformView;
        if (platformView == null)
            return;

        // Modal pages have their frame managed externally — don't override
        if (platformView is MacOSContainerView container && container.ExternalFrameManagement)
            return;

        // Guard against NaN values which crash CALayer
        var x = Sanitize(rect.X);
        var y = Sanitize(rect.Y);
        var width = Sanitize(rect.Width);
        var height = Sanitize(rect.Height);

        // NSView uses Frame for positioning (with IsFlipped=true for top-left origin)
        platformView.Frame = new CGRect(x, y, width, height);
    }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var platformView = PlatformView;
        if (platformView == null)
            return Size.Zero;

        // Sanitize constraints
        if (double.IsNaN(widthConstraint))
            widthConstraint = double.PositiveInfinity;
        if (double.IsNaN(heightConstraint))
            heightConstraint = double.PositiveInfinity;

        var widthConstrained = !double.IsPositiveInfinity(widthConstraint);
        var heightConstrained = !double.IsPositiveInfinity(heightConstraint);

        CGSize sizeThatFits;

        // MacOSContainerView has a custom SizeThatFits method
        // Use infinity (not nfloat.MaxValue) for unconstrained dimensions so
        // MAUI layout engines (especially FlexLayout) treat them as truly
        // unconstrained rather than as a huge finite space.
        if (platformView is MacOSContainerView containerView)
        {
            sizeThatFits = containerView.SizeThatFits(
                new CGSize(
                    widthConstrained ? widthConstraint : (nfloat)double.PositiveInfinity,
                    heightConstrained ? heightConstraint : (nfloat)double.PositiveInfinity));
        }
        else if (platformView is BorderNSView borderView)
        {
            sizeThatFits = borderView.SizeThatFits(
                new CGSize(
                    widthConstrained ? widthConstraint : (nfloat)double.PositiveInfinity,
                    heightConstrained ? heightConstraint : (nfloat)double.PositiveInfinity));
        }
        else
        {
            // For native AppKit controls, try IntrinsicContentSize first, then FittingSize
            var intrinsic = platformView.IntrinsicContentSize;
            if (intrinsic.Width >= 0 && intrinsic.Height >= 0)
            {
                sizeThatFits = intrinsic;
            }
            else
            {
                sizeThatFits = platformView.FittingSize;
            }
        }

        var width = IsExplicit(VirtualView.Width) ? VirtualView.Width :
                    widthConstrained ? Math.Min((double)sizeThatFits.Width, widthConstraint) :
                    (double)sizeThatFits.Width;

        var height = IsExplicit(VirtualView.Height) ? VirtualView.Height :
                     heightConstrained ? Math.Min((double)sizeThatFits.Height, heightConstraint) :
                     (double)sizeThatFits.Height;

        var minimumWidth = VirtualView.MinimumWidth;
        var minimumHeight = VirtualView.MinimumHeight;
        var maximumWidth = VirtualView.MaximumWidth;
        var maximumHeight = VirtualView.MaximumHeight;

        var finalWidth = Math.Max(IsExplicit(minimumWidth) ? minimumWidth : 0, width);
        var finalHeight = Math.Max(IsExplicit(minimumHeight) ? minimumHeight : 0, height);

        if (IsExplicit(maximumWidth))
            finalWidth = Math.Min(finalWidth, maximumWidth);
        if (IsExplicit(maximumHeight))
            finalHeight = Math.Min(finalHeight, maximumHeight);

        return new Size(finalWidth, finalHeight);
    }

    static bool IsExplicit(double value)
    {
        return !double.IsNaN(value) && value >= 0;
    }

    static double Sanitize(double value)
    {
        if (double.IsNaN(value) || double.IsNegativeInfinity(value))
            return 0;
        if (double.IsPositiveInfinity(value))
            return 0;
        return value;
    }

    static CGPath PathFToCGPath(PathF pathF)
    {
        var cgPath = new CGPath();
        var points = pathF.Points?.ToArray();
        var segments = pathF.SegmentTypes?.ToArray();
        if (points is null || segments is null)
            return cgPath;

        int index = 0;
        foreach (var op in segments)
        {
            switch (op)
            {
                case PathOperation.Move:
                    if (index < points.Length) { cgPath.MoveToPoint(points[index].X, points[index].Y); index++; }
                    break;
                case PathOperation.Line:
                    if (index < points.Length) { cgPath.AddLineToPoint(points[index].X, points[index].Y); index++; }
                    break;
                case PathOperation.Quad:
                    if (index + 1 < points.Length) { cgPath.AddQuadCurveToPoint(points[index].X, points[index].Y, points[index + 1].X, points[index + 1].Y); index += 2; }
                    break;
                case PathOperation.Cubic:
                    if (index + 2 < points.Length) { cgPath.AddCurveToPoint(points[index].X, points[index].Y, points[index + 1].X, points[index + 1].Y, points[index + 2].X, points[index + 2].Y); index += 3; }
                    break;
                case PathOperation.Close:
                    cgPath.CloseSubpath();
                    break;
            }
        }
        return cgPath;
    }
}
