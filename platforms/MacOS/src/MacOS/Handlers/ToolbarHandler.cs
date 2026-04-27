using System.Collections.Specialized;
using Foundation;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Platform;
using AppKit;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Manages an NSToolbar on the NSWindow, populated from Page.ToolbarItems.
/// Also renders a navigation back button and sets the window title from NavigationPage.
/// </summary>
public class MacOSToolbarManager : NSObject, INSToolbarDelegate
{
    const string ToolbarIdPrefix = "MauiToolbar_";
    const string ItemIdPrefix = "MauiToolbarItem_";
    const string SidebarItemIdPrefix = "MauiSidebarItem_";
    const string FlexibleSpaceId = "NSToolbarFlexibleSpaceItem";
    const string FixedSpaceId = "NSToolbarSpaceItem";
    const string SeparatorId = "NSToolbarSeparatorItem";
    const string SidebarToggleId = "MauiSidebarToggle";
    const string TrackingSeparatorId = "MauiTrackingSeparator";
    const string BackButtonId = "MauiBackButton";
    const string TitleId = "MauiTitle";
    const string SearchId = "MauiSearchItem";
    const string MenuIdPrefix = "MauiMenu_";
    const string GroupIdPrefix = "MauiGroup_";
    const string ShareId = "MauiShareItem";
    const string PopUpIdPrefix = "MauiPopUp_";
    const string ViewIdPrefix = "MauiView_";
    const nint SidebarItemTagOffset = 100000;
    static int _toolbarCounter;

    static string SystemItemIdentifier(SystemItemKind kind) => kind switch
    {
        SystemItemKind.ToggleSidebar => NSToolbar.NSToolbarToggleSidebarItemIdentifier,
        SystemItemKind.ToggleInspector => NSToolbar.NSToolbarToggleInspectorItemIdentifier,
        SystemItemKind.CloudSharing => NSToolbar.NSToolbarCloudSharingItemIdentifier,
        SystemItemKind.Print => NSToolbar.NSToolbarPrintItemIdentifier,
        SystemItemKind.ShowColors => NSToolbar.NSToolbarShowColorsItemIdentifier,
        SystemItemKind.ShowFonts => NSToolbar.NSToolbarShowFontsItemIdentifier,
        SystemItemKind.WritingTools => NSToolbar.NSToolbarWritingToolsItemIdentifier,
        SystemItemKind.InspectorTrackingSeparator => NSToolbar.NSToolbarInspectorTrackingSeparatorItemIdentifier,
        _ => NSToolbar.NSToolbarFlexibleSpaceItemIdentifier,
    };

    NSWindow? _window;
    NSToolbar? _toolbar;
    readonly List<ToolbarItem> _items = new();
    readonly List<ToolbarItem> _sidebarItems = new();
    readonly List<string> _itemIdentifiers = new();
    Page? _currentPage;
    FlyoutPage? _flyoutPage;
    NavigationPage? _navigationPage;
    Shell? _shell;
    NSSplitView? _splitView;
    MacOSSearchToolbarItem? _searchItem;
    NSSearchToolbarItem? _nativeSearchItem;
    readonly List<MacOSMenuToolbarItem> _menuItems = new();
    readonly List<MacOSToolbarItemGroup> _groupItems = new();
    MacOSShareToolbarItem? _shareItem;
    readonly List<MacOSPopUpToolbarItem> _popUpItems = new();
    readonly List<MacOSViewToolbarItem> _viewItems = new();
    bool _isRefreshing;

    public void AttachToWindow(NSWindow window)
    {
        _window = window;
        var toolbarId = $"{ToolbarIdPrefix}{_toolbarCounter++}";
        _toolbar = new NSToolbar(toolbarId)
        {
            Delegate = this,
            DisplayMode = NSToolbarDisplayMode.Icon,
            AllowsUserCustomization = false,
        };
        _window.Toolbar = _toolbar;
    }

    /// <summary>
    /// Sets the split view used by NSTrackingSeparatorToolbarItem to align the
    /// toolbar divider with the sidebar/content divider.
    /// </summary>
    public void SetSplitView(NSSplitView? splitView)
    {
        _splitView = splitView;
    }

    public void SetPage(Page? page)
    {
        if (_currentPage != null)
        {
            UnsubscribeCommands();
            if (_currentPage.ToolbarItems is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= OnToolbarItemsChanged;
        }

        _currentPage = page;

        // Walk up the page hierarchy to find FlyoutPage and NavigationPage
        _flyoutPage = FindAncestor<FlyoutPage>(page);
        _navigationPage = FindAncestor<NavigationPage>(page);
        _shell = FindShell(page);

        // Update the window title from the current page
        if (_window != null && page != null)
            _window.Title = page.Title ?? string.Empty;

        if (_currentPage != null)
        {
            if (_currentPage.ToolbarItems is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += OnToolbarItemsChanged;
            RefreshToolbar(_currentPage.ToolbarItems);
        }
        else
        {
            RefreshToolbar(null);
        }
    }

    static T? FindAncestor<T>(Page? page) where T : Page
    {
        while (page != null)
        {
            if (page is T match)
                return match;
            page = page.Parent as Page;
        }
        return null;
    }

    static Shell? FindShell(Page? page)
    {
        Element? element = page;
        while (element != null)
        {
            if (element is Shell shell)
                return shell;
            element = element.Parent;
        }
        // Pushed pages may not have a parent chain to Shell yet.
        // Fall back to checking the Window's root page.
        try
        {
            if (page?.Window?.Page is Shell windowShell)
                return windowShell;
        }
        catch { }
        return null;
    }

    void OnToolbarItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_currentPage != null)
            RefreshToolbar(_currentPage.ToolbarItems);
    }

    void UnsubscribeCommands()
    {
        foreach (var item in _items)
            item.PropertyChanged -= OnToolbarItemPropertyChanged;
        foreach (var item in _sidebarItems)
            item.PropertyChanged -= OnToolbarItemPropertyChanged;
    }

    bool ShouldShowBackButton()
    {
        // Respect HasNavigationBar on the current page
        if (_currentPage != null && !NavigationPage.GetHasNavigationBar(_currentPage))
            return false;

        if (_navigationPage != null)
            return _navigationPage.Navigation.NavigationStack.Count > 1;

        // Shell: check ShellSection's navigation stack
        if (_shell?.CurrentItem?.CurrentItem is ShellSection section)
        {
            var navStack = section.Navigation?.NavigationStack;
            if (navStack != null && navStack.Count > 1)
                return true;
        }

        return false;
    }

    string? GetBackButtonTitle()
    {
        IReadOnlyList<Page>? stack = null;

        if (_navigationPage != null)
            stack = _navigationPage.Navigation.NavigationStack;
        else if (_shell?.CurrentItem?.CurrentItem is ShellSection section)
            stack = section.Navigation?.NavigationStack;

        if (stack == null || stack.Count <= 1)
            return null;

        var previousPage = stack[stack.Count - 2];
        if (previousPage == null)
            return "Back";
        var backTitle = NavigationPage.GetBackButtonTitle(previousPage);
        if (string.IsNullOrEmpty(backTitle))
            backTitle = previousPage.Title;
        return string.IsNullOrEmpty(backTitle) ? "Back" : backTitle;
    }

