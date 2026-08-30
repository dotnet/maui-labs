using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;

#if ANDROID
using Android.Widget;
#elif IOS || MACCATALYST
using CoreGraphics;
using UIKit;
#elif WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
#endif

namespace Microsoft.Maui.DevFlow.Agent;

public partial class PlatformVisualTreeWalker
{
    protected override void PopulatePlatformLayoutSnapshot(
        LayoutElementSnapshot snapshot,
        object runtimeElement,
        LayoutInspectionRequest request)
    {
        if (runtimeElement is not VisualElement element ||
            element.Handler?.PlatformView is not { } platformView)
            return;

#if ANDROID
        if (platformView is global::Android.Views.View androidView)
            PopulateAndroid(snapshot, androidView);
#elif IOS || MACCATALYST
        if (platformView is UIView uiView)
            PopulateUIKit(snapshot, uiView, request);
#elif WINDOWS
        if (platformView is FrameworkElement frameworkElement)
            PopulateWinUI(snapshot, frameworkElement, request);
#else
        if (snapshot.WindowBounds is { } bounds)
            snapshot.FullRegion = LayoutPlatformEvidence.Region(bounds, "conservativeBounds");
#endif
    }

#if ANDROID
    private static void PopulateAndroid(
        LayoutElementSnapshot snapshot,
        global::Android.Views.View view)
    {
        var density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        var location = new int[2];
        view.GetLocationInWindow(location);
        var full = new LayoutRect
        {
            X = location[0] / density,
            Y = location[1] / density,
            Width = view.Width / density,
            Height = view.Height / density,
        };
        snapshot.FullRegion = LayoutPlatformEvidence.Region(full, "exact");

        var visible = new global::Android.Graphics.Rect();
        snapshot.VisibleRegion = view.GetLocalVisibleRect(visible)
            ? LayoutPlatformEvidence.Region(new LayoutRect
            {
                X = (location[0] + visible.Left) / density,
                Y = (location[1] + visible.Top) / density,
                Width = visible.Width() / density,
                Height = visible.Height() / density,
            }, "conservativeBounds")
            : LayoutPlatformEvidence.Region(
                new LayoutRect { X = full.X, Y = full.Y, Width = 0, Height = 0 },
                "conservativeBounds");

        if (view is TextView textView && textView.Layout is { } layout)
        {
            var ellipsis = 0;
            for (var line = 0; line < layout.LineCount; line++)
                ellipsis += layout.GetEllipsisCount(line);
            snapshot.TextEvidence = new LayoutTextEvidence
            {
                Kind = "android-text-layout",
                IsTruncated = ellipsis > 0,
                EllipsisCount = ellipsis,
                RenderedLineCount = layout.LineCount,
                MeasurementSource = "android-layout",
            };
        }

        if (view is global::Android.Widget.ScrollView scroll && scroll.ChildCount > 0)
        {
            var child = scroll.GetChildAt(0);
            if (child is not null)
            {
                snapshot.ContentRegion = LayoutPlatformEvidence.Region(new LayoutRect
                {
                    X = full.X,
                    Y = full.Y,
                    Width = child.Width / density,
                    Height = child.Height / density,
                }, "exact");
            }
        }
        else if (view is HorizontalScrollView horizontal && horizontal.ChildCount > 0)
        {
            var child = horizontal.GetChildAt(0);
            if (child is not null)
            {
                snapshot.ContentRegion = LayoutPlatformEvidence.Region(new LayoutRect
                {
                    X = full.X,
                    Y = full.Y,
                    Width = child.Width / density,
                    Height = child.Height / density,
                }, "exact");
            }
        }

        if (snapshot.Interactive)
        {
            snapshot.PlatformEvidenceLimitations.Add(
                "Android does not expose a non-destructive public topmost-view hit-test API; interaction occlusion was not sampled.");
        }
    }
#elif IOS || MACCATALYST
    private static void PopulateUIKit(
        LayoutElementSnapshot snapshot,
        UIView view,
        LayoutInspectionRequest request)
    {
        var window = view.Window;
        if (window is null)
            return;

        var fullNative = view.ConvertRectToView(view.Bounds, window);
        var full = Rect(fullNative);
        snapshot.FullRegion = LayoutPlatformEvidence.Region(full, "exact");
        LayoutRect? visible = LayoutPlatformEvidence.Intersect(full, Rect(window.Bounds));
        for (var ancestor = view.Superview; ancestor is not null && visible is not null; ancestor = ancestor.Superview)
        {
            if (!ancestor.ClipsToBounds)
                continue;
            var clip = Rect(ancestor.ConvertRectToView(ancestor.Bounds, window));
            visible = LayoutPlatformEvidence.Intersect(visible, clip);
        }
        snapshot.VisibleRegion = LayoutPlatformEvidence.Region(
            visible ?? new LayoutRect { X = full.X, Y = full.Y, Width = 0, Height = 0 },
            "exact");

        if (view is UIScrollView scroll)
        {
            snapshot.ContentRegion = LayoutPlatformEvidence.Region(new LayoutRect
            {
                X = full.X,
                Y = full.Y,
                Width = scroll.ContentSize.Width,
                Height = scroll.ContentSize.Height,
            }, "exact");
        }

        if (snapshot.Interactive &&
            !request.Occlusion.Mode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            var center = new CGPoint(fullNative.GetMidX(), fullNative.GetMidY());
            var receiver = window.HitTest(center, null);
            snapshot.HitTestSampleCount = 1;
            snapshot.BlockedHitTestSampleCount =
                receiver is null || (!ReferenceEquals(receiver, view) && !receiver.IsDescendantOfView(view))
                    ? 1
                    : 0;
            snapshot.PlatformEvidenceLimitations.Add(
                "UIKit interaction sampling checks one center point and excludes other application windows and system UI.");
        }
    }

