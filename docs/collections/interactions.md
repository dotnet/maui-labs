# Collection interactions: move, delete, refresh and item actions

[Research index](README.md) | [Interaction sources](interactions-sources.md) |
[Shared design](shared-design.md) | [Validation](performance-validation.md)

**Researched:** 2026-10-13. **Repository baseline:**
`56064071bfeaae0dd9aa962b6079d39a5281b875`.
The relevant backend files were also inspected in the current worktree.
This is documented/source-inspected behavior and design synthesis, **not new
native interaction runtime or performance certification**. The photo-masonry
prototype has not acquired these features through this research.

The scope is the four requested collection interactions on iOS, Mac Catalyst,
Android, WinUI, native macOS AppKit, Linux GTK4, and the WPF backend:
drag/drop and reordering, deletion/swipe actions, refresh, and context actions.
Native capability, a stock MAUI API, backend integration, and verified app
behavior are separate claims.

## 1. Principal findings

1. **Reordering and data transfer are different operations.** Moving an existing
   key within one ordered collection is not importing an external file, moving
   between repositories, or moving a native view into another hierarchy.
2. **A gesture is an intent, not durable mutation.** Native animation and drop
   feedback cannot prove that a database/server accepted the change.
3. **Interaction targets need stable keys and a source scope.** Native positions,
   recycled cells, mutable `BindingContext`, and old selection are unsafe targets
   for an action that executes later.
4. **Use one structural update authority.** Do not echo a native reorder with
   another `Move`, combine a differ-owned update with manual notifications, or
   insert a cell again after a UIKit placeholder commits it.
5. **Paging makes interaction correctness harder.** Reorder can invalidate an
   order-dependent cursor; an old append can resurrect a deleted record; undo
   must not blindly reuse an old index.
6. **Native gesture affordances are not uniform.** UIKit list swipe providers are
   not a universal masonry API. WinUI reorder excludes grouped sources and
   `VariableSizedWrapGrid`; WinUI refresh is touch-oriented. AppKit/WPF do not
   expose UIKit's refresh control.
7. **Preserve alternatives and native ownership.** A drag handle, swipe, or
   long-press cannot be the sole route to essential functionality. Keyboard,
   clickable/tappable actions, screen readers, focus and cancellation all matter.
8. **Samples demonstrate mechanisms, not production transaction policy.**
   The inspected WinUI drag sample assumes equal heights and realized containers;
   GTK's draggable-widget canvas is not a model-backed collection virtualizer.

