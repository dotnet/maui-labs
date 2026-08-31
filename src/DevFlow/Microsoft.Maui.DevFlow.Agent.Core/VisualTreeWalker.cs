using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core.Css;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Walks the MAUI visual tree and produces ElementInfo representations.
/// Uses IVisualTreeElement.GetVisualChildren() for tree traversal.
/// IDs are derived from element properties (Element.Id, AutomationId, platform stamps)
/// rather than cached maps — the tree is walked fresh each time.
/// </summary>
public class VisualTreeWalker
{
    // Per-walk state — fully rebuilt on each WalkTree call
    private readonly HashSet<string> _usedIds = new();
    private readonly Dictionary<Guid, string> _elementIdToExternalId = new();
    private readonly ConditionalWeakTable<IVisualTreeElement, GeneratedElementId> _objectToExternalId = new();
    private readonly ConcurrentDictionary<string, (BoundsInfo Bounds, object Marker)> _syntheticBounds = new();
    private readonly Dictionary<string, object> _walkElements = new(StringComparer.Ordinal);
    private readonly HashSet<Page> _activePages = new(ReferenceEqualityComparer.Instance);
    private int _walkMaxElements;
    private int _walkElementCount;

    /// <summary>
    /// Opt-in capture of the <c>id → runtime object</c> map for the current walk. Off by default:
    /// it holds strong references to live MAUI elements, so a caller must enable it, read
    /// <see cref="WalkElements"/> on the same UI-thread pass, and call
    /// <see cref="ClearWalkElements"/> when finished.
    /// </summary>
    /// <remarks>
    /// The map exists so a diagnostic can read managed layout state for a whole tree from a single
    /// walk instead of calling <see cref="GetElementById"/> once per element (which re-walks the
    /// tree every time). It is never serialized and never leaves the agent process.
    /// </remarks>
    internal bool CaptureWalkElements { get; set; }

    /// <summary>
    /// Restricts the current walk to layout-safe structural data. This mode deliberately skips
    /// text, values, item sources, Comet property dictionaries, and platform property probes.
    /// </summary>
    internal bool CaptureLayoutOnly { get; set; }

    /// <summary>
    /// Runtime objects recorded during the most recent walk, keyed by the id in the returned
    /// <see cref="ElementInfo"/> tree. Empty unless <see cref="CaptureWalkElements"/> was set
    /// before the walk. Cleared at the start of every walk.
    /// </summary>
    internal IReadOnlyDictionary<string, object> WalkElements => _walkElements;

    /// <summary>Drops the captured runtime references so live elements are not retained.</summary>
    internal void ClearWalkElements() => _walkElements.Clear();

    internal void PopulateLayoutSnapshot(
        LayoutDiagnostics.LayoutElementSnapshot snapshot,
        object runtimeElement,
        LayoutDiagnostics.LayoutInspectionRequest request)
        => PopulatePlatformLayoutSnapshot(snapshot, runtimeElement, request);

    protected virtual void PopulatePlatformLayoutSnapshot(
        LayoutDiagnostics.LayoutElementSnapshot snapshot,
        object runtimeElement,
        LayoutDiagnostics.LayoutInspectionRequest request)
    {
    }

    internal bool WalkWasTruncated { get; private set; }

    /// <summary>
    /// Marker object representing the navigation back button in Shell or NavigationPage.
    /// </summary>
    public class BackButtonMarker
    {
        public INavigation Navigation { get; init; } = null!;
        public string Title { get; init; } = "Back";
    }

    /// <summary>
    /// Looks up an element by ID by walking the tree fresh.
    /// Returns IVisualTreeElement, ToolbarItem, or other mapped objects.
    /// </summary>
    public object? GetElementById(string id, Application? app)
        => GetElementById(id, app, windowIndex: null);

    internal object? GetElementById(string id, Application? app, int? windowIndex)
    {
        if (app == null || string.IsNullOrEmpty(id)) return null;

        // Walk tree fresh, searching for matching ID
        _usedIds.Clear();
        _elementIdToExternalId.Clear();
        _objectToExternalId.Clear();
        _syntheticBounds.Clear();
        _walkElements.Clear();
        _activePages.Clear();
        _walkMaxElements = 0;
        _walkElementCount = 0;
        WalkWasTruncated = false;

        if (windowIndex is not null)
        {
            if (windowIndex.Value < 0 || windowIndex.Value >= app.Windows.Count)
                return null;
            return app.Windows[windowIndex.Value] is IVisualTreeElement windowElement
                ? FindByIdRecursive(windowElement, id, null)
                : null;
        }

        if (app is not IVisualTreeElement appElement) return null;
        foreach (var child in GetTraversalChildren(appElement))
        {
            var result = FindByIdRecursive(child, id, null);
            if (result != null) return result;
        }
        return null;
    }

    internal List<ElementInfo> WalkSubtree(
        Application app,
        string rootElementId,
        int maxDepth = 0,
        int maxElements = 0,
        int? windowIndex = null)
    {
        var runtime = GetElementById(rootElementId, app, windowIndex);
        if (runtime is not IVisualTreeElement root)
            return [];

        _walkElements.Clear();
        _activePages.Clear();
        _walkMaxElements = Math.Max(0, maxElements);
        _walkElementCount = 0;
        WalkWasTruncated = false;
        PopulateActivePages(app, windowIndex);
        try
        {
            var info = WalkElement(root, null, 1, maxDepth);
            if (info is null)
                return [];
            info.IsVisible &= AncestorsAreEffectivelyVisible(root);
            var results = new List<ElementInfo> { info };
            ApplySourceMap(results);
            return results;
        }
        finally
        {
            _walkMaxElements = 0;
            _activePages.Clear();
        }
    }

    internal List<ElementInfo> WalkRoot(
        IVisualTreeElement root,
        int maxDepth = 0,
        int maxElements = 0)
    {
        _usedIds.Clear();
        _elementIdToExternalId.Clear();
        _objectToExternalId.Clear();
        _syntheticBounds.Clear();
        _walkElements.Clear();
        _activePages.Clear();
        _walkMaxElements = Math.Max(0, maxElements);
        _walkElementCount = 0;
        WalkWasTruncated = false;
        try
        {
            var info = WalkElement(root, null, 1, maxDepth);
            if (info is null)
                return [];
            info.IsVisible &= AncestorsAreEffectivelyVisible(root);
            var results = new List<ElementInfo> { info };
            ApplySourceMap(results);
            return results;
        }
        finally
        {
            _walkMaxElements = 0;
            _activePages.Clear();
        }
    }

    private void PopulateActivePages(Application app, int? windowIndex)
    {
        _activePages.Clear();

        if (windowIndex is not null)
        {
            if (windowIndex.Value >= 0 && windowIndex.Value < app.Windows.Count)
            {
                foreach (var activePage in ActivePageResolver.ResolveVisiblePages(app.Windows[windowIndex.Value]))
                    _activePages.Add(activePage);
            }
            return;
        }

        var windows = app.Windows.Count > 0
            ? app.Windows
            : app is IVisualTreeElement appElement
                ? GetTraversalChildren(appElement).OfType<Window>().ToList()
                : [];
        foreach (var window in windows)
        {
            foreach (var activePage in ActivePageResolver.ResolveVisiblePages(window))
                _activePages.Add(activePage);
        }
    }