    private static LayoutRect Rect(CGRect rect) => new()
    {
        X = rect.X,
        Y = rect.Y,
        Width = rect.Width,
        Height = rect.Height,
    };
#elif WINDOWS
    private static void PopulateWinUI(
        LayoutElementSnapshot snapshot,
        FrameworkElement element,
        LayoutInspectionRequest request)
    {
        if (element.XamlRoot?.Content is not UIElement root)
            return;

        var native = element.TransformToVisual(root).TransformBounds(
            new global::Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var full = new LayoutRect
        {
            X = native.X,
            Y = native.Y,
            Width = native.Width,
            Height = native.Height,
        };
        snapshot.FullRegion = LayoutPlatformEvidence.Region(full, "exact");
        var rootBounds = new LayoutRect
        {
            X = 0,
            Y = 0,
            Width = element.XamlRoot.Size.Width,
            Height = element.XamlRoot.Size.Height,
        };
        var visible = LayoutPlatformEvidence.Intersect(full, rootBounds);
        if (visible is not null)
            snapshot.VisibleRegion = LayoutPlatformEvidence.Region(visible, "conservativeBounds");

        if (element is ScrollViewer scroll)
        {
            snapshot.ContentRegion = LayoutPlatformEvidence.Region(new LayoutRect
            {
                X = full.X,
                Y = full.Y,
                Width = scroll.ExtentWidth,
                Height = scroll.ExtentHeight,
            }, "exact");
        }
        if (element is TextBlock text)
        {
            snapshot.TextEvidence = new LayoutTextEvidence
            {
                Kind = "winui-text-block",
                IsTruncated = text.IsTextTrimmed,
                MeasurementSource = "winui",
            };
        }
        else if (element is RichTextBlock richText)
        {
            snapshot.TextEvidence = new LayoutTextEvidence
            {
                Kind = "winui-rich-text-block",
                IsTruncated = richText.HasOverflowContent,
                MeasurementSource = "winui",
            };
        }

        if (snapshot.Interactive &&
            !request.Occlusion.Mode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            var point = new global::Windows.Foundation.Point(
                full.X + full.Width / 2,
                full.Y + full.Height / 2);
            var receiver = VisualTreeHelper.FindElementsInHostCoordinates(point, root)
                .FirstOrDefault();
            snapshot.HitTestSampleCount = 1;
            snapshot.BlockedHitTestSampleCount =
                receiver is null || (!ReferenceEquals(receiver, element) && !IsDescendant(receiver, element))
                    ? 1
                    : 0;
            snapshot.PlatformEvidenceLimitations.Add(
                "WinUI interaction sampling checks one center point within this XamlRoot.");
        }
    }

    private static bool IsDescendant(DependencyObject candidate, DependencyObject ancestor)
    {
        for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }
#endif
}
