using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;

namespace Microsoft.Maui.DevFlow.Agent.WPF;

public partial class WpfVisualTreeWalker
{
    protected override void PopulatePlatformLayoutSnapshot(
        LayoutElementSnapshot snapshot,
        object runtimeElement,
        LayoutInspectionRequest request)
    {
        if (runtimeElement is not Microsoft.Maui.Controls.VisualElement element ||
            element.Handler?.PlatformView is not FrameworkElement frameworkElement)
            return;

        var window = System.Windows.Window.GetWindow(frameworkElement);
        if (window is null)
            return;

        var native = frameworkElement.TransformToAncestor(window).TransformBounds(
            new System.Windows.Rect(0, 0, frameworkElement.ActualWidth, frameworkElement.ActualHeight));
        var full = new LayoutRect
        {
            X = native.X,
            Y = native.Y,
            Width = native.Width,
            Height = native.Height,
        };
        snapshot.FullRegion = LayoutPlatformEvidence.Region(full, "exact");
        LayoutRect? visible = LayoutPlatformEvidence.Intersect(full, new LayoutRect
        {
            X = 0,
            Y = 0,
            Width = window.ActualWidth,
            Height = window.ActualHeight,
        });
        for (DependencyObject? ancestor = VisualTreeHelper.GetParent(frameworkElement);
             ancestor is FrameworkElement parent && visible is not null;
             ancestor = VisualTreeHelper.GetParent(parent))
        {
            if (!parent.ClipToBounds)
                continue;
            var clip = parent.TransformToAncestor(window).TransformBounds(
                new System.Windows.Rect(0, 0, parent.ActualWidth, parent.ActualHeight));
            visible = LayoutPlatformEvidence.Intersect(visible, new LayoutRect
            {
                X = clip.X,
                Y = clip.Y,
                Width = clip.Width,
                Height = clip.Height,
            });
        }
        snapshot.VisibleRegion = LayoutPlatformEvidence.Region(
            visible ?? new LayoutRect { X = full.X, Y = full.Y, Width = 0, Height = 0 },
            "exact");

        if (frameworkElement is ScrollViewer scroll)
        {
            snapshot.ContentRegion = LayoutPlatformEvidence.Region(new LayoutRect
            {
                X = full.X,
                Y = full.Y,
                Width = scroll.ExtentWidth,
                Height = scroll.ExtentHeight,
            }, "exact");
        }

        if (snapshot.Interactive &&
            !request.Occlusion.Mode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            var receiver = window.InputHitTest(new System.Windows.Point(
                full.X + full.Width / 2,
                full.Y + full.Height / 2)) as DependencyObject;
            snapshot.HitTestSampleCount = 1;
            snapshot.BlockedHitTestSampleCount =
                receiver is null || (!ReferenceEquals(receiver, frameworkElement) &&
                    !IsDescendant(receiver, frameworkElement))
                    ? 1
                    : 0;
            snapshot.PlatformEvidenceLimitations.Add(
                "WPF interaction sampling checks one center point within this application window.");
        }
    }

    private static bool IsDescendant(DependencyObject candidate, DependencyObject ancestor)
    {
        for (DependencyObject? current = candidate;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }
}
