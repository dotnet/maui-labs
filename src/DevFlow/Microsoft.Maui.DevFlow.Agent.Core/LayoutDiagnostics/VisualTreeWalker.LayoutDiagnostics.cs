using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class VisualTreeWalker
{
    internal LayoutCaptureSnapshot CaptureLayoutSnapshot(
        Application app,
        LayoutInspectionRequest request)
    {
        var tree = WalkTree(app, request.Scope.MaxDepth, request.Scope.Window);
        var capture = new LayoutCaptureSnapshot();
        if (tree.Count == 0)
        {
            capture.MarkIncomplete(
                "The MAUI visual tree is empty or the requested window is not available.");
            return capture;
        }

        var requestedWindow = request.Scope.Window;
        for (var rootIndex = 0; rootIndex < tree.Count; rootIndex++)
        {
            var appWindowIndex = requestedWindow ?? rootIndex;
            var window = appWindowIndex >= 0 && appWindowIndex < app.Windows.Count
                ? app.Windows[appWindowIndex]
                : null;
            var windowId = $"window-{appWindowIndex}";
            var scale = window is null ? 1 : ResolveWindowScale(window);
            var rootBounds = ResolveRootBounds(tree[rootIndex], window);
            capture.Windows.Add(new LayoutWindowInfo
            {
                Id = windowId,
                Scale = scale,
                Bounds = rootBounds
            });

            var rootClip = rootBounds is null
                ? null
                : new AppliedLayoutClip(
                    tree[rootIndex].Id,
                    "window-edge",
                    LayoutRegionMath.FromRect(rootBounds),
                    false);
            var inherited = rootClip is null ? [] : new List<AppliedLayoutClip> { rootClip };
            BuildLayoutNode(
                tree[rootIndex],
                capture,
                inherited,
                windowId,
                scale,
                insideScrollableViewport: false,
                ancestorVisible: true,
                request,
                ref _layoutTreeOrder);
        }

        _layoutTreeOrder = 0;
        capture.HasActiveAnimations = capture.Nodes.Any(node => node.HasActiveAnimation);
        return capture;
    }

    internal void ApplyLayoutScope(
        LayoutCaptureSnapshot capture,
        LayoutInspectionScope scope)
        => ApplyScopeFilter(capture, scope.RootElementId, scope.IncludeDescendants);

    internal void AppendNativeLayoutNodes(
        LayoutCaptureSnapshot capture,
        IEnumerable<ElementInfo> nativeRoots)
    {
        var roots = nativeRoots.ToList();
        var windowId = "native-window";
        if (capture.Windows.All(window => window.Id != windowId))
        {
            capture.Windows.Add(new LayoutWindowInfo
            {
                Id = windowId,
                LogicalUnit = "physicalPixel",
                Scale = 1,
                Origin = "screen-top-left"
            });
        }

        var treeOrder = capture.Nodes.Count == 0 ? 0 : capture.Nodes.Max(node => node.TreeOrder) + 1;
        foreach (var root in roots)
            AppendNativeNode(capture, root, windowId, ref treeOrder);
        if (roots.Count > 0)
            capture.Limitations.Add("Native automation elements use platform accessibility bounds and a separate screen coordinate space.");
    }

    public virtual IReadOnlyList<LayoutRuleSupportInfo> GetLayoutRuleSupport()
        => LayoutDiagnosticRules.All.Select(rule => new LayoutRuleSupportInfo
        {
            RuleId = rule,
            Support = rule switch
            {
                LayoutDiagnosticRules.GeometricOverlap => "partial",
                LayoutDiagnosticRules.AccessibilityVisibilityMismatch => "unsupported",
                LayoutDiagnosticRules.ElementClipped
                    or LayoutDiagnosticRules.ElementOutsideWindow
                    or LayoutDiagnosticRules.ContentOverflow
                    or LayoutDiagnosticRules.VisibleZeroArea => "partial",
                _ => "partial"
            },
            Confidence = rule switch
            {
                LayoutDiagnosticRules.GeometricOverlap => "medium",
                LayoutDiagnosticRules.AccessibilityVisibilityMismatch => "low",
                _ => "medium"
            },
            Limitations = rule switch
            {
                LayoutDiagnosticRules.TextNotFullyRendered =>
                    ["Exact text truncation requires a platform text-layout collector."],
                LayoutDiagnosticRules.InteractionOccluded =>
                    ["Interaction occlusion requires platform hit-test evidence."],
                LayoutDiagnosticRules.VisualOccluded =>
                    ["Visual occlusion is inferred from geometry, paint order, and opacity."],
                LayoutDiagnosticRules.AccessibilityVisibilityMismatch =>
                    ["Accessibility visibility is unavailable unless a native automation node supplies it."],
                _ => []
            }
        }).ToList();

    protected virtual double ResolveWindowScale(Window window) => 1;

    protected virtual void PopulatePlatformLayoutMetrics(
        LayoutPlatformMetrics metrics,
        VisualElement element,
        ElementInfo info,
        LayoutInspectionRequest request)
    {
    }

    protected string? FindElementIdForPlatformView(object platformView)
    {
        foreach (var pair in _externalIdToElement)
        {
            if (pair.Value is IView view
                && ReferenceEquals(view.Handler?.PlatformView, platformView))
            {
                return pair.Key;
            }
        }
        return null;
    }

    protected static bool ShouldCollectInteractionOcclusion(
        ElementInfo info,
        LayoutInspectionRequest request)
    {
        if (request.Occlusion.Mode.Equals("none", StringComparison.OrdinalIgnoreCase))
            return false;

        if (request.Occlusion.Mode.Equals("all", StringComparison.OrdinalIgnoreCase))
            return info.IsVisible;

        var interactive = info.Traits?.Contains("interactive") == true
            || info.Gestures is { Count: > 0 };
        if (!interactive)
            return false;
        return request.Rules is not { Count: > 0 }
            || request.Rules.Contains(
                LayoutDiagnosticRules.InteractionOccluded,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AppendNativeNode(
        LayoutCaptureSnapshot capture,
        ElementInfo info,
        string windowId,
        ref int treeOrder)
    {
        var bounds = info.WindowBounds ?? info.Bounds;
        var region = bounds is null
            ? LayoutRegionMath.Empty("unknown")
            : LayoutRegionMath.FromBounds(bounds);
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = info,
            LayoutRegion = region,
            FullRegion = region,
            VisibleRegion = info.IsVisible ? region : LayoutRegionMath.Empty(),
            WindowId = windowId,
            WindowScale = 1,
            TreeOrder = treeOrder++,
            IsInteractive = info.Traits?.Contains("interactive") == true,
            IsRendered = info.IsVisible,
            IsHitTestVisible = info.IsVisible && info.IsEnabled,
            Limitations = ["Native automation bounds do not expose paint clips or custom drawing."]
        });
        if (info.Children is null)
            return;
        foreach (var child in info.Children)
            AppendNativeNode(capture, child, windowId, ref treeOrder);
    }

    private int _layoutTreeOrder;

    private void BuildLayoutNode(
        ElementInfo info,
        LayoutCaptureSnapshot capture,
        IReadOnlyList<AppliedLayoutClip> inheritedClips,
        string windowId,
        double windowScale,
        bool insideScrollableViewport,
        bool ancestorVisible,
        LayoutInspectionRequest request,
        ref int treeOrder)
    {
        _externalIdToElement.TryGetValue(info.Id, out var visualTreeElement);
        var metrics = BuildBaseLayoutMetrics(info, visualTreeElement);
        if (visualTreeElement is VisualElement visualElement)
        {
            try
            {
                PopulatePlatformLayoutMetrics(metrics, visualElement, info, request);
            }
            catch (Exception ex)
            {
                metrics.Limitations.Add($"Platform layout metrics failed for {info.Type}: {ex.GetType().Name}");
            }
        }
        ApplyTextPrivacy(metrics.Text, info.Text, request.Privacy.Text);

        var layoutRegion = RegionFromInfo(info);
        var fullRegion = metrics.FullRegion ?? layoutRegion;
        var hasAuthoritativeVisibility = visualTreeElement is IView;
        var isRendered = ancestorVisible
            && (!hasAuthoritativeVisibility || info.IsVisible);
        var visibleRegion = isRendered
            ? fullRegion
            : LayoutRegionMath.Empty(fullRegion.Precision);
        var clipChain = new List<LayoutClipContribution>();

        foreach (var clip in inheritedClips)
            visibleRegion = ApplyClip(visibleRegion, clip, clipChain);

        if (metrics.SelfClipRegion is not null)
        {
            visibleRegion = ApplyClip(
                visibleRegion,
                new AppliedLayoutClip(info.Id, "explicit-clip", metrics.SelfClipRegion, false),
                clipChain);
        }

        foreach (var platformClip in metrics.SelfClips)
        {
            visibleRegion = ApplyClip(
                visibleRegion,
                new AppliedLayoutClip(
                    platformClip.ClipperElementId,
                    platformClip.Kind,
                    platformClip.Region,
                    false),
                clipChain);
        }

        if (metrics.NativeVisibleRegion is not null)
        {
            visibleRegion = ApplyClip(
                visibleRegion,
                new AppliedLayoutClip(null, metrics.NativeVisibleKind, metrics.NativeVisibleRegion, false),
                clipChain);
        }

        var node = new LayoutNodeSnapshot
        {
            Element = info,
            LayoutRegion = layoutRegion,
            FullRegion = fullRegion,
            VisibleRegion = visibleRegion,
            ContentRegion = metrics.ContentRegion,
            DescendantClipRegion = metrics.DescendantClipRegion,
            DescendantClipKind = metrics.DescendantClipKind,
            ClipChain = clipChain,
            Text = metrics.Text,
            Limitations = metrics.Limitations.Distinct(StringComparer.Ordinal).ToList(),
            WindowId = windowId,
            WindowScale = windowScale > 0 && double.IsFinite(windowScale) ? windowScale : 1,
            TreeOrder = treeOrder++,
            ZIndex = metrics.ZIndex,
            IsInteractive = info.Traits?.Contains("interactive") == true || info.Gestures is { Count: > 0 },
            IsHitTestVisible = isRendered && metrics.IsHitTestVisible,
            IsOpaque = isRendered && metrics.IsOpaque,
            IsRendered = isRendered,
            IsCoverageOpaque = metrics.IsCoverageOpaque,
            GeometryAvailable = metrics.GeometryAvailable,
            IsScrollable = metrics.IsScrollable,
            IsInsideScrollableViewport = insideScrollableViewport,
            AccessibilityVisible = metrics.AccessibilityVisible,
            HasActiveAnimation = metrics.HasActiveAnimation,
            InteractionOccluderId = metrics.InteractionOccluderId,
            InteractionBlockedLowerBound = metrics.InteractionBlockedLowerBound,
            InteractionBlockedUpperBound = metrics.InteractionBlockedUpperBound,
            InteractionSampleCount = metrics.InteractionSampleCount
        };
        if (info.NativeProperties?.TryGetValue("itemCount", out var itemCountValue) == true
            && int.TryParse(itemCountValue, out var itemCount))
        {
            var realizedChildren = info.Children?.Count ?? 0;
            if (itemCount > realizedChildren)
            {
                node.Limitations.Add(
                    $"{info.Type} contains {itemCount} data items; diagnostics cover {realizedChildren} currently realized visual children.");
            }
        }
        capture.Nodes.Add(node);
        capture.Limitations.AddRange(node.Limitations);

        var childClips = inheritedClips.ToList();
        var childInsideScroll = insideScrollableViewport;
        if (metrics.DescendantClipRegion is not null)
        {
            var clipKind = metrics.DescendantClipKind ?? "ancestor-layout-clip";
            childClips.Add(new AppliedLayoutClip(
                info.Id,
                clipKind,
                metrics.DescendantClipRegion,
                clipKind == "scroll-viewport"));
            childInsideScroll |= clipKind == "scroll-viewport";
        }

        if (info.Children is null)
            return;

        foreach (var child in info.Children)
        {
            BuildLayoutNode(
                child,
                capture,
                childClips,
                windowId,
                windowScale,
                childInsideScroll,
                isRendered,
                request,
                ref treeOrder);
        }
    }

    internal static void ApplyTextPrivacy(
        LayoutTextEvidence? evidence,
        string? text,
        string privacyMode)
    {
        if (evidence is null)
            return;

        evidence.TextLength = null;
        evidence.Text = null;
        if (text is null)
            return;

        if (privacyMode.Equals("length", StringComparison.OrdinalIgnoreCase)
            || privacyMode.Equals("raw", StringComparison.OrdinalIgnoreCase))
        {
            evidence.TextLength = text.Length;
        }

        if (privacyMode.Equals("raw", StringComparison.OrdinalIgnoreCase))
            evidence.Text = text;
    }

    private LayoutPlatformMetrics BuildBaseLayoutMetrics(
        ElementInfo info,
        IVisualTreeElement? visualTreeElement)
    {
        var layoutRegion = RegionFromInfo(info);
        var metrics = new LayoutPlatformMetrics
        {
            FullRegion = layoutRegion,
            ContentRegion = layoutRegion,
            IsHitTestVisible = info.IsVisible,
            IsScrollable = info.Traits?.Contains("scrollable") == true,
            Precision = layoutRegion.Precision
        };

        if (visualTreeElement is not VisualElement element)
            return metrics;

        metrics.IsHitTestVisible = element.IsVisible && !element.InputTransparent;
        metrics.ZIndex = element.ZIndex;
        metrics.IsScrollable |= element is ScrollView or ItemsView
            || element.GetType().Name == "ListView";

        if (element.Background is SolidColorBrush { Color: { } backgroundColor })
            metrics.IsOpaque = backgroundColor.Alpha >= 0.99f && element.Opacity >= 0.99;

        var desired = element.DesiredSize;
        if (layoutRegion.Area > 0
            && double.IsFinite(desired.Width) && double.IsFinite(desired.Height)
            && (desired.Width > 0 || desired.Height > 0))
        {
            metrics.ContentRegion = LayoutRegionMath.FromRect(
                layoutRegion.Bounds.X,
                layoutRegion.Bounds.Y,
                Math.Max(layoutRegion.Bounds.Width, desired.Width),
                Math.Max(layoutRegion.Bounds.Height, desired.Height),
                "conservativeBounds");
        }

        if (element is Microsoft.Maui.Controls.Layout layout && layout.IsClippedToBounds)
        {
            metrics.DescendantClipRegion = layoutRegion;
            metrics.DescendantClipKind = "ancestor-layout-clip";
        }
        else if (element is ScrollView)
        {
            metrics.DescendantClipRegion = layoutRegion;
            metrics.DescendantClipKind = "scroll-viewport";
        }

        if (element.Clip is not null)
        {
            try
            {
                var clipBounds = element.Clip switch
                {
                    RectangleGeometry rectangle => rectangle.Rect,
                    RoundRectangleGeometry roundRectangle => roundRectangle.Rect,
                    EllipseGeometry ellipse => new Rect(
                        ellipse.Center.X - ellipse.RadiusX,
                        ellipse.Center.Y - ellipse.RadiusY,
                        ellipse.RadiusX * 2,
                        ellipse.RadiusY * 2),
                    _ => Rect.Zero
                };

                if (clipBounds.Width > 0 && clipBounds.Height > 0)
                {
                    metrics.SelfClipRegion = LayoutRegionMath.FromRect(
                        layoutRegion.Bounds.X + clipBounds.X,
                        layoutRegion.Bounds.Y + clipBounds.Y,
                        clipBounds.Width,
                        clipBounds.Height,
                        "conservativeBounds");
                    metrics.Limitations.Add("MAUI clip geometry is represented by its bounding region.");
                }
                else
                {
                    metrics.Limitations.Add("MAUI clip geometry could not be represented as a rectangular region.");
                }
            }
            catch
            {
                metrics.Limitations.Add("MAUI clip geometry could not be inspected.");
            }
        }

        if (element is Label label)
        {
            metrics.Text = new LayoutTextEvidence
            {
                MaximumLines = label.MaxLines >= 0 ? label.MaxLines : null,
                AvailableWidth = layoutRegion.Bounds.Width,
                AvailableHeight = layoutRegion.Bounds.Height,
                MeasurementSource = "maui-label"
            };
        }

        return metrics;
    }

    internal static LayoutRegionInfo RegionFromInfo(ElementInfo info)
    {
        if (info.WindowBounds is { } windowBounds
            && IsKnownBounds(windowBounds))
            return LayoutRegionMath.FromBounds(windowBounds);
        if (info.Bounds is { } localBounds
            && IsKnownBounds(localBounds))
            return LayoutRegionMath.FromBounds(localBounds, "conservativeBounds");
        return LayoutRegionMath.Empty("unknown");
    }

    private static bool IsKnownBounds(BoundsInfo bounds)
        => double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width >= 0
            && bounds.Height >= 0;

    private static LayoutRegionInfo ApplyClip(
        LayoutRegionInfo current,
        AppliedLayoutClip clip,
        List<LayoutClipContribution> contributions)
    {
        var before = current.Area;
        var afterRegion = LayoutRegionMath.Intersect(current, clip.Region);
        var after = afterRegion.Area;
        if (before - after > 0.000001)
        {
            contributions.Add(new LayoutClipContribution
            {
                ClipperElementId = clip.ClipperElementId,
                Kind = clip.Kind,
                Precision = afterRegion.Precision,
                AreaBefore = before,
                AreaAfter = after,
                LostAreaRatio = before > 0 ? Math.Clamp(1 - after / before, 0, 1) : 0,
                Region = clip.Region
            });
        }
        return afterRegion;
    }

    private static LayoutRectInfo? ResolveRootBounds(ElementInfo root, Window? window)
    {
        var bounds = root.WindowBounds ?? root.Bounds;
        if (bounds is { Width: > 0, Height: > 0 })
        {
            return new LayoutRectInfo
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height
            };
        }

        if (window is not null
            && double.IsFinite(window.Width) && double.IsFinite(window.Height)
            && window.Width > 0 && window.Height > 0)
        {
            return new LayoutRectInfo { Width = window.Width, Height = window.Height };
        }

        return null;
    }

    private static void ApplyScopeFilter(
        LayoutCaptureSnapshot capture,
        string? rootElementId,
        bool includeDescendants)
    {
        if (string.IsNullOrWhiteSpace(rootElementId))
            return;

        var root = capture.Nodes.FirstOrDefault(node =>
            node.Element.Id.Equals(rootElementId, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            capture.Nodes.Clear();
            capture.MarkIncomplete(
                $"Requested root element '{rootElementId}' was not found.");
            return;
        }

        if (!includeDescendants)
        {
            capture.Nodes.RemoveAll(node => !ReferenceEquals(node, root));
            return;
        }

        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Element.Id };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in capture.Nodes)
            {
                if (node.Element.ParentId is not null
                    && included.Contains(node.Element.ParentId)
                    && included.Add(node.Element.Id))
                {
                    changed = true;
                }
            }
        }
        capture.Nodes.RemoveAll(node => !included.Contains(node.Element.Id));
    }

    private sealed record AppliedLayoutClip(
        string? ClipperElementId,
        string Kind,
        LayoutRegionInfo Region,
        bool IsScrollViewport);
}