    private static bool AncestorsAreEffectivelyVisible(IVisualTreeElement element)
    {
        for (var parent = element.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is VisualElement visual &&
                (!visual.IsVisible || (double.IsFinite(visual.Opacity) && visual.Opacity <= 0)))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns diagnostic info about the walker state.
    /// </summary>
    public string GetDiagnostics()
    {
        return $"SyntheticBounds: {_syntheticBounds.Count}, ElementIds: {_elementIdToExternalId.Count}";
    }

    /// <summary>
    /// Reverse lookup: gets the ID for a visual tree element using Element.Id.
    /// Uses the per-walk lookup populated by the most recent WalkTree call.
    /// </summary>
    public string? GetIdForElement(IVisualTreeElement element)
    {
        if (element is Element el && _elementIdToExternalId.TryGetValue(el.Id, out var id))
            return id;
        return null;
    }

    /// <summary>
    /// Hit tests synthetic elements against a point. Returns matching synthetics with their bounds.
    /// </summary>
    public List<(string Id, object Marker, BoundsInfo Bounds)> HitTestSynthetics(double x, double y)
    {
        var hits = new List<(string, object, BoundsInfo)>();
        foreach (var kvp in _syntheticBounds)
        {
            var (b, marker) = kvp.Value;
            if (x >= b.X && x <= b.X + b.Width && y >= b.Y && y <= b.Y + b.Height)
                hits.Add((kvp.Key, marker, b));
        }
        // Sort by area (smallest first) — more specific elements on top
        hits.Sort((a, b) => (a.Item3.Width * a.Item3.Height).CompareTo(b.Item3.Width * b.Item3.Height));
        return hits;
    }

    /// <summary>
    /// Performs bounds-based hit testing by walking the visual tree and checking
    /// window bounds for each element. This supplements GetVisualTreeElements
    /// which may not traverse into all containers on some platforms.
    /// Returns elements whose window bounds contain the given point.
    /// </summary>
    public List<VisualElement> HitTestByBounds(double x, double y, Application app, int? windowIndex = null)
    {
        var hits = new List<VisualElement>();
        var window = windowIndex.HasValue && windowIndex.Value < app.Windows.Count
            ? app.Windows[windowIndex.Value]
            : app.Windows.FirstOrDefault();
        if (window?.Page == null) return hits;

        HitTestByBoundsRecursive(window.Page, x, y, hits);

        // Also check modal pages
        var modalStack = window.Navigation?.ModalStack;
        if (modalStack != null)
        {
            foreach (var modal in modalStack)
                HitTestByBoundsRecursive(modal, x, y, hits);
        }

        return hits;
    }

    private void HitTestByBoundsRecursive(Element element, double x, double y, List<VisualElement> hits)
    {
        if (element is VisualElement ve && ve.IsVisible)
        {
            var wb = ResolveWindowBounds(ve);
            if (wb != null && wb.Width > 0 && wb.Height > 0 &&
                x >= wb.X && x <= wb.X + wb.Width &&
                y >= wb.Y && y <= wb.Y + wb.Height)
            {
                hits.Add(ve);
            }
        }

        if (element is IVisualTreeElement vte)
        {
            foreach (var child in GetTraversalChildren(vte))
            {
                if (child is Element childEl)
                    HitTestByBoundsRecursive(childEl, x, y, hits);
            }
        }
    }

    /// <summary>
    /// Gets the display type name for a synthetic marker object.
    /// </summary>
    public string GetSyntheticTypeName(object marker) => marker switch
    {
        NavBarTitleMarker => "NavBarTitle",
        FlyoutButtonMarker => "FlyoutButton",
        ShellFlyoutItemMarker => "FlyoutItem",
        ShellTabMarker => "Tab",
        SearchHandlerMarker => "SearchHandler",
        FlyoutToggleMarker => "FlyoutToggle",
        TabbedPageTabMarker => "Tab",
        BackButtonMarker => "BackButton",
        ToolbarItem => "ToolbarItem",
        _ => marker.GetType().Name.Replace("Marker", "")
    };

    /// <summary>
    /// Gets the display text for a synthetic marker object.
    /// </summary>
    public string? GetSyntheticText(object marker) => marker switch
    {
        NavBarTitleMarker m => m.Title,
        FlyoutButtonMarker => "☰",
        ShellFlyoutItemMarker m => m.Item is BaseShellItem bsi ? bsi.Title : null,
        ShellTabMarker m => m.Section.Title,
        SearchHandlerMarker m => m.Handler.Placeholder ?? "Search",
        FlyoutToggleMarker => "☰",
        TabbedPageTabMarker m => m.Page.Title,
        BackButtonMarker m => m.Title,
        ToolbarItem t => t.Text,
        _ => null
    };

    /// <summary>
    /// Builds an ElementInfo for a synthetic marker element (for /api/element endpoint).
    /// </summary>
    public ElementInfo? BuildSyntheticElementInfo(string id, object marker)
    {
        var info = new ElementInfo
        {
            Id = id,
            Type = GetSyntheticTypeName(marker),
            FullType = $"Microsoft.Maui.DevFlow.Agent.Core.{GetSyntheticTypeName(marker)}",
            Text = GetSyntheticText(marker),
            IsVisible = true,
            IsEnabled = true,
        };

        // Set bounds if available (synthetic bounds are already window-absolute)
        if (_syntheticBounds.TryGetValue(id, out var entry))
        {
            info.Bounds = entry.Bounds;
            info.WindowBounds = entry.Bounds;
        }

        // Enrich with MAUI properties
        PopulateSyntheticProperties(info, marker);

        // Platform-specific native info
        PopulateSyntheticNativeInfo(info, marker);

        return info;
    }

    /// <summary>
    /// Populates MAUI-level properties on a synthetic element.
    /// </summary>
    private static void PopulateSyntheticProperties(ElementInfo info, object marker)
    {
        var props = new Dictionary<string, string?>();

        switch (marker)
        {
            case NavBarTitleMarker m:
                props["title"] = m.Title;
                info.IsEnabled = false; // not interactive
                break;
            case FlyoutButtonMarker m:
                props["flyoutBehavior"] = m.Shell.FlyoutBehavior.ToString();
                props["flyoutIsPresented"] = m.Shell.FlyoutIsPresented.ToString();
                break;
            case ShellFlyoutItemMarker m when m.Item is BaseShellItem bsi:
                props["title"] = bsi.Title;
                props["route"] = bsi.Route;
                props["flyoutItemIsVisible"] = bsi.FlyoutItemIsVisible.ToString();
                if (bsi.AutomationId != null) info.AutomationId = bsi.AutomationId;
                info.IsFocused = m.Shell.CurrentItem == m.Item;
                if (bsi.Icon is FileImageSource fis1) props["icon"] = fis1.File;
                if (bsi.FlyoutIcon is FileImageSource fis2) props["flyoutIcon"] = fis2.File;
                break;
            case ShellTabMarker m:
                props["title"] = m.Section.Title;
                props["route"] = m.Section.Route;
                if (m.Section.AutomationId != null) info.AutomationId = m.Section.AutomationId;
                info.IsFocused = m.Shell.CurrentItem?.CurrentItem == m.Section;
                if (m.Section.Icon is FileImageSource fis3) props["icon"] = fis3.File;
                break;
            case SearchHandlerMarker m:
                props["placeholder"] = m.Handler.Placeholder;
                props["query"] = m.Handler.Query;
                props["isSearchEnabled"] = m.Handler.IsSearchEnabled.ToString();
                props["searchBoxVisibility"] = m.Handler.SearchBoxVisibility.ToString();
                info.IsVisible = m.Handler.SearchBoxVisibility != SearchBoxVisibility.Hidden;
                info.IsEnabled = m.Handler.IsSearchEnabled;
                break;
            case FlyoutToggleMarker m:
                props["isPresented"] = m.FlyoutPage.IsPresented.ToString();
                break;
            case TabbedPageTabMarker m:
                props["title"] = m.Page.Title;
                if (m.Page.AutomationId != null) info.AutomationId = m.Page.AutomationId;
                info.IsFocused = m.TabbedPage.CurrentPage == m.Page;
                if (m.Page.IconImageSource is FileImageSource fis4) props["icon"] = fis4.File;
                break;
            case BackButtonMarker m:
                props["title"] = m.Title;
                break;
        }

        if (props.Count > 0)
            info.NativeProperties = props;
    }

    /// <summary>
    /// Override in platform-specific subclasses to add native view info to synthetic elements.
    /// </summary>
    protected virtual void PopulateSyntheticNativeInfo(ElementInfo info, object marker) { }

    /// <summary>
    /// Override in platform-specific subclasses to resolve bounds for synthetic elements
    /// from platform-native views (UINavigationBar, UITabBar, Toolbar, etc.).
    /// </summary>
    protected virtual BoundsInfo? ResolveSyntheticBounds(object marker) => null;

    /// <summary>
    /// Override in platform-specific subclasses to resolve window-absolute bounds
    /// for a VisualElement using native coordinate APIs (e.g. UIView.ConvertRectToView,
    /// View.GetLocationOnScreen, Widget.ComputeBounds).
    /// </summary>
    protected virtual BoundsInfo? ResolveWindowBounds(VisualElement ve) => null;

    /// <summary>
    /// Public accessor for ResolveWindowBounds, used by DevFlowAgentService for hit test results.
    /// </summary>
    public BoundsInfo? ResolveWindowBoundsPublic(VisualElement ve) => ResolveWindowBounds(ve);

    /// <summary>
    /// Walks the visual tree starting from the application's windows.
    /// When windowIndex is null, walks all windows. Otherwise walks only the specified window.
    /// </summary>
    public List<ElementInfo> WalkTree(
        Application app,
        int maxDepth = 0,
        int? windowIndex = null)
        => WalkTree(app, maxDepth, windowIndex, maxElements: 0);

    internal List<ElementInfo> WalkTree(
        Application app,
        int maxDepth,
        int? windowIndex,
        int maxElements)
    {
        _usedIds.Clear();
        _elementIdToExternalId.Clear();
        _objectToExternalId.Clear();
        _syntheticBounds.Clear();
        _walkElements.Clear();
        _activePages.Clear();
        _walkMaxElements = Math.Max(0, maxElements);
        _walkElementCount = 0;
        WalkWasTruncated = false;
        PopulateActivePages(app, windowIndex);
        try
        {
            var results = new List<ElementInfo>();
            if (app is not IVisualTreeElement appElement)
                return results;

            if (windowIndex != null)
            {
                if (windowIndex.Value < 0 || windowIndex.Value >= app.Windows.Count)
                    return results;
                var window = app.Windows[windowIndex.Value];
                if (window is IVisualTreeElement windowElement)
                {
                    var info = WalkElement(windowElement, null, 1, maxDepth);
                    if (info != null)
                        results.Add(info);
                }
                ApplySourceMap(results);
                return results;
            }

            foreach (var child in GetTraversalChildren(appElement))
            {
                var info = WalkElement(child, null, 1, maxDepth);
                if (info != null)
                    results.Add(info);
                if (WalkWasTruncated)
                    break;
            }

            ApplySourceMap(results);
            return results;
        }
        finally
        {
            _walkMaxElements = 0;
            _activePages.Clear();
        }
    }

    /// <summary>
    /// Optional provider of XAML source maps. When set, <see cref="WalkTree"/> attaches
    /// sourceFile/sourceLine/sourceColumn to statically-declared elements. When null (default),
    /// source mapping is skipped entirely (no behavior change).
    /// </summary>
    public IXamlSourceMapProvider? SourceMapProvider { get; set; }

    /// <summary>
    /// True when source mapping can actually produce results — a provider is set and, if it is the
    /// shared <see cref="XamlSourceMapRegistry"/>, at least one provider has registered. Lets callers
    /// skip a full mapped walk in the common (no maps) case.
    /// </summary>
    internal bool HasActiveSourceMaps => SourceMapProvider switch
    {
        null => false,
        XamlSourceMapRegistry registry => registry.HasProviders,
        _ => true,
    };

    /// <summary>
    /// Collects <c>id → (file, line, column, hash)</c> from a source-mapped ElementInfo tree (as
    /// produced by <see cref="WalkTree"/>). Used to transfer source onto a separately-built,
    /// depth-limited element-detail subtree whose nodes lack the parent-path context to map directly.
    /// </summary>
    internal static Dictionary<string, (string File, int Line, int Column, string? Hash)> CollectSourceById(IReadOnlyList<ElementInfo> roots)
    {
        var map = new Dictionary<string, (string, int, int, string?)>(StringComparer.Ordinal);
        foreach (var root in roots)
            CollectSourceByIdCore(root, map);
        return map;
    }

    private static void CollectSourceByIdCore(ElementInfo node, Dictionary<string, (string, int, int, string?)> map)
    {
        if (node.Id is { } id && node.SourceFile is { } file && node.SourceLine is { } line)
            map[id] = (file, line, node.SourceColumn ?? 0, node.SourceHash);
        if (node.Children is { } children)
            foreach (var child in children)
                CollectSourceByIdCore(child, map);
    }

    /// <summary>Copies collected source locations onto a detail subtree, matching nodes by id.</summary>
    internal static void ApplySourceById(ElementInfo detail, IReadOnlyDictionary<string, (string File, int Line, int Column, string? Hash)> sources)
    {
        if (detail.Id is { } id && sources.TryGetValue(id, out var s))
        {
            detail.SourceFile = s.File;
            detail.SourceLine = s.Line;
            detail.SourceColumn = s.Column;
            detail.SourceHash = s.Hash;
        }
        if (detail.Children is { } children)
            foreach (var child in children)
                ApplySourceById(child, sources);
    }

    /// <summary>
    /// Identity-checked post-pass that attaches XAML source locations to a built ElementInfo tree.
    /// Source is attached only where mapped content children have exactly one order-preserving
    /// alignment with the runtime children. Alignment uses the resolved full CLR type (from a
    /// <c>clr-namespace</c> xmlns) or short type name, plus a sibling-unique AutomationId when the
    /// XAML declared one (<see cref="IdentityMatches"/>). Runtime-only children such as toolbar
    /// items, applied shapes, and DevFlow synthetics are skipped without shifting XAML paths, while
    /// ambiguous same-type insertions, removals, reorders, and identity mismatches resolve to null.
    /// <para>
    /// Siblings with identical static identities are left unmapped because runtime order cannot
    /// prove which declaration they came from. Add sibling-unique AutomationIds for precise
    /// tooling. Short-name type collisions are only resolved for
    /// <c>clr-namespace</c> custom controls (not <c>XmlnsDefinition</c>/URI namespaces).
    /// </para>
    /// </summary>
    public void ApplySourceMap(IReadOnlyList<ElementInfo>? roots)
    {
        if (SourceMapProvider is null || roots is null) return;
        foreach (var root in roots)
            ApplySourceMapToNode(root, default);
    }

    private void ApplySourceMapToNode(ElementInfo info, XamlSourceContext ctx)
    {
        var childContext = AttachSource(info, ctx);
        if (info.Children is null) return;

        var alignment = AlignSourceChildren(info.Children, childContext);
        for (var i = 0; i < info.Children.Count; i++)
        {
            var child = info.Children[i];
            ApplySourceMapToNode(
                child,
                alignment is not null && alignment[i] >= 0
                    ? childContext.ForChild(alignment[i])
                    : default);
        }
    }

    private XamlSourceContext AttachSource(ElementInfo info, XamlSourceContext ctx)
    {
        XamlSourceMap? childMap = null;
        var childBasePath = string.Empty;
        var expectedChildCount = -1;

        // 1) "Usage" location from the parent XAML at this path — only when the element's identity
        // (resolved CLR type and/or unique AutomationId) matches what was declared there.
        var attached = false;
        if (ctx.Matched && ctx.Map is not null && ctx.Map.TryGet(ctx.Path, out var entry)
            && IdentityMatches(entry, info))
        {
            AttachLocation(info, ctx.Map, entry);
            attached = true;
            childMap = ctx.Map;
            childBasePath = ctx.Path;
            expectedChildCount = entry.ChildCount;
        }

        // 2) If this element is itself a XAML file root, its descendants use that file's map.
        // Keyed by the element's runtime FullType, so a returned map means the element genuinely IS
        // that type: attaching its own root definition is precise even when the parent "usage" path
        // above did not match. Hence SourceFile means "usage line in the parent XAML" when (1)
        // attached, otherwise "definition line of this element's own XAML" — both precise, never a
        // guessed line.
        var ownMap = info.FullType is not null ? SourceMapProvider!.GetMap(info.FullType) : null;
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
            // No source for this element and no own map → break the chain (subtree stays null).
            return default;
        }

        if (childMap is null)
            return default;

        return new XamlSourceContext(childMap, childBasePath, matched: true, expectedChildCount);
    }

