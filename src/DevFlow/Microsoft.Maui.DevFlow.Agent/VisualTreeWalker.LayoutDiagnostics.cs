using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

#if ANDROID
using Android.Graphics;
using Android.Views;
using Android.Widget;
#elif IOS || MACCATALYST
using CoreGraphics;
using UIKit;
#elif MACOS
using AppKit;
using CoreGraphics;
#elif WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
#endif

namespace Microsoft.Maui.DevFlow.Agent;

public partial class PlatformVisualTreeWalker
{
    protected override double ResolveWindowScale(Microsoft.Maui.Controls.Window window)
    {
        try
        {
#if ANDROID
            if (window.Handler?.PlatformView is global::Android.App.Activity activity)
                return activity.Resources?.DisplayMetrics?.Density ?? 1;
#elif IOS || MACCATALYST
            if (window.Handler?.PlatformView is UIWindow uiWindow)
                return uiWindow.Screen.Scale;
#elif MACOS
            if (window.Handler?.PlatformView is NSWindow nsWindow)
                return nsWindow.BackingScaleFactor;
#elif WINDOWS
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window xamlWindow
                && xamlWindow.Content is FrameworkElement root)
                return root.XamlRoot?.RasterizationScale ?? 1;
#endif
        }
        catch
        {
        }
        return 1;
    }

    protected override void PopulatePlatformLayoutMetrics(
        LayoutPlatformMetrics metrics,
        VisualElement element,
        ElementInfo info,
        LayoutInspectionRequest request)
    {
        var platformView = element.Handler?.PlatformView;
        if (platformView is null)
            return;
        var collectInteraction = ShouldCollectInteractionOcclusion(info, request);

#if ANDROID
        if (platformView is global::Android.Views.View androidView)
            PopulateAndroidLayoutMetrics(metrics, androidView);
#elif IOS || MACCATALYST
        if (platformView is UIView uiView)
            PopulateUIKitLayoutMetrics(metrics, uiView, request, collectInteraction);
#elif MACOS
        if (platformView is NSView nsView)
            PopulateAppKitLayoutMetrics(metrics, nsView, request, collectInteraction);
#elif WINDOWS
        if (platformView is FrameworkElement frameworkElement)
            PopulateWinUILayoutMetrics(metrics, frameworkElement, request, collectInteraction);
#endif
    }

    public override IReadOnlyList<LayoutRuleSupportInfo> GetLayoutRuleSupport()
    {
        var support = base.GetLayoutRuleSupport().ToDictionary(rule => rule.RuleId);
        SetSupport(support, LayoutDiagnosticRules.ElementClipped, "partial", "high");
        SetSupport(support, LayoutDiagnosticRules.ElementOutsideWindow, "exact", "high");
        SetSupport(support, LayoutDiagnosticRules.ContentOverflow, "partial", "high");
        SetSupport(support, LayoutDiagnosticRules.GeometricOverlap, "partial", "high",
            "Some native stacks expose transformed bounding regions rather than exact paint geometry.");
        SetSupport(support, LayoutDiagnosticRules.VisualOccluded, "partial", "medium",
            "Transparent pixels and custom compositor content are not inspected.");
#if ANDROID
        SetSupport(support, LayoutDiagnosticRules.TextNotFullyRendered, "partial", "high",
            "Custom text renderers and ComposeView internals require their own diagnostics bridge.");
        SetSupport(support, LayoutDiagnosticRules.InteractionOccluded, "unsupported", "low",
            "Android has no non-destructive public topmost-view hit-test API.");
#elif IOS || MACCATALYST
        SetSupport(support, LayoutDiagnosticRules.TextNotFullyRendered, "partial", "high",
            "UILabel layout is reconstructed with TextKit sizing.");
        SetSupport(support, LayoutDiagnosticRules.InteractionOccluded, "partial", "high",
            "Point samples follow UIKit hit-test semantics.");
#elif MACOS
        SetSupport(support, LayoutDiagnosticRules.TextNotFullyRendered, "partial", "high",
            "NSTextField layout is reconstructed from its cell.");
        SetSupport(support, LayoutDiagnosticRules.InteractionOccluded, "partial", "medium",
            "Point samples do not include overlapping windows from other applications.");
#elif WINDOWS
        SetSupport(support, LayoutDiagnosticRules.TextNotFullyRendered, "partial", "exact",
            "TextBlock trimming and RichTextBlock overflow are exact; custom text renderers are not.");
        SetSupport(support, LayoutDiagnosticRules.InteractionOccluded, "partial", "high",
            "Point samples use FindElementsInHostCoordinates.");
#endif
        return support.Values.OrderBy(rule => rule.RuleId, StringComparer.Ordinal).ToList();
    }

    private static void SetSupport(
        Dictionary<string, LayoutRuleSupportInfo> support,
        string ruleId,
        string level,
        string confidence,
        params string[] limitations)
    {
        support[ruleId] = new LayoutRuleSupportInfo
        {
            RuleId = ruleId,
            Support = level,
            Confidence = confidence,
            Limitations = limitations.ToList()
        };
    }

