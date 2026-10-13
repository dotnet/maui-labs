# Native macOS: AppKit

Not Mac Catalyst. [Sources A1-A2, U1, U4, U9, R1](sources.md)

## Native mechanism

Use modern data-source NSCollectionView inside NSScrollView. Reusable items are
NSCollectionViewItem controllers, not UIKit cells. For a native table-like list,
consider NSTableView; this is not a return to MAUI ListView.

Flow/custom layouts serve lists/grids/line wrapping. AppKit has native
NSCollectionViewCompositionalLayout since macOS 10.15, including section providers,
boundary supplementaries and orthogonal shelves. Apple's Mac sample proves those
mechanisms; a NIB/window-controller example is not a C# handler.

Arbitrary waterfall, justified rows and coverflow need scenario-specific geometry.
Preserve native collection realization rather than constructing every item.

## Binding differences

Check the consuming macOS pack:

- AppKit uses registration/MakeItem and native supplementary acquisition.
- Inspected C# diffable identifier generic types require NSObject.
- Native prefetch is NSCollectionViewPrefetching / INSCollectionViewPrefetching.
- UIKit list/cell registration/reconfigure/completion overloads are not assumed.
- Inspected NSCollectionViewLayoutAttributes has frame/size/alpha/z-index,
  **not UIKit Transform3D**.

Use stable cached identifier objects and keyed content revision lookup. Publish
one update authority and supported reload/rebind for unchanged IDs.
Retain active delegate/data/prefetch bridges in handler fields: native weak
properties alone do not protect managed callback lifetime.

## Renderer, geometry and resources

Reuse compatible native/MAUI child trees, rebind context/logical ownership and
reset all state. Use MacOSViewHandler and the backend conversion/dispatcher;
stock generic placement may not supply AppKit frames correctly.

MAUI owns outer placement; native collection owns item frames. Constrain child
measure/arrange, cache by width/font/revision, avoid all-item inflate/scroll scans,
and preserve keyed anchor after corrections.

Verify flipped document/collection coordinates, clip-view bounds/insets/extent
and MAUI child bounds together. Resize changes constraints; monitor backing scale
for pixel-sized thumbnail cache keys independently of logical sizes.

Translate prefetch index paths to keys under current epoch and request bounded
repository work. Prepare thumbnails with AppKit/CoreGraphics/ImageIO-compatible,
documented thread-safe APIs, not copied UIImage calls. Native view changes stay
on the owning dispatcher; validate key/generation/epoch before completion.

## Chrome and coverflow

Use native sections/supplementaries where their rules fit. Pinning/global chrome/
empty state/completeness need explicit policy; observe child groups or snapshot.

True coverflow needs suitable layer-backed views, bounded candidates, untransformed
geometry plus layer transforms, depth/hit-test order, snapping, reduced motion
and keyboard/VoiceOver. Do not invent a layout-attributes Transform3D property.
Archived iCarousel is historical mechanism evidence, not a shipping default.

## Repo baseline and proof

The inspected maui-labs handler is a hand-built NSScrollView virtualizer with
flat-item visibility scans, template pools and reload-on-root-change. It is not
an NSCollectionView wrapper. Inspect current source, and register a distinct
bespoke handler instead of replacing every CollectionView.

Use a real AppKit window/backend dispatcher; the repo's existing shared native
runtime host can be extended for product-level work. Verify item/controller/
delegate ownership, reuse, stale callbacks, selection, trackpad/keyboard,
VoiceOver, live resize/backing scale and Release native/managed traces.
Portable geometry tests do not prove native rendering or performance.

For [scrolling profiling](apple-scrolling-profiling.md), target the actual Mac
process/window/display and verify the instrument's coverage instead of inheriting
a UIKit/simulator result. Measure trackpad/wheel and live resize separately.
Correlate native main-thread/render work with geometry and item-controller/MAUI/
asset lifetime. Native allocation data isn't automatically managed GC data;
buffered frame lifetime isn't presentation cadence.

## Requested interactions

Native [drag/drop](drag-drop-and-reordering.md) uses modern collection index-path
delegates, pasteboard writers, ValidateDrop/AcceptDrop and registered types.
Validation can adjust the proposed path/on-before operation. It is not UIKit.
[Context actions](context-menus-and-actions.md) use NSMenu/MenuForEvent and
current command validation. [Refresh](refresh-and-reconciliation.md) needs an
explicit action/indicator or custom required gesture; no UIKit refresh control.
[Swipe reveal](deletion-and-swipe-actions.md) is an explicitly implemented path.

The inspected repo gesture bridge lacks drag/drop recognizer cases; RefreshView
is a spinner overlay, SwipeView a partial horizontal reveal, and ContextFlyout a
basic projection. Native toolkit availability is not existing backend parity.
[Sources I19-I20, I26](sources.md#i19)
