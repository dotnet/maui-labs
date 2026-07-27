using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core.Css;
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
    private const string RegisteredNativeElementPrefix = "native:registered:";
    private readonly NativeElementRegistrationRegistry? _nativeElementRegistry;
    private readonly object _registeredNativeGate = new();
    private Dictionary<string, ElementInfo> _registeredNativeInfos = new(StringComparer.Ordinal);
    private Dictionary<string, string?> _elementInfoParents = new(StringComparer.Ordinal);

    // Per-walk state — fully rebuilt on each WalkTree call
    private readonly HashSet<string> _usedIds = new();
    private readonly Dictionary<Guid, string> _elementIdToExternalId = new();
    private readonly Dictionary<object, string> _objectToExternalId = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, (BoundsInfo Bounds, object Marker)> _syntheticBounds = new();
    private int _currentWindowId;

    public VisualTreeWalker()
    {
    }

    internal VisualTreeWalker(NativeElementRegistrationRegistry nativeElementRegistry)
    {
        _nativeElementRegistry = nativeElementRegistry;
    }

    /// <summary>
    /// Marker object representing the navigation back button in Shell or NavigationPage.
    /// </summary>
    public class BackButtonMarker
    {
        public INavigation Navigation { get; init; } = null!;
        public string Title { get; init; } = "Back";
        public BackButtonBehavior? Behavior { get; init; }
        public Page? Page { get; init; }
    }

    /// <summary>
    /// Looks up an element by ID by walking the tree fresh.
    /// Returns IVisualTreeElement, ToolbarItem, or other mapped objects.
    /// </summary>
    public object? GetElementById(string id, Application? app)
    {
        if (app == null || string.IsNullOrEmpty(id)) return null;
        if (_nativeElementRegistry?.TryGet(id, out var registration) == true)
            return registration.NativeElement;

        // Walk tree fresh, searching for matching ID
        _usedIds.Clear();
        _elementIdToExternalId.Clear();
        _objectToExternalId.Clear();
        _syntheticBounds.Clear();

        try
        {
            if (app is not IVisualTreeElement appElement) return null;
            if (app.Windows.Count == 0)
            {
                _currentWindowId = 0;
                foreach (var child in appElement.GetVisualChildren())
                {
                    var result = FindByIdRecursive(child, id, null);
                    if (result != null) return result;
                }
            }
            else
            {
                for (var index = 0; index < app.Windows.Count; index++)
                {
                    _currentWindowId = index;
                    if (app.Windows[index] is not IVisualTreeElement windowElement)
                        continue;

                    var result = FindByIdRecursive(windowElement, id, null);
                    if (result != null) return result;
                }
            }
            return null;
        }
        finally
        {
            _objectToExternalId.Clear();
        }
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

    public List<ElementInfo> HitTestRegisteredNativeElements(double x, double y)
        => HitTestRegisteredNativeElements(x, y, windowId: null);

    public List<ElementInfo> HitTestRegisteredNativeElements(double x, double y, int? windowId)
    {
        lock (_registeredNativeGate)
        {
            return _registeredNativeInfos.Values
                .Where(info =>
                {
                    if (!info.IsVisible || windowId.HasValue && info.WindowId != windowId)
                        return false;
                    var bounds = info.WindowBounds ?? info.Bounds;
                    return bounds is not null
                        && x >= bounds.X
                        && x <= bounds.X + bounds.Width
                        && y >= bounds.Y
                        && y <= bounds.Y + bounds.Height;
                })
                .OrderBy(info =>
                {
                    var bounds = info.WindowBounds ?? info.Bounds;
                    return bounds is null ? double.MaxValue : bounds.Width * bounds.Height;
                })
                .ThenByDescending(info => info.Capabilities?.Count ?? 0)
                .ToList();
        }
    }

    public bool IsRegisteredNativeElementUnderPage(string nativeElementId, Page page)
    {
        if (!_elementIdToExternalId.TryGetValue(page.Id, out var pageId))
            return false;

        lock (_registeredNativeGate)
        {
            if (!_registeredNativeInfos.TryGetValue(nativeElementId, out var info))
                return false;

            var currentId = info.OwnerId;
            while (currentId is not null)
            {
                if (currentId.Equals(pageId, StringComparison.Ordinal))
                    return true;
                if (!_elementInfoParents.TryGetValue(currentId, out currentId))
                    return false;
            }
        }

        return false;
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
            foreach (var child in vte.GetVisualChildren())
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
            IdentityToken = GetStableIdentity(marker),
            WindowId = _currentWindowId,
            Type = GetSyntheticTypeName(marker),
            FullType = $"Microsoft.Maui.DevFlow.Agent.Core.{GetSyntheticTypeName(marker)}",
            Text = GetSyntheticText(marker),
            IsVisible = true,
            IsEnabled = true,
        };
        if (marker is BackButtonMarker backButton)
        {
            info.IsVisible = (backButton.Behavior?.IsVisible ?? true)
                && (backButton.Page is null || IsNavigationBarVisible(backButton.Page));
            info.IsEnabled = backButton.Behavior?.IsEnabled ?? true;
        }

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
    public List<ElementInfo> WalkTree(Application app, int maxDepth = 0, int? windowIndex = null)
    {
        _usedIds.Clear();
        _elementIdToExternalId.Clear();
        _objectToExternalId.Clear();
        _syntheticBounds.Clear();
        try
        {
            var results = new List<ElementInfo>();
            if (app is not IVisualTreeElement appElement)
                return results;

            if (windowIndex != null)
            {
                if (windowIndex.Value < 0 || windowIndex.Value >= app.Windows.Count)
                    return results;
                _currentWindowId = windowIndex.Value;
                var window = app.Windows[windowIndex.Value];
                if (window is IVisualTreeElement windowElement)
                {
                    var info = WalkElement(windowElement, null, 1, maxDepth);
                    if (info != null)
                    {
                        SetWindowId(info, windowIndex.Value);
                        results.Add(info);
                    }
                }
                AppendRegisteredNativeElements(results, maxDepth);
                return results;
            }

            if (app.Windows.Count == 0)
            {
                _currentWindowId = 0;
                foreach (var child in appElement.GetVisualChildren())
                {
                    var info = WalkElement(child, null, 1, maxDepth);
                    if (info == null)
                        continue;

                    SetWindowId(info, 0);
                    results.Add(info);
                }
            }
            else
            {
                for (var index = 0; index < app.Windows.Count; index++)
                {
                    _currentWindowId = index;
                    if (app.Windows[index] is not IVisualTreeElement windowElement)
                        continue;

                    var info = WalkElement(windowElement, null, 1, maxDepth);
                    if (info == null)
                        continue;

                    SetWindowId(info, index);
                    results.Add(info);
                }
            }

            AppendRegisteredNativeElements(results, maxDepth);
            return results;
        }
        finally
        {
            _objectToExternalId.Clear();
            _currentWindowId = 0;
        }
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
    /// Performs a platform-native hit test in window logical coordinates.
    /// Results are ordered from the deepest native element to its ancestors.
    /// </summary>
    public virtual List<ElementInfo> HitTestNativeElements(
        IReadOnlyList<IntPtr> knownWindowHandles,
        double x,
        double y)
        => [];

    /// <summary>
    /// Resolves a native platform object by DevFlow element id.
    /// </summary>
    public virtual object? GetNativeElementById(string id)
        => _nativeElementRegistry?.TryGet(id, out var registration) == true
            ? registration.NativeElement
            : null;

    protected object? GetRegisteredNativeOwner(string id)
        => _nativeElementRegistry?.TryGet(id, out var registration) == true
            ? registration.Owner
            : null;

    public int? GetRegisteredNativeWindowId(string id, Application app)
        => _nativeElementRegistry?.TryGet(id, out var registration) == true
            ? GetWindowIdForElement(registration.Owner, app)
            : null;

    public static int? GetWindowIdForElement(object element, Application app)
    {
        if (element is not Element current)
            return null;

        while (current.Parent is Element parent)
            current = parent;

        for (var index = 0; index < app.Windows.Count; index++)
        {
            if (ReferenceEquals(app.Windows[index], current))
                return index;
        }

        return null;
    }

    /// <summary>
    /// Resolves native element details by DevFlow element id.
    /// </summary>
    public virtual ElementInfo? GetNativeElementInfoById(string id)
    {
        if (id.StartsWith("native:registered:", StringComparison.Ordinal))
        {
            if (_nativeElementRegistry?.TryGet(id, out var registration) != true)
            {
                lock (_registeredNativeGate)
                    _registeredNativeInfos.Remove(id);
                return null;
            }

            lock (_registeredNativeGate)
            {
                if (_registeredNativeInfos.TryGetValue(id, out var registeredInfo))
                {
                    var info = CreateRegisteredNativeElementInfo(registration, registeredInfo.OwnerId);
                    info.WindowId = registeredInfo.WindowId;
                    return info;
                }
            }

            return CreateRegisteredNativeElementInfo(registration, ownerId: null);
        }

        return FlattenElementInfos(WalkNativeTree(Array.Empty<IntPtr>()))
            .FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

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
    {
        if (!elementId.StartsWith(RegisteredNativeElementPrefix, StringComparison.Ordinal))
            return "Native element actions are not supported on this platform";

        if (_nativeElementRegistry?.TryGet(elementId, out var registration) != true)
            return $"Native element '{elementId}' was not found";

        var semanticResult = registration.Owner switch
        {
            MenuItem menuItem => ActivateMenuItem(menuItem),
            ShellContent shellContent
                when !registration.Role.Equals("ShellTabOverflow", StringComparison.Ordinal)
                    || IsOverflowRow(registration)
                => ActivateShellContent(shellContent, registration.Role),
            ShellSection shellSection
                when !registration.Role.Equals("ShellTabOverflow", StringComparison.Ordinal)
                    || IsOverflowRow(registration)
                => ActivateShellSection(shellSection, registration.Role),
            ShellItem shellItem
                when !registration.Role.Equals("ShellTabOverflow", StringComparison.Ordinal)
                => ActivateShellItem(shellItem, registration.Role),
            Shell shell
                when registration.Role.Equals("ShellFlyoutToggle", StringComparison.Ordinal)
                => ToggleShellFlyout(shell),
            Page page
                when page.Parent is TabbedPage
                    && (IsTabRole(registration.Role) || IsOverflowRow(registration))
                => ActivateTabbedPage(page),
            SearchHandler => TryNativeElementFocus(elementId),
            _ => null
        };
        if (semanticResult is null
            && registration.NativeElement is MenuItem logicalAction)
        {
            semanticResult = ActivateMenuItem(logicalAction);
        }

        if (semanticResult is not null)
            return semanticResult;

        return TryInvokeRegisteredNativeElement(elementId, registration.NativeElement)
            ?? $"Native element '{elementId}' owner '{registration.Owner.GetType().FullName}' does not support invoke";
    }

    protected virtual string? TryInvokeRegisteredNativeElement(
        string elementId,
        object nativeElement)
        => null;

    protected internal virtual string TryNativeElementTap(
        string elementId,
        object nativeElement)
    {
        if (elementId.StartsWith(RegisteredNativeElementPrefix, StringComparison.Ordinal)
            && (_nativeElementRegistry?.TryGet(elementId, out var registration) != true
                || !ReferenceEquals(registration.NativeElement, nativeElement)))
        {
            return $"Native element '{elementId}' is stale";
        }

        return TryNativeElementTap(elementId);
    }

    public virtual async Task<string> TryRegisteredNativeElementTapAsync(string elementId)
    {
        if (_nativeElementRegistry?.TryGet(elementId, out var registration) != true)
            return $"Native element '{elementId}' was not found";

        if (registration.Owner is Page page
            && registration.Role.Equals("BackButton", StringComparison.Ordinal))
        {
            return await ActivateBackButtonAsync(page);
        }

        return TryNativeElementTap(elementId);
    }

    protected internal virtual async Task<string> TryRegisteredNativeElementTapAsync(
        string elementId,
        object nativeElement)
    {
        if (_nativeElementRegistry?.TryGet(elementId, out var registration) != true
            || !ReferenceEquals(registration.NativeElement, nativeElement))
        {
            return $"Native element '{elementId}' is stale";
        }

        if (registration.Owner is Page page
            && registration.Role.Equals("BackButton", StringComparison.Ordinal))
        {
            return await ActivateBackButtonAsync(page);
        }

        return TryNativeElementTap(elementId, nativeElement);
    }

    /// <summary>
    /// Sets native element text/value for a native element id.
    /// </summary>
    public virtual string TryNativeElementSetValue(string elementId, string value)
    {
        if (!elementId.StartsWith(RegisteredNativeElementPrefix, StringComparison.Ordinal))
            return "Native element value actions are not supported on this platform";

        if (_nativeElementRegistry?.TryGet(elementId, out var registration) != true)
            return $"Native element '{elementId}' was not found";

        if (registration.Owner is SearchHandler searchHandler)
        {
            searchHandler.Query = value;
            _ = TrySetValueRegisteredNativeElement(elementId, registration.NativeElement, value);
            return "ok";
        }

        return TrySetValueRegisteredNativeElement(elementId, registration.NativeElement, value)
            ?? $"Native element '{elementId}' owner '{registration.Owner.GetType().FullName}' does not support writable value";
    }

    protected virtual string? TrySetValueRegisteredNativeElement(
        string elementId,
        object nativeElement,
        string value)
        => null;

    protected internal virtual string TryNativeElementSetValue(
        string elementId,
        object nativeElement,
        string value)
    {
        if (elementId.StartsWith(RegisteredNativeElementPrefix, StringComparison.Ordinal)
            && (_nativeElementRegistry?.TryGet(elementId, out var registration) != true
                || !ReferenceEquals(registration.NativeElement, nativeElement)))
        {
            return $"Native element '{elementId}' is stale";
        }

        return TryNativeElementSetValue(elementId, value);
    }

    /// <summary>
    /// Sets native keyboard focus for a native element id.
    /// </summary>
    public virtual string TryNativeElementFocus(string elementId)
        => "Native element focus is not supported on this platform";

    protected internal virtual string TryNativeElementFocus(
        string elementId,
        object nativeElement)
    {
        if (elementId.StartsWith(RegisteredNativeElementPrefix, StringComparison.Ordinal)
            && (_nativeElementRegistry?.TryGet(elementId, out var registration) != true
                || !ReferenceEquals(registration.NativeElement, nativeElement)))
        {
            return $"Native element '{elementId}' is stale";
        }

        return TryNativeElementFocus(elementId);
    }

    /// <summary>
    /// Scrolls or scrolls into view a native element id.
    /// </summary>
    public virtual string TryNativeElementScroll(string elementId, double deltaX, double deltaY)
        => "Native element scrolling is not supported on this platform";

    protected internal virtual string TryNativeElementScroll(
        string elementId,
        object nativeElement,
        double deltaX,
        double deltaY)
    {
        if (elementId.StartsWith(RegisteredNativeElementPrefix, StringComparison.Ordinal)
            && (_nativeElementRegistry?.TryGet(elementId, out var registration) != true
                || !ReferenceEquals(registration.NativeElement, nativeElement)))
        {
            return $"Native element '{elementId}' is stale";
        }

        return TryNativeElementScroll(elementId, deltaX, deltaY);
    }

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

    internal virtual ElementInfo CreateRegisteredNativeElementInfo(
        NativeElementRegistrationSnapshot registration,
        string? ownerId)
    {
        var nativeType = registration.NativeElement.GetType();
        var info = new ElementInfo
        {
            Id = registration.Id,
            IdentityToken = registration.Id,
            ParentId = ownerId,
            OwnerId = ownerId,
            Type = nativeType.Name,
            FullType = nativeType.FullName ?? nativeType.Name,
            Framework = "native",
            Origin = "native",
            Role = NormalizeRegisteredRole(registration.Role),
            Discriminator = registration.Discriminator,
            IsVisible = true,
            IsEnabled = true,
            BoundsQuality = "unknown",
            Capabilities = GetRegisteredNativeCapabilities(registration),
            NativeType = nativeType.FullName,
            NativeProperties = new Dictionary<string, string?>
            {
                ["ownerType"] = registration.Owner.GetType().FullName,
                ["registrationRole"] = registration.Role
            }
        };
        if (registration.NativeElement is MenuItem logicalAction)
        {
            info.Type = "DialogAction";
            info.Text = logicalAction.Text;
            info.AutomationId = logicalAction.AutomationId;
            info.IsEnabled = logicalAction.IsEnabled;
        }

        return info;
    }

    private List<string> GetRegisteredNativeCapabilities(
        NativeElementRegistrationSnapshot registration)
    {
        var capabilities = new List<string> { "select" };
        if (registration.Role.Equals("ShellTabOverflow", StringComparison.Ordinal)
            && registration.Discriminator?.Equals("TabBarItem", StringComparison.Ordinal) == true)
        {
            capabilities.Add("invoke");
        }
        else if (registration.Owner is MenuItem
            || registration.NativeElement is MenuItem
            || registration.Owner is ShellSection or ShellContent
                && (!registration.Role.Equals("ShellTabOverflow", StringComparison.Ordinal)
                    || IsOverflowRow(registration))
            || registration.Owner is ShellItem
                && !registration.Role.Equals("ShellTabOverflow", StringComparison.Ordinal)
            || registration.Owner is Shell
                && registration.Role.Equals("ShellFlyoutToggle", StringComparison.Ordinal)
            || registration.Owner is Page
                && registration.Role.Equals("BackButton", StringComparison.Ordinal))
        {
            capabilities.Add("invoke");
        }
        else if (registration.Owner is Page { Parent: TabbedPage }
            && (IsTabRole(registration.Role) || IsOverflowRow(registration)))
        {
            capabilities.Add("invoke");
        }
        else if (registration.Owner is SearchHandler)
        {
            capabilities.Add("invoke");
            capabilities.Add("focus");
            capabilities.Add("set-value");
        }
        else if (CanInvokeRegisteredNativeElement(registration.NativeElement))
        {
            capabilities.Add("invoke");
        }

        if (CanFocusRegisteredNativeElement(registration.NativeElement)
            && !capabilities.Contains("focus", StringComparer.Ordinal))
        {
            capabilities.Add("focus");
        }

        if (CanSetValueRegisteredNativeElement(registration.NativeElement)
            && !capabilities.Contains("set-value", StringComparer.Ordinal))
        {
            capabilities.Add("set-value");
        }

        return capabilities;
    }

    protected virtual bool CanInvokeRegisteredNativeElement(object nativeElement)
        => false;

    protected virtual bool CanFocusRegisteredNativeElement(object nativeElement)
        => false;

    protected virtual bool CanSetValueRegisteredNativeElement(object nativeElement)
        => false;

    private static string ActivateMenuItem(MenuItem menuItem)
    {
        ((IMenuItemController)menuItem).Activate();
        return "ok";
    }

    private static string ToggleShellFlyout(Shell shell)
    {
        if (shell.FlyoutBehavior == FlyoutBehavior.Locked)
            return "Shell flyout is locked and cannot be toggled";

        shell.FlyoutIsPresented = !shell.FlyoutIsPresented;
        return "ok";
    }

    private static string ActivateTabbedPage(Page page)
    {
        if (page.Parent is not TabbedPage tabbedPage)
            return $"Tab page '{page.Title}' is not attached to a TabbedPage";
        if (!page.IsEnabled)
            return $"Tab page '{page.Title}' is disabled";

        tabbedPage.CurrentPage = page;
        return "ok";
    }

    private static bool IsTabRole(string role)
        => role.Equals("ShellTab", StringComparison.Ordinal);

    private static bool IsOverflowRow(NativeElementRegistrationSnapshot registration)
        => registration.Role.Equals("ShellTabOverflow", StringComparison.Ordinal)
            && registration.Discriminator?.Equals("OverflowRow", StringComparison.Ordinal) == true;

    private static async Task<string> ActivateBackButtonAsync(Page page)
    {
        try
        {
            var behavior = GetEffectiveBackButtonBehavior(page);
            if (behavior is { IsEnabled: false })
                return "Back button is disabled";
            if (behavior?.Command is { } command)
            {
                if (!command.CanExecute(behavior.CommandParameter))
                    return "Back button command cannot execute";
                command.Execute(behavior.CommandParameter);
                return "ok";
            }

            var navigation = page.Navigation;
            if (navigation.ModalStack.Count > 0
                && ReferenceEquals(navigation.ModalStack[^1], page))
            {
                await navigation.PopModalAsync();
                return "ok";
            }

            if (navigation.NavigationStack.Count > 1)
            {
                await navigation.PopAsync();
                return "ok";
            }

            if (FindOwningShell(page) is { } shell)
            {
                await shell.GoToAsync("..");
                return "ok";
            }

            return "No navigation stack available";
        }
        catch (Exception ex)
        {
            return $"Back navigation failed: {ex.GetBaseException().Message}";
        }
    }

    internal static async Task<string> ActivateBackButtonMarkerAsync(BackButtonMarker marker)
    {
        if (marker.Page is not null && !IsNavigationBarVisible(marker.Page))
            return "Back button is hidden";
        if (marker.Behavior is { IsVisible: false })
            return "Back button is hidden";
        if (marker.Behavior is { IsEnabled: false })
            return "Back button is disabled";
        if (marker.Behavior?.Command is { } command)
        {
            if (!command.CanExecute(marker.Behavior.CommandParameter))
                return "Back button command cannot execute";
            command.Execute(marker.Behavior.CommandParameter);
            return "ok";
        }

        await marker.Navigation.PopAsync();
        return "ok";
    }

    private static Shell? FindOwningShell(Page page)
    {
        Element? current = page;
        while (current is not null)
        {
            if (current is Shell shell)
                return shell;
            current = current.Parent;
        }

        var app = Application.Current;
        if (app is null)
            return null;

        foreach (var window in app.Windows)
        {
            if (window.Page is not Shell shell)
                continue;

            if (ReferenceEquals(shell.CurrentPage, page)
                || shell.CurrentItem?.CurrentItem?.Navigation?.NavigationStack.Contains(page) == true
                || shell.CurrentItem?.CurrentItem?.Navigation?.ModalStack.Contains(page) == true)
            {
                return shell;
            }
        }

        return null;
    }

    private static string ActivateShellItem(ShellItem shellItem, string role)
    {
        if (shellItem.Parent is not Shell shell)
            return $"Shell item '{shellItem.Title}' is not attached to a Shell";

        shell.CurrentItem = shellItem;
        CloseFlyoutAfterActivation(shell, role);
        return "ok";
    }

    private static string ActivateShellSection(ShellSection shellSection, string role)
    {
        if (shellSection.Parent is not ShellItem shellItem || shellItem.Parent is not Shell shell)
            return $"Shell section '{shellSection.Title}' is not attached to a Shell";

        shellItem.CurrentItem = shellSection;
        shell.CurrentItem = shellItem;
        CloseFlyoutAfterActivation(shell, role);
        return "ok";
    }

    private static string ActivateShellContent(ShellContent shellContent, string role)
    {
        if (shellContent.Parent is not ShellSection shellSection
            || shellSection.Parent is not ShellItem shellItem
            || shellItem.Parent is not Shell shell)
        {
            return $"Shell content '{shellContent.Title}' is not attached to a Shell";
        }

        shellSection.CurrentItem = shellContent;
        shellItem.CurrentItem = shellSection;
        shell.CurrentItem = shellItem;
        CloseFlyoutAfterActivation(shell, role);
        return "ok";
    }

    private static void CloseFlyoutAfterActivation(Shell shell, string role)
    {
        if (role.Equals("ShellFlyout", StringComparison.Ordinal)
            && shell.FlyoutBehavior != FlyoutBehavior.Locked)
            shell.FlyoutIsPresented = false;
    }

    private static string NormalizeRegisteredRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role) || role.Contains('-'))
            return role;

        var builder = new System.Text.StringBuilder(role.Length + 4);
        for (var index = 0; index < role.Length; index++)
        {
            var character = role[index];
            if (index > 0 && char.IsUpper(character))
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private void AppendRegisteredNativeElements(List<ElementInfo> roots, int maxDepth)
    {
        if (_nativeElementRegistry is null)
            return;

        var registrations = _nativeElementRegistry.GetSnapshot();
        if (registrations.Count == 0)
        {
            lock (_registeredNativeGate)
            {
                _registeredNativeInfos = new Dictionary<string, ElementInfo>(StringComparer.Ordinal);
                _elementInfoParents = new Dictionary<string, string?>(StringComparer.Ordinal);
            }
            return;
        }

        var infosById = new Dictionary<string, (ElementInfo Info, int Depth)>(StringComparer.Ordinal);
        foreach (var root in roots)
            IndexElementInfo(root, depth: 1, infosById);
        var registeredInfos = new Dictionary<string, ElementInfo>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            string? ownerId = null;
            if (!_objectToExternalId.TryGetValue(registration.Owner, out ownerId)
                && registration.Owner is Element owner
                && _elementIdToExternalId.TryGetValue(owner.Id, out var mappedOwnerId))
                ownerId = mappedOwnerId;

            if (ownerId is null
                || !infosById.TryGetValue(ownerId, out var ownerEntry)
                || (maxDepth > 0 && ownerEntry.Depth >= maxDepth))
                continue;

            var info = CreateRegisteredNativeElementInfo(registration, ownerId);
            info.WindowId = ownerEntry.Info.WindowId;
            registeredInfos[info.Id] = info;
            ownerEntry.Info.Children ??= [];
            ownerEntry.Info.Children.Add(info);
        }

        var elementInfoParents = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var elementInfo in FlattenElementInfos(roots))
            elementInfoParents[elementInfo.Id] = elementInfo.ParentId;

        lock (_registeredNativeGate)
        {
            _registeredNativeInfos = registeredInfos;
            _elementInfoParents = elementInfoParents;
        }
    }

    private static void IndexElementInfo(
        ElementInfo info,
        int depth,
        Dictionary<string, (ElementInfo Info, int Depth)> infosById)
    {
        infosById[info.Id] = (info, depth);
        if (info.Children is null)
            return;

        foreach (var child in info.Children)
            IndexElementInfo(child, depth + 1, infosById);
    }

    private static void SetWindowId(ElementInfo info, int windowId)
    {
        info.WindowId = windowId;
        if (info.Children is null)
            return;

        foreach (var child in info.Children)
            SetWindowId(child, windowId);
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
        var id = GenerateId(element);
        var info = CreateElementInfo(element, id, parentId);

        if (maxDepth > 0 && currentDepth >= maxDepth)
            return info;

        info.Children ??= new List<ElementInfo>();

        var children = element.GetVisualChildren();
        foreach (var child in children)
        {
            var childInfo = WalkElement(child, id, currentDepth + 1, maxDepth);
            if (childInfo != null)
                info.Children.Add(childInfo);
        }

        // ShellContent-specific: ensure content page is included even if
        // GetVisualChildren() doesn't expose it (common on GTK/Linux after navigation).
        if (element is ShellContent sc && sc.Content is IVisualTreeElement scPage
            && !children.Contains(scPage))
        {
            var pageInfo = WalkElement(scPage, id, currentDepth + 1, maxDepth);
            if (pageInfo != null)
                info.Children.Add(pageInfo);
        }

        // Add ToolbarItems as synthetic children of Pages
        if (element is Page page)
        {
            // NavBarTitle — inject page title as synthetic element
            AddNavBarTitle(page, id, info);

            foreach (var toolbarItem in page.ToolbarItems)
            {
                var tiInfo = CreateToolbarItemInfo(toolbarItem, id);
                info.Children.Add(tiInfo);
            }

            // Add synthetic back button when there's a navigation stack
            var backInfo = CreateBackButtonInfo(page, id);
            if (backInfo != null)
                info.Children.Add(backInfo);

            // SearchHandler (Shell only)
            AddSearchHandler(page, id, info);
        }

        // Shell-level synthetics: flyout button, flyout items, tab bar
        if (element is Shell shell)
        {
            AddShellSynthetics(shell, id, info);
        }

        // NavigationPage-level synthetics
        if (element is NavigationPage navPage2)
        {
            AddNavigationPageSynthetics(navPage2, id, info);
        }

        // FlyoutPage-level synthetics
        if (element is FlyoutPage flyoutPage)
        {
            AddFlyoutPageSynthetics(flyoutPage, id, info);
        }

        // TabbedPage-level synthetics
        if (element is TabbedPage tabbedPage)
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
        return FlattenElementInfos(WalkTree(app))
            .Where(info => MatchesManagedQueryElement(info, type, automationId, text))
            .Select(info => info.WithoutChildren())
            .ToList();
    }

    private static bool MatchesManagedQueryElement(
        ElementInfo info,
        string? type,
        string? automationId,
        string? text)
    {
        if (type is not null
            && !info.Type.Equals(type, StringComparison.OrdinalIgnoreCase)
            && !info.FullType.Equals(type, StringComparison.OrdinalIgnoreCase))
            return false;

        if (automationId is not null
            && !string.Equals(info.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (text is not null
            && (info.Text is null || !info.Text.Contains(text, StringComparison.OrdinalIgnoreCase)))
            return false;

        return type is not null || automationId is not null || text is not null;
    }

    private void QueryRecursive(IVisualTreeElement element, string? type, string? automationId, string? text, string? parentId, List<ElementInfo> results)
    {
        var id = GenerateId(element);
        var info = CreateElementInfo(element, id, parentId);

        MatchAndAdd(info, type, automationId, text, results);

        var children = element.GetVisualChildren();
        foreach (var child in children)
            QueryRecursive(child, type, automationId, text, id, results);

        // ShellContent-specific: include content page if not already traversed
        if (element is ShellContent sc && sc.Content is IVisualTreeElement scPage
            && !children.Contains(scPage))
        {
            QueryRecursive(scPage, type, automationId, text, id, results);
        }

        // Also query ToolbarItems and back button on Pages
        if (element is Page page)
        {
            foreach (var toolbarItem in page.ToolbarItems)
            {
                var tiInfo = CreateToolbarItemInfo(toolbarItem, id);
                MatchAndAdd(tiInfo, type, automationId, text, results);
            }

            var backInfo = CreateBackButtonInfo(page, id);
            if (backInfo != null)
                MatchAndAdd(backInfo, type, automationId, text, results);
        }
    }

    private static void MatchAndAdd(ElementInfo info, string? type, string? automationId, string? text, List<ElementInfo> results)
    {
        bool matches = true;

        if (type != null && !info.Type.Equals(type, StringComparison.OrdinalIgnoreCase)
            && !info.FullType.Equals(type, StringComparison.OrdinalIgnoreCase))
            matches = false;

        if (automationId != null && !string.Equals(info.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
            matches = false;

        if (text != null && (info.Text == null || !info.Text.Contains(text, StringComparison.OrdinalIgnoreCase)))
            matches = false;

        if (matches && (type != null || automationId != null || text != null))
            results.Add(info);
    }

    /// <summary>
    /// Generates a stable ID for a MAUI visual tree element using Element.Id.
    /// </summary>
    private string GenerateId(IVisualTreeElement element)
    {
        // Check if we've already generated an ID for this Element.Id in this walk
        if (element is Element el && _elementIdToExternalId.TryGetValue(el.Id, out var cachedId))
            return cachedId;

        string id;
        if (element is VisualElement ve && !string.IsNullOrEmpty(ve.AutomationId))
        {
            id = ApplyWindowScope(ve.AutomationId);
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
            id = ApplyWindowScope(elem.Id.ToString("N")[..12]);
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
                id = ApplyWindowScope(automId);
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
                id = ApplyWindowScope(
                    platformId ?? RuntimeHelpers.GetHashCode(element).ToString("x8"));
            }
        }

        _usedIds.Add(id);
        if (element is Element elFinal)
            _elementIdToExternalId[elFinal.Id] = id;
        _objectToExternalId.TryAdd(element, id);
        return id;
    }

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
                id = ApplyWindowScope(automationId);
                if (_usedIds.Contains(id))
                    id = $"{id}_{el.Id.ToString("N")[..8]}";
            }
            else
            {
                // No automationId — use Element.Id directly, but check for collisions
                // with MAUI elements that share the same Element.Id
                id = ApplyWindowScope(el.Id.ToString("N")[..12]);
                if (_usedIds.Contains(id))
                    id = $"{id}_{RuntimeHelpers.GetHashCode(element):x8}";
            }

            _usedIds.Add(id);
            _elementIdToExternalId.TryAdd(el.Id, id);
            _objectToExternalId.TryAdd(identity, id);
            return id;
        }

        // Non-Element backing — use automationId + platform stamp or hash
        if (!string.IsNullOrEmpty(automationId))
        {
            var id = ApplyWindowScope(automationId);
            if (_usedIds.Contains(id))
            {
                var platformId = EnsurePlatformStableId(identity);
                var suffix = platformId ?? RuntimeHelpers.GetHashCode(identity).ToString("x8");
                id = $"{id}_{suffix[..Math.Min(8, suffix.Length)]}";
            }
            _usedIds.Add(id);
            _objectToExternalId.TryAdd(identity, id);
            return id;
        }

        // Last resort
        {
            var platformId = EnsurePlatformStableId(identity);
            var id = ApplyWindowScope(
                platformId ?? RuntimeHelpers.GetHashCode(identity).ToString("x8"));
            if (_usedIds.Contains(id))
                id = $"{id}_{RuntimeHelpers.GetHashCode(element):x8}";
            _usedIds.Add(id);
            _objectToExternalId.TryAdd(identity, id);
            return id;
        }
    }

    private string ApplyWindowScope(string id)
        => _currentWindowId > 0
            ? $"{id}:window:{_currentWindowId}"
            : id;

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
        BackButtonMarker m => m.Behavior is not null ? m.Behavior : m.Navigation,
        _ => element
    };

    internal virtual object GetElementIdentity(object element) => GetStableIdentity(element);

    internal virtual bool AreElementIdentitiesEqual(object first, object second)
        => ReferenceEquals(first, second);

    internal virtual bool ShouldRetainElementIdentityStrongly(object identity)
        => identity.GetType().IsValueType || identity is string;

    /// <summary>
    /// Recursively walks the tree searching for an element whose generated ID matches targetId.
    /// Returns the element/marker or null. Early exits on match.
    /// </summary>
    private object? FindByIdRecursive(IVisualTreeElement element, string targetId, string? parentId)
    {
        var id = GenerateId(element);
        if (id == targetId) return element;

        var children = element.GetVisualChildren();

        // Check synthetics on Pages
        if (element is Page page)
        {
            // NavBarTitle
            var navBarResult = FindNavBarTitleById(page, id, targetId);
            if (navBarResult != null) return navBarResult;

            // ToolbarItems
            foreach (var toolbarItem in page.ToolbarItems)
            {
                var tiId = GenerateObjectId(toolbarItem, toolbarItem.AutomationId);
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

        // ShellContent special case
        if (element is ShellContent sc && sc.Content is IVisualTreeElement scPage
            && !children.Contains(scPage))
        {
            var result = FindByIdRecursive(scPage, targetId, id);
            if (result != null) return result;
        }

        return null;
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
        if (!IsNavigationBarVisible(page))
            return null;

        var (nav, title) = ResolveBackNavigation(page);
        if (nav == null) return null;

        var marker = new BackButtonMarker
        {
            Navigation = nav,
            Title = title,
            Behavior = GetEffectiveBackButtonBehavior(page),
            Page = page
        };
        var id = GenerateObjectId(marker, "BackButton");
        return id == targetId ? marker : null;
    }

    private object? FindSearchHandlerById(Page page, string parentId, string targetId)
    {
        try
        {
            var handler = Shell.GetSearchHandler(page);
            if (handler == null) return null;

            var marker = new SearchHandlerMarker { Handler = handler };
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
        if (!IsNavigationBarVisible(page))
            return;

        var title = page.Title;
        if (string.IsNullOrEmpty(title)) return;

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

            var marker = new SearchHandlerMarker { Handler = handler };
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
            if (shell.FlyoutFooter is View footerView && footerView is IVisualTreeElement footerVte)
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
    public class SearchHandlerMarker { public SearchHandler Handler { get; init; } = null!; }
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
        info.IdentityToken = GetStableIdentity(marker);
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
        var id = GenerateObjectId(item, item.AutomationId);
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

    private ElementInfo? CreateBackButtonInfo(Page page, string parentId)
    {
        if (!IsNavigationBarVisible(page))
            return null;

        var (nav, title) = ResolveBackNavigation(page);
        if (nav == null) return null;

        var marker = new BackButtonMarker
        {
            Navigation = nav,
            Title = title,
            Behavior = GetEffectiveBackButtonBehavior(page),
            Page = page
        };
        var id = GenerateObjectId(marker, "BackButton");
        return new ElementInfo
        {
            Id = id,
            IdentityToken = GetStableIdentity(marker),
            ParentId = parentId,
            Type = "BackButton",
            FullType = "Microsoft.Maui.DevFlow.Agent.Core.BackButton",
            AutomationId = "BackButton",
            Text = $"← {title}",
            IsVisible = marker.Behavior?.IsVisible ?? true,
            IsEnabled = marker.Behavior?.IsEnabled ?? true,
        };
    }

    private static bool IsNavigationBarVisible(Page page)
    {
        try
        {
            if (page.Parent is Shell || FindAncestor<Shell>(page) is not null)
                return Shell.GetNavBarIsVisible(page);
            if (page.Parent is NavigationPage || FindAncestor<NavigationPage>(page) is not null)
                return NavigationPage.GetHasNavigationBar(page);
        }
        catch
        {
        }

        return true;
    }

    private static BackButtonBehavior? GetEffectiveBackButtonBehavior(Page page)
    {
        var behavior = Shell.GetBackButtonBehavior(page);
        if (behavior is not null)
            return behavior;

        Element? current = page.Parent;
        while (current is not null)
        {
            if (current is Shell shell)
                return Shell.GetBackButtonBehavior(shell);
            current = current.Parent;
        }

        return null;
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
        var cometResolved = CometViewResolver.TryResolveCometView(element);

        var info = new ElementInfo
        {
            Id = id,
            IdentityToken = GetStableIdentity(element),
            WindowId = _currentWindowId,
            ParentId = parentId,
            Type = cometResolved?.Type ?? element.GetType().Name,
            FullType = cometResolved?.FullType ?? element.GetType().FullName ?? element.GetType().Name,
        };

        if (element is VisualElement ve)
        {
            info.AutomationId = ve.AutomationId;
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
            if (ve is NavigableElement ne && ne.StyleClass is { Count: > 0 } sc)
                info.StyleClass = sc.ToList();

            // Populate native view info from handler
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
            if (element is IText iText && !string.IsNullOrEmpty(iText.Text))
            {
                info.Text = element is Entry entry
                    ? SensitiveValueRedactor.Redact(iText.Text, entry.IsPassword)
                    : iText.Text;
            }
        }

        // Extract text from common controls (including Shell elements)
        info.Text ??= element switch
        {
            Label l => l.Text,
            Button b => b.Text,
            Entry e => SensitiveValueRedactor.Redact(e.Text, e.IsPassword),
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

        info.IsSelected = element switch
        {
            CheckBox cb => cb.IsChecked,
            RadioButton rb => rb.IsChecked,
            Switch sw => sw.IsToggled,
            Picker pk => pk.SelectedIndex >= 0,
            _ => info.IsSelected
        };

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
        if (element is ItemsView itemsView)
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
        if (cometResolved != null)
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
    }
}