#if ANDROID
    private void PopulateAndroidLayoutMetrics(LayoutPlatformMetrics metrics, global::Android.Views.View view)
    {
        var density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        metrics.FullRegion = MapAndroidLocalRectToWindow(
            view,
            0,
            0,
            view.Width,
            view.Height,
            density,
            "exactPolygon");

        var localVisible = new global::Android.Graphics.Rect();
        if (view.GetLocalVisibleRect(localVisible))
        {
            metrics.NativeVisibleRegion = MapAndroidLocalRectToWindow(
                view,
                localVisible.Left,
                localVisible.Top,
                localVisible.Right,
                localVisible.Bottom,
                density,
                "conservativeBounds");
            metrics.NativeVisibleKind = "unknown-platform-clip";
        }
        else
        {
            metrics.NativeVisibleRegion = LayoutRegionMath.Empty();
            metrics.NativeVisibleKind = "unknown-platform-clip";
        }

        if (view.ClipBounds is global::Android.Graphics.Rect clipBounds)
        {
            metrics.SelfClips.Add(new LayoutPlatformClip
            {
                Kind = "explicit-clip",
                ClipperElementId = FindElementIdForPlatformView(view),
                Region = MapAndroidLocalRectToWindow(
                    view,
                    clipBounds.Left,
                    clipBounds.Top,
                    clipBounds.Right,
                    clipBounds.Bottom,
                    density,
                    "exactPolygon")
            });
        }

        if (view is ViewGroup viewGroup && viewGroup.ClipChildren)
        {
            metrics.DescendantClipRegion = metrics.FullRegion;
            metrics.DescendantClipKind = view is global::Android.Widget.ScrollView
                or global::Android.Widget.HorizontalScrollView
                ? "scroll-viewport"
                : "ancestor-layout-clip";
        }

        var effectiveAlpha = GetAndroidEffectiveAlpha(view);
        metrics.IsHitTestVisible = view.Visibility == ViewStates.Visible
            && view.Enabled
            && effectiveAlpha > 0.01;
        metrics.IsOpaque = effectiveAlpha >= 0.99
            && view.Background is global::Android.Graphics.Drawables.ColorDrawable color
            && color.Color.A == byte.MaxValue;
        metrics.HasActiveAnimation = view.Animation is { HasStarted: true, HasEnded: false };

        if (view is TextView textView)
        {
            var layout = textView.Layout;
            var ellipsisCount = 0;
            if (layout is not null)
            {
                for (var line = 0; line < layout.LineCount; line++)
                    ellipsisCount += layout.GetEllipsisCount(line);
            }

            var availableWidth = Math.Max(0, textView.Width - textView.CompoundPaddingLeft - textView.CompoundPaddingRight);
            var availableHeight = Math.Max(0, textView.Height - textView.CompoundPaddingTop - textView.CompoundPaddingBottom);
            var verticalClip = layout is not null && layout.Height > availableHeight + 1;
            var horizontalClip = layout is not null
                && Enumerable.Range(0, layout.LineCount)
                    .Any(line => layout.GetLineWidth(line) > availableWidth + 1);
            if (layout is not null && layout.LineCount > 0 && textView.Text is { } text)
            {
                var lastLineEnd = layout.GetLineEnd(layout.LineCount - 1);
                verticalClip |= lastLineEnd < text.Length;
            }
            metrics.Text = new LayoutTextEvidence
            {
                Kind = ellipsisCount > 0
                    ? "ellipsis"
                    : verticalClip
                        ? "vertical-hard-clip"
                        : horizontalClip
                            ? "horizontal-hard-clip"
                            : null,
                IsTruncated = ellipsisCount > 0 || verticalClip || horizontalClip,
                RenderedLineCount = layout?.LineCount,
                MaximumLines = textView.MaxLines < int.MaxValue ? textView.MaxLines : null,
                EllipsisCount = ellipsisCount,
                ContentWidth = layout?.Width / density,
                ContentHeight = layout?.Height / density,
                AvailableWidth = availableWidth / density,
                AvailableHeight = availableHeight / density,
                MeasurementSource = "android-layout"
            };
        }

        if (view is SurfaceView or TextureView)
        {
            metrics.IsCoverageOpaque = true;
            metrics.Limitations.Add("SurfaceView and TextureView pixels are opaque to view-tree layout diagnostics.");
        }
        metrics.Limitations.Add("Android interaction occlusion is not reported without a non-destructive platform hit-test API.");
    }

    private static LayoutRegionInfo MapAndroidLocalRectToWindow(
        global::Android.Views.View view,
        float left,
        float top,
        float right,
        float bottom,
        float density,
        string precision)
    {
        var points = new[]
        {
            left, top,
            right, top,
            right, bottom,
            left, bottom
        };
        MapAndroidLocalPointsToWindow(view, points);
        return LayoutRegionMath.FromPoints(
        [
            Point(points[0] / density, points[1] / density),
            Point(points[2] / density, points[3] / density),
            Point(points[4] / density, points[5] / density),
            Point(points[6] / density, points[7] / density)
        ], precision);
    }

    private static void MapAndroidLocalPointsToWindow(
        global::Android.Views.View view,
        float[] points)
    {
        var current = view;
        while (current.Parent is global::Android.Views.View parent)
        {
            if (current.Matrix is { IsIdentity: false } matrix)
                matrix.MapPoints(points);
            OffsetAndroidPoints(
                points,
                current.Left - parent.ScrollX,
                current.Top - parent.ScrollY);
            current = parent;
        }

        var rootOrigin = new float[] { 0, 0 };
        if (current.Matrix is { IsIdentity: false } rootMatrix)
        {
            rootMatrix.MapPoints(points);
            rootMatrix.MapPoints(rootOrigin);
        }
        var rootLocation = new int[2];
        current.GetLocationInWindow(rootLocation);
        OffsetAndroidPoints(
            points,
            rootLocation[0] - rootOrigin[0],
            rootLocation[1] - rootOrigin[1]);
    }

    private static void OffsetAndroidPoints(
        float[] points,
        float offsetX,
        float offsetY)
    {
        for (var index = 0; index < points.Length; index += 2)
        {
            points[index] += offsetX;
            points[index + 1] += offsetY;
        }
    }

    private static double GetAndroidEffectiveAlpha(global::Android.Views.View view)
    {
        var alpha = 1d;
        for (global::Android.Views.View? current = view;
             current is not null;
             current = current.Parent as global::Android.Views.View)
        {
            alpha *= current.Alpha;
            if (alpha < 0.99)
                break;
        }
        return alpha;
    }
