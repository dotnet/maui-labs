# Source, identity and update contract

Keep HTTP/SQL/business ordering in the repository. Native bind/layout callbacks
consume cheap indexed presentation data. Adapters and MVVM solve different
problems and can coexist. [Sources M1, M3, U3, B3, W1, G1](sources.md)

## Select a small boundary

| Source | Useful contract |
|---|---|
| Small static resident data | Indexed read-only list |
| Existing mutable MVVM collection | Adapter normalizes collection/item/group notifications |
| Search/filter/refresh | Immutable revisioned presentation snapshots |
| Huge/DB-backed source | Nonblocking indexed/windowed projections and explicit count/seek semantics |
| Unknown-total HTTP cursor | Growing/windowed source with continuation, not fictitious random access |

`ObservableCollection<T>` is not mandatory for immutable custom snapshots.
Replacing a source is a lifetime change; it must invalidate pending callbacks.

## Identity is not position

| Value | Purpose |
|---|---|
| Domain key | Durable real item identity across loads |
| Presentation key | Unique occurrence including row kind/section where needed |
| Content revision | Display/size changes under the same key |
| Source epoch | Filter/query/source/handler generation |
| Native index/index path | Temporary position only |

Keep selection, expansion, editor values and shelf offsets by key outside pooled
views. A repeated domain object needs distinct presentation occurrence keys.
Do not use mutable-model equality or position as persistent identity.

Native bridges need appropriate IDs: Apple identifier objects with stable
equality/hash, collision-safe Android long IDs if stable IDs are enabled, WinUI
key/index mapping, or GTK object/key lookup. An unchecked hash is not uniqueness.

## Publication transaction

1. Capture unchanging old/new comparison data and source epoch.
2. Prepare pure presentation/diff work off-thread where safe.
3. Validate unique keys, count/index ranges and accepted epoch.
4. Publish backing state plus native notifications/snapshot in one ordered,
   serialized UI transaction.
5. Restore keyed selection/focus/anchor after layout/update completion.

There is one structural update authority. Do not both apply a diffable snapshot
and independently insert its native cells. Do not mutate inputs while DiffUtil/
another background differ reads them.

Snapshot identity/order does not include all display fields. For unchanged IDs,
issue supported reconfiguration, payload, notification or keyed rebind. A
size-changing update must invalidate the appropriate cached geometry.

Interaction mutations also need [intended keyed targets and an order/mutation
revision](interaction-contracts.md). A source epoch alone does not stop an older
same-query page from resurrecting deleted data or reversing accepted order.
Native reorder/placeholder transactions can already own the structural update;
adopt their result instead of replaying it.

Coalesce complete superseded snapshots if valid. Never drop arbitrary insert/
remove operations from a dependent event stream. For wholesale query replacement
or a huge initial load, a deliberate reset may beat animated diffs; state its
selection/anchor policy instead of resetting on every small edit.

## Group and chrome projection

Choose native sections or a typed flat projection deliberately. Give headers,
footers, loading/error rows and items unique kind/key entries. Maintain a single
mapping to logical `(section key, item key, kind)`.

Root collection notifications do not see child group mutations. Observe and
unsubscribe child groups, or publish immutable section snapshots.
Handle empty/collapsed groups, moves between groups and section completeness.
Pages extending a group must not duplicate its header/footer.

## Binding choices

Typed native assignment avoids reflection/layout layers for a fixed design.
Retained MAUI templates remain appropriate when composition is required: declare
item/group `x:DataType`, reuse compatible handlers, and keep converters/getters cheap.
MAUI compiled binding, WinUI x:Bind and WPF binding are different implementations.

Do not synchronously query, format expensive data, decode images, or enumerate
the whole source in count/indexer/bind/type selection. Project needed values
before publication. Report genuine data/update failures explicitly.

**Test:** duplicates, changed content under one ID, range/move/reset events,
root/child group changes, source replacement, notification ordering and stale
epoch rejection using real source logic.