    void RefreshToolbar(IList<ToolbarItem>? toolbarItems)
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
        UnsubscribeCommands();
        _items.Clear();
        _sidebarItems.Clear();
        _itemIdentifiers.Clear();
        CleanupSearchItem();

        bool hasBackButton = ShouldShowBackButton();

        // Resolve search item — can come from explicit layout or page-level property
        _searchItem = _currentPage != null ? MacOSToolbar.GetSearchItem(_currentPage) : null;

        // Resolve other special toolbar items from page-level properties
        _menuItems.Clear();
        _groupItems.Clear();
        _popUpItems.Clear();
        _shareItem = _currentPage != null ? MacOSToolbar.GetShareItem(_currentPage) : null;
        var menuItems = _currentPage != null ? MacOSToolbar.GetMenuItems(_currentPage) : null;
        if (menuItems != null) _menuItems.AddRange(menuItems);
        var groupItems = _currentPage != null ? MacOSToolbar.GetItemGroups(_currentPage) : null;
        if (groupItems != null) _groupItems.AddRange(groupItems);
        var popUpItems = _currentPage != null ? MacOSToolbar.GetPopUpItems(_currentPage) : null;
        if (popUpItems != null) _popUpItems.AddRange(popUpItems);
        _viewItems.Clear();
        var viewItems = _currentPage != null ? MacOSToolbar.GetViewItems(_currentPage) : null;
        if (viewItems != null) _viewItems.AddRange(viewItems);

        // Register owner page on special items so IsVisible changes can trigger refresh
        if (_currentPage != null)
        {
            if (_searchItem != null) MacOSToolbarItem.SetOwnerPage(_searchItem, _currentPage);
            if (_shareItem != null) MacOSToolbarItem.SetOwnerPage(_shareItem, _currentPage);
            foreach (var m in _menuItems) MacOSToolbarItem.SetOwnerPage(m, _currentPage);
            foreach (var g in _groupItems) MacOSToolbarItem.SetOwnerPage(g, _currentPage);
            foreach (var p in _popUpItems) MacOSToolbarItem.SetOwnerPage(p, _currentPage);
            foreach (var v in _viewItems) MacOSToolbarItem.SetOwnerPage(v, _currentPage);
        }

        // Check for explicit layouts on the current page
        var explicitSidebarLayout = _currentPage != null
            ? MacOSToolbar.GetSidebarLayout(_currentPage) : null;
        bool hasExplicitSidebarLayout = explicitSidebarLayout != null && explicitSidebarLayout.Count > 0;

        var explicitContentLayout = _currentPage != null
            ? MacOSToolbar.GetContentLayout(_currentPage) : null;
        bool hasExplicitContentLayout = explicitContentLayout != null && explicitContentLayout.Count > 0;

        // Partition toolbar items into sidebar and content
        var contentItems = new List<ToolbarItem>();
        var sidebarLeading = new List<ToolbarItem>();
        var sidebarCenter = new List<ToolbarItem>();
        var sidebarTrailing = new List<ToolbarItem>();

        // Collect items referenced by explicit layouts for quick lookup.
        // This includes regular ToolbarItems AND special items (menu, group, popup, share, search)
        // so they aren't duplicated when convenience mode also processes them.
        HashSet<ToolbarItem> explicitItems = new();
        HashSet<object> explicitSpecialItems = new();
        void CollectExplicitLayoutItems(IList<MacOSToolbarLayoutItem> layout)
        {
            foreach (var entry in layout)
            {
                if (entry is ToolbarItemLayoutRef itemRef)
                    explicitItems.Add(itemRef.ToolbarItem);
                else if (entry is MenuLayoutRef menuRef)
                    explicitSpecialItems.Add(menuRef.MenuItem);
                else if (entry is GroupLayoutRef groupRef)
                    explicitSpecialItems.Add(groupRef.Group);
                else if (entry is PopUpLayoutRef popUpRef)
                    explicitSpecialItems.Add(popUpRef.PopUpItem);
                else if (entry is ShareLayoutRef)
                    explicitSpecialItems.Add("__share__");
                else if (entry is SearchLayoutRef)
                    explicitSpecialItems.Add("__search__");
            }
        }
        if (hasExplicitSidebarLayout)
            CollectExplicitLayoutItems(explicitSidebarLayout!);
        if (hasExplicitContentLayout)
            CollectExplicitLayoutItems(explicitContentLayout!);

        if (toolbarItems != null)
        {
            foreach (var item in toolbarItems)
            {
                // Skip sentinel items used to trigger refresh
                if (item.AutomationId == "__MacOSToolbar_Sentinel__")
                    continue;

                if (item.Order == ToolbarItemOrder.Secondary)
                    continue;

                // Items claimed by an explicit layout are handled in the layout walk
                if (explicitItems.Contains(item))
                    continue;

                if (hasExplicitSidebarLayout)
                {
                    // Explicit sidebar mode: unclaimed items go to content
                    contentItems.Add(item);
                }
                else
                {
                    // Convenience mode: partition by Placement property
                    var placement = MacOSToolbarItem.GetPlacement(item);
                    switch (placement)
                    {
                        case MacOSToolbarItemPlacement.SidebarLeading:
                            sidebarLeading.Add(item);
                            break;
                        case MacOSToolbarItemPlacement.SidebarCenter:
                            sidebarCenter.Add(item);
                            break;
                        case MacOSToolbarItemPlacement.SidebarTrailing:
                            sidebarTrailing.Add(item);
                            break;
                        default:
                            contentItems.Add(item);
                            break;
                    }
                }
            }
        }

        bool hasSpecialItems = _searchItem != null || _menuItems.Count > 0
            || _groupItems.Count > 0 || _shareItem != null || _popUpItems.Count > 0
            || _viewItems.Count > 0;
        bool hasContentItems = contentItems.Count > 0 || hasExplicitContentLayout || hasSpecialItems;
        bool hasSidebarItems = hasExplicitSidebarLayout
            || sidebarLeading.Count > 0 || sidebarCenter.Count > 0 || sidebarTrailing.Count > 0;
        bool hasToolbarItems = hasContentItems || hasSidebarItems;

        // Only show the toolbar if there's meaningful content
        bool needsToolbar = hasBackButton || hasToolbarItems;

