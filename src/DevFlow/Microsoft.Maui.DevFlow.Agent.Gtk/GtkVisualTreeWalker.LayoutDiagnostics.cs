using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Gtk;

public partial class GtkVisualTreeWalker
{
    protected override double ResolveWindowScale(Window window)
    {
        try
        {
            if (window.Handler?.PlatformView is global::Gtk.Widget widget)
                return Math.Max(1, widget.GetScaleFactor());
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
        if (element.Handler?.PlatformView is not global::Gtk.Widget widget)
            return;
        if (widget.GetRoot() is not global::Gtk.Widget root)
        {
            metrics.Limitations.Add("GTK widget is not attached to a root.");
            return;
        }
        if (!widget.ComputeBounds(root, out var bounds))
        {
            metrics.FullRegion = LayoutRegionMath.Empty("unknown");
            metrics.NativeVisibleRegion = LayoutRegionMath.Empty("unknown");
            metrics.Limitations.Add("GTK could not express widget bounds in the root coordinate space.");
            return;
        }

        metrics.FullRegion = LayoutRegionMath.FromRect(
            bounds.GetX(),
            bounds.GetY(),
            bounds.GetWidth(),
            bounds.GetHeight());
        var visible = metrics.FullRegion;
        for (var ancestor = widget.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor.GetOverflow() != global::Gtk.Overflow.Hidden)
                continue;
            if (!ancestor.ComputeBounds(root, out var ancestorBounds))
                continue;
            var clip = LayoutRegionMath.FromRect(
                ancestorBounds.GetX(),
                ancestorBounds.GetY(),
                ancestorBounds.GetWidth(),
                ancestorBounds.GetHeight());
            metrics.SelfClips.Add(new LayoutPlatformClip
            {
                ClipperElementId = FindElementIdForPlatformView(ancestor),
                Kind = ancestor is global::Gtk.ScrolledWindow ? "scroll-viewport" : "ancestor-layout-clip",
                Region = clip
            });
            visible = LayoutRegionMath.Intersect(visible, clip);
        }
        metrics.NativeVisibleRegion = visible;
        metrics.NativeVisibleKind = "unknown-platform-clip";

        if (widget.GetOverflow() == global::Gtk.Overflow.Hidden)
        {
            metrics.DescendantClipRegion = metrics.FullRegion;
            metrics.DescendantClipKind = widget is global::Gtk.ScrolledWindow
                ? "scroll-viewport"
                : "ancestor-layout-clip";
        }
        metrics.IsHitTestVisible = widget.GetVisible()
            && widget.GetSensitive()
            && widget.GetCanTarget()
            && widget.GetOpacity() > 0.01;
        metrics.IsOpaque = false;
        if (widget.GetType().Name is "GLArea" or "Video")
            metrics.IsCoverageOpaque = true;

        if (widget is global::Gtk.Label label)
        {
            var layout = label.GetLayout();
            layout.GetPixelSize(out var pixelWidth, out var pixelHeight);
            metrics.Text = new LayoutTextEvidence
            {
                Kind = layout.IsEllipsized() ? "ellipsis" : null,
                IsTruncated = layout.IsEllipsized(),
                RenderedLineCount = layout.GetLineCount(),
                MaximumLines = label.GetLines() > 0 ? label.GetLines() : null,
                ContentWidth = pixelWidth,
                ContentHeight = pixelHeight,
                AvailableWidth = bounds.GetWidth(),
                AvailableHeight = bounds.GetHeight(),
                MeasurementSource = "gtk-pango"
            };
        }

        if (ShouldCollectInteractionOcclusion(info, request))
            PopulateGtkInteractionOcclusion(metrics, widget, root, request);
        metrics.Limitations.Add("Custom gtk_snapshot_push_clip regions are not introspectable.");
    }

    public override IReadOnlyList<LayoutRuleSupportInfo> GetLayoutRuleSupport()
    {
        var support = base.GetLayoutRuleSupport().ToDictionary(rule => rule.RuleId);
        SetSupport(support, LayoutDiagnosticRules.ElementClipped, "partial", "high",
            "Custom snapshot-time clips are not introspectable.");
        SetSupport(support, LayoutDiagnosticRules.ElementOutsideWindow, "exact", "high");
        SetSupport(support, LayoutDiagnosticRules.ContentOverflow, "partial", "high");
        SetSupport(support, LayoutDiagnosticRules.TextNotFullyRendered, "partial", "exact",
            "Gtk.Label ellipsization is exact; custom Pango renderers are not.");
        SetSupport(support, LayoutDiagnosticRules.InteractionOccluded, "partial", "high",
            "Point samples use gtk_widget_pick within one surface.");
        SetSupport(support, LayoutDiagnosticRules.VisualOccluded, "partial", "low",
            "GTK widget opacity does not prove opaque painting; CSS and custom snapshot pixels are not inspected.");
        SetSupport(support, LayoutDiagnosticRules.GeometricOverlap, "exact", "high");
        return support.Values.OrderBy(rule => rule.RuleId, StringComparer.Ordinal).ToList();
    }

    private void PopulateGtkInteractionOcclusion(
        LayoutPlatformMetrics metrics,
        global::Gtk.Widget widget,
        global::Gtk.Widget root,
        LayoutInspectionRequest request)
    {
        if (!metrics.IsHitTestVisible || metrics.FullRegion is null)
            return;
        var bounds = metrics.FullRegion.Bounds;
        var gridSize = Math.Max(1, (int)Math.Floor(Math.Sqrt(Math.Max(
            1,
            request.Occlusion.MaxSamplesPerElement))));
        if (gridSize > 1 && gridSize % 2 == 0)
            gridSize--;
        var points = new List<(double X, double Y)>();
        for (var row = 0; row < gridSize; row++)
        {
            for (var column = 0; column < gridSize; column++)
            {
                var point = new LayoutPointInfo
                {
                    X = bounds.X + bounds.Width * (column + 0.5) / gridSize,
                    Y = bounds.Y + bounds.Height * (row + 0.5) / gridSize
                };
                if (LayoutRegionMath.Contains(metrics.FullRegion, point))
                    points.Add((point.X, point.Y));
            }
        }
        if (points.Count == 0)
        {
            var center = LayoutRegionMath.Center(metrics.FullRegion);
            points.Add((center.X, center.Y));
        }

        var blocked = 0;
        string? occluderId = null;
        foreach (var (x, y) in points)
        {
            var hit = root.Pick(x, y, global::Gtk.PickFlags.Default);
            if (hit is null || hit == widget || IsGtkDescendant(widget, hit))
                continue;
            blocked++;
            for (var current = hit; current is not null && occluderId is null; current = current.GetParent())
                occluderId = FindElementIdForPlatformView(current);
        }

        if (blocked == 0)
            return;
        var ratio = (double)blocked / points.Count;
        var probability = Math.Clamp(request.Occlusion.CoverageError, 0.001, 0.5);
        var margin = Math.Sqrt(Math.Log(2 / probability) / (2 * points.Count));
        metrics.InteractionOccluderId = occluderId ?? "native-unmapped";
        metrics.InteractionBlockedLowerBound = Math.Max(0, ratio - margin);
        metrics.InteractionBlockedUpperBound = Math.Min(1, ratio + margin);
        metrics.InteractionSampleCount = points.Count;
    }

    private static bool IsGtkDescendant(global::Gtk.Widget ancestor, global::Gtk.Widget widget)
    {
        for (var current = widget; current is not null; current = current.GetParent())
        {
            if (current == ancestor)
                return true;
        }
        return false;
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
}
