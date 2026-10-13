# Stock collection interactions

For a working CollectionView, add only requested interaction behavior. Do not
scaffold all-platform handlers or replace it with a bespoke control unless the
layout/interaction truly requires that implementation.

This is source/API guidance, not native execution certification.

## Shared command rules

Target a stable model/occurrence key, not a saved index, current selected item
when a different row was clicked, or a container whose BindingContext can change.
Capture intended keys before async confirmation/loading and revalidate them,
source/query generation, permissions and pending state before mutation.

Commands in an item DataTemplate need an explicit view-model source, such as
RelativeSource ancestor binding context or an app-provided typed command bridge.
Keep page/template x:DataType and any source-binding type annotation correct;
do not inherit the item type for a view-model command and silently lose compiled
binding. Capture a stable key as the command argument.

Use one command route per mutation. Command plus Clicked/Invoked handlers should
not both delete/reorder/refresh. Publish observable bound mutations on the owning
dispatcher; a custom backend need not implement stock MainThread.

Represent pending/error/undo explicitly. Optimistic changes need reconciliation:
an old page/refresh must not resurrect a deleted item or restore an old order.
Keep selection/focus/anchor by key and choose a surviving neighbour after deletion.

## Drag/drop and reorder

DragGestureRecognizer/DropGestureRecognizer and DataPackage transfer data; there
is no portable CollectionView CanReorderItems property. DropCompleted is not
proof of a successful durable Move. Specify reorder versus copy/import/transfer.

For resident mutable data, translate drop context into intended keys and a
before/after neighbour, validate manual order/group boundaries and apply the
source move once on the dispatcher. Multi-selection preserves block order after
removing moving keys; don't use one index decrement for every case.

Computed sort/filter and cursor windows need a real canonical-order policy.
Do not invent global unloaded indices or turn a native drag animation into
database ordering. External drops validate MIME/types/counts/URI permissions and
bounded asynchronous content rather than passing pooled View instances.

Provide a separate drag handle when long-press is also a context menu, and
clickable/tappable Move up/down/Move to group alternatives plus keyboard support.
Keyboard-only support is not a single-pointer non-drag alternative.

## Swipe/delete

Put a compatible SwipeView in the item template if that is the requested design.
Choose SwipeItems.Mode Reveal versus Execute/full swipe deliberately; a red action
is not confirmation/undo. SwipeGestureRecognizer alone is a direction notification,
not complete reveal/action/cancellation UI.

Use the same stable-key Delete command from swipe, visible action, menu and
keyboard. Revalidate after confirmation; handle failure and reset any cancelled/
rebound swipe state. Undo resolves current neighbours/revisions, not an old index.

Windows stock SwipeView is touch-oriented and not mouse swipe. Offer pointer
context/visible actions. The labs WPF SwipeView currently substitutes a context
menu; GTK/AppKit reveal implementations are partial, not universal stock parity.

In MAUI 10.0.41, ISwipeItem.OnInvoked already executes Command and dispatches
Clicked/Invoked. Do not directly Execute and then invoke that framework pipeline
in a custom bridge.

## Refresh

Use RefreshView with the finite CollectionView as its direct child. Do not add
another ScrollView around the collection; an eager BindableLayout sample is not
the high-volume recipe.

On inspected .NET 10 MAUI 10.0.41, IsRefreshing=true triggers Refreshing/Command.
Do not also execute the command. Public IsRefreshing is set before the pull's
command starts: using `if (IsRefreshing) return` as the work guard can skip the
requested work and leave the spinner stuck. Use a separate single-flight
operation/request owner and choose one invocation route.

Distinguish refresh from append/retry/query replacement. Finish error/cancel paths
too, but don't let an old operation's finally stop a newer spinner or publish
old data. Preserve useful same-query content/anchors or explicitly choose reset.

IsRefreshEnabled in the inspected .NET 10 source disables refresh while preserving
interactive children; IsEnabled=false disables the subtree. Verify the app's
resolved version and backend mapping. AppKit/WPF refresh handlers are indicators
without pull; GTK uses a button/spinner. A registered handler is not gesture proof.

WinUI native pull refresh is touch-only; a button/shortcut is needed. Native
deferrals must be completed on success, error, cancellation and teardown.

## Context actions

FlyoutBase.ContextFlyout/MenuFlyout is documented for Mac Catalyst and Windows.
Do not prescribe it as an automatic iOS/Android long-press menu. A visible More
button/action sheet or implemented native context provider is an alternative.
Define pointed/focused/selected/empty-space targets, not always SelectedItem.

On Catalyst verify Entry/icons/menu update limits. MAUI 10.0.41 Windows
MenuFlyout handler implements Add/Remove/Insert/Clear; the UIKit handler methods
are empty. The docs' blanket dynamic-update warning is not a universal behavior.

WPF ContextMenu is in a separate popup tree; PlacementTarget/current context must
bridge item/view-model binding. Native GTK/AppKit/WPF mappings in labs are partial.
Recycled/shared menus must not hold an old target or bypass command validation.

## Validation and sources

Check duplicate dispatch, late confirmation/recycle, same-epoch page resurrection,
failed mutation/undo, refresh A/B races and state ownership, pointer/touch/keyboard,
gesture conflicts, drag cancellation, focus/selection and listener cleanup.
These authored checks do not mean all native implementations have been exercised.

- [MAUI drag/drop](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/gestures/drag-and-drop?view=net-maui-10.0)
- [SwipeView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/swipeview?view=net-maui-10.0)
- [RefreshView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/refreshview?view=net-maui-10.0)
- [Context menus](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/context-menu?view=net-maui-10.0)
- MAUI 10.0.41 at `d15964ef4f0a6d0740b655ba7c6be9b96be8b187`:
  [RefreshView](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/RefreshView/RefreshView.cs),
  [SwipeItem](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/SwipeView/SwipeItem.cs),
  [Windows menu](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Core/src/Handlers/MenuFlyoutHandler/MenuFlyoutHandler.Windows.cs),
  [UIKit menu](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Core/src/Handlers/MenuFlyoutHandler/MenuFlyoutHandler.iOS.cs).
- [Single-pointer non-drag alternatives](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html):
  web accessibility guidance informing native design, not a native compliance claim.

Checked 2026-10-13. New interaction native runtime/profiling proof is not claimed.
