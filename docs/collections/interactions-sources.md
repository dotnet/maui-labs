# Collection interaction source ledger

[Interaction research](interactions.md) | [General sources](sources.md)

**Checked:** 2026-10-13. Native/vendor pages are live. GitHub examples and MAUI
implementation links below are commit-pinned. API/source inspection is not C#
compile proof, native runtime testing or a performance measurement.
Recommendations about optimistic mutation, cursor invalidation, tombstones,
rank persistence and conflict handling are synthesis for app-owned repositories,
not guarantees supplied by a gesture API.

## MAUI

### I1
[Drag and drop](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/gestures/drag-and-drop?view=net-maui-10.0).
DragGestureRecognizer/DropGestureRecognizer, data packages and platform args.
Describes data transfer, not automatic CollectionView reordering or durable
repository mutation.

### I2
[SwipeView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/swipeview?view=net-maui-10.0).
Reveal/Execute, directional items, events and programmatic Open/Close. Explicit
Windows touch-only swipe limitation. The article contains separate .NET 11
IconColor/TextColor sections; those are not .NET 10 API evidence.

### I3
[RefreshView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/refreshview?view=net-maui-10.0)
and [10.0.41 implementation](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/RefreshView/RefreshView.cs).
IsRefreshing=true dispatches Refreshing/Command; IsRefreshEnabled exists and
participates in coercion/CanExecute, distinct from subtree IsEnabled.
The docs' small FlexLayout/BindableLayout sample is not a large-list virtualizer.

### I4
[MAUI context menus](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/context-menu?view=net-maui-10.0).
Documents Catalyst/Windows scope and Entry/icons/dynamic-menu warnings.
Inspected 10.0.41
[MenuFlyout model](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Menu/MenuFlyout.cs),
[Windows handler](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Core/src/Handlers/MenuFlyoutHandler/MenuFlyoutHandler.Windows.cs)
and [UIKit handler](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Core/src/Handlers/MenuFlyoutHandler/MenuFlyoutHandler.iOS.cs)
show platform differences: Windows implements add/remove/insert/clear; UIKit
methods are empty. Do not flatten this into universal support or prohibition.

### I5
MAUI 10.0.41
[MenuItem](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Menu/MenuItem.cs)
and [SwipeItem](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/SwipeView/SwipeItem.cs).
Framework Activate/OnInvoked already execute commands and dispatch events.
Supports the double-dispatch warning; it is not a tested repair of the backends.

## UIKit / Mac Catalyst

