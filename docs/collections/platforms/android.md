# Android: RecyclerView-based bespoke controls

[Index](../README.md) | [Shared contracts](../shared-design.md) |
[Performance validation](../performance-validation.md)

## Native foundation

`RecyclerView` separates reusable view holders, adapters and layout managers.
That is already a strong fit for narrowly tailored MAUI controls.
Use `AndroidX.RecyclerView.Widget` from the consuming app's resolved bindings;
verify any extra Material, Flexbox or Paging dependency before adding it.
[B1](../sources.md#b1) [M4](../sources.md#m4)

| Role | Responsibilities |
|---|---|
| RecyclerView | Scrolling, recycling, native input, drawing and update coordination |
| Adapter | Cheap indexed access, view type, stable identity if enabled, create/bind/recycle |
| ViewHolder | Compatible native/MAUI child tree, cached child references and current binding identity |
| LayoutManager | Measure/layout, realization, scroll extent, offsets, prefetch and accessibility geometry |
| Repository | Pages, errors, request deduplication, database/HTTP work and bounded caches |

Adapters should not query SQL, perform HTTP, decode full-size images, or enumerate
the entire source in `OnBindViewHolder` or `GetItemViewType`.

## Layout recipes

| Scenario | First choice | Important constraints |
|---|---|---|
| Vertical/horizontal list | `LinearLayoutManager` | Known/fitted item extent, keyed updates, scroll-to-key |
| Uniform or span grid | `GridLayoutManager` | Span lookup must understand row kinds and composed-adapter positions |
| Pinterest-like staggered cards | `StaggeredGridLayoutManager` | Gap behavior, full-span chrome and per-span visibility; size metadata is still important |
| Stable/deterministic masonry | Specialized custom manager | Own column assignment and invalidation; stricter than default staggered behavior |
| Header/content/footer list | Typed adapter or `ConcatAdapter` | Stable-ID mode, view-type compatibility and index translation |
| Vertical feed with horizontal shelves | Nested recyclers | Finite shelf height, compatible shared pools, modest initial prefetch |
| Variable-width wrap | Specialized manager or consciously reviewed Flexbox path | No automatic maintained built-in general flexbox manager |
| Material carousel | `CarouselLayoutManager` + native masking roots | Uniform sizing assumptions and Material style, not 3D coverflow |
| 3D coverflow | Custom/extended manager plus view transforms | Snapping, bounded candidates, draw order, camera distance, hit testing |

### Grid and staggered are different

GridLayoutManager produces row/column-aligned geometry; a vertical row's cells
share row advancement. StaggeredGridLayoutManager supports unequal main-axis
extents and gaps. It is not a generic variable-span editorial grid.
[B1](../sources.md#b1)

In grids, headers/footers typically need a full span from `SpanSizeLookup`.
In staggered layouts, use the layout's full-span item mechanism. Clear stale
layout parameters when a reused holder changes row kind.

Staggered visibility is per span. Reduce the valid returned positions according
to the question being asked (first/last visible logical data), handle `NO_POSITION`,
and exclude chrome from data-loading thresholds. Gaps can trigger reassignment/
layout work; test image/height changes and reverse scrolling rather than disabling
gap correction as an unexplained performance fix.
[B8](../sources.md#b8)

## Official adapter example and a copying trap

The Flowers sample demonstrates:

- one-time native inflation and child lookup;
- a holder-local current item for click behavior;
- `ListAdapter` publication;
- `ConcatAdapter` joining a header and content adapter.

Its inspected `FlowerDiffCallback`, however, uses full object equality for
`areItemsTheSame` and only ID equality for `areContentsTheSame`. For a record whose
caption/image can change under the same ID, that is not the desired comparison.
Use **stable key equality for identity**, and **all relevant displayed fields or
content revision for contents**. An official sample is useful prior art, not a
substitute for checking the contracts. [B5](../sources.md#b5)
[B3](../sources.md#b3)

## Stable IDs, diffs and payloads

`DiffUtil`, `ListAdapter` and `AsyncListDiffer` require unchanged list/element
comparison inputs while diffing. Publish new immutable presentation objects or
immutable comparison snapshots; avoid passing one mutable list instance back
after modifying it. Compute heavy differences off-thread and dispatch accepted
results/notifications on the native UI thread.

The documented expected diff cost is O(N + D^2), with additional move detection
cost. A small edit in a large list is not cost-free, and a large random reshuffle
can be expensive. Disable move detection only when the ordering contract really
precludes moves; use a deliberate reset for appropriate wholesale replacement.
[B3](../sources.md#b3)

Stable IDs and diff identity are separate features. If enabling stable IDs:

- return IDs tied to domain/presentation identity, not current position;
- ensure uniqueness across headers, footers and repeated occurrences;
- use a collision-safe key-to-`long` mapping rather than unchecked hashes;
- configure `ConcatAdapter` stable-ID policy intentionally.

Do not copy MAUI's position-returning `GetItemId` into a new adapter and then
enable stable IDs expecting durable identity. [M4](../sources.md#m4)

Use payloads to update a changed badge/caption/image without rebinding unchanged
fields. A full bind remains mandatory when payloads are absent/dropped or a holder
is newly bound. A size-changing payload must invalidate appropriate measurement;
an image arriving with a reserved aspect ratio should not need global layout.

Avoid `NotifyDataSetChanged`, adapter replacement and repeated full invalidation
for small edits. Android's performance guide specifically calls out
`RV FullInvalidate` traces. [B2](../sources.md#b2)

## The important reuse rules

Create the child hierarchy once per compatible holder. Cache child references,
bind current content, and clear state that might leak from the prior item:
placeholder, selection/activation, expansion/editing, visibility, transform,
layout parameters, accessibility text and animation state.

Keep a current item key or resolve `BindingAdapterPosition` when an action occurs.
The position captured by a bind can become stale after insertion or an adapter
composition update. A detached/removed holder may have `NO_POSITION`.
[B4](../sources.md#b4)

`OnViewDetachedFromWindow` is not permanent destruction. Distinguish
visibility/playback from recycling and subscription ownership. Release appropriate
item-specific resources in `OnViewRecycled`, and always validate key/generation/
source epoch on delayed completions.

Do not solve reuse bugs by permanently making every holder nonrecyclable.
For transient animations, reset/cancel the work that prevents recycling and obey
the native lifecycle contract.

For MAUI templates, select the template/view type before construction, reuse a
compatible child tree and handler, rebind context, and disconnect only when it is
actually replaced/evicted. Use public handler bridges rather than inheriting
MAUI's internal item-holder classes. [M4](../sources.md#m4)

## `HasFixedSize` is about the viewport

`SetHasFixedSize(true)` means adapter changes cannot affect the **RecyclerView's
own size**. It does not mean every row has the same height. A finite, independently
sized viewport containing variable-height masonry cards may qualify; a recycler
whose height wraps adapter content may not.

Set the flag only after checking the parent/measure contract. It is not a shortcut
around missing measurement correctness. [B11](../sources.md#b11)

## Grouping, chrome and nested shelves

`ConcatAdapter` is useful for composed headers, content, load-state footers, and
independently owned sections. Its default view-type isolation prevents incompatible
holders from colliding. Sharing holder types requires a deliberately compatible
global type namespace. This matters especially with a shared `RecycledViewPool`.
[B4](../sources.md#b4)

Full-span section chrome still requires appropriate layout-manager configuration.
Sticky headers can be rendered by a specialized decoration/overlay or a custom
manager, but must have explicit interaction/accessibility and next-section
collision behavior. Drawing a header in an `ItemDecoration` does not automatically
make it an accessible, actionable native element.

For shelves, Android's official rendering guidance recommends:

- a shared `RecycledViewPool` among inner recyclers with compatible item types;
- `InitialPrefetchItemCount` on the inner linear manager, based on roughly the
  initially visible items, not the entire shelf;
- normal outer virtualization and finite inner viewport dimensions.

This schedules native inflation/binding while time is available; it does not make
heavy binds run on a background UI thread. [B2](../sources.md#b2)
[B7](../sources.md#b7)

Preserve inner scroll state by section key. Reset reused outer rows before binding
another shelf. Do not disable nested scrolling globally as a generic fix: verify
gesture/wheel behavior and measurement for the actual nesting.

## General wrap: dependency status matters

Google Flexbox has both an eager `FlexboxLayout` and a recycling
`FlexboxLayoutManager`. Only the latter is the relevant mechanism for large
scrolling data. It lacks some features of the eager layout, including wrap-reverse.

The repository is **archived** at the research date. Therefore the skill should
not casually prescribe it as the maintained built-in Android wrap solution.
Options are a deliberately reviewed compatible library/fork or a scenario-specific
manager, with line/checkpoint geometry and correct recycling.
[B9](../sources.md#b9)

Wrapping eager `FlexboxLayout` in a scroller does not provide virtualization.

## Material carousel versus coverflow

The inspected native Material carousel:

- uses a `RecyclerView.LayoutManager`;
- requires `MaskableFrameLayout` roots;
- supports horizontal and vertical orientations and multiple strategies;
- sizes its arrangement from the first item;
- uses keylines/masking and bounded fill bookkeeping;
- has its own snap helper and resize-listener lifecycle.

Choose it for that Material experience. It is not arbitrary variable-width
wrapping or perspective-based 3D coverflow. [B10](../sources.md#b10)

For true coverflow, retain recycler virtualization and apply transforms only to
the bounded child/candidate range. Define perspective/camera distance, rotation,
scale, translation, overlap, draw order and hit testing. Reset them during bind;
use visual/draw properties rather than continuously changing layout constraints.
Test native keyboard/accessibility order separately from visual depth order.

## A custom LayoutManager is a serious native implementation

Its scope includes:

| Surface | Required behavior |
|---|---|
| `OnLayoutChildren` | Zero/changed counts, available space, anchor, recycler acquisition, constrained measurement and arrangement |
| Scrolling | Clamp consumed delta, shift existing children, recycle departed range, fill arriving range |
| Geometry | Insets/decorations, RTL, orientation, extent/range estimates and resize |
| Changes | Add/remove/move/content invalidation and state restoration |
| Prefetch | Adjacent/initial positions with sensible distance and bounded work |
| Interaction | Focus search, scroll-to-key, smooth-scroll vector, accessibility row/column information |
| Animations | Pre-layout/disappearing items if predictive animations are actually supported |

Use recycler acquisition/scrap/recycle APIs, not manual allocation of all views.
Do not report predictive-animation support until those state transitions are
implemented and tested. Starting without that optional behavior is preferable
to falsely advertising it.
[B7](../sources.md#b7)

## Paging, Room and managed applications

Paging 3 provides architectural lessons: bounded demand, deduplication, retry,
load states, refresh generations, remote keys and a database source of truth.
`RemoteMediator` writes fetched rows/keys to the database; a database paging source
publishes updated data, avoiding two competing append paths.
[B6](../sources.md#b6)

Do not translate Kotlin samples literally into unverified C# APIs, or require
Room/Flow/Compose in a RecyclerView-backed .NET MAUI control. Use the app's existing
managed repository and cancellation model, unless a binding/integration spike
justifies a native paging dependency. Keep loading out of holder/layout callbacks.

## Performance lessons and required proof

Use Release builds and Android-native traces. Look for full invalidation,
`RV OnBindView`, `RV Prefetch`, layout duration, texture uploads, GC and repeated
inflation. Use Perfetto FrameTimeline where available; a fast managed method alone
does not prove a presented frame met its deadline.
[B2](../sources.md#b2)

The MAUI performance work also found expensive Java string/array marshaling and
repeated small C#-to-Java setter calls during scrolling. Cache values and avoid
redundant assignments/crossings before considering a native shim. Historical
publisher measurements explain why this matters, not the expected gain in a new
control. [M5](../sources.md#m5)

Verify the actual manager and holder types, delayed image reuse, group/full-span
state, reverse scrolling, source replacement, detach/reconnect and animation
transitions. Measure low-end physical-device scroll traces with both cold and
warm image caches; emulator-only smoothness is insufficient.
