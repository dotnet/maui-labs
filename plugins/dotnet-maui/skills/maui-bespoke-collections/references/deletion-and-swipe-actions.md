# Deletion and swipe actions

Use [interaction contracts](interaction-contracts.md) and
[context actions](context-menus-and-actions.md) for shared target/command rules.
[Sources I2, I5, I8, I11, I16, I26](sources.md#i2)

## Define what is deleted

Domain delete, archive, remove-from-group and remove-one-occurrence are different.
Specify permission, confirmation/undo, pending/optimistic behavior, duplicate
dispatch and unknown network-outcome policy. Destructive styling is not
confirmation or a successful repository operation.

Reveal exposes an explicit action; Execute/full swipe commits according to its
threshold/release semantics. Do not silently enable an irreversible full-swipe
delete. Prefer a safe explicit policy and bounded undo when appropriate.

## Stable mutation and recovery

Capture the intended key/source scope, then recheck it at commit. A delayed
confirmation cannot delete the old position's new occupant. Publish source/native
state through one authority; don't mutate a differ's live input and manually
notify the same change.

For confirmed mutation, expose pending state and restore native dismissal/reveal
visuals on failure. For optimistic deletion, retain a bounded operation/inverse/
tombstone until acknowledgement. Reconcile old same-epoch page/refresh data so
the item cannot reappear merely because it completed late.

Undo uses key/group and surviving neighbours plus revisions, not an old index or
entire old snapshot. Define expiry, pending-delete cancellation/compensation and
remote restore failure. A timeout may mean the delete committed; reconcile using
actual backend idempotency/concurrency support.

Remove/reset owned item demand, subscriptions, open swipe/menu/drag state and
images appropriately. Fix geometry/count/group chrome coherently; a deleted anchor
needs a surviving-neighbour fallback, and selected/focused keys need recovery.

## Native mechanisms

| Target | Path |
|---|---|
| UIKit list | UICollectionLayoutListConfiguration leading/trailing swipe providers, UIContextualAction/UISwipeActionsConfiguration |
| UIKit custom cards | Custom native reveal/action interaction or compatible retained MAUI SwipeView; no automatic list swipe for masonry |
| Android | ItemTouchHelper OnSwiped; app-owned actual reveal actions when required |
| WinUI | Per-item SwipeControl, Reveal/Execute, Invoked and close behavior |
| AppKit | Native menu/keyboard/button actions or custom pan/reveal |
| GTK4 | Native action buttons/menu plus custom gesture/revealer if needed |
| WPF | Context/keyboard/button actions or explicit custom manipulation/reveal |

UIKit's list swipe provider is not a UITableViewDelegate method for every
UICollectionView layout. PerformsFirstActionWithFullSwipe defaults true; set
policy deliberately. Complete native action callbacks once with the accepted UI
result, separately from durable persistence/error/undo.

Android holder positions can be NO_POSITION or have changed. Use current keyed
projection mapping. Restore rejected completed swipes; reset translation/elevation/
opacity in ClearView/rebind. Drawing a red Delete label in OnChildDraw is not
a real tappable or accessible button. Swipe thresholds have native units/
reference bounds; tune for actual multi-column geometry rather than copying
full-width row assumptions.

WinUI SwipeControl/stock MAUI Windows SwipeView are touch accelerators. Provide
context/visible/keyboard alternatives for pointer users. Reveal and Execute
differ; do not leave a retained row permanently translated/open after a failed
command or new binding.

Stock MAUI SwipeView also defines Open/Close, directional collections and
SwipeBehaviorOnInvoked. SwipeGestureRecognizer alone is a direction event, not
the interactive reveal/cancel/action UI. Native/custom-backend support must be
checked separately.

## Exactly one activation

Use Command or the mutation event route, not two independent mutation handlers.
In inspected MAUI 10.0.41, framework ISwipeItem.OnInvoked already executes
Command before Clicked/Invoked. MenuItem Activate also executes it. A native
bridge must not call Command.Execute and then those framework activation paths.

AppKit/GTK repo SwipeView handlers implement partial horizontal reveal; the WPF
handler substitutes a context menu. Do not claim cross-backend full
Reveal/Execute/top/bottom parity from registration alone.

## Gesture and validation gates

Separate horizontal swipe from vertical scroll/refresh, and from horizontal
shelves/navigation. Use start/end versus left/right deliberately in RTL.
Close/reset stale open side and transforms on cancel, rebind, removal and teardown.

Provide labelled accessible/native actions and a non-swipe route through the
same keyed command. Restore meaningful focus, selection and anchor after removal;
don't steal Delete/Undo from focused text editing.

Test cancel/below-threshold/full swipe, disabled permission, duplicate invocation,
late confirmation/recycle, network failure/unknown outcome, page resurrection,
undo after intervening edits, deleted anchor/focus and group-last-item deletion.
Native realization, event/listener and decoded-resource counts must stay bounded.