### I6
[Collection drag/drop](https://developer.apple.com/documentation/uikit/supporting-drag-and-drop-in-collection-views),
[placeholder commit](https://developer.apple.com/documentation/uikit/uicollectionviewdropplaceholdercontext/commitinsertion(datasourceupdates:)).
Delegates, localObject/item providers, proposals and asynchronous placeholders
since iOS 11. Commit uses the supplied current insertion path, updates backing
data in its callback and performs native insertion itself; false means the
placeholder was removed.

### I7
[Diffable reordering handlers](https://developer.apple.com/documentation/uikit/uicollectionviewdiffabledatasourcereference/reorderinghandlers)
since iOS/Catalyst 14; transaction difference/final identifiers update the backing
store after native reorder.
[Modern collections sample](https://developer.apple.com/documentation/uikit/implementing-modern-collection-views),
[archive](https://docs-assets.developer.apple.com/published/76a6e639ef2e/ImplementingModernCollectionViews.zip):
inspected `Modern Collection Views/Lists/ReorderableListViewController.swift`.
Archive SHA-256:
`26c83ac53bdb97a360e015a2d5a9a28f870b39fb46675aed299d0748bf2b0377`.
It uses a native list/reorder accessory and updates section backing stores from
the transaction. Local sample license inspected; no source incorporated.

### I8
[UICollectionLayoutListConfiguration swipe provider](https://developer.apple.com/documentation/uikit/uicollectionlayoutlistconfiguration-c.class/trailingswipeactionsconfigurationprovider)
since iOS/Catalyst 14 and
[full-swipe property](https://developer.apple.com/documentation/uikit/uiswipeactionsconfiguration/performsfirstactionwithfullswipe).
Full swipe defaults true; native list-section providers are not a generic custom
masonry/UITableView delegate API.

### I9
[Context-menu sample](https://developer.apple.com/documentation/uikit/adding-context-menus-in-your-app),
[multi-item callback](https://developer.apple.com/documentation/uikit/uicollectionviewdelegate/collectionview(_:contextmenuconfigurationforitemsat:point:)).
UIContextMenuInteraction/UIMenu/UIAction and collection providers. Multi-item/
empty-space callback since iOS/Catalyst 16. Sample deployment requirements do not
establish introduction dates of every API; the older single-item route is 13.

### I10
[UIRefreshControl](https://developer.apple.com/documentation/uikit/uirefreshcontrol).
Target/action ValueChanged and EndRefreshing on a scroll view/collection;
control introduced on iOS 6, distinct from the later UIScrollView property.
Apple DocC JSON and installed Apple 26.5.10318 API XML were inspected for named
UIKit bindings (list providers, full swipe, context overloads, generic diffable
ReorderingHandlers). No new interaction handler was compiled or run.

## Android

### I11
[ItemTouchHelper.Callback](https://developer.android.com/reference/androidx/recyclerview/widget/ItemTouchHelper.Callback).
OnMove/OnSwiped, drag handles, start/end directions, thresholds, draw/clear,
edge auto-scroll and custom-manager visibility requirements. Not external OS data
transfer or automatic database mutation.

### I12
[Drag/drop overview](https://developer.android.com/develop/ui/views/touch-and-input/drag-drop)
and [multi-window transfer](https://developer.android.com/develop/ui/views/touch-and-input/drag-drop/multi-window).
StartDragAndDrop/ClipData, global/URI flags and request/release permissions
for inter-app content on API 24+. Local object state is not a cross-app format.

### I13
[SwipeRefreshLayout API](https://developer.android.com/reference/androidx/swiperefreshlayout/widget/SwipeRefreshLayout)
and [swipe refresh/action alternative](https://developer.android.com/develop/ui/views/touch-and-input/swipe/add-swipe-interface).
One scrolling child, child-up override and nested scroll contracts. The how-to
uses an alpha dependency/ListView example; do not copy its version as the managed
package baseline or infer RecyclerView is unsupported from that wording.
Official Paging sample at `e849ce3004ccd1132a121cf513bbcb7996d95c30`:
[RedditActivity](https://github.com/android/architecture-components-samples/blob/e849ce3004ccd1132a121cf513bbcb7996d95c30/PagingWithNetworkSample/app/src/main/java/com/android/example/paging/pagingwithnetwork/reddit/ui/RedditActivity.kt),
[PostsAdapter](https://github.com/android/architecture-components-samples/blob/e849ce3004ccd1132a121cf513bbcb7996d95c30/PagingWithNetworkSample/lib/src/main/java/com/android/example/paging/pagingwithnetwork/reddit/ui/PostsAdapter.kt).
Refresh/load-state presentation and keyed DiffUtil comparison, not a recommendation
to require Kotlin dependencies in MAUI.

### I14
[Android menus](https://developer.android.com/develop/ui/views/components/menus).
Floating context menus, contextual ActionMode and anchored overflow PopupMenu have
different presentation/selection roles; an app must target its own model keys.

## WinUI

### I15
[CanReorderItems](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.canreorderitems?view=windows-app-sdk-1.8).
Requires AllowDrop; CanDragItems enables drag-item events. Built-in reorder excludes
grouped items and VariableSizedWrapGrid; IsSwipeEnabled affects touch interaction.
[Gallery ListView implementation](https://github.com/microsoft/WinUI-Gallery/blob/d78c358505b4f42e1ade9f1b1b6d79abf415f998/WinUIGallery/Samples/ListView/ListViewPage.xaml.cs)
and [reorder markup](https://github.com/microsoft/WinUI-Gallery/blob/d78c358505b4f42e1ade9f1b1b6d79abf415f998/WinUIGallery/Samples/ListView/ListviewsDragDropReordering.txt).
Demonstrates transfer; its first-cell-height/non-null-container assumptions are
unsafe for variable geometry and deep virtualized positions.

### I16
[SwipeControl guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/swipe)
and [Gallery implementation](https://github.com/microsoft/WinUI-Gallery/blob/d78c358505b4f42e1ade9f1b1b6d79abf415f998/WinUIGallery/Samples/SwipeControl/SwipeControlPage.xaml.cs).
Reveal versus Execute, actual source deletion and invoked/close semantics.
Sample page fields are not per-item production state/reuse policy.

### I17
[Pull-to-refresh](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/pull-to-refresh)
and [Gallery implementation](https://github.com/microsoft/WinUI-Gallery/blob/d78c358505b4f42e1ade9f1b1b6d79abf415f998/WinUIGallery/Samples/PullToRefresh/PullToRefreshPage.xaml.cs).
RefreshContainer/Visualizer, touch input, RequestRefresh and deferral completion.
Timer/static sample state is not cancellation-safe repository architecture.

### I18
[Native drag/drop](https://learn.microsoft.com/en-us/windows/apps/develop/data/drag-and-drop),
[menus](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/menus-and-context-menus),
[ItemsView 1.8](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemsview?view=windows-app-sdk-1.8).
UIElement transfer/context APIs, DataPackage operations and event deferrals;
ItemsView is not ListViewBase and does not inherit CanReorderItems.

## AppKit

### I19
[NSCollectionViewDelegate](https://developer.apple.com/documentation/appkit/nscollectionviewdelegate)
and [validate drop](https://developer.apple.com/documentation/appkit/nscollectionviewdelegate/collectionview(_:validatedrop:proposedindexpath:dropoperation:)).
Pasteboard writers, validate/accept, registered types, mutable proposed path/on-
before operation, modern index-path methods since macOS 10.11 versus legacy
single-section APIs.

### I20
[NSView menu(for:)](https://developer.apple.com/documentation/appkit/nsview/menu(for:)).
Event-sensitive NSMenu construction and native desktop targeting. No inference
that UIKit menu/swipe/refresh APIs are AppKit equivalents.

## GTK4

### I21
[Drag/drop overview](https://docs.gtk.org/gtk4/drag-and-drop.html),
[DragSource](https://docs.gtk.org/gtk4/class.DragSource.html),
[DropTarget](https://docs.gtk.org/gtk4/class.DropTarget.html),
[drag-end](https://docs.gtk.org/gtk4/signal.DragSource.drag-end.html).
Controllers/content providers, synchronous typed versus asynchronous target,
hover motion and Move delete_data ownership.
[GTK demo](https://github.com/GNOME/gtk/blob/f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d/demos/gtk-demo/dnd.c)
shows bounded widget-canvas mechanisms, not list/grid model virtualization.

### I22
[List framework](https://docs.gtk.org/gtk4/section-list-widget.html),
[edge-overshot](https://docs.gtk.org/gtk4/signal.ScrolledWindow.edge-overshot.html),
[PopoverMenu](https://docs.gtk.org/gtk4/class.PopoverMenu.html).
Item versus position, factory reuse, direct scroll ownership, overshoot notification
and Gio.Menu/GAction native menu roles/shortcuts. Overshoot is not a complete
pull-to-refresh feature.

## WPF and accessibility

### I23
[WPF drag/drop](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview).
DataObject, DoDragDrop, routed events, effect negotiation and app-owned mutation.
Do not import WinUI reorder flags or effect-as-data-mutation assumptions.

### I24
[ContextMenu.PlacementTarget](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.contextmenu.placementtarget?view=windowsdesktop-10.0)
and [Popup overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/popup).
Owning target when the popup opens; explicit model/context bridging avoids
assuming the popup shares the item visual-tree ancestry.

### I25
[WCAG 2.5.7 understanding](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html).
Single-pointer non-drag alternative is distinct from keyboard equivalence.
This is web guidance informing native design, not a native-app compliance verdict.

## maui-labs source inspection

### I26
Inspected baseline `56064071bfeaae0dd9aa962b6079d39a5281b875` and current worktree:

- [AppKit gestures](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/MacOS/src/MacOS/Handlers/GestureManager.cs),
  [refresh](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/MacOS/src/MacOS/Handlers/RefreshViewHandler.cs),
  [swipe](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/MacOS/src/MacOS/Handlers/SwipeViewHandler.cs),
  [context mapping](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/MacOS/src/MacOS/Handlers/MacOSViewHandler.cs).
- [GTK gestures](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Platform/GtkGestureExtensions.cs),
  [refresh](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers/RefreshViewHandler.cs),
  [swipe](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers/SwipeViewHandler.cs),
  [context mapping](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers/GtkViewHandler.cs).
- [WPF refresh/swipe](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Windows.WPF/src/Windows.WPF/Handlers/CollectionHandlers.cs),
  [gestures](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Windows.WPF/src/Windows.WPF/Platform/GestureManager.cs),
  [context mapping](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Windows.WPF/src/Windows.WPF/Hosting/AppHostBuilderExtensions.cs).

These establish partial/substituted behavior and framework-command double-dispatch
risks, not runtime testing. Those existing backends were not modified in this task.