Sources: [MAUI](interactions-sources.md#i1),
[UIKit](interactions-sources.md#i6),
[Android](interactions-sources.md#i11),
[WinUI](interactions-sources.md#i15),
[GTK](interactions-sources.md#i21),
[accessibility](interactions-sources.md#i25).

## 2. Define the interaction contract before selecting gestures

Specify only requested operations; do not add an app-wide editing framework.

| Question | Why it changes the implementation |
|---|---|
| Reorder, copy, import, transfer, or change group? | Different source mutation, permission and completion contracts |
| Manual order or computed sort/filter? | Moving visible rows may be meaningless under a server/date/score sort |
| Single item or selected block? | Multi-item order and source-removal adjustment must be defined |
| Within a group or across groups? | Target section, empty-group insertion, membership and permissions differ |
| Resident data or cursor/window? | Loaded positions are not global ranks or arbitrary insertion destinations |
| Optimistic or confirmed mutation? | Feedback, rollback, retries, pending state and undo differ |
| Swipe reveal or execute/full swipe? | Destructive activation and cancellation thresholds differ |
| Item, selection, or empty-space menu? | A right-clicked item need not be the current selection |
| Same-query refresh or query replacement? | Old content, anchors, cursors and load generations need different policies |
| Touch, pointer, keyboard, assistive input? | Gesture arbitration and non-gesture alternatives must be explicit |

### Stable identity and operation ownership

An app-level intent can conceptually carry:

```text
operation ID
source/query scope and source epoch
target item key(s) and expected content/order revision
target section plus before/after neighbour key for insertion
requested operation and conflict/optimistic policy
```

This is a design sketch, not a required runtime interface or a native API.
Keep domain identity separate from a presentation occurrence: removing one
occurrence from a shelf need not delete the shared product.

Capture the **intended keys** when the interaction is recognized. At commit,
resolve those keys in the accepted source and recheck existence, permissions,
pending operations and revisions. Do not replace them with the keys of whatever
item the old container now displays. Native positions may be read and translated
to keys synchronously, but do not preserve positions as durable intent.

Distinguish:

- **Source epoch:** query/source/handler lifetime changes.
- **Order/mutation revision:** structural changes inside the same source epoch.
- **Bind generation:** a recycled renderer's current ownership.
- **Operation ID:** duplicate command/network retry correlation.

A query epoch alone cannot prevent a same-epoch append from undoing a local
delete or reorder. Reconcile fetched pages with accepted mutations, or invalidate
and rebuild the affected ordered source. Keyed optimistic overlays/tombstones
are a useful policy, not a compulsory abstraction for a small in-memory list.

### A serialized mutation pipeline

```text
native interaction -> keyed intent -> validation -> accepted projection
                   -> one ordered native update -> persistence/reconciliation
                   -> explicit result, focus/anchor recovery, optional undo
```

For an optimistic change, publish a coherent local state promptly and persist
asynchronously. Track pending keys, and expose failure. Roll back by operation
and neighbours/revisions, not by assigning an entire old snapshot over newer
changes. For confirmed changes, show a pending state and restore any native
dismissal visual if the operation is rejected.

HTTP deletion/reordering may have committed even when a request times out.
Use the service's actual idempotency/concurrency contract and reconcile unknown
outcomes. Do not assume retrying a non-idempotent move is harmless.

For offline-first data, transact local rows, order/membership, remote keys and
outbox/tombstones coherently. Publish UI from the database/projection once,
not from both HTTP callbacks and database observers.
See [data/source research](shared-design.md) and
[source contracts](../../plugins/dotnet-maui/skills/maui-bespoke-collections/references/source-and-update-contracts.md).

## 3. Drag/drop and reordering

### One collection: reorder data, not pooled views

For manual ordering, work from stable selected keys in a declared order:

1. Capture source/order revision and the dragged key block.
2. Translate the proposed target into a section and neighbour key.
3. Validate that source and destination permit the move; exclude chrome.
4. Remove the moving keys from a working projection, resolve the destination
   against the survivors, and insert the block preserving its relative order.
5. Publish one coherent accepted order/native update and persist the chosen rank.
6. Preserve keyed selection/focus and an unaffected visible anchor where relevant.

For a move onto the moving block, declare a no-op or rejection policy. Handling
multi-selection with a single `destinationIndex--` is not sufficient.
If reordering changes group membership, update both sections atomically and
handle empty/disappearing groups, headers and footers.

Under a computed sort, either disable free reorder with a clear reason, define a
manual-order mode, or map the action to a genuine domain operation. Reordering a
filtered projection by visible index must not arbitrarily overwrite the canonical
order of hidden records. A windowed cursor feed cannot invent an unloaded target
rank or promise global seek just because the native control exposes an index.

### Geometry and hot-path work

A native drop target is transient hit-test information. For unequal heights,
masonry, transforms, RTL and groups, use current geometry and the actual scroll/
content coordinate conversion. Source order and visual column order are not
automatically the same.

Choose insertion semantics explicitly: before/after an item, into a group, or
freeform position. Do not use pointer Y divided by the first cell's height for
variable layouts, or assume `ContainerFromIndex(0)`/an offscreen row exists.

Keep drag-over/proposal/preview callbacks cheap. Use indexed visible candidates,
cached permissions and bounded previews; no HTTP, SQL, full decode or rebuilding
all item templates. Throttle custom insertion-marker/auto-scroll work to the
display loop. Keep only the active drag item/preview alive as needed rather than
disabling collection recycling globally.

Auto-scroll must cooperate with the native layout manager and nested scroll
owner. Refresh, source replacement or a conflicting mutation should either cancel
the drag or be reconciled under an explicit policy. Native interactive movement
can require buffering competing snapshots until its transaction completes.

### External and cross-collection drops

Use a versioned typed payload with explicit namespace/source identity. An in-app
object shortcut is not a portable cross-process representation. Validate formats,
item count, sizes, access permissions and destination rules; do not deserialize
arbitrary type names or trust a dropped path/URL as a local file.

Retrieve external content asynchronously with bounded demand and lifetime checks.
A placeholder has its own identity and cancellation/error state. Some native
placeholder APIs own the insertion transaction; do not also issue a duplicate
insert. A refresh may remove the placeholder before import completes.

Transfer/copy/move effects are negotiation and feedback. A reported native
`Move` is not proof of server durability. For two sources, define which authority
removes the source, when the destination accepts it, and how failure is handled.
Do not remove twice in both the drop and drag-completed callbacks.

### Platform mechanisms

| Target | Native starting point | Important boundary |
|---|---|---|
| UIKit | Interactive movement; collection drag/drop delegates since iOS 11; diffable reorder handlers since 14 | Adopt the final native transaction into the backing store; do not replay the move |
| Android | `ItemTouchHelper` for in-recycler move/swipe; `View.StartDragAndDrop`/`ClipData` for data transfer | These are different systems; holder positions can become `NO_POSITION` |
| WinUI | `ListViewBase.CanReorderItems` + `AllowDrop`; `CanDragItems` for drag-item events | Built-in reorder does not cover grouping/VariableSizedWrapGrid or ItemsRepeater |
| AppKit | `NSCollectionViewDelegate` pasteboard writer, validate/accept drop; register dragged types | Modern section/index-path APIs, not legacy single-section indexes |
| GTK4 | `GtkDragSource`, `GtkDropTarget`/`GtkDropTargetAsync`, `GdkContentProvider` | Mutate the item/model; do not transfer recycled list-item widgets |
| WPF | `DragDrop.DoDragDrop`, `DataObject`, routed drag/drop events and explicit source mutation | Effect flags communicate intent; they do not move the collection |

UIKit's `CommitInsertion` supplies the current insertion index path, returns
false when its placeholder is gone, and updates the collection itself. Its
callback should update backing data only. Native `didReorder` similarly reports
an already-performed diffable reorder. [I6-I7](interactions-sources.md#i6)

Android `OnMove` should reflect an accepted move in the adapter projection; do
not block it on persistence. If using a differ/ListAdapter, preserve immutable
inputs and one notification authority; do not mutate `CurrentList` and manually
notify the same diff. A custom manager must keep the dragged holder visible,
including after a move near an edge. A dedicated handle can separate drag from
long-press context actions. [I11](interactions-sources.md#i11)

AppKit must register accepted pasteboard types **and** implement validation.
`ValidateDrop` may adjust both the proposed index path and on/before operation;
`AcceptDrop` handles the actual change. Selection and modifier-key copy/move
policy need desktop tests. [I19](interactions-sources.md#i19)

GTK's `drag-end` includes `delete_data` for a successful Move. For an internal
reorder already committed by one coordinator, do not delete again in that signal.
Attach persistent controllers in factory setup and resolve the current bound
item; unbind cancels ownership and teardown removes controllers.
[I21-I22](interactions-sources.md#i21)

WPF needs a deliberate movement threshold before `DoDragDrop`, proper coordinate
conversion and cancellation. Its drag loop can process other UI activity; source
generation/revision checks still apply. Keep adorners/auto-scroll bounded and
remove them on every exit. [I23](interactions-sources.md#i23)

## 4. Deletion and swipe actions

### Action semantics

Distinguish deleting a domain record, removing it from a collection, archiving,
and hiding one occurrence. Name the action accurately and apply the appropriate
permissions. A red/destructive visual does not provide confirmation, rollback,
or undo.

Choose:

- **Reveal:** expose an actual button/action, then invoke explicitly.
- **Execute/full swipe:** commit after the platform's threshold/release rule.
- **Confirmed/pending:** keep or visibly mark the item until acceptance.
- **Optimistic:** remove locally, retain a bounded inverse/tombstone and reconcile.

Prefer an explicit safe policy for irreversible actions. Do not impose a
confirmation dialog for every reversible deletion, or enable destructive full
swipe without considering accidental activation and recovery.
WinUI documents Reveal as the safer versatile mode; UIKit full swipe defaults to
performing the first action. [I8](interactions-sources.md#i8),
[I16](interactions-sources.md#i16)

All invocation paths should call the same keyed command. Do not attach both
`Command` and an `Invoked`/`Clicked` mutation that performs the same work.
Native/framework activation callbacks may already execute the command.
MAUI 10.0.41 `ISwipeItem.OnInvoked` and `IMenuItemController.Activate` do.
[I5](interactions-sources.md#i5)

### Correct publication and recovery

Capture the intended key, then revalidate at commit. Cancel item-specific assets,
subscriptions, previews, open reveal state and focus references as appropriate.
Update geometry/count/chrome coherently and use the one structural authority.

If the visible anchor is deleted, restore a surviving neighbour using the declared
relative-offset policy. If a selected/focused row is deleted, choose a next/previous
or parent focus target; do not leave focus on a recycled container.

Prevent old same-epoch page results or database/server refreshes from resurrecting
the deleted key. Keep a tombstone/optimistic overlay until acknowledged or reconcile
by accepted mutation revision. For a confirmed delete that fails, reset any native
swipe-dismiss visual and show failure rather than leaving a phantom gap.

Undo should reference the operation, original item/group and surviving neighbours,
not `Insert(oldIndex, item)` after unrelated edits. Bound undo retention and expiry;
remote restore can fail and must be reported. A still-pending delete and its undo
need a defined cancellation/compensating-operation ordering.

### Platform boundaries

| Target | Native action path | Do not assume |
|---|---|---|
| UIKit list sections | `UICollectionLayoutListConfiguration` swipe providers, `UIContextualAction`, `UISwipeActionsConfiguration` | UITableView delegate swipe methods apply to custom collection masonry |
| UIKit arbitrary cards | App-owned reveal interaction/native actions or a suitable retained MAUI `SwipeView` | Any UICollectionView layout automatically gets list swipe |
| Android | `ItemTouchHelper.OnSwiped`; custom reveal buttons if needed | Painting a delete label makes a tappable/accessibility button |
| WinUI | Per-item `SwipeControl`, Reveal/Execute and `Invoked` | Mouse users can access touch swipe without an alternative |
| AppKit | Context/keyboard/toolbar delete; custom reveal interaction where required | There is an AppKit UITableView swipe configuration |
| GTK4 | Native action buttons/menu; custom gesture/revealer with axis arbitration | `GtkGestureSwipe` implements a complete editable list row |
| WPF | Context/keyboard/button actions; custom manipulation/reveal if required | WPF has WinUI `SwipeControl` or the repo's SwipeView is real swipe |

Reset translation, opacity, elevation, open side and interaction state during
cancel/recycle/rebind. Native state transitions differ; visibility loss is not
always recycling. Use semantic start/end versus left/right deliberately in RTL.
Vertical swipe conflicts with vertical scrolling and refresh; horizontal swipe
conflicts with horizontal shelves/navigation.

For UIKit contextual-action completion, call the completion once with the result
of the accepted UI action; define separately how durable persistence is reported.
For Android, a completed swipe can leave a holder translated pending removal:
restore the current keyed row when rejection is the policy rather than removing
whatever happens to occupy a cached position.

## 5. Refresh is a loading intent and reconciliation policy

### More than a spinner

Separate initial load, user refresh, append/prepend, retry and query replacement.
A single `IsBusy` flag often hides necessary concurrency/error distinctions.
Treat pull, button, menu and keyboard refresh as entry points to the same operation.

For same-query refresh, retaining useful content while fetching can preserve
context and avoid an empty/error flash. For a query replacement, a deliberate
clear/reset-to-top can be appropriate. Neither policy is universal.
Specify selected keys, expanded groups, current anchor and follow-tail behavior.

An operation owner (request ID/generation) should control refresh state:

```text
acquire/coalesce refresh intent
invalidate incompatible append/prepend and order-dependent cursors
fetch/reconcile off the UI thread
publish accepted source and load/error state on the owning dispatcher
finish this operation's native visual/deferral
clear the shared refresh state only if this operation still owns it
```

Cancellation does not guarantee no callback. A superseded refresh must not publish
old rows or stop a newer spinner in an unconditional `finally`. Native deferrals
still need completion on success, error, cancellation and teardown; separate
finishing that native request from overwriting the current app-owned load state.

Coordinate with active drag, swipe, edit and menus. A refresh can invalidate their
key mappings; cancel, defer, or reconcile those interactions explicitly. Preserve
accepted pending deletes/reorders when applying an older server/database result.
Pagination demand should pause/coalesce appropriately, not start overlapping
refresh and append work against invalid remote keys.

### Stock MAUI-specific trap

`RefreshView.Content` should be the finite scrolling collection directly, not a
new `ScrollView` around an already scrolling CollectionView. A small
BindableLayout/FlexLayout demonstration is not the large-data virtualized recipe.

On inspected MAUI 10.0.41, changing `IsRefreshing` to true invokes `Refreshing`
and `Command`. Therefore setting it true and separately executing that command
can issue duplicate work. Use one invocation route and a single-flight operation.
The public indicator is already true when a pull invokes the command; an
`if (IsRefreshing) return` work guard can skip loading and strand the spinner.
Use a separate operation owner.
Finish with the UI state false for the current owner, including errors.

`IsRefreshEnabled` exists in the inspected .NET 10 source and disables the refresh
gesture while leaving content interactive. `IsEnabled=false` disables the whole
subtree. `Command.CanExecute` participates in refresh enabling. Verify the resolved
MAUI package/backend mapping rather than projecting current docs onto old versions
or assuming a custom backend maps every new property.
[I3](interactions-sources.md#i3)

### Native refresh mechanisms

| Target | Starting point | Completion/input boundary |
|---|---|---|
| UIKit iOS/Catalyst | `UIRefreshControl` attached to UICollectionView/UIScrollView | ValueChanged starts work; EndRefreshing on the owner dispatcher; button/shortcut for desktop |
| Android | `SwipeRefreshLayout` with one real scrolling child | Listener requests refresh; `Refreshing`/isRefreshing ends it; correct child-up/nested-scroll detection |
| WinUI | `RefreshContainer` / `RefreshVisualizer` | GetDeferral and Complete; touch pull, RequestRefresh for non-touch entry |
| AppKit | Explicit refresh action plus native progress indicator | No UIKit refresh control; custom elastic gesture only if required and verified |
| GTK4 | Explicit action plus GtkSpinner; custom touch threshold if required | edge-overshot/reached alone is not an armed/released refresh state machine |
| WPF | Explicit command plus progress; custom touch behavior if required | No built-in UIKit/WinUI refresh container on WPF |

For a custom Android manager, verify `CanScrollVertically(-1)` reflects its real
offset, or provide a correct child-scroll-up callback. Do not disable nested
scrolling indiscriminately to make pull trigger: that can break parent/shelf
scroll ownership. Empty and short content need testing too.
[I13](interactions-sources.md#i13)

UIKit refresh control dates from iOS 6; the UIScrollView `refreshControl` property
for collection hosting is a separate API/version check. Match content insets,
bounce behavior and the actual scroll owner. [I10](interactions-sources.md#i10)

WinUI's documented refresh example uses a deferral. The Gallery's timers/static
sample fields illustrate animation, not production cancellation or disposal.
Do not copy timer completion as evidence of repository success.
[I17](interactions-sources.md#i17)

Desktop wheel/trackpad scroll-to-top should not unexpectedly refresh. Offer a
discoverable native action and shortcut. If a desktop product explicitly requires
pull, implement threshold, release, cancellation, spinner layout and conflicts,
and describe the custom behavior instead of advertising built-in parity.

## 6. Context menus and item actions

### The menu target is not necessarily the selection

Define whether a menu acts on the pointed item, focused item, current selected
block, or empty collection space. If right-clicking outside a selected block
changes selection, make that deliberate. Keyboard invocation should target focus,
not the last pointer location.

Capture intended keys/scope when constructing the action context and revalidate
those same keys at invocation. A reused cell's new binding must not change the
meaning of a menu already opened for another item. Conversely, cached permissions
and enabled state must not bypass authoritative command validation.

Build lightweight action descriptions from available state. Fetching HTTP/SQL in
a synchronous menu provider stalls interaction. For genuinely deferred native
menus, complete the supported callback once, bound the work and reject old
source/handler ownership. Keep previews pixel-sized and do not retain old pooled
view graphs indefinitely.

All routes (swipe, menu, visible button, keyboard and accessibility action) should
use the same domain command and pending/permission rules. Destructive styling is
not a different deletion protocol.

### Platform routes

| Target | Native route | Important distinction |
|---|---|---|
| UIKit | UICollectionView delegate context configuration or UIContextMenuInteraction; UIMenu/UIAction | Single-item context API since 13; multi-item/empty-space callback since 16 |
| Mac Catalyst | UIKit menus plus pointer/keyboard interaction | Not AppKit NSMenu; stock MAUI ContextFlyout has its own limits |
| Android | Floating context menu, PopupMenu for explicit overflow, ActionMode for selected content | Long-press drag versus context action needs an explicit policy |
| WinUI | ContextFlyout with MenuFlyout/CommandBarFlyout; ContextRequested when needed | Menu/command button and context invocation have distinct targets |
| AppKit | NSMenu and NSView.MenuForEvent/menu(for:) with validation | Resolve right-click coordinates/focus to a model key |
| GTK4 | Gio.Menu/GAction plus GtkPopoverMenu; shortcuts/keyboard and optional long-press | Factory-bound key context and controller/action cleanup |
| WPF | ContextMenu/MenuItem and routed/ICommand actions | Popup is a separate tree; explicit PlacementTarget/current context bridge |

UIKit's iOS 16 multi-item callback can supply zero, one or multiple index paths.
Zero means empty space, not item zero; multiple means a selection context. Convert
paths to stable keys immediately. The provider API does not excuse delayed use
of an old index path. [I9](interactions-sources.md#i9)

Stock MAUI `FlyoutBase.ContextFlyout` is documented for Mac Catalyst and Windows,
not portable mobile long-press. Entry on Catalyst and icon/dynamic-menu behavior
need specific checks. Documentation's blanket runtime add/remove warning is
broader than inspected 10.0.41 source: the Windows handler implements updates;
the UIKit menu handler's Add/Remove/Insert/Clear methods are empty. Do not assume
all platforms support dynamic menu changes or forbid them all.
[I4](interactions-sources.md#i4)

For WPF, avoid an ancestor lookup that assumes the ContextMenu is inside the row
visual tree. Resolve the placement/current context when opening, capture keys
for the action, and avoid a reused/shared menu silently retaining its first row.
[I24](interactions-sources.md#i24)

## 7. What the inspected maui-labs backends actually do

These are source observations, **not fixes made or runtime certification**.
[Repository sources](interactions-sources.md#i26)

| Backend | Drag/drop recognizer bridge | SwipeView | RefreshView | ContextFlyout |
|---|---|---|---|---|
| AppKit | GestureManager switch handles tap/pan/swipe/pinch/pointer, not drag/drop | Custom horizontal pan/reveal; maps left/right and close; no complete stock-policy claim | Overlay spinner; no gesture/refresh button in the inspected handler | NSMenu projection; basic snapshot-style mapping, not complete dynamic/submenu parity |
| GTK4 | GtkGestureExtensions switch lacks DragGestureRecognizer/DropGestureRecognizer | Custom horizontal drag/reveal; maps top/bottom names but builds only left/right | Button/spinner; not native pull | Right-button GtkPopoverMenu projection and action group |
| WPF | Mouse-based/reflection bridge exists, with limited data/result handling | Context-menu substitution, not swipe reveal | Progress bar, no pull gesture | Basic ContextMenu projection |

Native AppKit and GTK can implement real data transfer despite missing MAUI
recognizer bridging. A bespoke native handler can use their native APIs, but a
portable gesture recognizer is not proof of integration.

Additional source-derived hazards:

- GTK refresh click sets `RefreshView.IsRefreshing=true` and explicitly executes
  Command; inspected MAUI core already executes it on that transition.
- GTK menu activation explicitly executes Command and calls framework Activate;
  GTK swipe similarly executes Command and invokes the framework swipe callback.
  Those framework paths also execute the command in the inspected core.
- Basic menu projections can lose live enabled state, event semantics, submenu
  structure or updates. Recycled templates make stale-context errors more visible.
- WPF drag/drop reflection paths swallow exceptions and do not implement a
  production transfer/reorder transaction. Do not copy them as the complete recipe.

Treat these as gaps to address/test in an implementation task, not reasons to
silently advertise substituted behavior. This research does not modify the
existing backends.

## 8. Interaction performance and accessibility gates

Native virtualization must remain intact while a preview, insertion marker,
swipe button or menu is open. Bound active controllers/previews/retained source
items, and dispose them on cancel, drop, rebind, source change and disconnect.
Repeated interactions must not accumulate timers, signal handlers, event
subscriptions, adorners, popup windows or decoded previews.

Measure proposal/draw/auto-scroll work separately from mutation diff/layout and
repository latency. Drag hover should not trigger repeated network page loads or
serialize the entire dataset. Structural masonry moves can reflow a suffix;
describe that cost rather than asserting O(1) reorder because only one card moves.

Test touch versus pointer separately; also trackpad, keyboard, touch slop,
RTL, reduced motion, high contrast and text scale. Long-press may mean drag,
context menu or selection; a distinct handle/overflow affordance is often clearer.

Provide a clickable/tappable non-drag alternative such as Move up/down,
Move to group or select-then-place, plus keyboard/screen-reader operation.
Keyboard-only support is not an equivalent single-pointer route for someone
using touch or an adapted pointer. WCAG 2.5.7 supplies this design principle for
web content; it is not being declared a native-app legal certification.
[I25](interactions-sources.md#i25)

After changes, announce the meaningful result when appropriate and preserve
focus/selection by key. Do not intercept Delete/Ctrl+Z globally while a text
editor owns those keys. Refused/cancelled operations should restore coherent
visual state without announcing successful deletion or transfer.

## 9. Execution cases to add before claiming interaction support

| Area | Required evidence |
|---|---|
| Reorder | Up/down/self/end/empty targets, multi-selection block order, grouped moves, sorted/filtered/windowed limits, reload/persistence |
| Drag lifecycle | Edge auto-scroll, custom manager visibility, external imports, placeholder cancel/failure, source replacement, native bridge cleanup |
| Delete | Late menu/swipe after reuse, double command, confirmation cancel, failure recovery, same-epoch page resurrection, bounded undo and focus fallback |
| Refresh | Empty/short/scrolled content, query A/B races, stale spinner completion, append conflict, error/retry, active drag/edit and teardown |
| Menu | Pointer/focus/multi/empty-space target, changed permissions, recycled/shared popup, disabled actions, keyboard and native roles |
| Performance | Native/MAUI realization, proposal candidate count, preview bytes, controller/listener plateau and actual Release frame traces |

The authored skill evaluations catch routing, APIs and these failure modes.
They are not executed model results or proof that a generated native implementation
meets the matrix. The existing masonry example remains explicitly narrower.
The bespoke skill now has 52 authored scenarios (20 interaction additions); the
stock-control skill has eight focused interaction scenarios. Both remain
individually distributable without installing companion skills.
