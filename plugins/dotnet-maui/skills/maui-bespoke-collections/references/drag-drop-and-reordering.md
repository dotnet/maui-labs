# Drag/drop and item reordering

First read [interaction contracts](interaction-contracts.md). Separate reorder,
cross-collection transfer and external import; a drag gesture is not automatic
collection mutation. [Sources I1, I6-I7, I11-I12, I15, I18-I19, I21, I23](sources.md#i1)

## Ordering and targets

Define manual order, group boundaries, multi-selection block order, target meaning
(before/after/into/freeform), and persistence. Under computed sort, disable free
reorder, define manual mode, or map it to an actual domain operation.

Capture dragged keys in presentation order plus source/order revision. Remove the
moving keys from a working projection, then resolve insertion against surviving
neighbour keys and insert the block. Declare self-drop/no-op policy. A single
index decrement does not handle noncontiguous selected blocks.

For filtered/grouped/windowed data, translate projection positions into canonical
keys/membership. Chrome cannot be reordered data. Empty groups need a valid
destination policy. A cursor window is not a global rank/seek service; invalidate
or reconcile order-dependent continuation after accepted changes.

Use current layout/hit-test geometry and scroll/content coordinate conversion.
Masonry source order is not automatically visual column order. Do not divide
pointer Y by first-item height or demand unrealized containers for drop targeting.

## Native starting points

| Target | Mechanism and trap |
|---|---|
| UIKit | UICollectionView interactive movement or drag/drop delegates (iOS 11); diffable ReorderingHandlers (14) |
| Android | ItemTouchHelper for one recycler; View.StartDragAndDrop/ClipData for data transfer |
| WinUI | ListViewBase CanReorderItems + AllowDrop; CanDragItems for drag-item events; not ItemsRepeater/ItemsView flags |
| AppKit | NSCollectionViewDelegate pasteboard writer + ValidateDrop/AcceptDrop and RegisterForDraggedTypes |
| GTK4 | GtkDragSource + GdkContentProvider and GtkDropTarget/Async; mutate model objects, not recycled widgets |
| WPF | DragDrop.DoDragDrop/DataObject, routed drag events, custom projection mutation/auto-scroll |

### UIKit

Use native proposals for bounded permitted targets and distinguish sourceIndexPath,
in-app localObject and asynchronous NSItemProvider imports. Retain managed
delegate bridges for native weak properties; detach/cancel on disconnect.

Diffable DidReorder reports an already-applied native transaction: adopt its final
identifiers/difference into the backing store, then persist. Do not replay the
same move. A native list's reorder accessory is not permission to use its list
geometry for a required waterfall.

For placeholder import, CommitInsertion supplies the **current** index path and
performs collection insertion; update backing data only inside its callback.
False means the placeholder no longer exists. Cancel/delete placeholder on
failure and reject old source epochs. Do not cache the initial insertion index or
mix placeholder-owned updates with independent snapshots without a tested bridge.

### Android

Map holder positions to current keys; handle NO_POSITION and BindingAdapterPosition
versus AbsoluteAdapterPosition/ConcatAdapter offsets. Permit drag only for allowed
kinds/sections. A handle can call StartDrag while long-press drag is disabled.

OnMove must describe an accepted immediate projection change, not wait for HTTP.
With a mutable adapter, update data and issue the appropriate ordered notification.
With a differ/ListAdapter, preserve immutable inputs and one update authority;
do not edit CurrentList plus NotifyItemMoved.

A custom manager must keep the dragged holder visible after a move near edges.
Bound edge auto-scroll and candidate lookup; ItemTouchHelper transforms the
existing child. ClearView/reset all changed visual properties.

For inter-app content, use real ClipData/MIME/URI and API 24+ global/URI permissions.
RequestDragAndDropPermissions and release when done; local state is not portable
cross-process data.

### WinUI, AppKit, GTK and WPF

WinUI built-in reorder excludes grouped sources and VariableSizedWrapGrid.
IsSwipeEnabled affects touch drag/reorder; pointer-only success is insufficient.
Repeater/custom layout uses UIElement transfer events plus app-owned targeting/
ordering. WinUI event deferrals must complete on every exit.

AppKit requires registered pasteboard types as well as validation. Modern
index-path delegates cover sections; legacy index methods are not interchangeable.
ValidateDrop can alter both destination and on/before operation. Define modifier
copy/move, multi-selection and source-removal ownership.

GTK controllers belong in factory setup/teardown; callbacks resolve current item
identity and bind lifetime. Drag-end delete_data applies to Move; do not remove
again after an internal coordinator already committed the reorder. Native GTK
draggable-widget demos are not virtualized collection-model recipes.

WPF needs mouse/touch movement thresholds, transparent hit-testable drop surfaces,
correct coordinates and explicit mutation. DragDropEffects negotiates an effect,
not the data change. The drag loop is not a global application mutation lock.
Bound and clean up adorners/auto-scroll; null offscreen containers are expected.

## Performance, persistence and proof

No SQL/HTTP/decode or whole-source rebuild in drag-over/proposal/draw. Use cached
permissions, indexed visible targets and pixel-sized previews. Buffer or cancel
conflicting structural snapshots during native movement according to policy.

Persist accepted keys/ranks/membership through the repository. A later reload must
retain order. Cross-source Move has one removal authority and a defined failure
protocol; drop effects alone are not durable success.

Test self/up/down/end/empty, noncontiguous block, cross-group and filtered moves;
late query/page/mutation changes; source replacement; rejected/async external
drop; placeholder removal; edge auto-scroll and transformed/RTL hit testing.
Verify focus/accessibility, clickable non-drag alternatives and native/MAUI
realization/listener/preview counts. Research/examples are not runtime proof.
