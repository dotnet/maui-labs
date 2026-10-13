# iOS and Mac Catalyst: UIKit collections

[Index](../README.md) | [Shared contracts](../shared-design.md) |
[Performance validation](../performance-validation.md)

## Native foundation and version boundary

Use `UICollectionView` as the scrolling/recycling owner, with reusable
`UICollectionViewCell` and `UICollectionReusableView` implementations. Choose a
layout independently from the source and item renderer.

| Mechanism | Native introduction / caution |
|---|---|
| Flow/custom collection layouts | Established UIKit APIs; suitable when minimum versions predate compositional layouts |
| Compositional layout and diffable source | iOS 13; Catalyst availability must match its SDK/deployment annotations |
| List sections, modern registrations, section snapshots | iOS 14 family |
| `reconfigureItems`, preparation/thumbnail APIs and improved prefetch behavior | iOS 15 family |
| Current Apple sample project | iOS/Catalyst 18 and Xcode 16 requirements; not the introduction date of all APIs it demonstrates |

Verify the corresponding C# binding and deployment annotations. A classic
`RegisterClassForCell`/dequeue implementation can still use the exact native
collection and reuse lifecycle if a newer registration API is unsuitable.
Do not introduce a Swift shim simply because the sample uses Swift.
[U1](../sources.md#u1) [U2](../sources.md#u2) [U3](../sources.md#u3)
[U4](../sources.md#u4)

MAUI 10 makes its optimized UIKit collection handlers the default. Inspect the
app's actual version and handler registration before diagnosing a performance
problem or recommending a legacy-handler opt-out. A new bespoke control should
not inherit internal `Items2` machinery as its long-term public contract.
[M2](../sources.md#m2) [M4](../sources.md#m4)

## Layout recipes

| Scenario | Recommended first native mechanism | Scenario-specific work |
|---|---|---|
| Fixed-height vertical/horizontal feed | Flow or compositional layout | Insets, predictable cell sizing, scroll-to-key and anchor policy |
| Native list experience | Compositional list section | List appearance, accessories/swipe behavior, selection and group chrome |
| Uniform/adaptive grid | Flow or compositional groups | Column count from effective viewport, spacing, resize invalidation |
| Mixed editorial sections | Compositional section provider | Per-section geometry; stable section IDs and supplementary kinds |
| Horizontal shelves in vertical feed | Orthogonal compositional sections | Shelf extent, snap policy, per-section state, lazy data/assets |
| Variable-width wrap | Flow/custom layout | Constrained item widths, line-state/measurement cache |
| Justified photos | Metadata-driven custom groups/layout | Row construction, crop/final-row policy and aspect-ratio cache |
| Pinterest masonry | Custom `UICollectionViewLayout` | Column assignment, metadata geometry, spatial/range lookup and suffix invalidation |
| Snapping / coverflow | Compositional visible-item effects or custom layout | Focal alignment, perspective, bounds, z-order, hit testing and snapping |

Compositional layout is an excellent native tool, not a reason to force every
algorithm into a group. A finite editorial mosaic is different from arbitrary
shortest-column placement over a continuously changing source. Custom groups can
encode precomputed geometry when they fit; otherwise a dedicated layout is
clearer. [U1](../sources.md#u1)

### Concrete Apple examples

The inspected modern-collections sample uses actual native APIs:

- `PinnedSectionHeaderFooterViewController` creates boundary supplementary views,
  estimates their heights, pins the header, assigns z-index, and registers header
  and footer providers separately.
- `OrthogonalScrollingViewController` composes leading/trailing groups and sets
  `orthogonalScrollingBehavior` instead of building a separate MAUI scroll view
  for every shelf.
- Its data sources use unique identifiers across sections and reusable
  registrations defined outside the provider.

These examples are a good basis for small native recipes. They do not implement
the app's paging, bounded caches, or C# handler lifecycle. [U4](../sources.md#u4)

### Waterfall example: useful but not infinitely scalable

CHTCollectionViewWaterfallLayout demonstrates shortest-column placement using
aspect-ratio metadata, per-section geometry, header/footer attributes, and
cached rectangles. It builds union rectangles for batches of 20 attributes to
reduce fine-grained intersection work.

Source inspection also shows full geometry preparation and linear scans across
union rectangles. Therefore:

- take the reuse/geometry separation and metadata sizing;
- measure initial/rebuild costs on the intended loaded window;
- consider per-column ordered intervals or indexed blocks for deeper datasets;
- invalidate only the affected geometry suffix where correctness permits;
- do not advertise the example's union cache as an O(log N) lookup.

That analysis comes from the implementation, not a benchmark of the library.
[U6](../sources.md#u6)

## Data source and update rules

**Recommended default:** keyed immutable presentation state plus a native diffable
source for moderate loaded datasets. Use an indexed/windowed classic source where
full snapshots are an unsuitable memory/update cost.

Store stable item/section identifiers in snapshots, not whole mutable models
whose equality changes when a caption or image changes. Keep an independent,
cheap key-to-presentation lookup. A key may require an `NSObject` bridge in C#;
verify generated generic constraints and cache native identifiers rather than
recreating wrappers in hot callbacks.

Apply one ordered native update stream. Pure diff/input preparation can be moved
off the UI thread, but MAUI source publication and view work remain on the UI
dispatcher. Apple supports some background snapshot-apply patterns; using that
optimization requires following its consistent-queue contract and the actual
binding, not alternating queues opportunistically.

For content under an unchanged identifier, use supported item reconfiguration
or reload. `reconfigureItems` preserves the existing cell and reruns configuration;
it is not equivalent to disposing/recreating the cell. Bind code must reset all
current state even when no reuse callback occurs. Before iOS 15, nonanimated
snapshot application could internally reload; that old behavior is not a blanket
description of current UIKit. [U3](../sources.md#u3)

Diffable sources prevent many manual count/index inconsistencies; they do not
make snapshots or huge animated transactions free. Bound the loaded window and
choose reset versus incremental update deliberately for total query replacement.

## Reuse, self-sizing and the MAUI bridge

Create cell registrations **once per cell type**, outside the cell provider.
UIKit maintains reuse queues per registration instance; creating a registration
per requested cell defeats the expected reuse. [U3](../sources.md#u3)

For each reusable cell:

1. Create the native child hierarchy, or compatible MAUI template tree, once.
2. Bind current item key/revision and reset content, selection/accessories,
   transforms, image placeholder and accessibility fields.
3. Measure under the actual cross-axis constraint.
4. Arrange the child tree inside the native content bounds.
5. Release item-specific subscriptions and demand on rebind/recycle; destroy
   owned resources only on replacement/eviction/handler teardown.

For a MAUI template, cache compatibility by resolved template/renderer identity.
Do not assume a `DataTemplateSelector` always returns the same tree. Bind its
`BindingContext` correctly, maintain logical child ownership, and avoid creating
another handler whenever the same template receives a new record.

The MAUI 10.0.41 `TemplatedCell2` implementation shows constrained measurement,
constraint caching, template reuse and measure/arrange coordination. Its
supplementary sizing is distinct from first-item sizing.
[M4](../sources.md#m4)

For variable text heights, use estimated self-sizing only with constraints that
can resolve a size. Cache by width/font scale/content revision. Do not use the
first card's height for all cards unless the contract actually guarantees it.
Avoid dequeueing collection-managed cells as offscreen measuring prototypes;
metadata or a deliberately separate measuring object is safer.

## Prefetch and images: the important lifecycle details

Apple's performance talk distinguishes **preparation** from **display**:

- a prepared cell may never become visible;
- a displayed cell can leave and reenter visibility without immediate recycling;
- `didEndDisplaying` is not guaranteed to mean "this item is now in the reuse pool";
- heavy preparation must not be deferred to `willDisplay`.

Use visibility for video/playback behavior, binding for identity/subscriptions,
and repository demand for shared assets. Keep those lifetimes separate.
[U3](../sources.md#u3)

Data-source prefetch should request metadata/assets through a deduplicating,
cancellable scheduler. Index paths are hints: translate them to keys immediately
under the current source epoch. Do not launch one unlimited HTTP task per callback.

After network/file retrieval, decode/prepare thumbnails at their destination
pixel size away from UI commits using documented image APIs. Publish only a ready
asset and reconfigure the current keyed item. Capturing a cell and assigning the
result later can put the wrong photo in a reused cell.

Prepared images hold decoded pixel data. Cache original/compressed bytes on disk
and use a byte-budgeted memory cache for display-ready images, with key including
asset revision, output pixel size and crop. Background decoding is not a reason
to retain every decoded asset indefinitely. [U3](../sources.md#u3)

## Headers, footers, grouping and nesting

Prefer native sections plus supplementary views for genuine grouped collections.
Give section identity a stable lifetime; do not derive it solely from the group's
current ordinal. Empty groups, moves between sections, collapsed outlines and
mutating group children need explicit source semantics.

Global chrome can use a dedicated section/row or a layout-wide supplementary
mechanism where available; overlay chrome is a separate choice. Specify whether
it participates in scroll extent and selection. Pinning needs collision behavior
at the next section, not just elevated z-index.

Prefer orthogonal sections over nested collection controls when their geometry
and gesture model fit. If nesting is required, fix the shelf's cross-axis extent,
load inner records lazily, and retain offsets by section key before outer reuse.
[U1](../sources.md#u1) [U4](../sources.md#u4)

## Coverflow is a presentation contract

`visibleItemsInvalidationHandler` provides a bounded hook for visible-item effects.
Use it for appropriate scale/alpha/transform effects; use a custom layout when
perspective, overlap, target offset or bounds lookup need stronger control.
[U5](../sources.md#u5)

For a custom layout, distinguish geometry invalidation from offset-only transform
updates. Copy mutable attributes before modifying cached instances, include items
whose transformed bounds overlap the viewport, and define drawing/hit-test order.
Do not rebuild all N item rectangles on every content-offset change.

Historical iCarousel provides real perspective/3D coverflow plus bounded view
loading, but is archived. It is a mechanism reference, not a new dependency
recommendation. A modern UICollectionView-backed recipe should preserve native
scroll/focus/accessibility rather than inherit its old assumptions.
[U9](../sources.md#u9)

## Two instructive alternative architectures

**IGListKit:** stable diff identifiers and specialized section controllers support
composable feeds. Its FAQ recommends a deliberate reload for very large initial
inserts and acknowledges limitations around estimated Auto Layout sizing. Reuse
occurs in native cells, not by sharing section-controller instances across records.
Take the boundaries, not an assumed universal binding dependency.
[U7](../sources.md#u7)

**Texture:** thread-safe nodes move layout/drawing work off the main thread.
The documented `ASCollectionNode` architecture does not use cell reuse, and mixing
ordinary UIKit cells does not confer node-specific asynchronous benefits. It
demonstrates that performance can come from another ownership/rendering model,
but does not justify constructing MAUI views in `Task.Run`.
[U8](../sources.md#u8)

## Mac Catalyst-specific requirements

Catalyst is UIKit, not the repo's native AppKit backend. Share native UIKit code
with `#if IOS || MACCATALYST`, but evaluate:

- larger/resizable viewports and more simultaneous items;
- mouse, trackpad, wheel, keyboard focus, contextual actions and multi-selection;
- platform-appropriate safe areas/insets and window backing scale;
- accessibility and text scaling in desktop window sizes;
- cancellation/reconnect after window or source replacement.

Do not import an `NSCollectionView` recipe into a Catalyst handler merely because
the application runs on macOS. AppKit bindings explicitly mark many collection
types unavailable to Catalyst. [A4](../sources.md#a4)

## Required proof for a generated UIKit control

Compile both requested targets, verify the actual native collection/layout types,
and exercise reuse with delayed content, section changes and resize. Then run
Release device traces for scrolling, prepend/refresh and image-heavy updates.
Use Instruments native frame/hitch evidence together with managed allocations;
screenshots and successful compilation do not establish scrolling performance.