#elif IOS || MACCATALYST
    private void PopulateUIKitLayoutMetrics(
        LayoutPlatformMetrics metrics,
        UIView view,
        LayoutInspectionRequest request,
        bool collectInteraction)
    {
        var window = view.Window;
        if (window is null)
        {
            metrics.Limitations.Add("UIView is not attached to a UIWindow.");
            return;
        }

        metrics.FullRegion = UIKitRegion(view, view.Bounds, window);
        var visible = metrics.FullRegion;
        for (var ancestor = view.Superview; ancestor is not null; ancestor = ancestor.Superview)
        {
            if (ancestor.ClipsToBounds || ancestor.Layer.MasksToBounds || ancestor is UIScrollView)
            {
                var clip = UIKitRegion(ancestor, ancestor.Bounds, window);
                metrics.SelfClips.Add(new LayoutPlatformClip
                {
                    ClipperElementId = FindElementIdForPlatformView(ancestor),
                    Kind = ancestor is UIScrollView ? "scroll-viewport" : "ancestor-layout-clip",
                    Region = clip
                });
                visible = LayoutRegionMath.Intersect(visible, clip);
            }
            if (ancestor.Layer.Mask is not null)
                metrics.Limitations.Add("CALayer mask shape is represented conservatively.");
        }

        var windowRegion = UIKitRegion(window, window.Bounds, window);
        visible = LayoutRegionMath.Intersect(visible, windowRegion);
        metrics.NativeVisibleRegion = visible;
        metrics.NativeVisibleKind = "window-edge";
        metrics.DescendantClipRegion = view.ClipsToBounds || view.Layer.MasksToBounds || view is UIScrollView
            ? metrics.FullRegion
            : metrics.DescendantClipRegion;
        if (metrics.DescendantClipRegion is not null)
            metrics.DescendantClipKind = view is UIScrollView ? "scroll-viewport" : "ancestor-layout-clip";

        var effectiveAlpha = GetUIKitEffectiveAlpha(view);
        metrics.IsHitTestVisible = !view.Hidden
            && view.UserInteractionEnabled
            && effectiveAlpha > 0.01;
        metrics.IsOpaque = view.Opaque && effectiveAlpha >= 0.99;
        if (view.GetType().Name is "WKWebView" or "MTKView")
            metrics.IsCoverageOpaque = true;
        metrics.HasActiveAnimation = view.Layer.AnimationKeys is { Length: > 0 };

        if (view is UILabel label)
        {
            var width = Math.Max(0, label.Bounds.Width);
            var measured = label.SizeThatFits(new CGSize(width, double.MaxValue));
            var heightOverflow = measured.Height > label.Bounds.Height + 0.5;
            var widthOverflow = label.Lines == 1 && measured.Width > label.Bounds.Width + 0.5;
            var truncated = heightOverflow || widthOverflow;
            metrics.Text = new LayoutTextEvidence
            {
                Kind = truncated
                    ? label.LineBreakMode.ToString().Contains("Truncat", StringComparison.OrdinalIgnoreCase)
                        ? "ellipsis"
                        : heightOverflow ? "vertical-hard-clip" : "horizontal-hard-clip"
                    : null,
                IsTruncated = truncated,
                MaximumLines = label.Lines > 0 ? (int)label.Lines : null,
                ContentWidth = measured.Width,
                ContentHeight = measured.Height,
                AvailableWidth = label.Bounds.Width,
                AvailableHeight = label.Bounds.Height,
                MeasurementSource = "uikit-textkit"
            };
        }

        if (collectInteraction)
            PopulateUIKitInteractionOcclusion(metrics, view, window, request);
    }

    private void PopulateUIKitInteractionOcclusion(
        LayoutPlatformMetrics metrics,
        UIView view,
        UIWindow window,
        LayoutInspectionRequest request)
    {
        if (!metrics.IsHitTestVisible || metrics.FullRegion is null)
            return;

        var samples = SamplePoints(metrics.FullRegion, request.Occlusion.MaxSamplesPerElement);
        var blocked = 0;
        string? occluderId = null;
        foreach (var sample in samples)
        {
            var hit = window.HitTest(new CGPoint(sample.X, sample.Y), null);
            if (hit is null || hit == view || hit.IsDescendantOfView(view))
                continue;

            blocked++;
            for (var current = hit; current is not null && occluderId is null; current = current.Superview)
                occluderId = FindElementIdForPlatformView(current);
        }

        if (blocked == 0)
            return;

        var (lower, upper) = EstimateBlockedInterval(
            blocked,
            samples.Count,
            request.Occlusion.CoverageError);
        metrics.InteractionOccluderId = occluderId ?? "native-unmapped";
        metrics.InteractionBlockedLowerBound = lower;
        metrics.InteractionBlockedUpperBound = upper;
        metrics.InteractionSampleCount = samples.Count;
    }

    private static LayoutRegionInfo UIKitRegion(UIView source, CGRect rect, UIView target)
        => LayoutRegionMath.FromPoints(
        [
            ToPoint(source.ConvertPointToView(new CGPoint(rect.Left, rect.Top), target)),
            ToPoint(source.ConvertPointToView(new CGPoint(rect.Right, rect.Top), target)),
            ToPoint(source.ConvertPointToView(new CGPoint(rect.Right, rect.Bottom), target)),
            ToPoint(source.ConvertPointToView(new CGPoint(rect.Left, rect.Bottom), target))
        ], source.Transform.IsIdentity ? "exactRect" : "exactPolygon");

    private static double GetUIKitEffectiveAlpha(UIView view)
    {
        var alpha = 1d;
        for (UIView? current = view;
             current is not null;
             current = current.Superview)
        {
            alpha *= current.Alpha;
            if (alpha < 0.99)
                break;
        }
        return alpha;
    }

    private static LayoutPointInfo ToPoint(CGPoint point) => Point(point.X, point.Y);
