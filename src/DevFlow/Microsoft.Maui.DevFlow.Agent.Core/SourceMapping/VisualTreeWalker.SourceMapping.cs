using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class VisualTreeWalker
{
    private readonly ConditionalWeakTable<object, SourceTextEdit> _sourceTextEdits = new();
    private readonly ConditionalWeakTable<object, SourceObservation> _sourceObservations = new();
    private readonly object _sourceObservationsGate = new();

    public IXamlSourceMapProvider? SourceMapProvider { get; set; }

    internal bool HasActiveSourceMaps => SourceMapProvider switch
    {
        null => false,
        XamlSourceMapRegistry registry => registry.HasProviders,
        _ => true
    };

    internal void RetainSourceForTextEdit(object element, ElementInfo? source)
    {
        if (source?.IdentityToken is not { } identity
            || !ReferenceEquals(identity, GetElementIdentity(element))
            || source.SourceFile is not { } file
            || source.SourceLine is not { } line
            || source.SourceColumn is not { } column
            || source.SourceHash is not { Length: > 0 } hash
            || ExtractText(element) is not { } text
            || string.Equals(text, SensitiveValueRedactor.RedactedValue, StringComparison.Ordinal))
        {
            return;
        }

        _sourceTextEdits.Remove(identity);
        _sourceTextEdits.Add(identity, new SourceTextEdit(file, line, column, hash, text));
    }

    internal static Dictionary<string, (string File, int Line, int Column, string? Hash)>
        CollectSourceById(IReadOnlyList<ElementInfo> roots)
    {
        var result = new Dictionary<string, (string, int, int, string?)>(StringComparer.Ordinal);
        foreach (var root in roots)
            CollectSourceByIdCore(root, result);
        return result;
    }

    private static void CollectSourceByIdCore(
        ElementInfo node,
        Dictionary<string, (string, int, int, string?)> result)
    {
        if (node.SourceFile is { } file && node.SourceLine is { } line)
            result[node.Id] = (file, line, node.SourceColumn ?? 0, node.SourceHash);
        if (node.Children is null)
            return;
        foreach (var child in node.Children)
            CollectSourceByIdCore(child, result);
    }

    internal static void ApplySourceById(
        ElementInfo detail,
        IReadOnlyDictionary<string, (string File, int Line, int Column, string? Hash)> sources)
    {
        if (sources.TryGetValue(detail.Id, out var source))
        {
            detail.SourceFile = source.File;
            detail.SourceLine = source.Line;
            detail.SourceColumn = source.Column;
            detail.SourceHash = source.Hash;
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

        var registry = XamlSourceMapRegistry.Instance;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var generation = registry.Generation;
            var observations = new Dictionary<object, SourceObservation?>(ReferenceEqualityComparer.Instance);
            // Incremental XAML Hot Reload can leave existing MAUI source registrations unchanged.
            // A regenerated map needs independent evidence before it can supply a new source hash.
            foreach (var root in roots)
                ApplySourceMapToNode(root, default, parentIdentity: null, generation, observations);
            if (registry.Generation == generation)
            {
                lock (_sourceObservationsGate)
                {
                    if (registry.Generation == generation)
                    {
                        foreach (var (identity, observation) in observations)
                        {
                            if (!_sourceObservations.TryGetValue(identity, out var previous)
                                || previous.Generation <= generation)
                            {
                                if (observation is null)
                                    _sourceObservations.Remove(identity);
                                else
                                    _sourceObservations.AddOrUpdate(identity, observation);
                            }
                        }
                    }
                }
                if (registry.Generation == generation)
                    return;
            }
            foreach (var root in roots)
                ClearSourceTree(root);
        }
    }

    private void ApplySourceMapToNode(
        ElementInfo info,
        XamlSourceContext context,
        object? parentIdentity,
        long generation,
        Dictionary<object, SourceObservation?> observations)
    {
        var childContext = AttachSource(info, context, parentIdentity, generation, observations);
        if (info.Children is null)
            return;

        var alignment = AlignSourceChildren(info.Children, childContext);
        var childParent = !IsSyntheticElement(info) && info.IdentityToken is not null
            ? info.IdentityToken
            : parentIdentity;
        for (var index = 0; index < info.Children.Count; index++)
        {
            ApplySourceMapToNode(
                info.Children[index],
                alignment is not null && alignment[index] >= 0
                    ? childContext.ForChild(alignment[index])
                    : childContext.WithoutMatch(),
                childParent,
                generation,
                observations);
        }
    }

    private XamlSourceContext AttachSource(
        ElementInfo info,
        XamlSourceContext context,
        object? parentIdentity,
        long generation,
        Dictionary<object, SourceObservation?> observations)
    {
        ClearSource(info);
        var childMap = context.Map;
        var childBasePath = context.Path;
        var expectedChildCount = -1;
        var childMatched = false;
        var attached = false;
        XamlSourceMap? selectedMap = null;
        XamlSourceEntry? selectedEntry = null;

        if (context.Matched
            && context.Map is not null
            && context.Map.TryGet(context.Path, out var entry)
            && IdentityMatches(entry, info))
        {
            if (generation == 0 || HasPriorSourceIdentity(info, parentIdentity, context.Map, entry, generation))
            {
                AttachLocation(info, context.Map, entry);
                selectedMap = context.Map;
                selectedEntry = entry;
                attached = true;
            }
            childMap = context.Map;
            childBasePath = context.Path;
            expectedChildCount = entry.ChildCount;
            childMatched = true;
        }

        var ownMap = info.FullType is not null
            ? SourceMapProvider!.GetMap(info.FullType)
            : null;
        if (ownMap is not null && ownMap.TryGet(string.Empty, out var rootEntry))
        {
            if (!attached && (generation == 0 || HasPriorSourceIdentity(info, parentIdentity, ownMap, rootEntry, generation)))
            {
                AttachLocation(info, ownMap, rootEntry);
                selectedMap = ownMap;
                selectedEntry = rootEntry;
            }
            childMap = ownMap;
            childBasePath = string.Empty;
            expectedChildCount = rootEntry.ChildCount;
            childMatched = true;
        }

        if (generation == 0
            && (HasDiagnosticsIdentity(context.Map) || HasDiagnosticsIdentity(ownMap))
            && TryGetMauiSource(info, out var source))
        {
            var sourceMap = MatchesDiagnosticsIdentity(context.Map, source)
                ? context.Map
                : MatchesDiagnosticsIdentity(ownMap, source) ? ownMap : null;
            if (sourceMap is not null)
            {
                info.SourceFile = NormalizeSourceFile(sourceMap.File);
                info.SourceLine = source.Line;
                info.SourceColumn = source.Column;
                info.SourceHash = sourceMap.ContentHash;
                selectedMap = sourceMap;
                selectedEntry = sourceMap.TryGetByLocation(source.Line, source.Column, out var sourceEntry)
                    && (IdentityMatches(sourceEntry, info)
                        || ReferenceEquals(sourceMap, ownMap)
                        && ownMap!.TryGet(string.Empty, out var ownRoot)
                        && ownRoot.Line == source.Line
                        && ownRoot.Column == source.Column)
                        ? sourceEntry
                        : null;

                if (ownMap is null
                    && childMatched
                    && ReferenceEquals(sourceMap, context.Map)
                    && context.Map!.TryGet(context.Path, out var alignedEntry)
                    && (alignedEntry.Line != source.Line || alignedEntry.Column != source.Column))
                {
                    childMatched = false;
                }
            }
            else
            {
                ClearSource(info);
                selectedMap = null;
                selectedEntry = null;
                if (ownMap is null)
                    childMatched = false;
            }
        }

        if (info.IdentityToken is { } identity)
        {
            observations[identity] = selectedMap is not null && selectedEntry is { } selected
                ? new SourceObservation(
                    NormalizeSourceFile(selectedMap.File),
                    selectedMap.DiagnosticsPath,
                    selectedMap.DiagnosticsAssembly,
                    selected.QualifiedName,
                    selected.DeclarationSignature,
                    selected.AutomationId,
                    parentIdentity is null ? null : new WeakReference<object>(parentIdentity),
                    global::Microsoft.Maui.VisualDiagnostics.GetSourceInfo(identity),
                    generation)
                : null;
        }

        return childMap is null
            ? default
            : new XamlSourceContext(
                childMap,
                childBasePath,
                childMatched,
                expectedChildCount);
    }

    // Structural alignment alone cannot prove that a changed declaration still denotes this object.
    private bool HasPriorSourceIdentity(
        ElementInfo info,
        object? parentIdentity,
        XamlSourceMap map,
        XamlSourceEntry entry,
        long generation)
    {
        if (info.IdentityToken is not { } identity
            || !_sourceObservations.TryGetValue(identity, out var previous)
            || (previous.Generation != generation && previous.Generation != generation - 1)
            || !string.Equals(previous.File, NormalizeSourceFile(map.File), StringComparison.Ordinal)
            || !string.Equals(previous.DiagnosticsPath, map.DiagnosticsPath, StringComparison.Ordinal)
            || !string.Equals(previous.DiagnosticsAssembly, map.DiagnosticsAssembly, StringComparison.OrdinalIgnoreCase)
            || previous.QualifiedName is null
            || !string.Equals(previous.QualifiedName, entry.QualifiedName, StringComparison.Ordinal)
            || !ReferenceEquals(previous.MauiSource, global::Microsoft.Maui.VisualDiagnostics.GetSourceInfo(identity))
            || !SameParent(previous.Parent, parentIdentity))
        {
            return false;
        }

        return (previous.DeclarationSignature is not null
                && string.Equals(previous.DeclarationSignature, entry.DeclarationSignature, StringComparison.Ordinal))
            || (previous.AutomationId is not null
                && string.Equals(previous.AutomationId, entry.AutomationId, StringComparison.Ordinal));
    }

    private static bool SameParent(WeakReference<object>? previous, object? current)
        => previous is null
            ? current is null
            : current is not null && previous.TryGetTarget(out var parent) && ReferenceEquals(parent, current);

    private sealed record SourceObservation(
        string File,
        string? DiagnosticsPath,
        string? DiagnosticsAssembly,
        string? QualifiedName,
        string? DeclarationSignature,
        string? AutomationId,
        WeakReference<object>? Parent,
        global::Microsoft.Maui.SourceInfo? MauiSource,
        long Generation);

    private static bool TryGetMauiSource(ElementInfo info, out MauiSourceLocation source)
    {
        source = default;
        if (info.IdentityToken is null || IsSyntheticElement(info))
            return false;

        var diagnostics = global::Microsoft.Maui.VisualDiagnostics.GetSourceInfo(info.IdentityToken);
        if (diagnostics?.SourceUri is not { } uri
            || diagnostics.LineNumber <= 0
            || diagnostics.LinePosition <= 0)
        {
            return false;
        }

        const string assemblyMarker = ";assembly=";
        var value = uri.OriginalString;
        var separator = value.LastIndexOf(assemblyMarker, StringComparison.OrdinalIgnoreCase);
        if (separator <= 0 || separator + assemblyMarker.Length == value.Length)
            return false;

        var path = value[..separator].Replace('\\', '/');
        if (path.StartsWith("/", StringComparison.Ordinal)
            || path.Contains(':')
            || path.Split('/').Any(static part => part.Length == 0 || part == "." || part == ".."))
        {
            return false;
        }

        source = new MauiSourceLocation(
            path,
            value[(separator + assemblyMarker.Length)..],
            diagnostics.LineNumber,
            diagnostics.LinePosition);
        return true;
    }

    private static bool HasDiagnosticsIdentity(XamlSourceMap? map)
        => map?.DiagnosticsPath is { Length: > 0 }
            && map.DiagnosticsAssembly is { Length: > 0 };

    private static bool MatchesDiagnosticsIdentity(XamlSourceMap? map, MauiSourceLocation source)
        => HasDiagnosticsIdentity(map)
            && string.Equals(map!.DiagnosticsPath, source.Path, StringComparison.Ordinal)
            && string.Equals(map.DiagnosticsAssembly, source.Assembly, StringComparison.OrdinalIgnoreCase);

    private static void ClearSourceTree(ElementInfo info)
    {
        ClearSource(info);
        if (info.Children is null)
            return;
        foreach (var child in info.Children)
            ClearSourceTree(child);
    }

    private static void ClearSource(ElementInfo info)
    {
        info.SourceFile = null;
        info.SourceLine = null;
        info.SourceColumn = null;
        info.SourceHash = null;
    }

    private int[]? AlignSourceChildren(
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
                if (CanAttachToAlignedChild(
                    context.Map,
                    expected,
                    expectedPosition,
                    runtime[runtimePosition].Info))
                {
                    alignment[runtime[runtimePosition].OriginalIndex] = expectedPosition;
                }
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

    // Same-type siblings are interchangeable by type alone and code-behind may reorder them, so an
    // entry that shares its static identity is only attached when its literal Text is unique among
    // those siblings and the aligned runtime element still shows it (or its verified agent edit).
    // Anything unverifiable stays unmapped: a wrong line would edit a different element.
    private bool CanAttachToAlignedChild(
        XamlSourceMap map,
        XamlSourceEntry[] expected,
        int index,
        ElementInfo runtime)
    {
        var entry = expected[index];
        var hasSameTypeSibling = false;
        for (var other = 0; other < expected.Length; other++)
        {
            if (other == index || !HasSameStaticIdentity(entry, expected[other]))
                continue;

            hasSameTypeSibling = true;
            // A bound or missing Text can show anything at runtime, including this entry's text.
            if (entry.Text is null
                || expected[other].Text is null
                || string.Equals(entry.Text, expected[other].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (!hasSameTypeSibling)
            return true;

        // Every password Entry reports the redaction marker whatever it holds, so it proves nothing.
        if (string.Equals(runtime.Text, SensitiveValueRedactor.RedactedValue, StringComparison.Ordinal))
            return false;

        if (runtime.IdentityToken is { } identity && _sourceTextEdits.TryGetValue(identity, out var edit))
        {
            // An edited value can equal another sibling's literal; never let it claim that line.
            return string.Equals(edit.File, NormalizeSourceFile(map.File), StringComparison.Ordinal)
                && edit.Line == entry.Line
                && edit.Column == entry.Column
                && string.Equals(edit.Hash, map.ContentHash, StringComparison.Ordinal)
                && (string.Equals(edit.Text, runtime.Text, StringComparison.Ordinal)
                    || string.Equals(entry.Text, runtime.Text, StringComparison.Ordinal));
        }

        return string.Equals(entry.Text, runtime.Text, StringComparison.Ordinal);
    }

    // An unresolved FullTypeName (default xmlns) matches any runtime type with that short name, so
    // it may describe the same element as a resolved one.
    private static bool HasSameStaticIdentity(
        XamlSourceEntry left,
        XamlSourceEntry right)
        => string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal)
            && (string.IsNullOrEmpty(left.FullTypeName)
                || string.IsNullOrEmpty(right.FullTypeName)
                || string.Equals(left.FullTypeName, right.FullTypeName, StringComparison.Ordinal))
            && string.Equals(left.AutomationId, right.AutomationId, StringComparison.Ordinal);

    private static void AttachLocation(
        ElementInfo info,
        XamlSourceMap map,
        XamlSourceEntry entry)
    {
        info.SourceFile = NormalizeSourceFile(map.File);
        info.SourceLine = entry.Line;
        info.SourceColumn = entry.Column;
        info.SourceHash = map.ContentHash;
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

    private sealed record SourceTextEdit(string File, int Line, int Column, string Hash, string Text);

    private readonly record struct MauiSourceLocation(string Path, string Assembly, int Line, int Column);

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

        public XamlSourceContext WithoutMatch()
            => Map is null ? default : new XamlSourceContext(Map, Path, matched: false, expectedChildCount: -1);

        public string ChildPath(int index)
            => Path.Length == 0 ? index.ToString() : $"{Path}/{index}";
    }
}
