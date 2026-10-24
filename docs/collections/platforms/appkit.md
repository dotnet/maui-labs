# Native macOS: AppKit collections

[Index](../README.md) | [Shared contracts](../shared-design.md) |
[Performance validation](../performance-validation.md)

This chapter targets the repo's `Microsoft.Maui.Platforms.MacOS` backend, **not
Mac Catalyst**. Catalyst uses UIKit and the separate [UIKit recipe](uikit.md).

## Native foundation

Use the modern, data-source-driven `NSCollectionView` inside an `NSScrollView`.
Reusable items are `NSCollectionViewItem` **view controllers**, not UIKit cells.
Supplementary/decoration views and `NSCollectionViewLayout` supply native sections
and independent layout.

For a conventional row/table experience, consider `NSTableView` rather than forcing
a collection layout to recreate a table's interaction model. The recommendation
is about a native AppKit host, not reviving the obsolete MAUI `ListView`.
[A1](../sources.md#a1)

Avoid the historical content-array/prototype architecture when building a large,
modern data-driven collection. Apple recommends data-source-based sections.

## Layout choices and real AppKit examples

| Scenario | Native starting point |
|---|---|
| Vertical/horizontal list or line-based wrap | `NSCollectionViewFlowLayout` |
| Uniform/adaptive grid | Flow or `NSCollectionViewCompositionalLayout` |
| Mixed editorial sections | Compositional section provider |
| Vertical feed with horizontal shelves | Native compositional orthogonal sections |
| Section headers/footers | Registered supplementary views and layout boundary items |
| Arbitrary waterfall/masonry | Specialized `NSCollectionViewLayout` with metadata geometry |
| Justified rows | Specialized grouping/geometry that matches the row policy |
| Coverflow | Custom layout plus native layer transforms and explicit input/hit testing |

AppKit compositional layout is not missing: Apple introduced
`NSCollectionViewCompositionalLayout` in **macOS 10.15**, alongside UIKit's family.
The repo backend's documented macOS 14 minimum is above that native introduction,
although binding/SDK compatibility still needs verification.
[A2](../sources.md#a2) [U1](../sources.md#u1)

The inspected Apple sample has actual Mac equivalents:

- `OrthogonalScrollingWindowController` uses
  `NSCollectionViewCompositionalLayout`, nested layout groups,
  `.continuous` orthogonal sections and `NSCollectionViewDiffableDataSource`.
- `SectionHeadersFootersWindowController` registers `NSView` supplementaries,
  creates estimated boundary header/footer sizes, and provides them through
  the native diffable source.

This establishes that AppKit can support sophisticated native shelves and group
chrome without a MAUI scroller per item. The sample's window-controller/NIB setup
is not a C# handler implementation. [U4](../sources.md#u4)

## Do not copy UIKit API names blindly

Shared layout concepts do not mean identical generated APIs:

| UIKit concept | AppKit distinction |
|---|---|
| `UICollectionViewCell` | `NSCollectionViewItem`, with native view-controller ownership |
| Cell/supplementary registrations | AppKit registration and `MakeItem`/supplementary acquisition |
| Diffable section/item Swift `Hashable` values | Inspected C# AppKit source constrains identifier types to `NSObject` |
| UICollectionView list appearance/configuration | Not the same AppKit collection API; consider native table/list policy |
| `UICollectionViewLayoutAttributes.Transform3D` | Not present on the inspected AppKit attributes binding |
| UIKit prefetch protocol | AppKit has its own `NSCollectionViewPrefetching` |

The inspected AppKit layout-attributes binding exposes frame, size, alpha, hidden
and z-index. A 3D effect needs a separate native view/layer strategy, not an
invented `NSCollectionViewLayoutAttributes.Transform3D` setter.
[A4](../sources.md#a4)

Do not assume UIKit's `reconfigureItems`, modern cell registration or snapshot
completion overload has a matching AppKit binding. Check the actual installed
pack and use supported reload/rebind behavior for unchanged IDs.

## Data source and updates

**Recommended default:** indexed keyed presentation state with a native diffable
source for moderate loaded collections, or a classic indexed source when
windowed data/update costs require it.

Maintain section/item identity independently from temporary `NSIndexPath`.
Translate managed keys into stable cached native identifiers; keep content
revision/lookup outside the snapshot. Reconfigure/reload changed content under
unchanged keys deliberately, and preserve selection/focus/anchors by key.

Retain data-source/delegate/prefetch objects in handler-owned fields. Native weak
delegate properties are not sufficient managed ownership; premature collection
of a bridge object can silently remove callbacks.

Publish view changes on the actual window/AppKit dispatcher. Prepare pure
immutable data/geometry off-thread if appropriate, without creating/measuring
`NSView` or MAUI views there. Backend navigation/UI callbacks must also dispatch
correctly. [R4](../sources.md#r4)

## Item reuse and MAUI child hosting

Use native reusable item/supplementary acquisition. Construct compatible content
once, bind the current key/revision, and clear all old appearance/resource state.
Do not store durable item state in an item controller the native collection can
recycle. [A1](../sources.md#a1)

For a retained MAUI template:

- resolve compatibility by template/renderer, not current position;
- reuse its child handlers and update binding context;
- measure at the actual cell width/height constraint;
- arrange inside the native item's view;
- maintain logical ownership and inherited resources/semantics;
- disconnect/dispose only when its retained tree is replaced or permanently
  evicted, and drain all pools on handler teardown.

Define the boundary between MAUI's outer placement and AppKit's item geometry.
Avoid two independent full layout passes that fight over the item frames.
Use the repo's public `MacOSViewHandler` and platform conversion helpers rather
than a stock handler that assumes UIKit or returns an unusable native desired size.
[R4](../sources.md#r4)

## Measurement and scrolling behavior

Photo metadata and bounded card chrome are preferable to inflating every item for
a complete layout. Variable text requires a width/font/content-sensitive size
cache, estimates, precise correction and keyed anchor preservation.

For a custom waterfall, cache lightweight metadata and column/interval indexes.
Recalculate affected suffixes when earlier height/placement changes; do not
linear-scan all flattened rows at every scroll notification.

AppKit coordinate conventions matter. Verify the collection/document's flipped
coordinates, clip-view visible bounds, content insets, native scroll extent and
MAUI item bounds together. An offset measured in document coordinates is not
automatically a MAUI frame coordinate.

Window resize invalidates cross-axis geometry; moving across displays can change
backing scale without changing logical sizes. Re-key pixel-sized thumbnails
separately from logical layout constraints. Include live-resize cost in traces.

## Prefetch and images

AppKit provides `NSCollectionViewPrefetching` and a native `prefetchDataSource`.
The inspected macios binding exposes `INSCollectionViewPrefetching`, prefetch/
cancellation callbacks, and the weak property. Verify their availability in the
consuming SDK/deployment target before generating code.
[A3](../sources.md#a3) [A4](../sources.md#a4)

Translate callback index paths into keys under the current source epoch, then
request data/assets from the bounded scheduler. Cancellation releases that
consumer's demand; it must not invalidate an asset still needed elsewhere.

Use appropriately sized thumbnails and a byte-budgeted decoded-image cache.
Choose AppKit/CoreGraphics/ImageIO-compatible preparation, with documented thread
safety, instead of copying `UIImage`-specific APIs. Assign native view state only
on the UI thread, and verify key/bind generation/epoch on completion.

Native prefetch improves time available; it does not make synchronous I/O or
unbounded decoding in a cell provider acceptable.

## Groups, sticky chrome and coverflow

Use native section/supplementary machinery when it matches the requested design.
Global chrome, section chrome and pinned overlays remain separate policies.
Paging across groups must not duplicate headers; child group mutations need
observation or new section snapshots.

For a native custom coverflow:

- maintain a bounded logical candidate range and accurate extent;
- separate untransformed geometry from layer transforms;
- use suitable layer-backed native item views;
- define depth/drawing order and transformed hit-test behavior;
- preserve keyboard/VoiceOver order and a reduced-motion alternative;
- test `ScrollTo`, resize and focus outside the current realized range.

Historical iCarousel demonstrates AppKit native views, pools and perspective, but
is archived and predates current desktop/accessibility assumptions. It is not a
maintained recommendation to bypass `NSCollectionView`.
[U9](../sources.md#u9)

## What the current repo handler does instead

The baseline `CollectionViewHandler` is a custom `NSScrollView` virtualizer:

| Observed source behavior | Implication |
|---|---|
| Flat `ItemInfo` list with estimated sizes | All loaded row metadata is materialized |
| Scan every flattened item to choose visibility | View virtualization does not imply sublinear scroll lookup |
| Template-keyed MAUI reuse pool, capped per template | Reuse exists, but total template/type count and retained resources still matter |
| Root collection event calls reload | Small mutations clear/rebuild flattened state rather than a native keyed diff |
| Width/constraint changes invalidate measurements | Useful prior art for resize, but not a native collection layout |

Do not state that the repo already wraps `NSCollectionView`. A bespoke
`NSCollectionView` handler is a new path; do not silently replace the existing
handler as part of generating an app-specific control. [R1](../sources.md#r1)

## Required proof on macOS

Use a real AppKit process/window with the backend's registration/dispatcher, not
only portable geometry tests. The repo already has a shared runtime test host
and a `collection-view-grid` scenario for composed-card resize/measurement.
Extend the appropriate harness when implementing a product-level control.
[R5](../sources.md#r5)

Check native item/controller ownership, supplementary reuse, disposal, source
replacement and stale callbacks. Verify keyboard/trackpad/VoiceOver, live resize,
backing-scale changes and anchor retention. Then use native Instruments traces
plus managed allocation evidence; no AppKit performance measurement was performed
for this research.
