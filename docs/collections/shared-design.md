# Shared design, geometry and data access

[Research index](README.md) | [Performance gates](performance-validation.md)

This chapter is a **proposed design vocabulary and set of defaults**, synthesized
from the cited native implementations. It is not a specification for a new
cross-platform collection framework. A generated control should take only the
parts its scenario needs.

## 1. Start with a scenario contract

A screenshot is insufficient. Record the decisions that determine architecture:

| Dimension | Questions that change the implementation |
|---|---|
| Geometry | List, uniform grid, variable row grid, shortest-column masonry, stable-column masonry, flow, justified rows, shelves, or coverflow? Which axis? |
| Size | Fixed extent, aspect-ratio metadata, bounded text, or genuinely arbitrary self-sizing content? Can dimensions arrive before images? |
| Scale | Hundreds, tens of thousands, or effectively unbounded items? How many loaded records and visible cells are expected? |
| Source | Already resident list, HTTP cursor, indexed local database, or network-backed local cache? Known total count? |
| Navigation | Append only, prepend chat history, arbitrary index jump, jump to key/date, or bidirectional loading? |
| Updates | Occasional immutable refresh, live item updates, inserts/removes/moves, or rapid telemetry? |
| Sections | Fixed groups, changing groups, expandable outlines, per-group paging, or editorial sections with distinct layouts? |
| Chrome | Scrolling global header/footer, section headers/footers, sticky chrome, empty state, loading/error rows, or overlays? |
| Interaction | Invocation versus selection, multi-selection, editing, swipe, reorder, drag/drop, keyboard and accessibility? |
| Targets | Exact platforms, backend packages, native/managed versions and minimum deployment versions? |
| Fidelity | Equivalent experience or identical placement? Is a platform-specific alternative allowed? |

**Recommended scope:** one named control and one primary layout contract, such as
`PhotoMasonryView` with a predictable photo/caption card. Supporting a resize-driven
column count does not require supporting unrelated layouts at runtime.