    /// <summary>
    /// Finds the unique order-preserving alignment from parsed XAML content children to runtime
    /// children. Unmatched runtime children are framework extras and receive no parent source
    /// context. Zero or multiple alignments are rejected so source is precise or null.
    /// </summary>
    private static int[]? AlignSourceChildren(IReadOnlyList<ElementInfo> children, XamlSourceContext ctx)
    {
        if (!ctx.Matched || ctx.Map is null || ctx.ExpectedChildCount < 0)
            return null;

        var expected = new XamlSourceEntry[ctx.ExpectedChildCount];
        for (var i = 0; i < expected.Length; i++)
        {
            if (!ctx.Map.TryGet(ctx.ChildPath(i), out expected[i]))
                return null;
        }

        var ambiguousExpected = new bool[expected.Length];
        for (var i = 0; i < expected.Length; i++)
        {
            for (var j = i + 1; j < expected.Length; j++)
            {
                if (!HasSameStaticIdentity(expected[i], expected[j]))
                    continue;

                ambiguousExpected[i] = true;
                ambiguousExpected[j] = true;
            }
        }

        var runtime = new List<(int OriginalIndex, ElementInfo Info)>();
        for (var i = 0; i < children.Count; i++)
        {
            if (!IsSyntheticElement(children[i]))
                runtime.Add((i, children[i]));
        }

        // Count order-preserving embeddings, saturating at 2 because only uniqueness matters.
        var ways = new byte[expected.Length + 1, runtime.Count + 1];
        for (var j = 0; j <= runtime.Count; j++)
            ways[expected.Length, j] = 1;

        for (var i = expected.Length - 1; i >= 0; i--)
        {
            for (var j = runtime.Count - 1; j >= 0; j--)
            {
                var count = ways[i, j + 1];
                if (IdentityMatches(expected[i], runtime[j].Info))
                    count = (byte)Math.Min(2, count + ways[i + 1, j + 1]);
                ways[i, j] = count;
            }
        }

        if (ways[0, 0] != 1)
            return null;

        var alignment = Enumerable.Repeat(-1, children.Count).ToArray();
        var expectedIndex = 0;
        var runtimeIndex = 0;
        while (expectedIndex < expected.Length)
        {
            if (runtimeIndex >= runtime.Count)
                return null;

            var skipWays = ways[expectedIndex, runtimeIndex + 1];
            var takeWays = IdentityMatches(expected[expectedIndex], runtime[runtimeIndex].Info)
                ? ways[expectedIndex + 1, runtimeIndex + 1]
                : 0;

            if (takeWays == 1 && skipWays == 0)
            {
                if (!ambiguousExpected[expectedIndex])
                    alignment[runtime[runtimeIndex].OriginalIndex] = expectedIndex;
                expectedIndex++;
                runtimeIndex++;
            }
            else if (skipWays == 1 && takeWays == 0)
            {
                runtimeIndex++;
            }
            else
            {
                return null;
            }
        }

        return alignment;
    }

