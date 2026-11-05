namespace Microsoft.Maui.DevFlow.Agent.Core;

internal readonly record struct LayoutOverlapCandidate(
    LayoutNodeSnapshot First,
    LayoutNodeSnapshot Second,
    LayoutRegionInfo Intersection,
    bool SameParent);

internal static class LayoutSpatialIndex
{
    public static List<LayoutOverlapCandidate> FindOverlaps(
        IReadOnlyList<LayoutNodeSnapshot> nodes)
    {
        var results = new List<LayoutOverlapCandidate>();
        var nodesById = nodes.ToDictionary(
            node => node.Element.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var windowNodes in nodes
            .Where(node => node.VisibleRegion.Area > 0)
            .GroupBy(node => node.WindowId, StringComparer.Ordinal))
        {
            var sorted = windowNodes
                .OrderBy(node => node.VisibleRegion.Bounds.X)
                .ThenBy(node => node.VisibleRegion.Bounds.Y)
                .ThenBy(node => node.TreeOrder)
                .ToList();
            var active = new List<LayoutNodeSnapshot>();

            foreach (var current in sorted)
            {
                var currentLeft = current.VisibleRegion.Bounds.X;
                active.RemoveAll(candidate =>
                    candidate.VisibleRegion.Bounds.X + candidate.VisibleRegion.Bounds.Width
                    <= currentLeft);

                foreach (var candidate in active)
                {
                    if (!YAxisOverlaps(candidate.VisibleRegion.Bounds, current.VisibleRegion.Bounds)
                        || IsAncestor(candidate, current, nodesById)
                        || IsAncestor(current, candidate, nodesById))
                    {
                        continue;
                    }

                    var intersection = LayoutRegionMath.Intersect(
                        candidate.VisibleRegion,
                        current.VisibleRegion);
                    if (intersection.Area <= 0)
                        continue;

                    results.Add(new LayoutOverlapCandidate(
                        candidate,
                        current,
                        intersection,
                        string.Equals(
                            candidate.Element.ParentId,
                            current.Element.ParentId,
                            StringComparison.OrdinalIgnoreCase)));
                }

                active.Add(current);
            }
        }

        return results
            .OrderBy(candidate => candidate.First.TreeOrder)
            .ThenBy(candidate => candidate.Second.TreeOrder)
            .ToList();
    }

    private static bool YAxisOverlaps(
        LayoutRectInfo first,
        LayoutRectInfo second)
        => first.Y < second.Y + second.Height
            && first.Y + first.Height > second.Y;

    private static bool IsAncestor(
        LayoutNodeSnapshot possibleAncestor,
        LayoutNodeSnapshot node,
        IReadOnlyDictionary<string, LayoutNodeSnapshot> nodesById)
    {
        var parentId = node.Element.ParentId;
        while (parentId is not null)
        {
            if (parentId.Equals(
                possibleAncestor.Element.Id,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            parentId = nodesById.TryGetValue(parentId, out var parent)
                ? parent.Element.ParentId
                : null;
        }
        return false;
    }
}