Stock `CollectionView` remains appropriate when it already satisfies the
requirements. Do not rewrite it simply because a bespoke recipe exists. Conversely,
do not promise that changing `ItemsLayout` makes it a native waterfall or general
virtualized wrap engine. [M2](sources.md#m2)

## 2. Separate five responsibilities

```text
HTTP / database / resident data
             |
repository + bounded load scheduler
             |
indexed presentation source + identity + ordered changes
             |
native adapter / data source / list model
             |
native layout + reusable item renderer + interaction policy
```

The MAUI handler connects the control's public properties and commands to the last
two layers. It should not own HTTP authentication, SQL queries, business sorting,
or persistent data storage.

| Responsibility | Owns | Must not do |
|---|---|---|
| Repository | Query/cursor consistency, paging, errors, cache/database writes | Create views or rely on a recycled cell's index |
| Presentation source | Stable keys, cheap index access, metadata, logical rows and ordered updates | Perform synchronous I/O in an indexer |
| Native adapter | Snapshot/notification publication, ID translation, cell configuration | Independently requery/change counts during native layout |
| Layout | Geometry, realization range, extent, invalidation and anchor policy | Download images to discover their size |
| Renderer | Create once, bind/unbind, appearance/accessibility, item-specific resources | Become the owner of durable item state |

This separation is explicit in UIKit's data/layout/presentation model, Android's
adapter/holder/manager design, and GTK's model/factory/view framework.
[U2](sources.md#u2) [B1](sources.md#b1) [G1](sources.md#g1)

## 3. Adapter-first does not mean no binding

**Recommendation:** make the native bridge adapter-oriented, and choose the
developer-facing data and rendering patterns per control.

An adapter represents count, indexed access, stable identity, updates and reuse.
MVVM describes application state/commands. Binding connects a renderer to that
state. An MVVM application can use an immutable, adapter-backed presentation source
and still use compiled bindings for each realized MAUI template.

| Input/rendering choice | Good fit | Cost / obligation |
|---|---|---|
| Typed native cells with direct assignment | A fixed, high-volume card or chat bubble | Per-platform renderer code; native accessibility/theme behavior must be wired |
| Cached MAUI `DataTemplate` per reusable slot | Apps need reusable MAUI composition or existing templates | Handler/view tree, binding-context, measure/arrange and logical-child ownership |
| Immutable presentation snapshots | Search/filter/refresh with clear revisions | Diff computation and snapshot memory; unchanged identity needs explicit content invalidation |
| Observable collections through an adapter | Existing MVVM models with incremental mutations | Normalize event shapes, dispatch publication, observe child groups and coalesce work |
| Indexed/windowed source | Very large or database-backed datasets | Placeholders, cache eviction, count/seek semantics and range-based loading |

Do not offer every row in this table as a public plugin system by default. Select
one source boundary and one renderer strategy for the requested control. Introduce
an interface only when it helps testing or an actual alternative implementation.
[M1](sources.md#m1)

For MAUI templates, declare `x:DataType` in each item/group template and keep binding
scope correct. Compiled bindings improve lookup/type safety, but they do not
eliminate measurement, deeply nested trees, expensive converters, or image decode.
Native WinUI `x:Bind` templates and MAUI XAML templates are different systems;
MAUI `x:DataType` does not automatically compile a native cell binder.
[M3](sources.md#m3)

Mutable `ObservableCollection<T>` is not mandatory for immutable snapshots or a
custom indexed source. It is an appropriate compatibility input when ongoing
collection notifications are needed, not the only high-performance data contract.

## 4. Identity, presentation state and update transactions

### Stable identity

Distinguish:

- **Domain key:** the real item's identity across loads and refreshes.
- **Presentation key:** a unique occurrence in this control, including row kind
  and section/occurrence when one domain object appears more than once.
- **Content revision:** whether displayed fields changed without changing identity.
- **Source epoch:** which filter/query/source/handler generation owns the result.
- **Native position:** a temporary index or index path, never permanent identity.

A practical presentation row might carry key, kind, section key, content revision,
renderer/template key, and size metadata. Keep native IDs as a mapping from those
keys: cached `NSString`/other `NSObject` identifiers on Apple, collision-free
`long` IDs when Android stable IDs are enabled, or a WinUI key/index mapping.
Do not cast an arbitrary string hash to a stable ID and ignore collisions.
[U3](sources.md#u3) [A4](sources.md#a4) [W1](sources.md#w1)

Selection, expansion, editing values and nested scroll offsets belong in keyed
presentation/application state. A pooled view may remember ephemeral rendering
state only while bound. This prevents one record's expansion/selection/image from
leaking into another record's cell. [G1](sources.md#g1) [P1](sources.md#p1)

### Correct updates

**Recommended publication protocol:**

1. Capture an immutable old revision and the intended new revision.
2. Prepare presentation metadata/diff away from the UI thread when the operation
   and inputs are thread-safe. No native/MAUI view creation or measurement there.
3. Validate unique keys, count changes, index ranges and source epoch.
4. Publish backing state and native notifications/snapshot as one correctly
   ordered, serialized UI transaction.
5. Restore keyed selection/focus/anchor after the native transaction settles.

There must be one authority for structural updates. Do not both send a native
diffable snapshot and independently insert/delete its cells. Do not mutate objects
being compared by a background diff. Android's `DiffUtil` explicitly requires
unchanging lists and relevant element fields. [B3](sources.md#b3)

Native snapshots generally encode **identity and order**, not every field of an
item. Updating a caption/image under an unchanged key therefore needs a content
update: reconfigure/reload, a payload, a property notification, or binder refresh.
Choose the supported mechanism on each platform. [U3](sources.md#u3)

Batch/coalesce frequent notifications, but preserve ordering and atomicity.
Dropping old *complete snapshots* can be legitimate when the latest supersedes
them; dropping arbitrary insert/remove operations cannot. A source change must
invalidate queued work before it can revive the old source.

Avoid full reload for small edits. Initial large loads or total query replacement
can justify a deliberate reset if incremental animation is more expensive, provided
selection/anchor policy is explicit. IGListKit documents this tradeoff.
[U7](sources.md#u7) [B2](sources.md#b2)

## 5. Virtualize four things independently

| Dimension | Desired invariant |
|---|---|
| Native views | Realized containers scale with viewport + bounded overscan/pinned/focused content, not total count |
| MAUI child trees | Compatible templates are reused rather than reinflated on every bind |
| Data/assets | Loaded projections and decoded images have explicit bounds/eviction, not an ever-growing visited-item cache |
| Geometry work | Ordinary scroll locates candidates without measuring or scanning the entire dataset |

Some lightweight geometry metadata can legitimately be O(N) while realized views
are O(V). State its memory and rebuild cost; that is not the same as bounded data
virtualization. A million-item snapshot or rectangle array may still be too large.
Conversely, avoiding all metadata may make random seeks impossibly expensive.

Prefetch and overscan are different. Overscan realizes nearby views; prefetch
prepares data/assets or future holders. Both need limits. More cached views can
improve reverse scrolling while increasing native/managed memory and retained
subscriptions. Tune them after measuring, not by maximizing every cache setting.
[U3](sources.md#u3) [B2](sources.md#b2) [G1](sources.md#g1)

## 6. Geometry recipes and their limits

The following complexity goals are design analysis, not benchmarks. Let N be
logical items, V viewport candidates, and C columns.

### Fixed linear and uniform grid

Fixed extent makes position/range arithmetic cheap. A list maps offset to item
index; a grid maps offset to row and then row to item indices. Handle spacing,
insets, global/section chrome, the partial final row, and RTL explicitly.

Use the bounded cross-axis viewport to determine column count. Cache geometry
inputs once per layout pass rather than repeatedly retrieving native properties.
The WinUI ActivityFeed sample illustrates this approach, but its three-item row
assumption is not a general arbitrary-count implementation. [W8](sources.md#w8)

### Variable linear or variable row grid

Use estimates until constrained measurements are known. Cache by:

```text
(presentation key, content revision, renderer/template key,
 cross-axis constraint, font/text scale, relevant style revision)
```

Width changes, text-scale changes, template changes and content changes can
invalidate height; a plain item-index cache is insufficient.

Prefix extents plus binary search work for immutable geometry. A Fenwick/segment
tree can support point-size changes and prefix lookup in O(log N), but an
array-backed tree does **not** make structural insertions/moves O(log N). Rebuild,
chunk, or use an order-statistics structure when structural edits justify it.
Do not advertise an asymptotic property that the chosen data structure lacks.

A variable row grid advances by the maximum row item height. Updating one card may
invalidate its row and subsequent row positions, not every card's measurement.

### Masonry / waterfall

For predictable photo cards, fetch source pixel dimensions with metadata:

```text
column width = (available width - insets - intercolumn gaps) / column count
image height = column width * source pixel height / source pixel width
card height = image height + known chrome + constrained caption estimate/measure
```

Shortest-column placement can be computed from metadata without inflating cells.
For small C, a linear search across column heights is often simpler than a heap;
for larger C, a priority queue gives O(N log C) initial placement. These are
algorithm choices to profile, not a universal heap requirement.

Store ordered intervals per column or suitable block/spatial indexes so a viewport
query can approach O(C log N + V), rather than enumerate every item rectangle.
Respect full-span headers/footers, which advance all columns to a common boundary.
They also form useful invalidation checkpoints.

An early item height change or removal can change every later shortest-column
assignment. Do not claim constant-time reflow. Choose explicitly among:

- recompute an affected suffix from cached metadata/checkpoints;
- preserve a stable assigned column and update its extents;
- defer disruptive rebalancing until refresh;
- limit supported edits or loaded windows for the scenario.

CHT's cached attributes/union rectangles and WinUI's incremental bounds are useful
examples, but both inspected implementations retain linear lookup or broad reflow
work that must be reconsidered for larger datasets. [U6](sources.md#u6)
[W8](sources.md#w8)

### Wrap and justified rows

Wrap depends on sequential line breaks. Knowing the width/height of an item does
not directly tell its position without preceding line state. Cache line starts,
line extents and checkpoints; invalidate from the changed line forward. A deep jump
can require prefix computation when no prior metadata/checkpoint exists.

For justified image rows, use aspect ratios to choose a row height and widths,
with explicit minimum/maximum height, spacing, crop and final-row policy.
WinUI `LinedFlowLayout` is a native option when its rules match; it is not
shortest-column masonry or unrestricted variable-height flow. [W2](sources.md#w2)

For arbitrary text wrap, measuring all offscreen MAUI/native views to construct an
exact extent defeats virtualization. Use bounded constraints, size metadata,
estimates, or a specialized thread-safe text measuring facility whose native
documentation explicitly permits it.

### Carousel / coverflow

Keep logical scrolling geometry separate from visual transforms. Locate a bounded
candidate range, transform only those items, and reset transforms on rebind.
Account for transformed visual bounds when deciding whether a candidate is visible.

Specify focal alignment, item extent, snapping, overlap, perspective, z-order,
hit testing, reduced motion and keyboard movement. Animating layout constraints on
every frame is generally more expensive than composited drawing transforms.
[U5](sources.md#u5) [B2](sources.md#b2)

Do not call a Material masked carousel a 3D coverflow. The historical iCarousel
source demonstrates perspective plus view pools, but is archived and is not the
recommended shipping dependency. [B10](sources.md#b10) [U9](sources.md#u9)

An infinite-loop carousel needs unique occurrence keys and bounded caches even
when domain keys repeat. A giant artificial item count is not a substitute for
correct extent, modulo state, restoration and accessibility semantics. Add looping
only if the requested scenario needs it.

## 7. Grouping, headers/footers and nested collections

### Two useful section representations

**Native sections:** preferable when the native engine provides the exact desired
layout and supplementary behavior. Model section identity separately from item
identity; use native header/footer providers and registered reuse kinds.

**Typed flattened rows:** useful for Android/WPF lists and controlled cross-platform
feeds. Give every header, footer, loading/error row and item its own presentation
kind/key. Header/footer rows must not accidentally count as selectable items or
as remaining data in a pagination threshold.

Do not mix the representations ad hoc. Maintain a single mapping between native
positions and logical `(section key, item key, row kind)` entries.

Global header/footer, per-section chrome, sticky overlays and empty/loading state
are distinct. A sticky header is not achieved merely by inserting a header row.
For a custom sticky header, clamp its position between its section's start and the
next section boundary; account for z-order, hit testing and duplicate accessibility
exposure. A group footer can be final only when group completeness is known.

Grouping also changes source observation. Root notifications do not observe
mutations inside an existing group. Subscribe/unsubscribe to mutable child groups,
or publish immutable section snapshots, including empty/collapsed groups.
[R1](sources.md#r1)

### Paging across groups

A page can begin/end halfway through a group. Carry section keys and continuation/
completion metadata so the next page extends the same section instead of adding
another header. Decide whether global ordering is guaranteed by the server/database;
sorting only the loaded page can produce inconsistent group boundaries.

Collapsed groups need explicit loaded/total counts and cancellation policy.
Avoid loading all their children merely to render the collapsed header.

### Nested / orthogonal collections

One scroll owner per axis is a useful default, not a blanket prohibition on
nesting. A finite-height horizontal shelf inside a vertical feed can retain
virtualization at both levels. An unbounded same-axis inner collection inside a
parent scroller commonly defeats it.

Prefer native orthogonal sections on UIKit/AppKit when they fit. Android's official
guidance supports nested recyclers with compatible shared pools and bounded
initial prefetch. [U1](sources.md#u1) [U4](sources.md#u4) [B2](sources.md#b2)

For nested controls:

- budget realized views across outer and inner viewports;
- pool only compatible renderer types, not merely equal integer type numbers;
- store inner offset/anchor by section key before outer-row recycling;
- cancel or deprioritize data loads for unbound sections without corrupting shared
  consumers;
- test diagonal gestures, wheel routing, keyboard focus, reorders and empty sections;
- avoid constructing every nested collection in the source before realization.

## 8. Data-source decision table

| Data reality | Recommended starting policy |
|---|---|
| Small resident list | Indexed read-only/observable input; no paging system needed |
| Remote append-only feed | Cursor-based repository, explicit load states, single-flight append, bounded asset prefetch |
| Remote chat/history | Bidirectional cursors, prepend anchor preservation, follow-tail only when the user was already following |
| Large local database | Indexed projected pages, keyset continuation, query worker, bounded cache; explicit seek/count strategy if needed |
| Offline-first HTTP + DB | Database is UI source of truth; network writes data and remote keys transactionally, then invalidates/requeries |
| Known-count random access | Nonblocking placeholder indexer, range-based page cache and source-supported seek semantics |
| Unknown-count cursor source | Growing/windowed extent; no fictitious random-access count or exact scrollbar promise |
| Live updates | Revisioned keyed changes, rate-aware coalescing and precise size/content invalidation |

Pagination limits returned data; data virtualization also bounds retained data.
Appending pages forever is still an unbounded resident list. Choose retention
policy based on back-scroll, offline requirements and source-supported refetch.

## 9. A bounded loading protocol

**Proposed scheduler state**, per query epoch and load direction:

```text
Idle -> Loading -> Ready / End / Error
                 -> Cancelled (source change or explicit cancellation)
```

An end-of-data result is not the same as a transport/query failure. Preserve data
already shown when append fails and expose retry; do not turn exceptions into an
empty-success page. Refresh must not leave a spinner active after failure.

Deduplicate by query/epoch/cursor or page key. Bound concurrency independently for
metadata requests and image decoding. Share a request among consumers; cancelling
one recycled cell should release its demand, not necessarily cancel work still
needed by another visible cell.

The load trigger can be native prefetch, viewport distance, remaining logical items
or explicit user action. Avoid starting I/O synchronously from size lookup, count,
indexer, `GetItemViewType`, or cell creation.

Choose initial prefetch from measured latency and scroll demand, not an unexplained
fixed threshold. A useful **heuristic**, capped by memory/concurrency budgets, is:

```text
prefetch distance ~= scroll speed *
                     (page latency + decode/bind latency + safety margin)
```

Use hysteresis and prioritize visible content; do not continuously request every
page crossed during a fast fling. Small collections that cannot fill the viewport
may need bounded repeated page loads. Stop on end/error/no-progress; guard a server
that repeats its cursor instead of entering an infinite threshold loop.

## 10. HTTP-backed sources

Use the app's existing network/client/authentication infrastructure and reuse
appropriately managed `HttpClient` instances. Verify mobile handler behavior rather
than transplanting desktop socket settings. [D7](sources.md#d7)

Recommended responsibilities:

- use the server's continuation token/ordering and snapshot semantics;
- include stable IDs, thumbnail keys, dimensions and content revisions in metadata;
- pass cancellation through request and parsing work; still reject stale results
  by epoch because cancellation can race with completion;
- bound response/page size and parsing, avoid loading an entire catalog to show
  the first viewport;
- distinguish transient failures, rate limits, expired cursors and authentication
  failure; apply the app's retry/backoff policy rather than infinite cell retries;
- separate metadata loading from thumbnail requests; key the image cache by asset
  revision, target pixel size and transform/crop variant;
- maintain explicit freshness and offline/error presentation.

An `IAsyncEnumerable<T>` or streamed JSON source still needs an indexed/windowed
presentation buffer and ordered native publication. A stream is not an O(1)
random-access collection. Source-generated serializers are appropriate where
the consuming app's trimming/AOT configuration requires them.

## 11. Database-backed and offline-first sources

### Query shaping

Project only fields required by the card: ID, sort/group keys, caption, dimensions,
thumbnail reference and revision. Fetch related details when explicitly needed;
never trigger ORM lazy-loading getters from a cell binder. Index filtering,
sorting and continuation together, and inspect the actual query plan.
[D1](sources.md#d1) [D2](sources.md#d2)

For next/previous scrolling, indexed keyset pagination is generally preferable to
deep `OFFSET`. Always add a unique tie-breaker. For example, this **next-page SQL
sketch** uses non-null integer sort keys and a unique integer ID:

```sql
CREATE INDEX IX_Photos_CreatedUtc_Id ON Photos (CreatedUtc DESC, Id DESC);

SELECT Id, CreatedUtc, Caption, PixelWidth, PixelHeight, ThumbnailKey, Revision
FROM Photos
WHERE CreatedUtc < $lastCreatedUtc
   OR (CreatedUtc = $lastCreatedUtc AND Id < $lastId)
ORDER BY CreatedUtc DESC, Id DESC
LIMIT $pageSize;
```

Fetch the first page without the continuation predicate. Validate page size and
bind parameters. Adapt null handling, collation, tenant/filter predicates, order
direction and indexes to the real schema. Keyset does not make arbitrary
index/page jumps free, and does not create a consistent multi-page snapshot when
rows' sort keys change. Use snapshot/version semantics, anchors or a deliberate
refresh policy where required. [D1](sources.md#d1) [D3](sources.md#d3)

For true random access, choose an explicit indexed seek/rank scheme, cached page
anchors, or measured offset queries under known constraints. Do not expose
`ScrollTo(100000)` when a cursor-only source cannot fulfill it.

### Thread and transaction ownership

`Microsoft.Data.Sqlite` async APIs execute synchronously. Calling
`ExecuteReaderAsync` on the UI thread therefore does not establish nonblocking
loading. Run database work through a bounded repository-owned worker/queue with
correct connection/context ownership, not one uncoordinated `Task.Run` per cell.
[D4](sources.md#d4)

EF Core contexts are not thread-safe. A page operation can own a short-lived
context or use a factory; concurrent loads must not share it unsafely.
Dispose transactions/readers promptly. [D6](sources.md#d6)

WAL can improve reader/writer concurrency but still has a single writer,
checkpoint work, busy/error cases, and durability choices. Do not hold a read
transaction for the lifetime of a scrolled page or automatically weaken
durability to improve a benchmark. [D5](sources.md#d5)

### Offline-first coordination

Android's `RemoteMediator` demonstrates a useful cross-platform architecture:

```text
native demand -> repository/network fetch
             -> transaction: upsert projections + cursor/remote keys
             -> database invalidation
             -> new query projection -> native update
```

There is one UI source of truth. Do not append the HTTP response directly and
then append the same rows again from the database observer. Store remote keys
with the associated query/filter and rows atomically; refresh invalidates older
append/prepend work. Keep local database eviction distinct from the in-memory page
cache, and preserve offline guarantees deliberately. [B6](sources.md#b6)

## 12. Renderer and handler lifetime

### Create, bind, unbind, destroy

| Stage | Responsibilities |
|---|---|
| Create/setup | Construct compatible child tree once, cache child references, attach stable container-level listeners |
| Bind | Assign current key/revision, reset all item-dependent appearance, attach item subscriptions, start/request bounded work |
| Unbind/recycle | Release item subscriptions/demand, invalidate binding generation, clear sensitive/transient state; retain compatible child tree |
| Destroy/teardown | Remove stable listeners, disconnect/dispose owned handlers/views/native references, release pooled resources |

Disappearing is not always recycling. UIKit prefetched cells may be prepared but
never displayed, or displayed again without a new bind; Android detach can be
temporary. Keep visibility-driven playback separate from binding ownership.
[U3](sources.md#u3) [B4](sources.md#b4) [G2](sources.md#g2)

An async completion must validate **item key + binding generation + source epoch**
before touching a currently bound view. Prefer updating keyed data and requesting
native reconfiguration over capturing a reusable cell. Even reconfiguration may
not call `PrepareForReuse`; bind must be idempotent and clear obsolete state.

For retained MAUI templates, preserve the handler while pooling a compatible tree,
rebind context correctly, and detach logical ownership appropriately. Permanently
evicted/replaced trees must disconnect handlers. Do not rebuild handlers on every
bind merely to get correctness, and do not retain every tree ever visited.
Prior art: [M4](sources.md#m4), contrast [R1](sources.md#r1).

### Handler lifecycle

A generated handler should:

- create the native scrolling/collection owner and renderer/layout resources;
- connect once to the current source, native events and dispatcher;
- publish mutations on the view/window's dispatcher, including custom backends;
- invalidate queued work when source, context or handler changes;
- unsubscribe old root/group/item sources before adopting a replacement;
- disconnect native delegate/callback references and cancel owned work;
- drain/dispose pooled child trees and native assets it owns, without disposing
  shared repository/cache services;
- report errors through application-standard logging and explicit error state,
  including failures at native callback boundaries.

`#if IOS || MACCATALYST` shares UIKit code; `MACOS` uses AppKit, not that same
implementation. GTK/WPF host projects can have different TFM/symbol conventions;
inspect them instead of inventing a `LINUX` or `WPF` symbol.
Use the existing custom-backend base handlers and registered dispatchers rather
than assuming stock `MainThread` services work everywhere. [R4](sources.md#r4)

MAUI owns the **outer** control's placement. The native collection engine owns
item placement, scrolling and virtualization. When hosting MAUI templates,
bridge its constrained child measure/arrange into native cell sizing without
letting two engines independently lay out the collection.

## 13. Explicit unsupported behavior

Maintain a small capability table for the generated control's actual targets.
Separate native availability, binding availability, backend integration, runtime
correctness and measured performance.

If a requested feature is unavailable, say what is missing and choose an explicit
custom implementation, approved alternative presentation, or scoped-out target.
Do not silently:

- replace a virtualized grid with an ordinary wrap/flow container;
- draw a fake native control where exact native behavior was requested;
- omit group footers, sticky behavior or accessibility;
- claim a current-main or experimental API works on the app's stable SDK;
- report an empty list when loading or native rendering actually failed.