        if (!needsToolbar)
        {
            // When a sidebar split view controller is active (Shell or FlyoutPage
            // with UseNativeSidebar), the toolbar must remain attached (even if empty)
            // for AllowsFullHeightLayout to extend the sidebar under the titlebar.
            bool hasNativeSidebar = _shell != null
                || (_flyoutPage != null && MacOSFlyoutPage.GetUseNativeSidebar(_flyoutPage));

            if (!hasNativeSidebar)
            {
                if (_window?.Toolbar != null)
                    _window.Toolbar = null;
                return;
            }

            // Keep toolbar attached but clear its items — add tracking separator
            // if we have a split view so the divider aligns
            if (_window != null && _window.Toolbar == null && _toolbar != null)
                _window.Toolbar = _toolbar;

            if (_toolbar != null)
            {
                while (_toolbar.Items.Length > 0)
                    _toolbar.RemoveItem(0);

                if (_splitView != null)
                    _toolbar.InsertItem(TrackingSeparatorId, 0);
            }
            return;
        }

        // === Build toolbar item list ===

        // Sidebar-placed toolbar items
        int sidebarIdx = 0;

        if (hasExplicitSidebarLayout)
        {
            // Explicit layout: walk the layout array and emit items/spacers in order
            foreach (var entry in explicitSidebarLayout!)
            {
                if (entry is ToolbarItemLayoutRef itemRef)
                {
                    var id = $"{SidebarItemIdPrefix}{sidebarIdx}";
                    _sidebarItems.Add(itemRef.ToolbarItem);
                    _itemIdentifiers.Add(id);
                    itemRef.ToolbarItem.PropertyChanged += OnToolbarItemPropertyChanged;
                    sidebarIdx++;
                }
                else if (entry is SearchLayoutRef)
                {
                    _itemIdentifiers.Add(SearchId);
                }
                else if (entry is MenuLayoutRef menuRef)
                {
                    int mIdx = _menuItems.IndexOf(menuRef.MenuItem);
                    if (mIdx < 0) { _menuItems.Add(menuRef.MenuItem); mIdx = _menuItems.Count - 1; }
                    _itemIdentifiers.Add($"{MenuIdPrefix}{mIdx}");
                }
                else if (entry is GroupLayoutRef groupRef)
                {
                    int gIdx = _groupItems.IndexOf(groupRef.Group);
                    if (gIdx < 0) { _groupItems.Add(groupRef.Group); gIdx = _groupItems.Count - 1; }
                    _itemIdentifiers.Add($"{GroupIdPrefix}{gIdx}");
                }
                else if (entry is ShareLayoutRef)
                {
                    _itemIdentifiers.Add(ShareId);
                }
                else if (entry is PopUpLayoutRef popUpRef)
                {
                    int pIdx = _popUpItems.IndexOf(popUpRef.PopUpItem);
                    if (pIdx < 0) { _popUpItems.Add(popUpRef.PopUpItem); pIdx = _popUpItems.Count - 1; }
                    _itemIdentifiers.Add($"{PopUpIdPrefix}{pIdx}");
                }
                else if (entry is SystemItemLayoutItem sysItem)
                {
                    _itemIdentifiers.Add(SystemItemIdentifier(sysItem.Kind));
                }
                else if (entry is SpacerLayoutItem spacer)
                {
                    _itemIdentifiers.Add(spacer.Kind switch
                    {
                        SpacerKind.Flexible => FlexibleSpaceId,
                        SpacerKind.Fixed => FixedSpaceId,
                        SpacerKind.Separator => SeparatorId,
                        _ => FlexibleSpaceId,
                    });
                }
            }
        }
        else
        {
            // Convenience mode: [Leading] <flex> [Center] <flex> [Trailing]
            foreach (var item in sidebarLeading)
            {
                var id = $"{SidebarItemIdPrefix}{sidebarIdx}";
                _sidebarItems.Add(item);
                _itemIdentifiers.Add(id);
                item.PropertyChanged += OnToolbarItemPropertyChanged;
                sidebarIdx++;
            }

            if (sidebarLeading.Count > 0 && (sidebarCenter.Count > 0 || sidebarTrailing.Count > 0))
                _itemIdentifiers.Add(FlexibleSpaceId);
            else if (sidebarLeading.Count == 0 && sidebarCenter.Count > 0)
                _itemIdentifiers.Add(FlexibleSpaceId);

            foreach (var item in sidebarCenter)
            {
                var id = $"{SidebarItemIdPrefix}{sidebarIdx}";
                _sidebarItems.Add(item);
                _itemIdentifiers.Add(id);
                item.PropertyChanged += OnToolbarItemPropertyChanged;
                sidebarIdx++;
            }

            if (sidebarCenter.Count > 0 && sidebarTrailing.Count > 0)
                _itemIdentifiers.Add(FlexibleSpaceId);
            else if (sidebarCenter.Count == 0 && sidebarLeading.Count == 0 && sidebarTrailing.Count > 0)
                _itemIdentifiers.Add(FlexibleSpaceId);

            foreach (var item in sidebarTrailing)
            {
                var id = $"{SidebarItemIdPrefix}{sidebarIdx}";
                _sidebarItems.Add(item);
                _itemIdentifiers.Add(id);
                item.PropertyChanged += OnToolbarItemPropertyChanged;
                sidebarIdx++;
            }

            // Search item in sidebar (convenience mode — only when placed in sidebar
            // and not already claimed by an explicit layout)
            if (_searchItem != null && _searchItem.Placement != MacOSToolbarItemPlacement.Content
                && !explicitSpecialItems.Contains("__search__"))
                _itemIdentifiers.Add(SearchId);
        }

        // Tracking separator — divides sidebar area from content area
        if (_splitView != null)
            _itemIdentifiers.Add(TrackingSeparatorId);

        // Back button — placed at the start of the content area (right of sidebar)
        if (hasBackButton)
            _itemIdentifiers.Add(BackButtonId);