#elif MACOS
    private void PopulateAppKitLayoutMetrics(
        LayoutPlatformMetrics metrics,
        NSView view,
        LayoutInspectionRequest request,
        bool collectInteraction)
    {
        var content = view.Window?.ContentView;
        if (content is null)
        {
            metrics.Limitations.Add("NSView is not attached to an NSWindow.");
            return;
        }

        var contentHeight = content.Bounds.Height;
        metrics.FullRegion = AppKitRegion(view, view.Bounds, content, contentHeight);
        var visibleRect = view.VisibleRect();
        metrics.NativeVisibleRegion = visibleRect.IsEmpty
            ? LayoutRegionMath.Empty()
            : AppKitRegion(view, visibleRect, content, contentHeight);
        metrics.NativeVisibleKind = view.EnclosingScrollView is not null
            ? "scroll-viewport"
            : "unknown-platform-clip";
        var effectiveAlpha = GetAppKitEffectiveAlpha(view);
        metrics.IsHitTestVisible = !view.Hidden && effectiveAlpha > 0.01;
        metrics.IsOpaque = view.IsOpaque && effectiveAlpha >= 0.99;
        if (view.GetType().Name is "WKWebView" or "MTKView" or "AVPlayerView")
            metrics.IsCoverageOpaque = true;

        if (view is NSClipView)
        {
            metrics.DescendantClipRegion = metrics.FullRegion;
            metrics.DescendantClipKind = "scroll-viewport";
        }
        else if (view.WantsLayer && view.Layer?.MasksToBounds == true)
        {
            metrics.DescendantClipRegion = metrics.FullRegion;
            metrics.DescendantClipKind = "ancestor-layout-clip";
        }

        if (view is NSTextField textField && textField.Cell is not null)
        {
            var measured = textField.Cell.CellSizeForBounds(textField.Bounds);
            var truncated = measured.Width > textField.Bounds.Width + 0.5
                || measured.Height > textField.Bounds.Height + 0.5;
            metrics.Text = new LayoutTextEvidence
            {
                Kind = truncated ? "ellipsis" : null,
                IsTruncated = truncated,
                ContentWidth = measured.Width,
                ContentHeight = measured.Height,
                AvailableWidth = textField.Bounds.Width,
                AvailableHeight = textField.Bounds.Height,
                MeasurementSource = "appkit-textkit"
            };
        }

        if (!collectInteraction)
            return;

        var samples = SamplePoints(metrics.FullRegion, request.Occlusion.MaxSamplesPerElement);
        var blocked = 0;
        string? occluderId = null;
        foreach (var sample in samples)
        {
            var nativePoint = new CGPoint(sample.X, contentHeight - sample.Y);
            var hit = content.HitTest(nativePoint);
            if (hit is null || hit == view || hit.IsDescendantOf(view))
                continue;
            blocked++;
            for (var current = hit; current is not null && occluderId is null; current = current.Superview)
                occluderId = FindElementIdForPlatformView(current);
        }
        if (blocked > 0)
        {
            var (lower, upper) = EstimateBlockedInterval(
                blocked,
                samples.Count,
                request.Occlusion.CoverageError);
            metrics.InteractionOccluderId = occluderId ?? "native-unmapped";
            metrics.InteractionBlockedLowerBound = lower;
            metrics.InteractionBlockedUpperBound = upper;
            metrics.InteractionSampleCount = samples.Count;
        }
    }

    private static LayoutRegionInfo AppKitRegion(
        NSView source,
        CGRect rect,
        NSView target,
        double targetHeight)
    {
        var converted = source.ConvertRectToView(rect, target);
        return LayoutRegionMath.FromRect(
            converted.X,
            targetHeight - converted.Y - converted.Height,
            converted.Width,
            converted.Height,
            "conservativeBounds");
    }

    private static double GetAppKitEffectiveAlpha(NSView view)
    {
        var alpha = 1d;
        for (NSView? current = view;
             current is not null;
             current = current.Superview)
        {
            alpha *= current.AlphaValue;
            if (alpha < 0.99)
                break;
        }
        return alpha;
    }
