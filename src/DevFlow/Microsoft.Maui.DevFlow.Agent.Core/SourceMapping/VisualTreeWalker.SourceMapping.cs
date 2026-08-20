using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class VisualTreeWalker
{
    public IXamlSourceMapProvider? SourceMapProvider { get; set; }

    internal bool HasActiveSourceMaps => SourceMapProvider switch
    {
        null => false,
        XamlSourceMapRegistry registry => registry.HasProviders,
        _ => true
    };

    internal static Dictionary<string, (string File, int Line, int Column)>
        CollectSourceById(IReadOnlyList<ElementInfo> roots)
    {
        var result = new Dictionary<string, (string, int, int)>(StringComparer.Ordinal);
        foreach (var root in roots)
            CollectSourceByIdCore(root, result);
        return result;
    }

    private static void CollectSourceByIdCore(
        ElementInfo node,
        Dictionary<string, (string, int, int)> result)
    {
        if (node.SourceFile is { } file && node.SourceLine is { } line)
            result[node.Id] = (file, line, node.SourceColumn ?? 0);
        if (node.Children is null)
            return;
        foreach (var child in node.Children)
            CollectSourceByIdCore(child, result);
    }

    internal static void ApplySourceById(
        ElementInfo detail,
        IReadOnlyDictionary<string, (string File, int Line, int Column)> sources)
    {
        if (sources.TryGetValue(detail.Id, out var source))
        {
            detail.SourceFile = source.File;
            detail.SourceLine = source.Line;
            detail.SourceColumn = source.Column;
        }
        if (detail.Children is null)
            return;
        foreach (var child in detail.Children)
            ApplySourceById(child, sources);
    }

    public void ApplySourceMap(IReadOnlyList<ElementInfo>? roots)
    {
        if (SourceMapProvider is null || roots is null)
            return;
        foreach (var root in roots)
            ApplySourceMapToNode(root, default);
    }

    private void ApplySourceMapToNode(ElementInfo info, XamlSourceContext context)
    {
        var childContext = AttachSource(info, context);
        if (info.Children is null)
            return;

        var alignment = AlignSourceChildren(info.Children, childContext);
        for (var index = 0; index < info.Children.Count; index++)
        {
            ApplySourceMapToNode(
                info.Children[index],
                alignment is not null && alignment[index] >= 0
                    ? childContext.ForChild(alignment[index])
                    : default);
        }
    }

    private XamlSourceContext AttachSource(
        ElementInfo info,
        XamlSourceContext context)
    {
        XamlSourceMap? childMap = null;
        var childBasePath = string.Empty;
        var expectedChildCount = -1;
        var attached = false;

        if (context.Matched
            && context.Map is not null
            && context.Map.TryGet(context.Path, out var entry)
            && IdentityMatches(entry, info))
        {
            AttachLocation(info, context.Map, entry);
            attached = true;
            childMap = context.Map;
            childBasePath = context.Path;
            expectedChildCount = entry.ChildCount;
        }

        var ownMap = info.FullType is not null
            ? SourceMapProvider!.GetMap(info.FullType)
            : null;
        if (ownMap is not null && ownMap.TryGet(string.Empty, out var rootEntry))
        {
            if (!attached)
                AttachLocation(info, ownMap, rootEntry);
            childMap = ownMap;
            childBasePath = string.Empty;
            expectedChildCount = rootEntry.ChildCount;
        }
        else if (!attached)
        {
            return default;
        }

        return childMap is null
            ? default
            : new XamlSourceContext(
                childMap,
                childBasePath,
                matched: true,
                expectedChildCount);
    }

    private static int[]? AlignSourceChildren(
        IReadOnlyList<ElementInfo> children,
        XamlSourceContext context)
    {
        if (!context.Matched || context.Map is null || context.ExpectedChildCount < 0)
            return null;

        var expected = new XamlSourceEntry[context.ExpectedChildCount];
        for (var index = 0; index < expected.Length; index++)
        {
            if (!context.Map.TryGet(context.ChildPath(index), out expected[index]))
                return null;
        }

        var ambiguousExpected = new bool[expected.Length];
        for (var first = 0; first < expected.Length; first++)
        {
            for (var second = first + 1; second < expected.Length; second++)
            {
                if (!HasSameStaticIdentity(expected[first], expected[second]))
                    continue;
                ambiguousExpected[first] = true;
                ambiguousExpected[second] = true;
            }
        }

        var runtime = new List<(int OriginalIndex, ElementInfo Info)>();
        for (var index = 0; index < children.Count; index++)
        {
            if (!IsSyntheticElement(children[index]))
                runtime.Add((index, children[index]));
        }

        var ways = new byte[expected.Length + 1, runtime.Count + 1];
        for (var runtimeIndex = 0; runtimeIndex <= runtime.Count; runtimeIndex++)
            ways[expected.Length, runtimeIndex] = 1;

        for (var expectedIndex = expected.Length - 1; expectedIndex >= 0; expectedIndex--)
        {
            for (var runtimeIndex = runtime.Count - 1; runtimeIndex >= 0; runtimeIndex--)
            {
                var count = ways[expectedIndex, runtimeIndex + 1];
                if (IdentityMatches(expected[expectedIndex], runtime[runtimeIndex].Info))
                {
                    count = (byte)Math.Min(
                        2,
                        count + ways[expectedIndex + 1, runtimeIndex + 1]);
                }
                ways[expectedIndex, runtimeIndex] = count;
            }
        }

        if (ways[0, 0] != 1)
            return null;

        var alignment = Enumerable.Repeat(-1, children.Count).ToArray();
        var expectedPosition = 0;
        var runtimePosition = 0;
        while (expectedPosition < expected.Length)
        {
            if (runtimePosition >= runtime.Count)
                return null;

            var skipWays = ways[expectedPosition, runtimePosition + 1];
            var takeWays = IdentityMatches(
                expected[expectedPosition],
                runtime[runtimePosition].Info)
                ? ways[expectedPosition + 1, runtimePosition + 1]
                : 0;

            if (takeWays == 1 && skipWays == 0)
            {
                if (!ambiguousExpected[expectedPosition])
                    alignment[runtime[runtimePosition].OriginalIndex] = expectedPosition;
                expectedPosition++;
                runtimePosition++;
            }
            else if (skipWays == 1 && takeWays == 0)
            {
                runtimePosition++;
            }
            else
            {
                return null;
            }
        }

        return alignment;
    }

    private static bool HasSameStaticIdentity(
        XamlSourceEntry left,
        XamlSourceEntry right)
        => string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal)
            && string.Equals(left.FullTypeName, right.FullTypeName, StringComparison.Ordinal)
            && string.Equals(left.AutomationId, right.AutomationId, StringComparison.Ordinal);

    private static void AttachLocation(
        ElementInfo info,
        XamlSourceMap map,
        XamlSourceEntry entry)
    {
        info.SourceFile = NormalizeSourceFile(map.File);
        info.SourceLine = entry.Line;
        info.SourceColumn = entry.Column;
    }

    private static string NormalizeSourceFile(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (!Path.IsPathRooted(path))
            return normalized;

        var segments = normalized.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        return string.Join(
            "/",
            segments.Skip(Math.Max(0, segments.Length - 3)));
    }

    private static bool IdentityMatches(
        XamlSourceEntry entry,
        ElementInfo info)
    {
        if (entry.FullTypeName is { Length: > 0 } fullType)
        {
            if (!string.Equals(fullType, info.FullType, StringComparison.Ordinal))
                return false;
        }
        else if (!string.Equals(entry.TypeName, info.Type, StringComparison.Ordinal))
        {
            return false;
        }

        return string.IsNullOrEmpty(entry.AutomationId)
            || string.Equals(entry.AutomationId, info.AutomationId, StringComparison.Ordinal);
    }

    private static bool IsSyntheticElement(ElementInfo info)
        => info.FullType.StartsWith(
            "Microsoft.Maui.DevFlow.Agent.Core.",
            StringComparison.Ordinal);

    private readonly struct XamlSourceContext
    {
        public XamlSourceContext(
            XamlSourceMap? map,
            string path,
            bool matched,
            int expectedChildCount)
        {
            Map = map;
            Path = path;
            Matched = matched;
            ExpectedChildCount = expectedChildCount;
        }

        public XamlSourceMap? Map { get; }
        public string Path { get; }
        public bool Matched { get; }
        public int ExpectedChildCount { get; }

        public XamlSourceContext ForChild(int index)
            => Map is null || !Matched
                ? default
                : new XamlSourceContext(
                    Map,
                    ChildPath(index),
                    matched: true,
                    expectedChildCount: -1);

        public string ChildPath(int index)
            => Path.Length == 0 ? index.ToString() : $"{Path}/{index}";
    }
}