        // Content area items (after tracking separator)
        if (hasExplicitContentLayout)
        {
            // Explicit content layout: walk the layout array
            int contentIdx = 0;
            foreach (var entry in explicitContentLayout!)
            {
                if (entry is ToolbarItemLayoutRef itemRef)
                {
                    var id = $"{ItemIdPrefix}{contentIdx}";
                    _items.Add(itemRef.ToolbarItem);
                    _itemIdentifiers.Add(id);
                    itemRef.ToolbarItem.PropertyChanged += OnToolbarItemPropertyChanged;
                    contentIdx++;
                }
                else if (entry is SearchLayoutRef)
                {
                    _itemIdentifiers.Add(SearchId);
                }
                else if (entry is MenuLayoutRef menuRef)
                {
                    int mIdx = _menuItems.IndexOf(menuRef.MenuItem);
                    if (mIdx < 0) { _menuItems.Add(menuRef.MenuItem); mIdx = _menuItems.Count - 1; }
                    _itemIdentifiers.Add($"{MenuIdPrefix}{mIdx}");
                }
                else if (entry is GroupLayoutRef groupRef)
                {
                    int gIdx = _groupItems.IndexOf(groupRef.Group);
                    if (gIdx < 0) { _groupItems.Add(groupRef.Group); gIdx = _groupItems.Count - 1; }
                    _itemIdentifiers.Add($"{GroupIdPrefix}{gIdx}");
                }
                else if (entry is ShareLayoutRef)
                {
                    _itemIdentifiers.Add(ShareId);
                }
                else if (entry is PopUpLayoutRef popUpRef)
                {
                    int pIdx = _popUpItems.IndexOf(popUpRef.PopUpItem);
                    if (pIdx < 0) { _popUpItems.Add(popUpRef.PopUpItem); pIdx = _popUpItems.Count - 1; }
                    _itemIdentifiers.Add($"{PopUpIdPrefix}{pIdx}");
                }
                else if (entry is TitleLayoutItem)
                {
                    _itemIdentifiers.Add(TitleId);
                }
                else if (entry is SystemItemLayoutItem sysItem)
                {
                    _itemIdentifiers.Add(SystemItemIdentifier(sysItem.Kind));
                }
                else if (entry is SpacerLayoutItem spacer)
                {
                    _itemIdentifiers.Add(spacer.Kind switch
                    {
                        SpacerKind.Flexible => FlexibleSpaceId,
                        SpacerKind.Fixed => FixedSpaceId,
                        SpacerKind.Separator => SeparatorId,
                        _ => FlexibleSpaceId,
                    });
                }
            }

            // Append any remaining unclaimed content items after the explicit layout
            foreach (var item in contentItems)
            {
                var id = $"{ItemIdPrefix}{contentIdx}";
                _items.Add(item);
                _itemIdentifiers.Add(id);
                item.PropertyChanged += OnToolbarItemPropertyChanged;
                contentIdx++;
            }
        }
        else
        {
            // Default content layout: [flex] [title] [flex] [items...]
            _itemIdentifiers.Add(FlexibleSpaceId);
            _itemIdentifiers.Add(TitleId);
            _itemIdentifiers.Add(FlexibleSpaceId);

            int contentIdx = 0;
            foreach (var item in contentItems)
            {
                var id = $"{ItemIdPrefix}{contentIdx}";
                _items.Add(item);
                _itemIdentifiers.Add(id);
                item.PropertyChanged += OnToolbarItemPropertyChanged;
                contentIdx++;
            }

            // Search item in content area (convenience mode — only when placed in content
            // and not already claimed by an explicit layout)
            if (_searchItem != null && _searchItem.Placement == MacOSToolbarItemPlacement.Content
                && !explicitSpecialItems.Contains("__search__"))
                _itemIdentifiers.Add(SearchId);

            // Other special items in content area (convenience mode — skip items
            // already claimed by an explicit layout to avoid duplicates)
            for (int mi = 0; mi < _menuItems.Count; mi++)
                if (_menuItems[mi].Placement == MacOSToolbarItemPlacement.Content
                    && !explicitSpecialItems.Contains(_menuItems[mi]))
                    _itemIdentifiers.Add($"{MenuIdPrefix}{mi}");
            for (int gi = 0; gi < _groupItems.Count; gi++)
                if (_groupItems[gi].Placement == MacOSToolbarItemPlacement.Content
                    && !explicitSpecialItems.Contains(_groupItems[gi]))
                    _itemIdentifiers.Add($"{GroupIdPrefix}{gi}");
            if (_shareItem != null && _shareItem.Placement == MacOSToolbarItemPlacement.Content
                && !explicitSpecialItems.Contains("__share__"))
                _itemIdentifiers.Add(ShareId);
            for (int pi = 0; pi < _popUpItems.Count; pi++)
                if (_popUpItems[pi].Placement == MacOSToolbarItemPlacement.Content
                    && !explicitSpecialItems.Contains(_popUpItems[pi]))
                    _itemIdentifiers.Add($"{PopUpIdPrefix}{pi}");
            for (int vi = 0; vi < _viewItems.Count; vi++)
                if (_viewItems[vi].Placement == MacOSToolbarItemPlacement.Content)
                    _itemIdentifiers.Add($"{ViewIdPrefix}{vi}");
        }


        // Attach toolbar if not already attached
        if (_window != null && _window.Toolbar == null && _toolbar != null)
            _window.Toolbar = _toolbar;

        // Diff-based update: only modify the native toolbar when the identifier
        // list actually changed, and suppress animation to avoid flash/flicker.
        if (_toolbar != null)
        {
            var currentIds = _toolbar.Items.Select(i => i.Identifier).ToList();
            var desiredIds = _itemIdentifiers;

            if (!currentIds.SequenceEqual(desiredIds))
            {
                NSAnimationContext.BeginGrouping();
                NSAnimationContext.CurrentContext.Duration = 0;

                // Remove items that are no longer present (iterate backwards)
                for (int i = currentIds.Count - 1; i >= 0; i--)
                {
                    if (i >= desiredIds.Count || currentIds[i] != desiredIds[i])
                        _toolbar.RemoveItem(i);
                    else
                        currentIds[i] = null!; // mark as matched
                }

                // Re-read after removals, then insert missing items
                var afterRemoval = _toolbar.Items.Select(i => i.Identifier).ToList();
                for (int i = 0; i < desiredIds.Count; i++)
                {
                    if (i >= afterRemoval.Count || afterRemoval[i] != desiredIds[i])
                    {
                        _toolbar.InsertItem(desiredIds[i], i);
                        afterRemoval.Insert(i, desiredIds[i]);
                    }
                }

                NSAnimationContext.EndGrouping();
            }

            // Sync IsHidden on existing native items so that
            // MacOSToolbarItem.IsVisible changes take effect without
            // removing/re-inserting items.
            SyncItemVisibility();

            // Always update the title label text — the identifier list may
            // not change when navigating between pages with the same toolbar
            // structure, but the title text does.
            UpdateTitleLabel();
        }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// Updates the title label text on the existing toolbar title item
    /// without recreating the entire toolbar.
    /// </summary>
    void UpdateTitleLabel()
    {
        if (_toolbar == null) return;
        var title = _currentPage?.Title ?? string.Empty;
        foreach (var item in _toolbar.Items)
        {
            if (item.Identifier == TitleId && item.View is NSTextField label)
            {
                label.StringValue = title;
                label.SizeToFit();
                break;
            }
        }
    }