    private static bool HasSameStaticIdentity(XamlSourceEntry left, XamlSourceEntry right)
        => string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal)
            && string.Equals(left.FullTypeName, right.FullTypeName, StringComparison.Ordinal)
            && string.Equals(left.AutomationId, right.AutomationId, StringComparison.Ordinal);

    private static void AttachLocation(ElementInfo info, XamlSourceMap map, XamlSourceEntry entry)
    {
        info.SourceFile = map.File;
        info.SourceLine = entry.Line;
        info.SourceColumn = entry.Column;
        info.SourceHash = map.ContentHash;
        info.SourceConfidence = "mapped";
    }

    /// <summary>
    /// Matches a parsed entry to a runtime element by identity: the resolved full CLR type when the
    /// XAML namespace gave one (closing short-name collisions like <c>maui:Label</c> vs
    /// <c>local:Label</c>), otherwise the short type name; PLUS, when the entry carries a
    /// (sibling-unique) AutomationId, the runtime element must carry the same one. The AutomationId
    /// check makes a same-type, same-count sibling reorder resolve to null rather than a wrong line.
    /// </summary>
    private static bool IdentityMatches(XamlSourceEntry entry, ElementInfo info)
    {
        if (entry.FullTypeName is { Length: > 0 } fullType)
        {
            if (!string.Equals(fullType, info.FullType, StringComparison.Ordinal))
                return false;
        }
        else if (!TypeMatches(entry.TypeName, info.Type))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(entry.AutomationId) &&
            !string.Equals(entry.AutomationId, info.AutomationId, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool TypeMatches(string expected, string? actual)
        => !string.IsNullOrEmpty(actual) && string.Equals(expected, actual, StringComparison.Ordinal);

    private static bool IsSyntheticElement(ElementInfo info)
        => info.FullType is { } ft && ft.StartsWith("Microsoft.Maui.DevFlow.Agent.Core.", StringComparison.Ordinal);

    private readonly struct XamlSourceContext
    {
        public readonly XamlSourceMap? Map;
        public readonly string Path;
        public readonly bool Matched;
        public readonly int ExpectedChildCount;

        public XamlSourceContext(XamlSourceMap? map, string path, bool matched, int expectedChildCount)
        {
            Map = map;
            Path = path;
            Matched = matched;
            ExpectedChildCount = expectedChildCount;
        }

        public XamlSourceContext ForChild(int index)
            => Map is null || !Matched
                ? default
                : new XamlSourceContext(Map, ChildPath(index), true, expectedChildCount: -1);

        public string ChildPath(int index)
            => Path.Length == 0 ? index.ToString() : $"{Path}/{index}";
    }

    /// <summary>
    /// Indicates whether this walker can discover native platform elements that are not
    /// represented in the MAUI visual tree.
    /// </summary>
    public virtual bool SupportsNativeElements => false;

    /// <summary>
    /// Returns native top-level window handles already represented by the MAUI tree so
    /// native probes can avoid duplicating them.
    /// </summary>
    public virtual IReadOnlyList<IntPtr> GetKnownNativeWindowHandles(Application app, int? windowIndex = null)
        => Array.Empty<IntPtr>();

    /// <summary>
    /// Captures native platform elements, typically on a worker thread.
    /// </summary>
    public virtual List<ElementInfo> WalkNativeTree(IReadOnlyList<IntPtr> knownWindowHandles, int maxDepth = 0)
        => [];

    /// <summary>
    /// Resolves a native platform object by DevFlow element id.
    /// </summary>
    public virtual object? GetNativeElementById(string id)
        => null;

    /// <summary>
    /// Resolves native element details by DevFlow element id.
    /// </summary>
    public virtual ElementInfo? GetNativeElementInfoById(string id)
        => FlattenElementInfos(WalkNativeTree(Array.Empty<IntPtr>()))
            .FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Queries native platform elements using the same filter shape as MAUI tree queries.
    /// </summary>
    public virtual List<ElementInfo> QueryNative(
        IReadOnlyList<IntPtr> knownWindowHandles,
        string? type = null,
        string? automationId = null,
        string? text = null,
        string? selector = null)
    {
        var tree = WalkNativeTree(knownWindowHandles);
        if (!string.IsNullOrWhiteSpace(selector))
        {
            return CssSelectorEngine.Query(tree, selector)
                .Where(e => MatchesElementInfo(e, type, automationId, text))
                .ToList();
        }

        return FlattenElementInfos(tree)
            .Where(e => MatchesElementInfo(e, type, automationId, text))
            .OrderByDescending(e => e.AutomationId is not null)
            .ThenByDescending(e => e.Traits?.Contains("actionable") == true)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Performs a native tap/invoke action for a native element id.
    /// </summary>
    public virtual string TryNativeElementTap(string elementId)
        => "Native element actions are not supported on this platform";

    /// <summary>
    /// Sets native element text/value for a native element id.
    /// </summary>
    public virtual string TryNativeElementSetValue(string elementId, string value)
        => "Native element value actions are not supported on this platform";

    /// <summary>
    /// Sets native keyboard focus for a native element id.
    /// </summary>
    public virtual string TryNativeElementFocus(string elementId)
        => "Native element focus is not supported on this platform";

    /// <summary>
    /// Scrolls or scrolls into view a native element id.
    /// </summary>
    public virtual string TryNativeElementScroll(string elementId, double deltaX, double deltaY)
        => "Native element scrolling is not supported on this platform";

    public static IEnumerable<ElementInfo> FlattenElementInfos(IEnumerable<ElementInfo> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            if (root.Children is not null)
            {
                foreach (var child in FlattenElementInfos(root.Children))
                    yield return child;
            }
        }
    }

    private static bool MatchesElementInfo(ElementInfo info, string? type, string? automationId, string? text)
    {
        if (!string.IsNullOrWhiteSpace(type) &&
            !info.Type.Equals(type, StringComparison.OrdinalIgnoreCase) &&
            !(info.FullType?.EndsWith(type, StringComparison.OrdinalIgnoreCase) == true))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(automationId) &&
            !string.Equals(info.AutomationId, automationId, StringComparison.OrdinalIgnoreCase) &&
            !info.Id.Equals(automationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(text) &&
            !(info.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true) &&
            !(info.Value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Walks from a specific element.
    /// </summary>
    public ElementInfo? WalkElement(IVisualTreeElement element, string? parentId, int currentDepth, int maxDepth)
    {
        if (_walkMaxElements > 0 && _walkElementCount >= _walkMaxElements)
        {
            WalkWasTruncated = true;
            return null;
        }
        _walkElementCount++;

        var id = GenerateId(element);
        var info = CreateElementInfo(element, id, parentId);

        if (CaptureWalkElements)
            _walkElements[id] = element;

        if (maxDepth > 0 && currentDepth >= maxDepth)
            return info;

        info.Children ??= new List<ElementInfo>();

        var children = GetTraversalChildren(element);
        foreach (var child in children)
        {
            var childInfo = WalkElement(child, id, currentDepth + 1, maxDepth);
            if (childInfo != null)
                info.Children.Add(childInfo);
            if (WalkWasTruncated)
                break;
        }

        // Add ToolbarItems as synthetic children of Pages
        if (!CaptureLayoutOnly && !WalkWasTruncated && element is Page page)
        {
            // NavBarTitle — inject page title as synthetic element
            AddNavBarTitle(page, id, info);

            foreach (var toolbarItem in page.ToolbarItems)
            {
                if (!TryReserveSyntheticElement())
                    break;
                var tiInfo = CreateToolbarItemInfo(toolbarItem, id);
                info.Children.Add(tiInfo);
            }

            // Add synthetic back button when there's a navigation stack
            if (!WalkWasTruncated)
            {
                var backInfo = CreateBackButtonInfo(page, id);
                if (backInfo != null && TryReserveSyntheticElement())
                    info.Children.Add(backInfo);
            }

            // SearchHandler (Shell only)
            if (!WalkWasTruncated)
                AddSearchHandler(page, id, info);
        }

        // Shell-level synthetics: flyout button, flyout items, tab bar
        if (!CaptureLayoutOnly && !WalkWasTruncated && element is Shell shell)
        {
            AddShellSynthetics(shell, id, info);
        }

        // NavigationPage-level synthetics
        if (!CaptureLayoutOnly && !WalkWasTruncated && element is NavigationPage navPage2)
        {
            AddNavigationPageSynthetics(navPage2, id, info);
        }

        // FlyoutPage-level synthetics
        if (!CaptureLayoutOnly && !WalkWasTruncated && element is FlyoutPage flyoutPage)
        {
            AddFlyoutPageSynthetics(flyoutPage, id, info);
        }

        // TabbedPage-level synthetics
        if (!CaptureLayoutOnly && !WalkWasTruncated && element is TabbedPage tabbedPage)
        {
            AddTabbedPageSynthetics(tabbedPage, id, info);
        }

        if (info.Children.Count == 0)
            info.Children = null;

        return info;
    }

    /// <summary>
    /// Queries elements matching the given criteria.
    /// </summary>
    public List<ElementInfo> Query(Application app, string? type = null, string? automationId = null, string? text = null)
    {
        var elements = FlattenElementInfos(WalkTree(app)).ToList();
        var results = elements
            .Where(info => MatchesElementInfo(info, type, automationId, text))
            .ToList();
        foreach (var result in results)
            result.Children = null;
        return results;
    }

    /// <summary>
    /// Generates a stable ID for a MAUI visual tree element using Element.Id.
    /// </summary>
    private string GenerateId(IVisualTreeElement element)
    {
        if (_objectToExternalId.TryGetValue(element, out var objectCachedId))
            return objectCachedId.Value;

        // Check if we've already generated an ID for this Element.Id in this walk
        if (element is Element el && _elementIdToExternalId.TryGetValue(el.Id, out var cachedId))
            return cachedId;

        string id;
        if (element is VisualElement ve && !string.IsNullOrEmpty(ve.AutomationId))
        {
            id = ve.AutomationId;
            if (_usedIds.Contains(id))
            {
                // Duplicate AutomationId — suffix with Element.Id
                if (element is Element dupEl)
                    id = $"{id}_{dupEl.Id.ToString("N")[..8]}";
                else
                    id = $"{id}_{RuntimeHelpers.GetHashCode(element):x8}";
            }
        }
        else if (element is Element elem)
        {
            id = elem.Id.ToString("N")[..12];
        }
        else
        {
            // Non-Element IVisualTreeElement — check IView.AutomationId first
            // (Comet views auto-stamp AutomationId via AppHostBuilderExtensions)
            string? automId = null;
            if (element is IView iv && !string.IsNullOrEmpty(iv.AutomationId))
                automId = iv.AutomationId;

            if (automId != null)
            {
                id = automId;
                if (_usedIds.Contains(id))
                    id = $"{id}_{RuntimeHelpers.GetHashCode(element):x8}";
            }
            else
            {
                // Try the element itself first, then try the handler's PlatformView
                // (Comet views stamp AccessibilityIdentifier on the native view, not IView.AutomationId)
                var platformId = EnsurePlatformStableId(element);
                if (platformId == null && element is IView iViewForPlatform)
                {
                    var platformView = CometViewResolver.GetPropertySafe(
                        iViewForPlatform.Handler?.GetType() ?? typeof(object), "PlatformView")
                        ?.GetValue(iViewForPlatform.Handler);
                    if (platformView != null)
                        platformId = EnsurePlatformStableId(platformView);
                }
                id = platformId ?? RuntimeHelpers.GetHashCode(element).ToString("x8");
            }
        }

        _usedIds.Add(id);
        _objectToExternalId.Add(element, new GeneratedElementId(id));
        if (element is Element elFinal)
            _elementIdToExternalId[elFinal.Id] = id;
        return id;
    }

    private bool TryReserveSyntheticElement()
    {
        if (_walkMaxElements > 0 && _walkElementCount >= _walkMaxElements)
        {
            WalkWasTruncated = true;
            return false;
        }
        _walkElementCount++;
        return true;
    }

    private sealed record GeneratedElementId(string Value);

    /// <summary>
    /// Generates a stable ID for a synthetic/non-visual element.
    /// Uses Element.Id from the backing object when available.
    /// </summary>
    private string GenerateObjectId(object element, string? automationId)
    {
        var identity = GetStableIdentity(element);

        // If backing is an Element, use its Element.Id for suffix disambiguation
        if (identity is Element el)
        {
            string id;
            if (!string.IsNullOrEmpty(automationId))
            {
                id = automationId;
                if (_usedIds.Contains(id))
                    id = $"{id}_{el.Id.ToString("N")[..8]}";
            }
            else
            {
                // No automationId — use Element.Id directly, but check for collisions
                // with MAUI elements that share the same Element.Id
                id = el.Id.ToString("N")[..12];
                if (_usedIds.Contains(id))
                    id = $"{id}_{RuntimeHelpers.GetHashCode(element):x8}";
            }

            _usedIds.Add(id);
            return id;
        }

        // Non-Element backing — use automationId + platform stamp or hash
        if (!string.IsNullOrEmpty(automationId))
        {
            var id = automationId;
            if (_usedIds.Contains(id))
            {
                var platformId = EnsurePlatformStableId(identity);
                var suffix = platformId ?? RuntimeHelpers.GetHashCode(identity).ToString("x8");
                id = $"{id}_{suffix[..Math.Min(8, suffix.Length)]}";
            }
            _usedIds.Add(id);
            return id;
        }

        // Last resort
        {
            var platformId = EnsurePlatformStableId(identity);
            var id = platformId ?? RuntimeHelpers.GetHashCode(identity).ToString("x8");
            _usedIds.Add(id);
            return id;
        }
    }

    /// <summary>
    /// Override in platform-specific subclasses to stamp a stable GUID on a platform view.
    /// Returns the stamped ID, or null if not applicable.
    /// </summary>
    protected virtual string? EnsurePlatformStableId(object platformObj) => null;

    /// <summary>
    /// Returns the stable underlying object for identity comparison.
    /// Synthetic markers wrap persistent MAUI objects (Page, Shell, ShellItem, etc.)
    /// that survive across tree walks.
    /// </summary>
    private static object GetStableIdentity(object element) => element switch
    {
        NavBarTitleMarker m => m.Page,
        FlyoutButtonMarker m => m.Shell,
        ShellFlyoutItemMarker m => m.Item,
        ShellTabMarker m => m.Section,
        SearchHandlerMarker m => m.Handler,
        FlyoutToggleMarker m => m.FlyoutPage,
        TabbedPageTabMarker m => m.Page,
        BackButtonMarker m => m.Navigation,
        _ => element
    };

    /// <summary>
    /// Recursively walks the tree searching for an element whose generated ID matches targetId.
    /// Returns the element/marker or null. Early exits on match.
    /// </summary>
    private object? FindByIdRecursive(IVisualTreeElement element, string targetId, string? parentId)
    {
        var id = GenerateId(element);
        if (id == targetId) return element;

        var children = GetTraversalChildren(element);

        // Check synthetics on Pages
        if (element is Page page)
        {
            // NavBarTitle
            var navBarResult = FindNavBarTitleById(page, id, targetId);
            if (navBarResult != null) return navBarResult;

            // ToolbarItems
            foreach (var toolbarItem in page.ToolbarItems)
            {
                var tiId = GetToolbarItemId(toolbarItem);
                if (tiId == targetId) return toolbarItem;
            }

            // BackButton
            var backResult = FindBackButtonById(page, id, targetId);
            if (backResult != null) return backResult;

            // SearchHandler
            var searchResult = FindSearchHandlerById(page, id, targetId);
            if (searchResult != null) return searchResult;
        }

        // Shell synthetics
        if (element is Shell shell)
        {
            var shellResult = FindShellSyntheticsById(shell, id, targetId);
            if (shellResult != null) return shellResult;
        }

        // NavigationPage synthetics
        if (element is NavigationPage navPage)
        {
            var navResult = FindNavigationPageSyntheticsById(navPage, id, targetId);
            if (navResult != null) return navResult;
        }

        // FlyoutPage synthetics
        if (element is FlyoutPage flyoutPage)
        {
            var flyoutResult = FindFlyoutPageSyntheticsById(flyoutPage, id, targetId);
            if (flyoutResult != null) return flyoutResult;
        }

        // TabbedPage synthetics
        if (element is TabbedPage tabbedPage)
        {
            var tabbedResult = FindTabbedPageSyntheticsById(tabbedPage, id, targetId);
            if (tabbedResult != null) return tabbedResult;
        }

        // Recurse into children
        foreach (var child in children)
        {
            var result = FindByIdRecursive(child, targetId, id);
            if (result != null) return result;
        }

        return null;
    }

    private static IReadOnlyList<IVisualTreeElement> GetTraversalChildren(IVisualTreeElement element)
    {
        var children = element.GetVisualChildren();
        List<IVisualTreeElement>? expanded = null;

        void Add(IVisualTreeElement? child)
        {
            if (child is null)
                return;

            var current = expanded ?? children;
            if (current.Any(existing => ReferenceEquals(existing, child)))
                return;

            expanded ??= children.ToList();
            expanded.Add(child);
        }

        switch (element)
        {
            case ShellContent shellContent:
                Add(shellContent.Content as IVisualTreeElement);
                break;
            case NavigationPage navigationPage:
                Add(navigationPage.CurrentPage);
                break;
            case FlyoutPage flyoutPage:
                Add(flyoutPage.Flyout);
                Add(flyoutPage.Detail);
                break;
            case TabbedPage tabbedPage:
                Add(tabbedPage.CurrentPage);
                break;
        }

        return expanded ?? children;
    }

    private object? FindNavBarTitleById(Page page, string parentId, string targetId)
    {
        try
        {
            if (page.Parent is Shell || FindAncestor<Shell>(page) != null)
            {
                if (!Shell.GetNavBarIsVisible(page)) return null;
            }
            else if (page.Parent is NavigationPage)
            {
                if (!NavigationPage.GetHasNavigationBar(page)) return null;
            }
        }
        catch { return null; }

        var title = page.Title;
        if (string.IsNullOrEmpty(title)) return null;

        var marker = new NavBarTitleMarker { Title = title, Page = page };
        var id = GenerateObjectId(marker, $"NavBarTitle_{parentId}");
        return id == targetId ? marker : null;
    }

    private object? FindBackButtonById(Page page, string parentId, string targetId)
    {
        var (nav, title) = ResolveBackNavigation(page);
        if (nav == null) return null;

        var marker = new BackButtonMarker { Navigation = nav, Title = title };
        var id = GenerateObjectId(marker, "BackButton");
        return id == targetId ? marker : null;
    }

    private object? FindSearchHandlerById(Page page, string parentId, string targetId)
    {
        try
        {
            var handler = Shell.GetSearchHandler(page);
            if (handler == null) return null;

            var marker = new SearchHandlerMarker { Handler = handler, Page = page };
            var id = GenerateObjectId(marker, $"SearchHandler_{parentId}");
            return id == targetId ? marker : null;
        }
        catch { return null; }
    }

    private object? FindShellSyntheticsById(Shell shell, string parentId, string targetId)
    {
        // Flyout button
        if (shell.FlyoutBehavior != FlyoutBehavior.Disabled)
        {
            var flyoutMarker = new FlyoutButtonMarker { Shell = shell };
            var flyoutId = GenerateObjectId(flyoutMarker, "FlyoutButton");
            if (flyoutId == targetId) return flyoutMarker;
        }

        // Flyout items
        try
        {
            foreach (var item in shell.Items)
            {
                if (item is BaseShellItem bsi && bsi.FlyoutItemIsVisible)
                {
                    var fiMarker = new ShellFlyoutItemMarker { Item = item, Shell = shell };
                    var fiId = GenerateObjectId(fiMarker, $"FlyoutItem_{bsi.Route ?? bsi.Title}");
                    if (fiId == targetId) return fiMarker;
                }
            }
        }
        catch { }

        // Tab bar items
        try
        {
            var currentPage = shell.CurrentPage;
            if (currentPage != null && Shell.GetTabBarIsVisible(currentPage))
            {
                var currentItem = shell.CurrentItem;
                if (currentItem?.Items != null && currentItem.Items.Count > 1)
                {
                    foreach (var section in currentItem.Items)
                    {
                        var tabMarker = new ShellTabMarker { Section = section, Shell = shell };
                        var tabId = GenerateObjectId(tabMarker, $"Tab_{section.Route ?? section.Title}");
                        if (tabId == targetId) return tabMarker;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private object? FindNavigationPageSyntheticsById(NavigationPage navPage, string parentId, string targetId)
    {
        try
        {
            var currentPage = navPage.CurrentPage;
            if (currentPage != null && !string.IsNullOrEmpty(currentPage.Title)
                && NavigationPage.GetHasNavigationBar(currentPage))
            {
                var marker = new NavBarTitleMarker { Title = currentPage.Title, Page = currentPage };
                var id = GenerateObjectId(marker, $"NavBarTitle_{parentId}");
                if (id == targetId) return marker;
            }
        }
        catch { }
        return null;
    }

    private object? FindFlyoutPageSyntheticsById(FlyoutPage flyoutPage, string parentId, string targetId)
    {
        var marker = new FlyoutToggleMarker { FlyoutPage = flyoutPage };
        var id = GenerateObjectId(marker, "FlyoutToggle");
        return id == targetId ? marker : null;
    }

    private object? FindTabbedPageSyntheticsById(TabbedPage tabbedPage, string parentId, string targetId)
    {
        try
        {
            foreach (var child in tabbedPage.Children)
            {
                if (child is Page tabPage)
                {
                    var marker = new TabbedPageTabMarker { Page = tabPage, TabbedPage = tabbedPage };
                    var tabId = GenerateObjectId(marker, $"Tab_{tabPage.AutomationId ?? tabPage.Title}");
                    if (tabId == targetId) return marker;
                }
            }
        }
        catch { }
        return null;
    }

    // ── Synthetic element injection helpers ──

    private void AddNavBarTitle(Page page, string parentId, ElementInfo parentInfo)
    {
        // Skip if nav bar is hidden
        try
        {
            if (page.Parent is Shell || FindAncestor<Shell>(page) != null)
            {
                if (!Shell.GetNavBarIsVisible(page)) return;
            }
            else if (page.Parent is NavigationPage)
            {
                if (!NavigationPage.GetHasNavigationBar(page)) return;
            }
        }
        catch { }

        var title = page.Title;
        if (string.IsNullOrEmpty(title)) return;
        if (!TryReserveSyntheticElement()) return;

        var marker = new NavBarTitleMarker { Title = title, Page = page };
        var id = GenerateObjectId(marker, $"NavBarTitle_{parentId}");
        parentInfo.Children ??= new List<ElementInfo>();
        var navBarInfo = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = "NavBarTitle",
            FullType = "Microsoft.Maui.DevFlow.Agent.Core.NavBarTitle",
            Text = title,
            IsVisible = true,
            IsEnabled = false, // not interactive
        };
        TryPopulateSyntheticBounds(id, marker, navBarInfo);
        parentInfo.Children.Add(navBarInfo);
    }

    private void AddSearchHandler(Page page, string parentId, ElementInfo parentInfo)
    {
        try
        {
            var handler = Shell.GetSearchHandler(page);
            if (handler == null) return;
            if (!TryReserveSyntheticElement()) return;

            var marker = new SearchHandlerMarker { Handler = handler, Page = page };
            var id = GenerateObjectId(marker, $"SearchHandler_{parentId}");
            parentInfo.Children ??= new List<ElementInfo>();
            var shInfo = new ElementInfo
            {
                Id = id,
                ParentId = parentId,
                Type = "SearchHandler",
                FullType = "Microsoft.Maui.DevFlow.Agent.Core.SearchHandler",
                Text = handler.Placeholder ?? "Search",
                IsVisible = handler.SearchBoxVisibility != SearchBoxVisibility.Hidden,
                IsEnabled = handler.IsSearchEnabled,
                NativeProperties = new Dictionary<string, string?>
                {
                    ["placeholder"] = handler.Placeholder,
                    ["query"] = handler.Query,
                    ["searchBoxVisibility"] = handler.SearchBoxVisibility.ToString(),
                },
            };
            TryPopulateSyntheticBounds(id, marker, shInfo);
            parentInfo.Children.Add(shInfo);
        }
        catch { } // Shell.GetSearchHandler may throw if not in Shell context
    }

    private void AddShellSynthetics(Shell shell, string parentId, ElementInfo parentInfo)
    {
        parentInfo.Children ??= new List<ElementInfo>();

        // Flyout button
        if (shell.FlyoutBehavior != FlyoutBehavior.Disabled)
        {
            if (!TryReserveSyntheticElement()) return;
            var flyoutMarker = new FlyoutButtonMarker { Shell = shell };
            var flyoutId = GenerateObjectId(flyoutMarker, "FlyoutButton");
            var flyoutBtnInfo = new ElementInfo
            {
                Id = flyoutId,
                ParentId = parentId,
                Type = "FlyoutButton",
                FullType = "Microsoft.Maui.DevFlow.Agent.Core.FlyoutButton",
                AutomationId = "FlyoutButton",
                Text = "☰",
                IsVisible = shell.FlyoutBehavior == FlyoutBehavior.Flyout,
                IsEnabled = true,
            };
            TryPopulateSyntheticBounds(flyoutId, flyoutMarker, flyoutBtnInfo);
            parentInfo.Children.Insert(0, flyoutBtnInfo);
        }

        // Flyout items
        try
        {
            foreach (var item in shell.Items)
            {
                if (item is BaseShellItem bsi && bsi.FlyoutItemIsVisible)
                {
                    if (!TryReserveSyntheticElement())
                        break;
                    var isSelected = shell.CurrentItem == item;
                    var fiMarker = new ShellFlyoutItemMarker { Item = item, Shell = shell };
                    var fiId = GenerateObjectId(fiMarker, $"FlyoutItem_{bsi.Route ?? bsi.Title}");
                    var fiInfo = new ElementInfo
                    {
                        Id = fiId,
                        ParentId = parentId,
                        Type = "FlyoutItem",
                        FullType = "Microsoft.Maui.DevFlow.Agent.Core.FlyoutItem",
                        AutomationId = bsi.AutomationId,
                        Text = bsi.Title,
                        IsVisible = bsi.IsVisible,
                        IsEnabled = bsi.IsEnabled,
                        IsSelected = isSelected,
                        IsFocused = isSelected,
                    };
                    // Enrich with route info
                    var fiProps = new Dictionary<string, string?>();
                    if (!string.IsNullOrEmpty(bsi.Route)) fiProps["route"] = bsi.Route;
                    if (bsi.Icon is FileImageSource fIcon) fiProps["icon"] = fIcon.File;
                    if (bsi.FlyoutIcon is FileImageSource fFlyout) fiProps["flyoutIcon"] = fFlyout.File;
                    if (fiProps.Count > 0) fiInfo.NativeProperties = fiProps;
                    TryPopulateSyntheticBounds(fiId, fiMarker, fiInfo);
                    parentInfo.Children.Add(fiInfo);
                }
            }
        }
        catch { }

        // Tab bar items for current ShellItem
        try
        {
            var currentPage = shell.CurrentPage;
            if (currentPage != null && Shell.GetTabBarIsVisible(currentPage))
            {
                var currentItem = shell.CurrentItem;
                if (currentItem?.Items != null && currentItem.Items.Count > 1)
                {
                    foreach (var section in currentItem.Items)
                    {
                        if (!TryReserveSyntheticElement())
                            break;
                        var isSelected = currentItem.CurrentItem == section;
                        var tabMarker = new ShellTabMarker { Section = section, Shell = shell };
                        var tabId = GenerateObjectId(tabMarker, $"Tab_{section.Route ?? section.Title}");
                        var tabInfo = new ElementInfo
                        {
                            Id = tabId,
                            ParentId = parentId,
                            Type = "Tab",
                            FullType = "Microsoft.Maui.DevFlow.Agent.Core.Tab",
                            AutomationId = section.AutomationId,
                            Text = section.Title,
                            IsVisible = section.IsVisible,
                            IsEnabled = section.IsEnabled,
                            IsSelected = isSelected,
                            IsFocused = isSelected,
                        };
                        var tabProps = new Dictionary<string, string?>();
                        if (!string.IsNullOrEmpty(section.Route)) tabProps["route"] = section.Route;
                        if (section.Icon is FileImageSource tIcon) tabProps["icon"] = tIcon.File;
                        if (tabProps.Count > 0) tabInfo.NativeProperties = tabProps;
                        TryPopulateSyntheticBounds(tabId, tabMarker, tabInfo);
                        parentInfo.Children.Add(tabInfo);
                    }
                }
            }
        }
        catch { }

        // Flyout header/footer
        try
        {
            if (shell.FlyoutHeader is View headerView && headerView is IVisualTreeElement headerVte)
            {
                var headerInfo = WalkElement(headerVte, parentId, 1, 3);
                if (headerInfo != null)
                {
                    headerInfo.Type = "FlyoutHeader";
                    parentInfo.Children.Add(headerInfo);
                }
            }
            if (!WalkWasTruncated &&
                shell.FlyoutFooter is View footerView && footerView is IVisualTreeElement footerVte)
            {
                var footerInfo = WalkElement(footerVte, parentId, 1, 3);
                if (footerInfo != null)
                {
                    footerInfo.Type = "FlyoutFooter";
                    parentInfo.Children.Add(footerInfo);
                }
            }
        }
        catch { }
    }

    private void AddNavigationPageSynthetics(NavigationPage navPage, string parentId, ElementInfo parentInfo)
    {
        try
        {
            var currentPage = navPage.CurrentPage;
            if (currentPage != null && !string.IsNullOrEmpty(currentPage.Title)
                && NavigationPage.GetHasNavigationBar(currentPage))
            {
                if (!TryReserveSyntheticElement()) return;
                var marker = new NavBarTitleMarker { Title = currentPage.Title, Page = currentPage };
                var id = GenerateObjectId(marker, $"NavBarTitle_{parentId}");
                parentInfo.Children ??= new List<ElementInfo>();
                var npNavInfo = new ElementInfo
                {
                    Id = id,
                    ParentId = parentId,
                    Type = "NavBarTitle",
                    FullType = "Microsoft.Maui.DevFlow.Agent.Core.NavBarTitle",
                    Text = currentPage.Title,
                    IsVisible = true,
                    IsEnabled = false,
                };
                TryPopulateSyntheticBounds(id, marker, npNavInfo);
                parentInfo.Children.Insert(0, npNavInfo);
            }
        }
        catch { }
    }

    private void AddFlyoutPageSynthetics(FlyoutPage flyoutPage, string parentId, ElementInfo parentInfo)
    {
        if (!TryReserveSyntheticElement()) return;
        var marker = new FlyoutToggleMarker { FlyoutPage = flyoutPage };
        var id = GenerateObjectId(marker, "FlyoutToggle");
        parentInfo.Children ??= new List<ElementInfo>();
        var ftInfo = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = "FlyoutToggle",
            FullType = "Microsoft.Maui.DevFlow.Agent.Core.FlyoutToggle",
            AutomationId = "FlyoutToggle",
            Text = "☰",
            IsVisible = true,
            IsEnabled = true,
        };
        TryPopulateSyntheticBounds(id, marker, ftInfo);
        parentInfo.Children.Insert(0, ftInfo);
    }

    private void AddTabbedPageSynthetics(TabbedPage tabbedPage, string parentId, ElementInfo parentInfo)
    {
        parentInfo.Children ??= new List<ElementInfo>();
        try
        {
            foreach (var child in tabbedPage.Children)
            {
                if (child is Page tabPage)
                {
                    if (!TryReserveSyntheticElement())
                        break;
                    var isSelected = tabbedPage.CurrentPage == tabPage;
                    var marker = new TabbedPageTabMarker { Page = tabPage, TabbedPage = tabbedPage };
                    var tabId = GenerateObjectId(marker, $"Tab_{tabPage.AutomationId ?? tabPage.Title}");
                    var tpTabInfo = new ElementInfo
                    {
                        Id = tabId,
                        ParentId = parentId,
                        Type = "Tab",
                        FullType = "Microsoft.Maui.DevFlow.Agent.Core.Tab",
                        AutomationId = tabPage.AutomationId,
                        Text = tabPage.Title,
                        IsVisible = true,
                        IsEnabled = true,
                        IsSelected = isSelected,
                        IsFocused = isSelected,
                    };
                    if (tabPage.IconImageSource is FileImageSource tpIcon)
                        tpTabInfo.NativeProperties = new Dictionary<string, string?> { ["icon"] = tpIcon.File };
                    TryPopulateSyntheticBounds(tabId, marker, tpTabInfo);
                    parentInfo.Children.Add(tpTabInfo);
                }
            }
        }
        catch { }
    }

    // ── Marker types for synthetic elements ──

    public class NavBarTitleMarker { public string Title { get; init; } = ""; public Page Page { get; init; } = null!; }
    public class SearchHandlerMarker
    {
        public SearchHandler Handler { get; init; } = null!;
        public Page Page { get; init; } = null!;
    }
    public class FlyoutButtonMarker { public Shell Shell { get; init; } = null!; }
    public class ShellFlyoutItemMarker { public ShellItem Item { get; init; } = null!; public Shell Shell { get; init; } = null!; }
    public class ShellTabMarker { public ShellSection Section { get; init; } = null!; public Shell Shell { get; init; } = null!; }
    public class FlyoutToggleMarker { public FlyoutPage FlyoutPage { get; init; } = null!; }
    public class TabbedPageTabMarker { public Page Page { get; init; } = null!; public TabbedPage TabbedPage { get; init; } = null!; }

    /// <summary>
    /// Tries to resolve and store bounds for a synthetic element.
    /// </summary>
    private void TryPopulateSyntheticBounds(string id, object marker, ElementInfo info)
    {
        try
        {
            var bounds = ResolveSyntheticBounds(marker);
            if (bounds != null)
            {
                info.Bounds = bounds;
                info.WindowBounds = bounds;
                _syntheticBounds[id] = (bounds, marker);
            }
        }
        catch { }
    }

    private ElementInfo CreateToolbarItemInfo(ToolbarItem item, string parentId)
    {
        var id = GetToolbarItemId(item);
        _usedIds.Add(id);
        var tiInfo = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = "ToolbarItem",
            FullType = item.GetType().FullName ?? "Microsoft.Maui.Controls.ToolbarItem",
            AutomationId = item.AutomationId,
            Text = item.Text,
            IsVisible = true,
            IsEnabled = item.IsEnabled,
        };
        TryPopulateSyntheticBounds(id, item, tiInfo);
        return tiInfo;
    }

    internal static string GetToolbarItemId(ToolbarItem item)
    {
        var suffix = item.Id.ToString("N")[..8];
        return string.IsNullOrWhiteSpace(item.AutomationId)
            ? $"ToolbarItem_{suffix}"
            : $"{item.AutomationId}_{suffix}";
    }

    private ElementInfo? CreateBackButtonInfo(Page page, string parentId)
    {
        var (nav, title) = ResolveBackNavigation(page);
        if (nav == null) return null;

        var marker = new BackButtonMarker { Navigation = nav, Title = title };
        var id = GenerateObjectId(marker, "BackButton");
        return new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = "BackButton",
            FullType = "Microsoft.Maui.DevFlow.Agent.Core.BackButton",
            AutomationId = "BackButton",
            Text = $"← {title}",
            IsVisible = true,
            IsEnabled = true,
        };
    }

    private (INavigation? Nav, string Title) ResolveBackNavigation(Page page)
    {
        INavigation? nav = null;
        string title = "Back";

        var navPage = FindAncestor<NavigationPage>(page);
        if (navPage != null && navPage.Navigation.NavigationStack.Count > 1
            && navPage.CurrentPage == page)
        {
            nav = navPage.Navigation;
            var prev = nav.NavigationStack[nav.NavigationStack.Count - 2];
            if (prev != null && !string.IsNullOrEmpty(prev.Title))
                title = prev.Title;
        }

        if (nav == null)
        {
            var shell = FindAncestor<Shell>(page);
            if (shell == null)
            {
                try { if (page.Window?.Page is Shell windowShell) shell = windowShell; }
                catch { }
            }
            if (shell?.CurrentItem?.CurrentItem is ShellSection section)
            {
                var navStack = section.Navigation?.NavigationStack;
                if (navStack != null && navStack.Count > 1 && navStack[^1] == page)
                {
                    nav = section.Navigation;
                    var prev = navStack[navStack.Count - 2];
                    if (prev != null && !string.IsNullOrEmpty(prev.Title))
                        title = prev.Title;
                }
            }
        }

        return (nav, title);
    }

    private static T? FindAncestor<T>(Element? element) where T : Element
    {
        while (element != null)
        {
            if (element is T match)
                return match;
            element = element.Parent;
        }
        return null;
    }

    private ElementInfo CreateElementInfo(IVisualTreeElement element, string id, string? parentId)
    {
        // Try to resolve Comet view type first (if Comet is loaded)
        var cometResolved = CaptureLayoutOnly
            ? null
            : CometViewResolver.TryResolveCometView(element);

        var info = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = cometResolved?.Type ?? element.GetType().Name,
            FullType = cometResolved?.FullType ?? element.GetType().FullName ?? element.GetType().Name,
            // Window and Shell item nodes are structural rather than IView instances, but their
            // descendants are the live rendered tree. Preserve their structural visibility so
            // effective-visibility consumers do not incorrectly hide every control beneath them.
            IsVisible = element switch
            {
                Window => true,
                BaseShellItem shellItem => IsActiveShellItem(shellItem),
                _ => false,
            },
            IsEnabled = element switch
            {
                Window => true,
                BaseShellItem shellItem => shellItem.IsEnabled,
                _ => false,
            },
        };

        if (element is Page page)
        {
            info.Role = "page";
            info.IsSelected = _activePages.Contains(page);
        }

        if (element is VisualElement ve)
        {
            info.AutomationId = ve.AutomationId;
            if (!CaptureLayoutOnly)
            {
                info.StableItemKey = FindStableItemKey(ve);
                info.CollectionScope = FindCollectionScope(ve);
            }
            info.IsVisible = ve.IsVisible;
            info.IsEnabled = ve.IsEnabled;
            info.IsFocused = ve.IsFocused;
            info.Opacity = double.IsFinite(ve.Opacity) ? ve.Opacity : 1;
            info.Bounds = new BoundsInfo
            {
                X = double.IsFinite(ve.Frame.X) ? ve.Frame.X : 0,
                Y = double.IsFinite(ve.Frame.Y) ? ve.Frame.Y : 0,
                Width = double.IsFinite(ve.Frame.Width) ? ve.Frame.Width : 0,
                Height = double.IsFinite(ve.Frame.Height) ? ve.Frame.Height : 0
            };

            // Populate style classes
            if (!CaptureLayoutOnly &&
                ve is NavigableElement ne &&
                ne.StyleClass is { Count: > 0 } sc)
                info.StyleClass = sc.ToList();

            // Populate native view info from handler
            if (!CaptureLayoutOnly)
                PopulateNativeInfo(info, ve);

            // Resolve window-absolute bounds via platform-native APIs
            info.WindowBounds = ResolveWindowBounds(ve);
        }
        // Comet views implement IView but NOT VisualElement — extract info from IView + handler
        else if (element is IView iView)
        {
            info.IsVisible = iView.Visibility != Visibility.Collapsed;
            info.IsEnabled = iView.IsEnabled;
            info.Opacity = double.IsFinite(iView.Opacity) ? iView.Opacity : 1;

            // Try to get bounds from the IView's Frame
            var frame = iView.Frame;
            if (frame.Width > 0 || frame.Height > 0)
            {
                info.Bounds = new BoundsInfo
                {
                    X = double.IsFinite(frame.X) ? frame.X : 0,
                    Y = double.IsFinite(frame.Y) ? frame.Y : 0,
                    Width = double.IsFinite(frame.Width) ? frame.Width : 0,
                    Height = double.IsFinite(frame.Height) ? frame.Height : 0
                };
            }

            // Try to extract text from Comet views via IText interface
            if (!CaptureLayoutOnly &&
                element is IText iText &&
                !string.IsNullOrEmpty(iText.Text))
                info.Text = iText.Text;
        }

        if (!CaptureLayoutOnly)
        {
            // Extract text from common controls (including Shell elements)
            info.Text ??= element switch
            {
                Label l => l.Text,
                Button b => b.Text,
                Entry e => e.Text,
                Editor ed => ed.Text,
                SearchBar sb => sb.Text,
                Span s => s.Text,
                BaseShellItem si => si.Title,
                // Fallback to IText interface for non-Controls types (e.g. Comet views)
                IText it when info.Text == null => it.Text,
                _ => null
            };

            // Extract value/state from stateful controls
            info.Value = element switch
            {
                Switch sw => sw.IsToggled.ToString(),
                CheckBox cb => cb.IsChecked.ToString(),
                Slider sl => sl.Value.ToString("F2"),
                Stepper st => st.Value.ToString("F2"),
                ProgressBar pb => pb.Progress.ToString("F2"),
                Picker pk => pk.SelectedItem?.ToString(),
                DatePicker dp => dp.Date.ToString(),
                TimePicker tp => tp.Time.ToString(),
                RadioButton rb => rb.IsChecked.ToString(),
                _ => null
            };
        }

        if (!CaptureLayoutOnly)
        {
            info.IsSelected = element switch
            {
                CheckBox cb => cb.IsChecked,
                RadioButton rb => rb.IsChecked,
                Switch sw => sw.IsToggled,
                Picker pk => pk.SelectedIndex >= 0,
                _ => info.IsSelected
            };
        }

        // Populate gesture recognizer metadata
        if (element is View view && view.GestureRecognizers.Count > 0)
        {
            var gestures = new List<string>();
            foreach (var gr in view.GestureRecognizers)
            {
                var name = gr switch
                {
                    TapGestureRecognizer => "tap",
                    SwipeGestureRecognizer => "swipe",
                    PanGestureRecognizer => "pan",
                    PinchGestureRecognizer => "pinch",
                    PointerGestureRecognizer => "pointer",
                    DragGestureRecognizer => "drag",
                    DropGestureRecognizer => "drop",
                    _ => gr.GetType().Name.Replace("GestureRecognizer", "").ToLowerInvariant()
                };
                if (!gestures.Contains(name))
                    gestures.Add(name);
            }
            if (gestures.Count > 0)
                info.Gestures = gestures;
        }

        // Populate ItemsView metadata (item count for CollectionView/ListView/CarouselView)
        if (!CaptureLayoutOnly && element is ItemsView itemsView)
        {
            info.NativeProperties ??= new Dictionary<string, string?>();
            try
            {
                if (itemsView.ItemsSource != null)
                {
                    var count = itemsView.ItemsSource switch
                    {
                        System.Collections.ICollection c => c.Count,
                        _ => itemsView.ItemsSource.Cast<object>().Count()
                    };
                    info.NativeProperties["itemCount"] = count.ToString();
                }
            }
            catch { /* ItemsSource may not support counting */ }
        }

        // Add Comet-specific properties if this is a Comet view
        if (!CaptureLayoutOnly && cometResolved != null)
        {
            info.NativeProperties ??= new Dictionary<string, string?>();
            foreach (var kvp in cometResolved.Value.Properties)
            {
                info.NativeProperties[kvp.Key] = kvp.Value;
            }

            // Optionally add environment values (can be verbose, so commented out by default)
            // var envValues = CometViewResolver.GetEnvironmentValues(cometResolved.Value.CometView);
            // foreach (var kvp in envValues)
            // {
            //     info.NativeProperties[$"Env_{kvp.Key}"] = kvp.Value;
            // }
        }

        return info;
    }

    private static bool IsActiveShellItem(BaseShellItem item)
    {
        if (!item.IsVisible)
            return false;

        var shell = Shell.Current;
        if (shell is null)
            return true;

        return item switch
        {
            ShellItem shellItem => ReferenceEquals(shell.CurrentItem, shellItem),
            ShellSection section => ReferenceEquals(shell.CurrentItem?.CurrentItem, section),
            ShellContent content => ReferenceEquals(
                shell.CurrentItem?.CurrentItem?.CurrentItem,
                content),
            _ => true,
        };
    }

    private static string? FindStableItemKey(Element? element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current is BindableObject bindable)
            {
                var key = DevFlowTest.GetStableItemKey(bindable)?.Trim();
                if (string.IsNullOrWhiteSpace(key) &&
                    bindable.BindingContext is IDevFlowStableItemKey provider)
                {
                    key = provider.DevFlowStableItemKey?.Trim();
                }
                if (!string.IsNullOrWhiteSpace(key))
                {
                    if (key.Length > 256)
                        return null;
                    var digest = System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(key));
                    return "sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
                }
            }
            if (current is CollectionView or CarouselView || current.GetType().Name == "ListView")
                break;
        }
        return null;
    }

    private static string? FindCollectionScope(Element? element)
    {
        for (var current = element?.Parent; current is not null; current = current.Parent)
        {
            if (current is CollectionView or CarouselView || current.GetType().Name == "ListView")
                return (current as VisualElement)?.AutomationId;
        }
        return null;
    }

    protected virtual void PopulateNativeInfo(ElementInfo info, VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return;

            info.NativeType = platformView.GetType().FullName;
        }
        catch
        {
            // Native info is best-effort; don't fail the tree walk
        }
    }

    /// <summary>
    /// Queries elements matching a CSS selector string.
    /// Walks the full tree, then runs the CSS selector engine against it.
    /// </summary>
    public List<ElementInfo> QueryCss(Application app, string selector)
    {
        var tree = WalkTree(app);
        return CssSelectorEngine.Query(tree, selector);
    }

    /// <summary>
    /// Clears the element ID mappings.
    /// </summary>
    public void Reset()
    {
        _usedIds.Clear();
        _elementIdToExternalId.Clear();
        _objectToExternalId.Clear();
        _syntheticBounds.Clear();
        _walkElements.Clear();
    }
}
