# Item context menus and action surfaces

Use [interaction contracts](interaction-contracts.md) and
[deletion](deletion-and-swipe-actions.md) for shared mutation policy.
[Sources I4-I5, I9, I14, I18, I20, I22, I24, I26](sources.md#i4)

## Resolve the intended context

Specify pointed item, focused item, selected block or empty-space context.
Right-click does not inherently mean SelectedItem, and keyboard invocation should
not use an old pointer location. Selection changes on context invocation are an
explicit UX policy.

At menu creation, capture intended keys/source scope, not a cell/index or a
reference whose BindingContext will change. Revalidate the same keys, current
permissions/revisions and pending state at invocation. A menu opened for A must
not delete B after its anchor container rebinds.

Use the same domain commands for menu, swipe, visible action, shortcut and
accessibility entry points. Avoid double mutation through Command plus Clicked/
Invoked or direct Command.Execute plus framework Activate.

## Native routes

| Target | Route |
|---|---|
| UIKit | UICollectionView delegate configuration or UIContextMenuInteraction; UIMenu/UIAction |
| Catalyst | UIKit menu with desktop pointer/keyboard checks, not NSMenu |
| Android | Floating context menu/ActionMode for content selection; anchored PopupMenu for explicit overflow |
| WinUI | ContextFlyout with MenuFlyout/CommandBarFlyout; ContextRequested for custom invocation |
| AppKit | NSMenu and NSView.MenuForEvent/menu(for:) with command validation |
| GTK4 | Gio.Menu/GAction and GtkPopoverMenu with keyboard/shortcut routing |
| WPF | ContextMenu/MenuItem and explicit PlacementTarget/current context bridge |

UIKit single-item context routing dates from iOS 13; the multi-item callback is
16. Zero paths means empty space, not item zero; multiple paths represent selected
content. Map native paths to stable keys immediately. Returning no configuration
is different from an empty configuration/cancellation presentation.

Android long-press drag and context action can compete. Use a distinct drag handle
or menu button where appropriate. ActionMode targets an explicit selected-key
set; a recycled holder is not its selection store. Popups are not permission to
retain a full row/controller graph indefinitely.

WinUI ContextFlyout is native; native ItemsView/Repeater context handling does not
inherit ListViewBase reordering. Define contextual invocation versus a command
button's Flyout, and preserve keyboard/focus semantics.

AppKit converts event coordinates to a current model item or uses focused/selected
keys for keyboard action. Native NSMenuItem validation should reflect current
command state; an old IsEnabled snapshot is not authorization.

GTK factory setup owns controllers; bind owns current keys and unbind removes
item-specific context. Popover/action-group cleanup belongs to teardown or
replacement. Native menu roles/shortcuts do not appear merely by painting labels.

WPF popups are separate trees. Do not use row-ancestor Binding to resolve the view
model as though ContextMenu were inside the row. Resolve PlacementTarget/current
context when opening, capture keys for execution, and avoid a shared popup
retaining its first DataContext.

## Stock MAUI boundaries

FlyoutBase.ContextFlyout/MenuFlyout is documented for Mac Catalyst and Windows,
not a portable iOS/Android long-press item menu. Mobile can use a visible action/
action sheet or a genuinely implemented native context provider.

Verify Catalyst Entry/icons and dynamic-menu support in the consuming version.
The current docs' blanket runtime mutation warning is broader than inspected
10.0.41 source: Windows Add/Remove/Insert/Clear are implemented; UIKit handler
methods are empty. Test/rebuild the native menu deliberately instead of assuming
uniform updates or universal lack of support.

The repo AppKit/GTK/WPF projections are partial basic mappings. Their existence
does not prove live command enabling, full submenu/event semantics, keyboard/
touch parity or recycled item correctness.

## Providers, previews and lifetime

Build cheap action descriptions from known data/permissions; no synchronous
HTTP/SQL/expensive decode in a menu provider. Where native deferred menus are
supported, bound work, complete callback once and check source/handler ownership.

Bound/pixel-size previews. Close/detach owned popovers, action groups, controllers,
delegates, shortcut handlers and previews on source change/rebind/disconnect.
Do not dispose app-shared commands/services. Reset stale open/hover/focus state.

Keep discoverable labelled alternatives when long-press/right-click is unavailable.
After deletion/move, restore focus/selection by key and announce meaningful results,
not cancelled or rejected operations. Respect text-editor shortcuts.

**Proof:** pointed versus selected/focused/multi/empty targets; recycled/shared
popup; source/query/permission changes while open; disabled/pending actions;
pointer/touch/keyboard/screen-reader activation; exactly-once command dispatch;
source replacement and popup/listener/preview plateau.
