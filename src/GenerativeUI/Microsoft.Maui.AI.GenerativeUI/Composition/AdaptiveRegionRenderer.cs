using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Registry;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

public sealed record AdaptiveRenderDiff(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Reused,
    IReadOnlyList<string> Moved,
    IReadOnlyList<string> Reconfigured,
    IReadOnlyList<string> Removed);

/// <summary>
/// Reconciles validated layout nodes into app-authored native components inside fixed region hosts.
/// </summary>
public sealed class AdaptiveRegionRenderer(
    GenerativeUiRegistry registry,
    IServiceProvider services)
{
    public AdaptiveRenderDiff Render(
        ComponentLayoutDocument layout,
        AdaptiveSurfaceSession session)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfDisposed();
        if (!string.Equals(layout.Surface, session.Surface, StringComparison.Ordinal))
            throw new InvalidOperationException($"Layout surface '{layout.Surface}' does not match session surface '{session.Surface}'.");

        var previous = session.MountedNodes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var previousNodes = previous.Values.Select(value => value.Node).ToDictionary(node => node.Id, StringComparer.Ordinal);
        var nextNodes = layout.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var previousRoots = (session.CurrentLayout?.Regions ?? [])
            .ToDictionary(region => region.RootNodeId, region => region.Region, StringComparer.Ordinal);
        var nextRoots = layout.Regions.ToDictionary(region => region.RootNodeId, region => region.Region, StringComparer.Ordinal);
        var oldBySemanticIdentity = previous.Values
            .GroupBy(mounted => SemanticIdentity(mounted.Node, previousNodes, previousRoots), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<MountedAdaptiveNode>(group), StringComparer.Ordinal);
        var usedViews = new HashSet<View>();
        var nextMounted = new Dictionary<string, MountedAdaptiveNode>(StringComparer.Ordinal);
        var added = new List<string>();
        var reused = new List<string>();
        var moved = new List<string>();
        var reconfigured = new List<string>();

        session.ClearRegionHosts();
        foreach (var mounted in previous.Values)
            DetachChildren(mounted.Node, mounted.View);

        foreach (var node in layout.Nodes.OrderBy(node => Depth(node, nextNodes)).ThenBy(node => node.Order))
        {
            var mounted = FindReusable(node, previous, oldBySemanticIdentity, nextNodes, nextRoots, usedViews);
            View view;
            if (mounted is null)
            {
                view = CreateView(node, session.StateRoot);
                added.Add(node.Id);
            }
            else
            {
                view = mounted.View;
                usedViews.Add(view);
                reused.Add(node.Id);
                if (!string.Equals(mounted.Node.ParentId, node.ParentId, StringComparison.Ordinal) ||
                    mounted.Node.Order != node.Order)
                {
                    moved.Add(node.Id);
                }

                if (RequiresReconfiguration(mounted.Node, node))
                    reconfigured.Add(node.Id);
            }

            ConfigureView(view, node, session.StateRoot);
            nextMounted[node.Id] = new MountedAdaptiveNode(node, view);
        }

        foreach (var node in layout.Nodes.OrderByDescending(node => Depth(node, nextNodes)).ThenBy(node => node.Order))
            AttachChildren(node, nextMounted, layout.Nodes);

        foreach (var region in layout.Regions)
        {
            if (session.TryGetRegionHost(region.Region, out var host))
                host.SetAdaptiveContent(nextMounted[region.RootNodeId].View);
        }

        var removed = previous.Values
            .Where(mounted => !usedViews.Contains(mounted.View))
            .Select(mounted => mounted.Node.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var mounted in previous.Values.Where(mounted => !usedViews.Contains(mounted.View)))
        {
            if (mounted.View is ICompositionComponent component)
                component.Detach();
        }

        session.MountedNodes.Clear();
        foreach (var pair in nextMounted)
            session.MountedNodes.Add(pair.Key, pair.Value);
        session.CurrentLayout = layout;

        return new AdaptiveRenderDiff(
            added,
            reused,
            moved,
            reconfigured,
            removed);
    }

    public AdaptiveRenderDiff Render(
        AdaptiveCompositionResult composition,
        AdaptiveSurfaceSession session)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsCurrentGeneration(composition.Generation))
            throw new OperationCanceledException("A newer adaptive layout generation superseded this result.");

        return Render(composition.Layout, session);
    }

    private static MountedAdaptiveNode? FindReusable(
        ComponentLayoutNode node,
        IReadOnlyDictionary<string, MountedAdaptiveNode> previous,
        IReadOnlyDictionary<string, Queue<MountedAdaptiveNode>> oldBySemanticIdentity,
        IReadOnlyDictionary<string, ComponentLayoutNode> nextNodes,
        IReadOnlyDictionary<string, string> nextRoots,
        ISet<View> usedViews)
    {
        if (previous.TryGetValue(node.Id, out var exact) &&
            Compatible(exact.Node, node) &&
            !usedViews.Contains(exact.View))
        {
            usedViews.Add(exact.View);
            return exact;
        }

        var identity = SemanticIdentity(node, nextNodes, nextRoots);
        if (!oldBySemanticIdentity.TryGetValue(identity, out var candidates))
            return null;

        while (candidates.Count > 0)
        {
            var candidate = candidates.Dequeue();
            if (!usedViews.Contains(candidate.View) && Compatible(candidate.Node, node))
            {
                usedViews.Add(candidate.View);
                return candidate;
            }
        }

        return null;
    }

    private View CreateView(ComponentLayoutNode node, UiObject stateRoot)
        => node.Kind switch
        {
            ComponentLayoutNodeKind.Stack => new StackLayout(),
            ComponentLayoutNodeKind.Grid => new Grid(),
            ComponentLayoutNodeKind.Tabs => new AdaptiveTabsView(),
            ComponentLayoutNodeKind.Section => new AdaptiveSectionView(),
            ComponentLayoutNodeKind.Component => CreateComponent(node, stateRoot),
            _ => throw new ArgumentOutOfRangeException(nameof(node.Kind)),
        };

    private View CreateComponent(ComponentLayoutNode node, UiObject stateRoot)
    {
        var registration = registry.GetComponent(node.Component!)
            ?? throw new InvalidOperationException($"Component '{node.Component}' is not registered.");
        var instance = ActivatorUtilities.CreateInstance(services, registration.ComponentType);
        if (instance is not View view || instance is not ICompositionComponent)
        {
            throw new InvalidOperationException(
                $"Component '{node.Component}' must be a View implementing {nameof(ICompositionComponent)}.");
        }

        view.BindingContext = ResolveData(stateRoot, node.DataPath!);
        return view;
    }

    private static void ConfigureView(View view, ComponentLayoutNode node, UiObject stateRoot)
    {
        switch (node.Kind)
        {
            case ComponentLayoutNodeKind.Component:
                view.BindingContext = ResolveData(stateRoot, node.DataPath!);
                ((ICompositionComponent)view).ApplyVariant(node.Variant);
                break;
            case ComponentLayoutNodeKind.Stack:
                var stack = (StackLayout)view;
                stack.Orientation = node.Orientation == AdaptiveStackOrientation.Horizontal
                    ? StackOrientation.Horizontal
                    : StackOrientation.Vertical;
                stack.Spacing = 12;
                break;
            case ComponentLayoutNodeKind.Grid:
                ConfigureGrid((Grid)view, node.GridPreset!.Value);
                break;
            case ComponentLayoutNodeKind.Section:
                ((AdaptiveSectionView)view).Title = node.Title;
                break;
        }
    }

    private static void AttachChildren(
        ComponentLayoutNode node,
        IReadOnlyDictionary<string, MountedAdaptiveNode> mounted,
        IReadOnlyList<ComponentLayoutNode> nodes)
    {
        var children = nodes
            .Where(child => string.Equals(child.ParentId, node.Id, StringComparison.Ordinal))
            .OrderBy(child => child.Order)
            .Select(child => mounted[child.Id])
            .ToArray();

        var view = mounted[node.Id].View;
        switch (node.Kind)
        {
            case ComponentLayoutNodeKind.Stack:
                var stack = (StackLayout)view;
                foreach (var child in children)
                    stack.Children.Add(child.View);
                break;
            case ComponentLayoutNodeKind.Grid:
                var grid = (Grid)view;
                for (var index = 0; index < children.Length; index++)
                {
                    var columns = grid.ColumnDefinitions.Count;
                    grid.Add(children[index].View, index % columns, index / columns);
                }
                break;
            case ComponentLayoutNodeKind.Section:
                ((AdaptiveSectionView)view).Body = children.FirstOrDefault()?.View;
                break;
            case ComponentLayoutNodeKind.Tabs:
                ((AdaptiveTabsView)view).SetTabs(children.Select(child =>
                    (child.Node.Title ?? child.Node.Id, child.View)).ToArray());
                break;
        }
    }

    private static void ConfigureGrid(Grid grid, AdaptiveGridPreset preset)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnSpacing = 12;
        grid.RowSpacing = 12;
        switch (preset)
        {
            case AdaptiveGridPreset.SingleColumn:
                grid.ColumnDefinitions.Add(new(GridLength.Star));
                break;
            case AdaptiveGridPreset.TwoEqualColumns:
                grid.ColumnDefinitions.Add(new(GridLength.Star));
                grid.ColumnDefinitions.Add(new(GridLength.Star));
                break;
            case AdaptiveGridPreset.PrimaryWithSidebar:
                grid.ColumnDefinitions.Add(new(new GridLength(2, GridUnitType.Star)));
                grid.ColumnDefinitions.Add(new(GridLength.Star));
                break;
            case AdaptiveGridPreset.SidebarWithPrimary:
                grid.ColumnDefinitions.Add(new(GridLength.Star));
                grid.ColumnDefinitions.Add(new(new GridLength(2, GridUnitType.Star)));
                break;
        }
    }

    private static void DetachChildren(ComponentLayoutNode node, View view)
    {
        switch (node.Kind)
        {
            case ComponentLayoutNodeKind.Stack:
                ((StackLayout)view).Children.Clear();
                break;
            case ComponentLayoutNodeKind.Grid:
                ((Grid)view).Children.Clear();
                break;
            case ComponentLayoutNodeKind.Section:
                ((AdaptiveSectionView)view).Body = null;
                break;
            case ComponentLayoutNodeKind.Tabs:
                ((AdaptiveTabsView)view).ClearTabs();
                break;
        }
    }

    private static UiObject ResolveData(UiObject stateRoot, string dataPath)
        => UiObjectPath.ResolveDotted(stateRoot, dataPath)
           ?? throw new InvalidOperationException($"Adaptive data path '{dataPath}' was not found.");

    private static bool Compatible(ComponentLayoutNode left, ComponentLayoutNode right)
        => left.Kind == right.Kind &&
           (left.Kind != ComponentLayoutNodeKind.Component ||
            string.Equals(left.Component, right.Component, StringComparison.OrdinalIgnoreCase));

    private static bool RequiresReconfiguration(ComponentLayoutNode left, ComponentLayoutNode right)
        => left.Orientation != right.Orientation ||
           left.GridPreset != right.GridPreset ||
           !string.Equals(left.Title, right.Title, StringComparison.Ordinal) ||
           !string.Equals(left.DataPath, right.DataPath, StringComparison.Ordinal) ||
           !string.Equals(left.Variant, right.Variant, StringComparison.OrdinalIgnoreCase);

    private static int Depth(
        ComponentLayoutNode node,
        IReadOnlyDictionary<string, ComponentLayoutNode> nodes)
    {
        var depth = 0;
        var current = node;
        while (current.ParentId is not null && nodes.TryGetValue(current.ParentId, out current!))
            depth++;
        return depth;
    }

    private static string SemanticIdentity(
        ComponentLayoutNode node,
        IReadOnlyDictionary<string, ComponentLayoutNode> nodes,
        IReadOnlyDictionary<string, string> roots)
    {
        var parent = roots.TryGetValue(node.Id, out var region)
            ? $"$region:{region}"
            : node.ParentId ?? "<root>";
        if (node.ParentId is not null && nodes.TryGetValue(node.ParentId, out var parentNode))
        {
            parent = SemanticIdentity(parentNode, nodes, roots);
        }

        return string.Join(
            "|",
            parent,
            node.Kind,
            node.Component?.ToUpperInvariant(),
            node.DataPath,
            string.IsNullOrWhiteSpace(node.Variant) ? "default" : node.Variant.ToUpperInvariant(),
            node.Order);
    }
}