#elif WINDOWS
    private void PopulateWinUILayoutMetrics(
        LayoutPlatformMetrics metrics,
        FrameworkElement element,
        LayoutInspectionRequest request,
        bool collectInteraction)
    {
        var root = element.XamlRoot?.Content as UIElement;
        if (root is null)
        {
            metrics.Limitations.Add("WinUI element is not attached to a XamlRoot.");
            return;
        }

        metrics.FullRegion = WinUIRegion(element, root);
        var visible = metrics.FullRegion;
        for (DependencyObject? ancestor = VisualTreeHelper.GetParent(element);
             ancestor is UIElement ancestorElement;
             ancestor = VisualTreeHelper.GetParent(ancestorElement))
        {
            LayoutRegionInfo? clip = null;
            var kind = "ancestor-layout-clip";
            if (ancestorElement.Clip is RectangleGeometry rectangle)
            {
                clip = TransformWinUIRect(ancestorElement, root, rectangle.Rect);
            }
            else if (ancestorElement is ScrollViewer && ancestorElement is FrameworkElement scrollElement)
            {
                clip = WinUIRegion(scrollElement, root);
                kind = "scroll-viewport";
            }

            if (clip is null)
                continue;
            metrics.SelfClips.Add(new LayoutPlatformClip
            {
                ClipperElementId = FindElementIdForPlatformView(ancestorElement),
                Kind = kind,
                Region = clip
            });
            visible = LayoutRegionMath.Intersect(visible, clip);
        }

        if (element.XamlRoot is { } xamlRoot)
        {
            var rootClip = LayoutRegionMath.FromRect(0, 0, xamlRoot.Size.Width, xamlRoot.Size.Height);
            visible = LayoutRegionMath.Intersect(visible, rootClip);
        }
        metrics.NativeVisibleRegion = visible;
        metrics.NativeVisibleKind = "window-edge";

        if (element.Clip is RectangleGeometry selfRectangle)
            metrics.SelfClipRegion = TransformWinUIRect(element, root, selfRectangle.Rect);
        if (element is ScrollViewer)
        {
            metrics.DescendantClipRegion = metrics.FullRegion;
            metrics.DescendantClipKind = "scroll-viewport";
        }

        metrics.IsHitTestVisible = element.Visibility == Microsoft.UI.Xaml.Visibility.Visible
            && element.IsHitTestVisible;
        metrics.IsOpaque = GetWinUIBackgroundAlpha(element) >= 0.99
            && GetWinUIEffectiveOpacity(element) >= 0.99;
        if (element.GetType().Name is "SwapChainPanel" or "WebView2")
            metrics.IsCoverageOpaque = true;

        if (element is TextBlock textBlock)
        {
            var verticalClip = textBlock.DesiredSize.Height > textBlock.ActualHeight + 0.5;
            var trimmed = textBlock.IsTextTrimmed;
            metrics.Text = new LayoutTextEvidence
            {
                Kind = trimmed ? "ellipsis" : verticalClip ? "vertical-hard-clip" : null,
                IsTruncated = trimmed || verticalClip,
                MaximumLines = textBlock.MaxLines > 0 ? textBlock.MaxLines : null,
                ContentWidth = textBlock.DesiredSize.Width,
                ContentHeight = textBlock.DesiredSize.Height,
                AvailableWidth = textBlock.ActualWidth,
                AvailableHeight = textBlock.ActualHeight,
                MeasurementSource = trimmed
                    ? "winui-is-text-trimmed"
                    : "winui-desiredsize-heuristic"
            };
        }
        else if (element is RichTextBlock richTextBlock)
        {
            metrics.Text = new LayoutTextEvidence
            {
                Kind = richTextBlock.HasOverflowContent ? "vertical-hard-clip" : null,
                IsTruncated = richTextBlock.HasOverflowContent,
                ContentWidth = richTextBlock.DesiredSize.Width,
                ContentHeight = richTextBlock.DesiredSize.Height,
                AvailableWidth = richTextBlock.ActualWidth,
                AvailableHeight = richTextBlock.ActualHeight,
                MeasurementSource = "winui-is-text-trimmed"
            };
        }

        if (collectInteraction)
            PopulateWinUIInteractionOcclusion(metrics, element, root, request);
    }

    private void PopulateWinUIInteractionOcclusion(
        LayoutPlatformMetrics metrics,
        FrameworkElement element,
        UIElement root,
        LayoutInspectionRequest request)
    {
        if (!metrics.IsHitTestVisible || metrics.FullRegion is null)
            return;

        var samples = SamplePoints(metrics.FullRegion, request.Occlusion.MaxSamplesPerElement);
        var blocked = 0;
        string? occluderId = null;
        foreach (var sample in samples)
        {
            var hit = VisualTreeHelper.FindElementsInHostCoordinates(
                    new global::Windows.Foundation.Point(sample.X, sample.Y),
                    root,
                    true)
                .FirstOrDefault();
            if (hit is null || hit == element || IsWinUIDescendant(element, hit))
                continue;

            blocked++;
            for (DependencyObject? current = hit;
                 current is UIElement currentElement && occluderId is null;
                 current = VisualTreeHelper.GetParent(currentElement))
            {
                occluderId = FindElementIdForPlatformView(currentElement);
            }
        }

        if (blocked == 0)
            return;
        var (lower, upper) = EstimateBlockedInterval(
            blocked,
            samples.Count,
            request.Occlusion.CoverageError);
        metrics.InteractionOccluderId = occluderId ?? "native-unmapped";
        metrics.InteractionBlockedLowerBound = lower;
        metrics.InteractionBlockedUpperBound = upper;
        metrics.InteractionSampleCount = samples.Count;
    }

    private static LayoutRegionInfo WinUIRegion(FrameworkElement element, UIElement root)
        => TransformWinUIRect(
            element,
            root,
            new global::Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static LayoutRegionInfo TransformWinUIRect(
        UIElement source,
        UIElement target,
        global::Windows.Foundation.Rect rect)
    {
        var transform = source.TransformToVisual(target);
        return LayoutRegionMath.FromPoints(
        [
            ToPoint(transform.TransformPoint(new global::Windows.Foundation.Point(rect.Left, rect.Top))),
            ToPoint(transform.TransformPoint(new global::Windows.Foundation.Point(rect.Right, rect.Top))),
            ToPoint(transform.TransformPoint(new global::Windows.Foundation.Point(rect.Right, rect.Bottom))),
            ToPoint(transform.TransformPoint(new global::Windows.Foundation.Point(rect.Left, rect.Bottom)))
        ], "exactPolygon");
    }

    private static bool IsWinUIDescendant(UIElement ancestor, UIElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    private static double GetWinUIBackgroundAlpha(FrameworkElement element)
    {
        Microsoft.UI.Xaml.Media.Brush? brush = element switch
        {
            Panel panel => panel.Background,
            Control control => control.Background,
            Microsoft.UI.Xaml.Controls.Border border => border.Background,
            _ => null
        };
        return brush is Microsoft.UI.Xaml.Media.SolidColorBrush solid
            ? solid.Color.A / 255d * solid.Opacity
            : 0;
    }

    private static double GetWinUIEffectiveOpacity(UIElement element)
    {
        var opacity = 1d;
        for (DependencyObject? current = element;
             current is UIElement currentElement;
             current = VisualTreeHelper.GetParent(currentElement))
        {
            opacity *= currentElement.Opacity;
            if (opacity < 0.99)
                break;
        }
        return opacity;
    }

    private static LayoutPointInfo ToPoint(global::Windows.Foundation.Point point) => Point(point.X, point.Y);
#endif

    private static List<LayoutPointInfo> SamplePoints(LayoutRegionInfo region, int maxSamples)
    {
        var bounds = region.Bounds;
        var gridSize = Math.Max(1, (int)Math.Floor(Math.Sqrt(Math.Max(1, maxSamples))));
        if (gridSize > 1 && gridSize % 2 == 0)
            gridSize--;
        var points = new List<LayoutPointInfo>(gridSize * gridSize);
        for (var row = 0; row < gridSize; row++)
        {
            for (var column = 0; column < gridSize; column++)
            {
                var point = Point(
                    bounds.X + bounds.Width * (column + 0.5) / gridSize,
                    bounds.Y + bounds.Height * (row + 0.5) / gridSize);
                if (LayoutRegionMath.Contains(region, point))
                    points.Add(point);
            }
        }
        return points.Count > 0 ? points : [LayoutRegionMath.Center(region)];
    }

    private static (double Lower, double Upper) EstimateBlockedInterval(
        int blocked,
        int total,
        double errorProbability)
    {
        if (total <= 0)
            return (0, 1);
        var ratio = (double)blocked / total;
        var probability = Math.Clamp(errorProbability, 0.001, 0.5);
        var margin = Math.Sqrt(Math.Log(2 / probability) / (2 * total));
        return (Math.Max(0, ratio - margin), Math.Min(1, ratio + margin));
    }

    private static LayoutPointInfo Point(double x, double y) => new() { X = x, Y = y };
}
