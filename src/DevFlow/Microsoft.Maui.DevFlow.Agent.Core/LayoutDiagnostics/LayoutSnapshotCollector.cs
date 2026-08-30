using Microsoft.Maui.Controls;

namespace Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;

/// <summary>
/// Projects one already-captured visual tree walk into the geometry-only snapshots the
/// <see cref="LayoutDiagnosticsAnalyzer"/> consumes.
///
/// The collector never walks the tree itself and never resolves an element by id. It reads the
/// <see cref="ElementInfo"/> tree produced by a single <see cref="VisualTreeWalker.WalkTree"/> call
/// plus the per-walk runtime object map from that same walk, so one UI-thread pass produces the
/// whole report. Everything it reads comes from <see cref="IView"/>, which every MAUI
/// <c>VisualElement</c> and every Comet view implements.
/// </summary>
internal static class LayoutSnapshotCollector
{
    internal readonly record struct CollectResult(
        List<LayoutElementSnapshot> Snapshots,
        bool Truncated,
        bool RootFound);

    /// <summary>
    /// Walks <paramref name="roots"/> in pre-order (deterministic), stopping at
    /// <paramref name="maxElements"/>.
    /// </summary>
    /// <param name="rootElementId">
    /// When set, only that element's subtree is projected. When it is not present in the tree the
    /// result reports <see cref="CollectResult.RootFound"/> as <c>false</c>.
    /// </param>
    public static CollectResult Collect(
        IReadOnlyList<ElementInfo> roots,
        IReadOnlyDictionary<string, object> runtimeElements,
        string? rootElementId,
        int maxElements,
        VisualTreeWalker? walker = null,
        LayoutInspectionRequest? request = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(runtimeElements);

        var snapshots = new List<LayoutElementSnapshot>();
        var budget = Math.Clamp(maxElements, 1, LayoutDiagnosticsFormat.MaxElements);

        if (!string.IsNullOrEmpty(rootElementId))
        {
            var subtree = FindById(roots, rootElementId!);
            if (subtree is null)
                return new CollectResult(snapshots, Truncated: false, RootFound: false);
            var truncatedSubtree = !Project(
                subtree.Value.Node,
                subtree.Value.Node.ParentId,
                runtimeElements,
                snapshots,
                budget,
                subtree.Value.AncestorsVisible,
                walker,
                request,
                "window-0");
            return new CollectResult(snapshots, truncatedSubtree, RootFound: true);
        }

        for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            var root = roots[rootIndex];
            if (!Project(
                root,
                null,
                runtimeElements,
                snapshots,
                budget,
                ancestorsVisible: true,
                walker,
                request,
                $"window-{rootIndex}"))
                return new CollectResult(snapshots, Truncated: true, RootFound: true);
        }

