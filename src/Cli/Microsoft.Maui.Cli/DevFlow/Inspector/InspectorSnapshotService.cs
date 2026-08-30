using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

internal static class InspectorSnapshotService
{
    public static InspectorSnapshotResponse Create(
        string snapshotId,
        DateTime capturedAt,
        string screenshotUrl,
        List<ElementInfo> roots,
        int width,
        int height,
        double rootOffsetX,
        double rootOffsetY,
        string? agentId,
        string? appName,
        string? platform) => new()
        {
            SnapshotId = snapshotId,
            Revision = snapshotId,
            CapturedAt = capturedAt,
            ScreenshotUrl = screenshotUrl,
            Roots = roots,
            Target = new InspectorSnapshotTarget
            {
                AgentId = agentId,
                AppName = appName,
                Platform = platform
            },
            Viewport = new InspectorSnapshotViewport
            {
                Width = width,
                Height = height,
                RootOffsetX = rootOffsetX,
                RootOffsetY = rootOffsetY
            }
        };

    public static List<ElementInfo> FilterActiveMatches(
        IEnumerable<ElementInfo> activeRoots,
        IEnumerable<ElementInfo> candidates)
    {
        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        AddIds(activeRoots, activeIds);
        return candidates.Where(candidate => activeIds.Contains(candidate.Id)).ToList();
    }

    public static void TrimDepth(List<ElementInfo> roots, int maxDepth)
    {
        if (maxDepth <= 0) return;
        foreach (var root in roots)
            TrimDepth(root, 1, maxDepth);
    }

    private static void AddIds(IEnumerable<ElementInfo> elements, HashSet<string> ids)
    {
        foreach (var element in elements)
        {
            if (!string.IsNullOrEmpty(element.Id))
                ids.Add(element.Id);
            if (element.Children is { Count: > 0 })
                AddIds(element.Children, ids);
        }
    }

    private static void TrimDepth(ElementInfo element, int depth, int maxDepth)
    {
        if (depth >= maxDepth)
        {
            element.Children = null;
            return;
        }
        if (element.Children is null) return;
        foreach (var child in element.Children)
            TrimDepth(child, depth + 1, maxDepth);
    }
}

internal sealed class InspectorSnapshotResponse
{
    public bool Ok { get; init; } = true;
    public int ProtocolVersion { get; init; } = 1;
    public string Projection { get; init; } = "activeVisual";
    public required string SnapshotId { get; init; }
    public required string Revision { get; init; }
    public DateTime CapturedAt { get; init; }
    public required InspectorSnapshotTarget Target { get; init; }
    public required InspectorSnapshotViewport Viewport { get; init; }
    public required string ScreenshotUrl { get; init; }
    public required List<ElementInfo> Roots { get; init; }
}

internal sealed class InspectorSnapshotTarget
{
    public string? AgentId { get; init; }
    public string? AppName { get; init; }
    public string? Platform { get; init; }
}

internal sealed class InspectorSnapshotViewport
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double RootOffsetX { get; init; }
    public double RootOffsetY { get; init; }
}

internal sealed class InspectorQueryResponse
{
    public bool Ok { get; init; } = true;
    public int ProtocolVersion { get; init; } = 1;
    public string Projection { get; init; } = "activeVisual";
    public required string SnapshotId { get; init; }
    public required string Revision { get; init; }
    public required List<ElementInfo> Elements { get; init; }
}