    /// <summary>
    /// Updates <c>NSToolbarItem.Hidden</c> on all native toolbar items to match
    /// <see cref="MacOSToolbarItem.IsVisibleProperty"/>. Covers regular items,
    /// sidebar items, menu items, search items, groups, share, popups, and views.
    /// </summary>
    void SyncItemVisibility()
    {
        if (_toolbar == null) return;

        foreach (var nsItem in _toolbar.Items)
        {
            BindableObject? source = null;
            var id = nsItem.Identifier;

            if (id.StartsWith(ItemIdPrefix))
            {
                if (int.TryParse(id.AsSpan(ItemIdPrefix.Length), out int idx)
                    && idx >= 0 && idx < _items.Count)
                    source = _items[idx];
            }
            else if (id.StartsWith(SidebarItemIdPrefix))
            {
                if (int.TryParse(id.AsSpan(SidebarItemIdPrefix.Length), out int idx)
                    && idx >= 0 && idx < _sidebarItems.Count)
                    source = _sidebarItems[idx];
            }
            else if (id.StartsWith(MenuIdPrefix))
            {
                if (int.TryParse(id.AsSpan(MenuIdPrefix.Length), out int idx)
                    && idx >= 0 && idx < _menuItems.Count)
                    source = _menuItems[idx];
            }
            else if (id.StartsWith(GroupIdPrefix))
            {
                if (int.TryParse(id.AsSpan(GroupIdPrefix.Length), out int idx)
                    && idx >= 0 && idx < _groupItems.Count)
                    source = _groupItems[idx];
            }
            else if (id.StartsWith(PopUpIdPrefix))
            {
                if (int.TryParse(id.AsSpan(PopUpIdPrefix.Length), out int idx)
                    && idx >= 0 && idx < _popUpItems.Count)
                    source = _popUpItems[idx];
            }
            else if (id.StartsWith(ViewIdPrefix))
            {
                if (int.TryParse(id.AsSpan(ViewIdPrefix.Length), out int idx)
                    && idx >= 0 && idx < _viewItems.Count)
                    source = _viewItems[idx];
            }
            else if (id == SearchId && _searchItem != null)
            {
                source = _searchItem;
            }
            else if (id == ShareId && _shareItem != null)
            {
                source = _shareItem;
            }

            if (source != null)
                nsItem.Hidden = !MacOSToolbarItem.GetIsVisible(source);
        }
    }

