# Android: RecyclerView

Native/source guidance. The maui-labs metadata photo-masonry slice has Android
Release emulator behavior proof and diagnostic FrameTimeline evidence; other
recipes remain unverified. No physical-device performance certification is claimed.
[Sources B1-B9, M4-M5](sources.md)

## Select a layout manager

| Scenario | Starting point |
|---|---|
| Linear vertical/horizontal | LinearLayoutManager |
| Row/column grid | GridLayoutManager plus explicit span lookup |
| Staggered unequal extents | StaggeredGridLayoutManager |
| Strict stable-column/deterministic masonry | Custom manager/geometry |
| Header/content/footer composition | Typed rows or ConcatAdapter |
| Shelves | Bounded nested recyclers and compatible shared pool |
| General wrap | Custom manager or consciously reviewed compatible Flexbox path |
| Material carousel | CarouselLayoutManager and MaskableFrameLayout roots |
| True coverflow | Specialized manager/visible child transforms and snapping |

Verify resolved AndroidX/Material C# bindings; do not add guessed package versions.
Grid rows and staggered columns are different geometry. Full-span headers need
SpanSizeLookup in grid or full-span parameters in staggered. Reset those parameters
when row kinds change.

Read visible positions across all valid spans, handle NO_POSITION, and exclude
chrome from data thresholds. Test gap/reassignment behavior on reverse scroll
and height changes; do not disable gap correction as an unexplained fix.

## Adapter, identity and binding

Create compatible holders once, cache child references, then bind cheap indexed
presentation values. Clicks use current key/BindingAdapterPosition, never an old
position captured during bind.

DiffUtil/ListAdapter inputs must not mutate while compared. Identity compares
stable keys; content compares displayed fields/revision. The official Flowers
sample's full-object identity and ID-only content callbacks are not a safe
mutable-content recipe. Keep UI publication ordered and serialized.

Stable IDs are separate from diff identity. If enabled, use unique collision-safe
long IDs across row kinds/occurrences. Do not copy position-returning GetItemId
and then enable stable IDs. ConcatAdapter stable-ID mode must be deliberate.

Payloads can skip unchanged setters but cannot replace a correct full bind.
Size changes invalidate geometry. Avoid NotifyDataSetChanged for small edits.
Reset image, selection, expansion, transforms, accessibility and layout params;
guard delayed results by key/generation/epoch.

OnViewDetachedFromWindow can be temporary; OnViewRecycled releases binding demand.
Do not disable recycling permanently to hide state bugs. Retain compatible MAUI
template trees/handlers and disconnect only on replacement/eviction/teardown.

## Important fixed-size and pool rules

HasFixedSize describes the RecyclerView's own size being independent of adapter
content, not uniform item heights. A fixed viewport can contain variable rows.

Share RecycledViewPool only for compatible holders with a consistent view-type
namespace. ConcatAdapter isolates child view types by default; disabling isolation
requires real compatibility. Set inner InitialPrefetchItemCount near initially
visible items, not the whole shelf. Prefetch schedules UI work when time exists,
not background view inflation. Preserve inner offsets by section key.

For a custom manager that recycles a whole outgoing window before a nonoverlapping
scroll jump, a small per-type pool can discard compatible holders before the
incoming fill. Measure warm/repeat creation counts; the mobile prototype sizes
its single-type pool to the peak viewport-plus-overscan realization, not the
dataset. Do not solve this by retaining every created holder in a diagnostic
HashSet or by adding a dataset-sized cache. [Local proof R4](sources.md#r4)

`RequestLayout` is asynchronous. An offset field changing is not proof that the
corresponding cells were laid out. Wait for settled native layout and verify the
actual frames and expected visible keys; otherwise a traversal can collapse into
one layout and falsely appear to recycle.

## Library and custom-manager gates

Google Flexbox is archived. FlexboxLayout is eager; FlexboxLayoutManager recycles
and has different feature support, including no wrap-reverse. Review a dependency/
fork or implement the scenario; never use an eager scroll wrapper as virtualization.

Material carousel masks/keylines items, requires native masking roots and derives
arrangement from first-item size. It supports horizontal/vertical strategies but
is not arbitrary variable-width wrap or perspective coverflow.

A custom LayoutManager owns recycler acquisition/scrap/recycle, constrained measure,
zero/change counts, bounded fill/clamped scroll, extent/anchor/restore, RTL/insets/
decorations, changes, prefetch, focus and accessibility. Only advertise predictive
animation support after implementing/testing pre-layout/disappearing transitions.

## Loading and performance

Reuse Paging/RemoteMediator principles with the app's managed repository; do not
require Kotlin Flow/Room/Compose without a verified integration need.
No SQL/HTTP/decode in count/bind/type/measure. Keep native work on UI dispatcher.

Use Release physical-device Perfetto/FrameTimeline evidence: RV bind/layout/
prefetch/full invalidation, texture uploads and JNI setter/callback costs.
Measure managed allocations and retained MAUI trees too.

## Requested interactions

[Drag/reorder](drag-drop-and-reordering.md) inside a recycler uses ItemTouchHelper;
external transfer uses View.StartDragAndDrop/ClipData instead. Translate current
holder positions to keys and handle NO_POSITION/ConcatAdapter offsets. A custom
manager must keep the dragged holder visible during edge moves.

[Swipe deletion](deletion-and-swipe-actions.md) needs explicit source mutation/
failure reset, not just OnChildDraw decoration. [Refresh](refresh-and-reconciliation.md)
uses SwipeRefreshLayout and correct child-up/nested-scroll detection.
[Context actions](context-menus-and-actions.md) distinguish floating context,
ActionMode selection and anchored overflow; arbitrate long-press against drag.
[Sources I11-I14](sources.md#i11)

**Proof:** actual manager/holders, span/chrome changes, compatible pools, stale
assets, source/disconnect and structural/animation transitions; frame/memory/load
budgets. Native Java sample timings are not .NET MAUI measured gains.
