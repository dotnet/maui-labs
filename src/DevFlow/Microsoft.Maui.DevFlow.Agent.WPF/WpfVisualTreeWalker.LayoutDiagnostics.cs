using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.WPF;

public partial class WpfVisualTreeWalker
{
    protected override double ResolveWindowScale(Microsoft.Maui.Controls.Window window)
    {
        try
        {
            if (window.Handler?.PlatformView is System.Windows.Window wpfWindow)
                return VisualTreeHelper.GetDpi(wpfWindow).DpiScaleX;
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
        if (element.Handler?.PlatformView is not FrameworkElement frameworkElement)
            return;

        var window = System.Windows.Window.GetWindow(frameworkElement);
        if (window is null)
        {
            metrics.Limitations.Add("WPF element is not attached to a Window.");
            return;
        }

        var fullRegion = WpfRegion(frameworkElement, window);
        if (fullRegion is null)
        {
            metrics.GeometryAvailable = false;
            metrics.IsCoverageOpaque = true;
            metrics.Limitations.Add(
                "WPF could not transform this element into window coordinates.");
            return;
        }

        metrics.FullRegion = fullRegion;
        var visible = fullRegion;
        for (DependencyObject? ancestor = VisualTreeHelper.GetParent(frameworkElement);
             ancestor is Visual ancestorVisual;
             ancestor = VisualTreeHelper.GetParent(ancestorVisual))
        {
            Geometry? geometry = VisualTreeHelper.GetClip(ancestorVisual);
            var kind = "explicit-clip";
            if (ancestorVisual is FrameworkElement ancestorElement)
            {
                geometry ??= LayoutInformation.GetLayoutClip(ancestorElement);
                if (ancestorElement is ScrollViewer)
                {
                    geometry ??= new RectangleGeometry(new System.Windows.Rect(ancestorElement.RenderSize));
                    kind = "scroll-viewport";
                }
                else if (ancestorElement.ClipToBounds && geometry is null)
                {
                    geometry = new RectangleGeometry(new System.Windows.Rect(ancestorElement.RenderSize));
                    kind = "ancestor-layout-clip";
                }
            }

            if (geometry is null)
                continue;

            var clip = TransformWpfBounds(ancestorVisual, window, geometry.Bounds);
            if (clip is null)
            {
                metrics.IsCoverageOpaque = true;
                metrics.Limitations.Add(
                    "A WPF ancestor clip could not be transformed into window coordinates.");
                continue;
            }
            metrics.SelfClips.Add(new LayoutPlatformClip
            {
                ClipperElementId = FindElementIdForPlatformView(ancestorVisual),
                Kind = kind,
                Region = clip
            });
            visible = LayoutRegionMath.Intersect(visible, clip);
        }

        var windowRegion = LayoutRegionMath.FromRect(0, 0, window.ActualWidth, window.ActualHeight);
        visible = LayoutRegionMath.Intersect(visible, windowRegion);
        metrics.NativeVisibleRegion = visible;
        metrics.NativeVisibleKind = "window-edge";

        var ownClips = new List<Geometry>();
        var explicitClip = VisualTreeHelper.GetClip(frameworkElement);
        if (explicitClip is not null)
            ownClips.Add(explicitClip);
        if (LayoutInformation.GetLayoutClip(frameworkElement) is { } layoutClip
            && !ReferenceEquals(layoutClip, explicitClip))
        {
            ownClips.Add(layoutClip);
        }
        foreach (var ownClip in ownClips)
        {
            var transformedClip = TransformWpfBounds(
                frameworkElement,
                window,
                ownClip.Bounds);
            if (transformedClip is null)
            {
                metrics.IsCoverageOpaque = true;
                metrics.Limitations.Add(
                    "The WPF element clip could not be transformed into window coordinates.");
                continue;
            }
            metrics.SelfClipRegion = metrics.SelfClipRegion is null
                ? transformedClip
                : LayoutRegionMath.Intersect(
                    metrics.SelfClipRegion,
                    transformedClip);
            if (ownClip is not RectangleGeometry)
                metrics.Limitations.Add(
                    "A non-rectangular WPF element clip is represented by its bounds.");
        }
        if (frameworkElement is System.Windows.Interop.HwndHost)
            metrics.IsCoverageOpaque = true;
        if (frameworkElement is ScrollViewer || frameworkElement.ClipToBounds)
        {
            metrics.DescendantClipRegion = metrics.FullRegion;
            metrics.DescendantClipKind = frameworkElement is ScrollViewer
                ? "scroll-viewport"
                : "ancestor-layout-clip";
        }

        metrics.IsHitTestVisible = frameworkElement.Visibility == System.Windows.Visibility.Visible
            && frameworkElement.IsHitTestVisible
            && frameworkElement.IsEnabled;
        metrics.IsOpaque = GetBackgroundAlpha(frameworkElement) >= 0.99
            && GetEffectiveOpacity(frameworkElement) >= 0.99;

        if (frameworkElement is TextBlock textBlock)
            metrics.Text = MeasureTextBlock(textBlock);

        if (ShouldCollectInteractionOcclusion(info, request))
            PopulateWpfInteractionOcclusion(metrics, frameworkElement, window, request);
    }

    public override IReadOnlyList<LayoutRuleSupportInfo> GetLayoutRuleSupport()
    {
        var support = base.GetLayoutRuleSupport().ToDictionary(rule => rule.RuleId);
        SetSupport(support, LayoutDiagnosticRules.ElementClipped, "partial", "high",
            "HwndHost and custom drawing surfaces are separate coordinate spaces.");
        SetSupport(support, LayoutDiagnosticRules.ElementOutsideWindow, "exact", "high");
        SetSupport(support, LayoutDiagnosticRules.ContentOverflow, "partial", "high");
        SetSupport(support, LayoutDiagnosticRules.TextNotFullyRendered, "partial", "high",
            "TextBlock is measured with WPF FormattedText.");
        SetSupport(support, LayoutDiagnosticRules.InteractionOccluded, "partial", "high",
            "Point samples use WPF visual hit testing.");
        SetSupport(support, LayoutDiagnosticRules.VisualOccluded, "partial", "medium",
            "Transparent pixels and separate HWND surfaces are not inspected.");
        SetSupport(support, LayoutDiagnosticRules.GeometricOverlap, "exact", "high");
        return support.Values.OrderBy(rule => rule.RuleId, StringComparer.Ordinal).ToList();
    }

    private void PopulateWpfInteractionOcclusion(
        LayoutPlatformMetrics metrics,
        FrameworkElement element,
        System.Windows.Window window,
        LayoutInspectionRequest request)
    {
        if (!metrics.IsHitTestVisible || metrics.FullRegion is null)
            return;

        var points = SamplePoints(metrics.FullRegion, request.Occlusion.MaxSamplesPerElement);
        var blocked = 0;
        string? occluderId = null;
        foreach (var point in points)
        {
            var hit = VisualTreeHelper.HitTest(window, new System.Windows.Point(point.X, point.Y))?.VisualHit;
            if (hit is null || ReferenceEquals(hit, element) || element.IsAncestorOf(hit))
                continue;

            blocked++;
            for (DependencyObject? current = hit;
                 current is not null && occluderId is null;
                 current = VisualTreeHelper.GetParent(current))
            {
                occluderId = FindElementIdForPlatformView(current);
            }
        }

        if (blocked == 0)
            return;
        var (lower, upper) = EstimateBlockedInterval(
            blocked,
            points.Count,
            request.Occlusion.CoverageError);
        metrics.InteractionOccluderId = occluderId ?? "native-unmapped";
        metrics.InteractionBlockedLowerBound = lower;
        metrics.InteractionBlockedUpperBound = upper;
        metrics.InteractionSampleCount = points.Count;
    }

    private static LayoutTextEvidence MeasureTextBlock(TextBlock textBlock)
    {
        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var formatted = new FormattedText(
            textBlock.Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            textBlock.FlowDirection,
            new Typeface(
                textBlock.FontFamily,
                textBlock.FontStyle,
                textBlock.FontWeight,
                textBlock.FontStretch),
            textBlock.FontSize,
            textBlock.Foreground,
            dpi.PixelsPerDip)
        {
            Trimming = textBlock.TextTrimming
        };
        if (textBlock.TextWrapping != TextWrapping.NoWrap)
        {
            formatted.MaxTextWidth = Math.Max(
                0.01,
                textBlock.ActualWidth);
        }

        var contentWidth = formatted.WidthIncludingTrailingWhitespace;
        var widthOverflow = contentWidth > textBlock.ActualWidth + 0.5;
        var heightOverflow = formatted.Height > textBlock.ActualHeight + 0.5;
        var truncated = widthOverflow || heightOverflow;
        return new LayoutTextEvidence
        {
            Kind = truncated
                ? textBlock.TextTrimming != TextTrimming.None
                    ? "ellipsis"
                    : heightOverflow ? "vertical-hard-clip" : "horizontal-hard-clip"
                : null,
            IsTruncated = truncated,
            ContentWidth = contentWidth,
            ContentHeight = formatted.Height,
            AvailableWidth = textBlock.ActualWidth,
            AvailableHeight = textBlock.ActualHeight,
            MeasurementSource = "wpf-textformatter"
        };
    }

    private static LayoutRegionInfo? WpfRegion(FrameworkElement element, Visual target)
        => TransformWpfBounds(
            element,
            target,
            new System.Windows.Rect(new System.Windows.Point(), element.RenderSize));

    private static LayoutRegionInfo? TransformWpfBounds(Visual source, Visual target, System.Windows.Rect rect)
    {
        try
        {
            var transform = source.TransformToAncestor(target);
            var points = new[]
            {
                transform.Transform(rect.TopLeft),
                transform.Transform(rect.TopRight),
                transform.Transform(rect.BottomRight),
                transform.Transform(rect.BottomLeft)
            };
            return LayoutRegionMath.FromPoints(points.Select(Point), "exactPolygon");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static double GetBackgroundAlpha(FrameworkElement element)
    {
        System.Windows.Media.Brush? brush = element switch
        {
            Panel panel => panel.Background,
            Control control => control.Background,
            System.Windows.Controls.Border border => border.Background,
            _ => null
        };
        return brush is System.Windows.Media.SolidColorBrush solid
            ? solid.Color.A / 255d * solid.Opacity
            : 0;
    }

    private static double GetEffectiveOpacity(FrameworkElement element)
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
                var point = new LayoutPointInfo
                {
                    X = bounds.X + bounds.Width * (column + 0.5) / gridSize,
                    Y = bounds.Y + bounds.Height * (row + 0.5) / gridSize
                };
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

    private static LayoutPointInfo Point(System.Windows.Point point) => new() { X = point.X, Y = point.Y };

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
}