        return new CollectResult(snapshots, Truncated: false, RootFound: true);
    }

    /// <summary>Returns false when the element budget was exhausted.</summary>
    private static bool Project(
        ElementInfo node,
        string? parentId,
        IReadOnlyDictionary<string, object> runtimeElements,
        List<LayoutElementSnapshot> snapshots,
        int budget,
        bool ancestorsVisible,
        VisualTreeWalker? walker,
        LayoutInspectionRequest? request,
        string windowId)
    {
        if (snapshots.Count >= budget)
            return false;

        var effectivelyVisible = IsEffectivelyVisible(node, ancestorsVisible);
        snapshots.Add(BuildSnapshot(
            node,
            parentId,
            runtimeElements,
            effectivelyVisible,
            walker,
            request,
            windowId));

        if (node.Children is not { Count: > 0 } children)
            return true;

        foreach (var child in children)
        {
            if (!Project(
                child,
                node.Id,
                runtimeElements,
                snapshots,
                budget,
                effectivelyVisible,
                walker,
                request,
                windowId))
                return false;
        }

        return true;
    }

    private static LayoutElementSnapshot BuildSnapshot(
        ElementInfo node,
        string? parentId,
        IReadOnlyDictionary<string, object> runtimeElements,
        bool effectivelyVisible,
        VisualTreeWalker? walker,
        LayoutInspectionRequest? request,
        string windowId)
    {
        var snapshot = new LayoutElementSnapshot
        {
            Id = node.Id,
            ParentId = parentId ?? node.ParentId,
            WindowId = windowId,
            Type = node.Type,
            AutomationId = node.AutomationId,
            Role = node.Role,
            Interactive = effectivelyVisible && node.IsEnabled &&
                node.Traits?.Any(trait =>
                    trait.Equals("interactive", StringComparison.OrdinalIgnoreCase) ||
                    trait.Equals("editable", StringComparison.OrdinalIgnoreCase) ||
                    trait.Equals("scrollable", StringComparison.OrdinalIgnoreCase)) == true,
            SourceFile = node.SourceFile,
            SourceLine = node.SourceLine,
            SourceColumn = node.SourceColumn,
            IsVisible = effectivelyVisible,
            IsEnabled = node.IsEnabled,
            Opacity = double.IsFinite(node.Opacity) ? node.Opacity : 1,
            Frame = ToRect(node.Bounds),
            WindowBounds = ToRect(node.WindowBounds),
        };

        if (!runtimeElements.TryGetValue(node.Id, out var runtime) || runtime is not IView view)
            return snapshot;

        snapshot.HasLayoutState = true;
        snapshot.IsRealized = view.Handler is not null;
        snapshot.ZIndex = view.ZIndex;

        var frame = view.Frame;
        if (double.IsFinite(frame.X) && double.IsFinite(frame.Y) &&
            double.IsFinite(frame.Width) && double.IsFinite(frame.Height))
        {
            snapshot.Frame = frame.Width >= 0 && frame.Height >= 0
                ? new LayoutRect
                {
                    X = frame.X,
                    Y = frame.Y,
                    Width = frame.Width,
                    Height = frame.Height,
                }
                : null;
        }

        var desired = view.DesiredSize;
        if (double.IsFinite(desired.Width) && double.IsFinite(desired.Height) &&
            desired.Width >= 0 && desired.Height >= 0)
        {
            snapshot.DesiredSize = new LayoutSize { Width = desired.Width, Height = desired.Height };
        }

        snapshot.ExplicitWidth = Requested(view.Width);
        snapshot.ExplicitHeight = Requested(view.Height);
        snapshot.MinimumWidth = Requested(view.MinimumWidth);
        snapshot.MinimumHeight = Requested(view.MinimumHeight);
        snapshot.MaximumWidth = Constraint(view.MaximumWidth);
        snapshot.MaximumHeight = Constraint(view.MaximumHeight);
        snapshot.Margin = ToThickness(view.Margin);

        if (runtime is IPadding padded)
            snapshot.Padding = ToThickness(padded.Padding);

        if (request?.Scope.IncludeNativeElements != false)
            walker?.PopulateLayoutSnapshot(snapshot, runtime, request ?? new LayoutInspectionRequest());
        return snapshot;
    }

    private readonly record struct SubtreeMatch(ElementInfo Node, bool AncestorsVisible);

    private static SubtreeMatch? FindById(IReadOnlyList<ElementInfo> roots, string id)
    {
        foreach (var root in roots)
        {
            var match = FindById(root, id, ancestorsVisible: true);
            if (match is not null)
                return match;
        }
        return null;
    }

    private static SubtreeMatch? FindById(ElementInfo node, string id, bool ancestorsVisible)
    {
        if (string.Equals(node.Id, id, StringComparison.Ordinal))
            return new SubtreeMatch(node, ancestorsVisible);
        if (node.Children is not { Count: > 0 } children)
            return null;

        var effectivelyVisible = IsEffectivelyVisible(node, ancestorsVisible);
        foreach (var child in children)
        {
            var match = FindById(child, id, effectivelyVisible);
            if (match is not null)
                return match;
        }
        return null;
    }

    private static bool IsEffectivelyVisible(ElementInfo node, bool ancestorsVisible)
        => ancestorsVisible &&
           node.IsVisible &&
           (!double.IsFinite(node.Opacity) || node.Opacity > 0);

    /// <summary>MAUI reports "no explicit request" as -1; anything non-finite is unusable.</summary>
    private static double? Requested(double value)
        => double.IsFinite(value) && value >= 0 ? value : null;

    /// <summary>Maximum requests default to <see cref="double.PositiveInfinity"/> when unset.</summary>
    private static double? Constraint(double value)
        => double.IsFinite(value) && value >= 0 ? value : null;

    private static LayoutThickness? ToThickness(Thickness thickness)
    {
        if (!double.IsFinite(thickness.Left) || !double.IsFinite(thickness.Top) ||
            !double.IsFinite(thickness.Right) || !double.IsFinite(thickness.Bottom))
            return null;

        return new LayoutThickness
        {
            Left = thickness.Left,
            Top = thickness.Top,
            Right = thickness.Right,
            Bottom = thickness.Bottom,
        };
    }

    private static LayoutRect? ToRect(BoundsInfo? bounds)
    {
        if (bounds is null) return null;
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            bounds.Width < 0 || bounds.Height < 0)
            return null;

        return new LayoutRect
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
        };
    }
}
