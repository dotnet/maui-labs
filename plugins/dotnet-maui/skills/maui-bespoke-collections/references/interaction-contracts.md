# Interaction and mutation contracts

Use for requested drag/reorder, delete, refresh and item actions. These are
source-backed design rules, not features certified by the mobile masonry example.
[Sources I1-I26](sources.md#i1)

## Start with semantics

Specify operation, target platforms/input, source volume/order, grouping,
permissions, feedback, confirmation/undo and persistence. Include only requested
interactions; a new generic edit framework is not the default.

| Distinction | Consequence |
|---|---|
| Reorder versus copy/import/transfer | Different identity, source-removal and completion contracts |
| Manual versus computed order | Dragging a date/score-sorted row does not define durable manual rank |
| Domain record versus presentation occurrence | Removing a shelf occurrence need not delete its shared product |
| Item versus selection versus empty space | Pointer context and selection/focus need not agree |
| Same-query refresh versus query change | Retain/reconcile versus deliberate reset are separate policies |
| Optimistic versus confirmed | Pending state, rollback, unknown outcome and undo must be defined |

## Four identity/version dimensions

Capture intended keys, source/query scope and expected revision when an
interaction begins. Resolve those same keys at commit and recheck existence,
permissions, pending state and conflicts. Never retarget an old menu/drop to a
different item because its container was rebound.

| Dimension | Protects |
|---|---|
| Stable domain/presentation key | Intended record/occurrence, not native position |
| Source epoch | Query/source/handler replacement |
| Order/mutation revision | Concurrent edits within the same epoch |
| Bind generation | Reusable view ownership |

Operation IDs additionally correlate duplicate dispatch/retries/undo.
Epoch checks alone do not prevent a same-epoch page from resurrecting a deleted
item or undoing a reorder. Reconcile pages against accepted mutations or invalidate
the affected source; use bounded keyed overlays/tombstones when appropriate.

## One mutation authority

```text
interaction -> keyed intent -> validate -> accepted projection/native transaction
            -> persist/reconcile -> explicit outcome + focus/anchor + optional undo
```

Publish data and native updates coherently on the owning dispatcher. Do not:

- Replay a UIKit diffable native reorder as a second move.
- Insert again after a UIKit placeholder commits insertion.
- Mutate a differ-owned list and manually notify the same changes.
- Remove a transferred item in both Drop and drag-completed callbacks.
- Execute Command and call a framework activation method that executes it again.

Native acceptance/animation is not server durability. Publish optimistic feedback
or pending state deliberately and persist off the UI thread. Roll back an
operation against current revisions/neighbours, not a whole old snapshot over
newer edits. A timed-out request may have committed: use actual backend
idempotency/concurrency and reconciliation, not blind replay.

For offline-first data, transact item/order/membership, remote keys and outbox/
tombstone state and publish one database projection. Do not double-publish HTTP
and observer results.

## Interaction versus renderer lifetime

Store selected keys, open reveal ownership, pending operations, drag context and
undo outside pooled views. Scope them to source/operation lifetime, not one cell
forever. Keep active previews/controllers bounded.

Reset translation/opacity/elevation/open side on cancel, recycle and rebind.
Detach listeners, stop auto-scroll, close/detach owned menus/adorners, cancel
owned import/preparation and reject stale completion on source change/disconnect.
Some native drag APIs temporarily retain the active container; do not disable
recycling globally.

## Input and alternatives

Arbitrate scroll, swipe, drag and long-press menu explicitly. A dedicated reorder
handle plus an overflow action can avoid ambiguous long-press. Respect RTL and
transformed hit testing, nested scroll ownership and platform touch slop.

Offer clickable/tappable non-drag Move up/down/Move to group or select-then-place,
plus keyboard/screen-reader routes through the same commands. Keyboard-only
equivalence is not a single-pointer alternative. WCAG 2.5.7 is web guidance used
as a native design principle, not a native compliance verdict.

Restore focus/selection/anchor by key; choose a surviving neighbour after removal.
Do not steal Delete/Ctrl+Z from focused text editing. Report rejection/failure
explicitly and do not announce cancelled operations as successful.

Read only needed topic references:
[drag/reorder](drag-drop-and-reordering.md),
[delete/swipe](deletion-and-swipe-actions.md),
[refresh](refresh-and-reconciliation.md),
[context actions](context-menus-and-actions.md).