    void OnToolbarItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_toolbar != null)
            _toolbar.ValidateVisibleItems();
    }

    // INSToolbarDelegate
    [Export("toolbar:itemForItemIdentifier:willBeInsertedIntoToolbar:")]
    public NSToolbarItem ToolbarItemForIdentifier(NSToolbar toolbar, string itemIdentifier, bool willBeInserted)
    {
        // Tracking separator — divides sidebar area from content area
        if (itemIdentifier == TrackingSeparatorId && _splitView != null)
        {
            return NSTrackingSeparatorToolbarItem.GetTrackingSeparatorToolbar(
                TrackingSeparatorId, _splitView, 0);
        }

        // Sidebar toggle button (hamburger menu)
        if (itemIdentifier == SidebarToggleId)
        {
            var nsItem = new NSToolbarItem(SidebarToggleId)
            {
                Label = "Sidebar",
                PaletteLabel = "Toggle Sidebar",
                ToolTip = "Toggle Sidebar",
                Target = this,
                Action = new ObjCRuntime.Selector("sidebarToggleClicked:"),
            };

            var button = new NSButton
            {
                Title = "☰",
                BezelStyle = NSBezelStyle.TexturedRounded,
                Target = this,
                Action = new ObjCRuntime.Selector("sidebarToggleClicked:"),
            };
            button.SetButtonType(NSButtonType.MomentaryPushIn);
            nsItem.View = button;
            return nsItem;
        }

        // Navigation back button
        if (itemIdentifier == BackButtonId)
        {
            var backTitle = GetBackButtonTitle() ?? "Back";
            var nsItem = new NSToolbarItem(BackButtonId)
            {
                Label = backTitle,
                PaletteLabel = "Back",
                ToolTip = $"Back to {backTitle}",
                Target = this,
                Action = new ObjCRuntime.Selector("backButtonClicked:"),
            };

            var button = new NSButton
            {
                BezelStyle = NSBezelStyle.TexturedRounded,
                Target = this,
                Action = new ObjCRuntime.Selector("backButtonClicked:"),
            };
            button.SetButtonType(NSButtonType.MomentaryPushIn);

            // Use SF Symbol chevron for a native Finder-like look
            var chevronImage = NSImage.GetSystemSymbol("chevron.left", null);
            if (chevronImage != null)
            {
                button.Image = chevronImage;
                button.Title = backTitle;
                button.ImagePosition = NSCellImagePosition.ImageLeading;
            }
            else
            {
                button.Title = $"‹ {backTitle}";
            }

            nsItem.View = button;
            return nsItem;
        }

        // Centered page title
        if (itemIdentifier == TitleId)
        {
            var title = _currentPage?.Title ?? string.Empty;
            var nsItem = new NSToolbarItem(TitleId)
            {
                Label = "",
                PaletteLabel = "Title",
            };

            var label = new NSTextField
            {
                StringValue = title,
                Editable = false,
                Bordered = false,
                DrawsBackground = false,
                Font = NSFont.BoldSystemFontOfSize(13),
                TextColor = NSColor.Label,
                Alignment = NSTextAlignment.Center,
            };
            label.SizeToFit();
            nsItem.View = label;
            return nsItem;
        }

        // Native search toolbar item
        if (itemIdentifier == SearchId && _searchItem != null)
        {
            return CreateSearchToolbarItem();
        }

        // Menu toolbar items
        if (itemIdentifier.StartsWith(MenuIdPrefix))
        {
            var indexStr = itemIdentifier.Substring(MenuIdPrefix.Length);
            if (int.TryParse(indexStr, out int mIdx) && mIdx >= 0 && mIdx < _menuItems.Count)
                return CreateMenuToolbarItem(itemIdentifier, _menuItems[mIdx]);
        }

        // Group toolbar items (segmented control)
        if (itemIdentifier.StartsWith(GroupIdPrefix))
        {
            var indexStr = itemIdentifier.Substring(GroupIdPrefix.Length);
            if (int.TryParse(indexStr, out int gIdx) && gIdx >= 0 && gIdx < _groupItems.Count)
                return CreateGroupToolbarItem(itemIdentifier, _groupItems[gIdx]);
        }

        // Share toolbar item
        if (itemIdentifier == ShareId && _shareItem != null)
        {
            return CreateShareToolbarItem(itemIdentifier);
        }

        // PopUp toolbar items
        if (itemIdentifier.StartsWith(PopUpIdPrefix))
        {
            var indexStr = itemIdentifier.Substring(PopUpIdPrefix.Length);
            if (int.TryParse(indexStr, out int pIdx) && pIdx >= 0 && pIdx < _popUpItems.Count)
                return CreatePopUpToolbarItem(itemIdentifier, _popUpItems[pIdx]);
        }

        // Custom view toolbar items
        if (itemIdentifier.StartsWith(ViewIdPrefix))
        {
            var indexStr = itemIdentifier.Substring(ViewIdPrefix.Length);
            if (int.TryParse(indexStr, out int vIdx) && vIdx >= 0 && vIdx < _viewItems.Count)
                return CreateViewToolbarItem(itemIdentifier, _viewItems[vIdx]);
        }

        if (itemIdentifier.StartsWith(SidebarItemIdPrefix))
        {
            var indexStr = itemIdentifier.Substring(SidebarItemIdPrefix.Length);
            if (int.TryParse(indexStr, out int sIdx) && sIdx >= 0 && sIdx < _sidebarItems.Count)
            {
                var mauiItem = _sidebarItems[sIdx];
                return CreateToolbarButton(itemIdentifier, mauiItem, sIdx);
            }
        }

        if (itemIdentifier.StartsWith(ItemIdPrefix))
        {
            var indexStr = itemIdentifier.Substring(ItemIdPrefix.Length);
            if (int.TryParse(indexStr, out int index) && index >= 0 && index < _items.Count)
            {
                var mauiItem = _items[index];
                return CreateToolbarButton(itemIdentifier, mauiItem, index);
            }
        }

        return new NSToolbarItem(itemIdentifier);
    }

    NSToolbarItem CreateToolbarButton(string identifier, ToolbarItem mauiItem, int tag)
    {
        // Use a tag offset to distinguish sidebar items from content items
        nint effectiveTag = identifier.StartsWith(SidebarItemIdPrefix) ? tag + SidebarItemTagOffset : tag;

        // Read per-item attached properties
        var toolTipOverride = MacOSToolbarItem.GetToolTip(mauiItem);
        var toolTip = toolTipOverride ?? mauiItem.Text ?? string.Empty;

        var nsItem = new NSToolbarItem(identifier)
        {
            Label = mauiItem.Text ?? string.Empty,
            PaletteLabel = mauiItem.Text ?? string.Empty,
            ToolTip = toolTip,
            Enabled = mauiItem.IsEnabled,
            Tag = effectiveTag,
            Bordered = MacOSToolbarItem.GetIsBordered(mauiItem),
        };

        // Visibility priority
        var visPriority = MacOSToolbarItem.GetVisibilityPriority(mauiItem);
        if (visPriority != MacOSToolbarItemVisibilityPriority.Standard)
            nsItem.VisibilityPriority = (nint)(long)visPriority;

        // Item visibility (maps to NSToolbarItem.IsHidden, macOS 15+)
        if (!MacOSToolbarItem.GetIsVisible(mauiItem))
            nsItem.Hidden = true;

        // Background tint color
        var tintColor = MacOSToolbarItem.GetBackgroundTintColor(mauiItem);
        if (tintColor != null)
            nsItem.BackgroundTintColor = AppKit.NSColor.FromRgba(
                (nfloat)tintColor.Red, (nfloat)tintColor.Green,
                (nfloat)tintColor.Blue, (nfloat)tintColor.Alpha);

        // Check for SF Symbol icon via MacOSToolbarItem or IconImageSource
        var iconSource = mauiItem.IconImageSource;
        NSImage? image = null;
        if (iconSource is FileImageSource fileSource && !string.IsNullOrEmpty(fileSource.File))
            image = NSImage.GetSystemSymbol(fileSource.File, null) ?? new NSImage(fileSource.File);

        if (image != null)
        {
            // Icon items: use native NSToolbarItem rendering (Action/Target works)
            nsItem.Image = image;
            nsItem.Target = this;
            nsItem.Action = new ObjCRuntime.Selector("toolbarItemClicked:");
        }
        else
        {
            // Text-only items: need an NSButton view for visible text rendering.
            // Use Activated event on the button since Action/Target on view-based
            // toolbar items doesn't dispatch reliably in .NET macOS bindings.
            var button = new NSButton
            {
                BezelStyle = NSBezelStyle.TexturedRounded,
                Title = mauiItem.Text ?? string.Empty,
            };
            button.SetButtonType(NSButtonType.MomentaryPushIn);

            var capturedMauiItem = mauiItem;
            button.Activated += (s, e) =>
            {
                if (capturedMauiItem.IsEnabled)
                    ((IMenuItemController)capturedMauiItem).Activate();
            };

            nsItem.View = button;
        }

        return nsItem;
    }

    [Export("toolbarAllowedItemIdentifiers:")]
    public string[] ToolbarAllowedItemIdentifiers(NSToolbar toolbar)
    {
        // Always include all possible identifiers so NSToolbar doesn't reject
        // items added later (e.g., back button appearing after a push navigation)
        var ids = new List<string>(_itemIdentifiers)
        {
            FlexibleSpaceId, FixedSpaceId, SeparatorId,
            BackButtonId, SidebarToggleId, TitleId, TrackingSeparatorId,
            SearchId,
            NSToolbar.NSToolbarToggleSidebarItemIdentifier,
            NSToolbar.NSToolbarToggleInspectorItemIdentifier,
            NSToolbar.NSToolbarCloudSharingItemIdentifier,
            NSToolbar.NSToolbarPrintItemIdentifier,
            NSToolbar.NSToolbarShowColorsItemIdentifier,
            NSToolbar.NSToolbarShowFontsItemIdentifier,
            NSToolbar.NSToolbarWritingToolsItemIdentifier,
            NSToolbar.NSToolbarInspectorTrackingSeparatorItemIdentifier,
        };
        return ids.Distinct().ToArray();
    }

    [Export("toolbarDefaultItemIdentifiers:")]
    public string[] ToolbarDefaultItemIdentifiers(NSToolbar toolbar)
    {
        return _itemIdentifiers.ToArray();
    }

    [Export("toolbarItemClicked:")]
    void OnToolbarItemClicked(NSObject sender)
    {
        nint tag = -1;
        if (sender is NSToolbarItem item)
            tag = item.Tag;
        else if (sender is NSButton button)
            tag = button.Tag;

        ToolbarItem? mauiItem = null;
        if (tag >= SidebarItemTagOffset)
        {
            int sIdx = (int)(tag - SidebarItemTagOffset);
            if (sIdx >= 0 && sIdx < _sidebarItems.Count)
                mauiItem = _sidebarItems[sIdx];
        }
        else if (tag >= 0 && tag < _items.Count)
        {
            mauiItem = _items[(int)tag];
        }

        if (mauiItem != null && mauiItem.IsEnabled)
            ((IMenuItemController)mauiItem).Activate();
    }

    [Export("sidebarToggleClicked:")]
    void OnSidebarToggleClicked(NSObject sender)
    {
        if (_flyoutPage != null)
            _flyoutPage.IsPresented = !_flyoutPage.IsPresented;
    }

    [Export("backButtonClicked:")]
    void OnBackButtonClicked(NSObject sender)
    {
        if (_navigationPage != null && _navigationPage.Navigation.NavigationStack.Count > 1)
        {
            _navigationPage.PopAsync();
            return;
        }

        // Shell: pop via ShellSection navigation
        if (_shell?.CurrentItem?.CurrentItem is ShellSection section)
        {
            var navStack = section.Navigation?.NavigationStack;
            if (navStack != null && navStack.Count > 1)
                section.Navigation.PopAsync();
        }
    }

    NSToolbarItem CreateSearchToolbarItem()
    {
        var nsSearchItem = new NSSearchToolbarItem(SearchId);
        _nativeSearchItem = nsSearchItem;

        var searchField = nsSearchItem.SearchField;
        if (_searchItem != null)
        {
            if (!string.IsNullOrEmpty(_searchItem.Placeholder))
                searchField.PlaceholderString = _searchItem.Placeholder;
            if (_searchItem.PreferredWidth > 0)
                nsSearchItem.PreferredWidthForSearchField = (nfloat)_searchItem.PreferredWidth;
            nsSearchItem.ResignsFirstResponderWithCancel = _searchItem.ResignsFirstResponderWithCancel;
            if (!string.IsNullOrEmpty(_searchItem.Text))
                searchField.StringValue = _searchItem.Text;
        }

        // Subscribe to text changes via NSTextField.Changed notification
        searchField.Changed += OnSearchFieldTextChanged;

        // Subscribe to search field delegate events
        searchField.SearchingStarted += OnSearchFieldStarted;
        searchField.SearchingEnded += OnSearchFieldEnded;

        // Subscribe to Enter/Return via the text field action
        nsSearchItem.Target = this;
        nsSearchItem.Action = new ObjCRuntime.Selector("searchItemAction:");

        return nsSearchItem;
    }

    [Export("searchItemAction:")]
    void OnSearchItemAction(NSObject sender)
    {
        if (_searchItem == null || _nativeSearchItem == null) return;
        var text = _nativeSearchItem.SearchField.StringValue ?? string.Empty;
        _searchItem.Text = text;
        _searchItem.RaiseSearchCommitted(text);
    }

    void OnSearchFieldTextChanged(object? sender, EventArgs e)
    {
        if (_searchItem == null || _nativeSearchItem == null) return;
        _searchItem.Text = _nativeSearchItem.SearchField.StringValue ?? string.Empty;
    }

    void OnSearchFieldStarted(object? sender, EventArgs e)
    {
        _searchItem?.RaiseSearchStarted();
    }

    void OnSearchFieldEnded(object? sender, EventArgs e)
    {
        _searchItem?.RaiseSearchEnded();
    }

    void CleanupSearchItem()
    {
        if (_nativeSearchItem != null)
        {
            _nativeSearchItem.SearchField.Changed -= OnSearchFieldTextChanged;
            _nativeSearchItem.SearchField.SearchingStarted -= OnSearchFieldStarted;
            _nativeSearchItem.SearchField.SearchingEnded -= OnSearchFieldEnded;
            _nativeSearchItem = null;
        }
        _searchItem = null;
    }

    // ── Menu Toolbar Item ──────────────────────────────────────────────

    NSToolbarItem CreateMenuToolbarItem(string identifier, MacOSMenuToolbarItem menuItem)
    {
        var nsMenuItem = new NSMenuToolbarItem(identifier)
        {
            Label = menuItem.Text ?? string.Empty,
            PaletteLabel = menuItem.Text ?? string.Empty,
            ToolTip = menuItem.Text ?? string.Empty,
            ShowsIndicator = menuItem.ShowsIndicator,
        };

        NSImage? image = null;
        if (!string.IsNullOrEmpty(menuItem.Icon))
            image = NSImage.GetSystemSymbol(menuItem.Icon, null);

        if (image != null)
            nsMenuItem.Image = image;

        // ShowsTitle: set the title directly on the toolbar item — modern macOS
        // renders icon + title together with native hover/click states when Bordered.
        if (menuItem.ShowsTitle && !string.IsNullOrEmpty(menuItem.Text))
            nsMenuItem.Title = menuItem.Text;

        nsMenuItem.Menu = BuildNSMenu(menuItem.Items);
        return nsMenuItem;
    }

    static NSMenu BuildNSMenu(IList<MacOSMenuItem> items)
    {
        var menu = new NSMenu();
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                menu.AddItem(NSMenuItem.SeparatorItem);
                continue;
            }

            var nsItem = new NSMenuItem(item.Text ?? string.Empty);
            nsItem.Enabled = item.IsEnabled;
            nsItem.State = item.IsChecked ? NSCellStateValue.On : NSCellStateValue.Off;

            if (!string.IsNullOrEmpty(item.Icon))
            {
                var img = NSImage.GetSystemSymbol(item.Icon, null);
                if (img != null) nsItem.Image = img;
            }

            if (!string.IsNullOrEmpty(item.KeyEquivalent))
                nsItem.KeyEquivalent = item.KeyEquivalent;

            // Wire up click via activated handler
            var capturedItem = item;
            nsItem.Activated += (s, e) =>
            {
                capturedItem.RaiseClicked();
                if (capturedItem.Command?.CanExecute(capturedItem.CommandParameter) == true)
                    capturedItem.Command.Execute(capturedItem.CommandParameter);
            };

            if (item.SubItems.Count > 0)
                nsItem.Submenu = BuildNSMenu(item.SubItems);

            menu.AddItem(nsItem);
        }
        return menu;
    }

    // ── Group Toolbar Item (Segmented Control) ─────────────────────────

    NSToolbarItem CreateGroupToolbarItem(string identifier, MacOSToolbarItemGroup group)
    {
        var labels = new string[group.Segments.Count];
        var images = new NSImage?[group.Segments.Count];

        for (int i = 0; i < group.Segments.Count; i++)
        {
            var seg = group.Segments[i];
            labels[i] = seg.Label ?? seg.Text ?? string.Empty;
            if (!string.IsNullOrEmpty(seg.Icon))
                images[i] = NSImage.GetSystemSymbol(seg.Icon, null);
        }

        // Skip NSToolbarItemGroup.Create entirely — its action/target never fires
        // in .NET bindings. Instead, create a plain NSToolbarItem with our own
        // NSSegmentedControl as its view.
        var segControl = new NSSegmentedControl();
        segControl.SegmentCount = group.Segments.Count;
        segControl.SegmentStyle = NSSegmentStyle.Automatic;
        segControl.TrackingMode = group.SelectionMode switch
        {
            MacOSToolbarGroupSelectionMode.SelectOne => NSSegmentSwitchTracking.SelectOne,
            MacOSToolbarGroupSelectionMode.SelectAny => NSSegmentSwitchTracking.SelectAny,
            _ => NSSegmentSwitchTracking.Momentary,
        };
        for (int i = 0; i < group.Segments.Count; i++)
        {
            var seg = group.Segments[i];
            segControl.SetLabel(seg.Text ?? string.Empty, i);
            if (!string.IsNullOrEmpty(seg.Icon) && images[i] != null)
                segControl.SetImage(images[i]!, i);
            segControl.SetEnabled(seg.IsEnabled, i);
        }
        if (group.SelectionMode == MacOSToolbarGroupSelectionMode.SelectOne && group.SelectedIndex >= 0)
            segControl.SelectedSegment = group.SelectedIndex;

        var capturedGroup = group;
        segControl.Activated += (s, e) =>
        {
            var sc = (NSSegmentedControl)s!;
            var selected = new bool[capturedGroup.Segments.Count];
            for (int i = 0; i < capturedGroup.Segments.Count; i++)
                selected[i] = sc.IsSelectedForSegment(i);
            capturedGroup.SelectedIndex = (int)sc.SelectedSegment;
            capturedGroup.RaiseSelectionChanged((int)sc.SelectedSegment, selected);
        };

        var nsItem = new NSToolbarItem(identifier);
        nsItem.View = segControl;
        nsItem.Label = group.Label ?? string.Empty;
        nsItem.PaletteLabel = group.Label ?? string.Empty;

        return nsItem;
    }

    // ── Share Toolbar Item ─────────────────────────────────────────────

    NSToolbarItem CreateShareToolbarItem(string identifier)
    {
        var nsShare = new NSSharingServicePickerToolbarItem(identifier)
        {
            Label = _shareItem?.Label ?? "Share",
            PaletteLabel = _shareItem?.Label ?? "Share",
        };

        if (_shareItem != null)
            nsShare.WeakDelegate = new SharingDelegate(_shareItem);

        return nsShare;
    }

    class SharingDelegate : NSObject, INSSharingServicePickerToolbarItemDelegate
    {
        readonly MacOSShareToolbarItem _item;
        public SharingDelegate(MacOSShareToolbarItem item) => _item = item;

        public NSObject[] GetItems(NSSharingServicePickerToolbarItem pickerToolbarItem)
        {
            if (_item.ShareItemsProvider == null) return Array.Empty<NSObject>();
            var objects = _item.ShareItemsProvider();
            return objects.Select<object, NSObject>(o => o switch
            {
                string s => new Foundation.NSString(s),
                Uri u => new Foundation.NSUrl(u.AbsoluteUri),
                NSObject ns => ns,
                _ => new Foundation.NSString(o.ToString() ?? string.Empty),
            }).ToArray();
        }
    }

    // ── PopUp Toolbar Item ─────────────────────────────────────────────

    NSToolbarItem CreatePopUpToolbarItem(string identifier, MacOSPopUpToolbarItem popUp)
    {
        var nsItem = new NSToolbarItem(identifier)
        {
            Label = string.Empty,
            PaletteLabel = "Selection",
        };

        var button = new NSPopUpButton();
        button.PullsDown = popUp.PullsDown;

        foreach (var item in popUp.Items)
            button.AddItem(item);

        if (popUp.SelectedIndex >= 0 && popUp.SelectedIndex < popUp.Items.Count)
            button.SelectItem(popUp.SelectedIndex);

        if (popUp.Width > 0)
        {
            var frame = button.Frame;
            frame.Width = (nfloat)popUp.Width;
            button.Frame = frame;
        }

        var capturedPopUp = popUp;
        button.Activated += (s, e) =>
        {
            capturedPopUp.SelectedIndex = (int)button.IndexOfSelectedItem;
            capturedPopUp.RaiseSelectionChanged((int)button.IndexOfSelectedItem);
        };

        nsItem.View = button;
        return nsItem;
    }

    // ── Custom View Toolbar Item ───────────────────────────────────────

    NSToolbarItem CreateViewToolbarItem(string identifier, MacOSViewToolbarItem viewItem)
    {
        var nsItem = new NSToolbarItem(identifier)
        {
            Label = viewItem.Label ?? string.Empty,
            PaletteLabel = viewItem.Label ?? string.Empty,
        };

        if (viewItem.View == null)
            return nsItem;

        // Get the MAUI handler's platform view
        var mauiView = viewItem.View;

        // Ensure the view has a handler by setting its parent to the current page
        if (mauiView.Handler == null && _currentPage?.Handler?.MauiContext is IMauiContext mauiContext)
        {
            mauiView.Parent = _currentPage;
            var handler = mauiView.ToHandler(mauiContext);
        }

        if (mauiView.Handler?.PlatformView is NSView platformView)
        {
            // Measure the MAUI view to get its desired size
            nfloat toolbarHeight = 28;
            var measured = mauiView.Measure(
                viewItem.MaxWidth > 0 ? viewItem.MaxWidth : 400,
                toolbarHeight);
            var desiredWidth = (nfloat)Math.Ceiling(measured.Width);
            var desiredHeight = toolbarHeight;

            // Fallback: if MAUI measure returns 0, ask the native view
            if (desiredWidth <= 0)
            {
                var fittingSize = platformView.FittingSize;
                if (fittingSize.Width > 0)
                    desiredWidth = (nfloat)Math.Ceiling(fittingSize.Width);
                else
                    desiredWidth = 150; // reasonable fallback
            }

            // Apply min/max bounds
            if (viewItem.MinWidth > 0 && desiredWidth < viewItem.MinWidth)
                desiredWidth = (nfloat)viewItem.MinWidth;
            if (viewItem.MaxWidth > 0 && desiredWidth > viewItem.MaxWidth)
                desiredWidth = (nfloat)viewItem.MaxWidth;

            // Arrange the MAUI view at measured size
            mauiView.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, desiredWidth, desiredHeight));

            NSView itemView;
            if (viewItem.ShowsToolbarButtonStyle)
            {
                // Wrap in an NSButton for native toolbar hover/click states
                var button = new NSButton(new CoreGraphics.CGRect(0, 0, desiredWidth, desiredHeight));
                button.BezelStyle = NSBezelStyle.TexturedRounded;
                button.Bordered = true;
                button.Title = string.Empty;
                button.SetButtonType(NSButtonType.MomentaryPushIn);
                button.ImagePosition = NSCellImagePosition.NoImage;

                button.AddSubview(platformView);
                platformView.TranslatesAutoresizingMaskIntoConstraints = false;
                platformView.LeadingAnchor.ConstraintEqualTo(button.LeadingAnchor).Active = true;
                platformView.TrailingAnchor.ConstraintEqualTo(button.TrailingAnchor).Active = true;
                platformView.CenterYAnchor.ConstraintEqualTo(button.CenterYAnchor).Active = true;
                platformView.HeightAnchor.ConstraintEqualTo(desiredHeight).Active = true;

                var capturedViewItem = viewItem;
                button.Activated += (_, _) => capturedViewItem.RaiseClicked();

                itemView = button;
            }
            else
            {
                // Place the MAUI view directly — handle interactions via MAUI gestures
                platformView.Frame = new CoreGraphics.CGRect(0, 0, desiredWidth, desiredHeight);
                itemView = platformView;
            }

            nsItem.View = itemView;
            nsItem.MinSize = new CoreGraphics.CGSize(desiredWidth, desiredHeight);
            nsItem.MaxSize = new CoreGraphics.CGSize(
                viewItem.MaxWidth > 0 ? (nfloat)viewItem.MaxWidth : desiredWidth,
                desiredHeight);
        }

        return nsItem;
    }

    public void Detach()
    {
        CleanupSearchItem();
        SetPage(null);
        if (_window != null)
        {
            _window.Toolbar = null;
            _window = null;
        }
        _toolbar = null;
    }
}
