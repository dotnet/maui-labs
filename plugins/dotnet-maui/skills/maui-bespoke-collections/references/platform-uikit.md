# UIKit: iOS and Mac Catalyst

Native/source guidance. The maui-labs metadata photo-masonry slice has iOS
Release simulator behavior proof; other recipes and Catalyst remain unverified.
No physical-device performance certification is claimed.
[Sources U1-U8, M2-M4](sources.md)

## Host and layout

Use UICollectionView with reusable cells/supplementaries. Choose independently:

| Scenario | Native starting point |
|---|---|
| Linear/list/grid/line-based wrap | Flow or compositional layout; native list sections when appropriate |
| Adaptive editorial sections | Compositional section provider |
| Shelves | Compositional orthogonal sections |
| Arbitrary shortest-column masonry | Custom UICollectionViewLayout with metadata/range indexes |
| Justified photos | Specialized row/group geometry |
| Coverflow | Bounded visible-item effects/custom attributes, transforms and snapping |

Compositional layout is not a reason to force every unbounded algorithm into one
group. Use custom layout when arbitrary placement/update constraints warrant it.

## Version/binding proof

Compositional/diffable families arrived with iOS 13; lists/registrations/section
snapshots with 14; item reconfiguration/image preparation improvements with 15.
Verify each Catalyst annotation and installed C# API. The updated Apple sample's
iOS/Catalyst 18 project minimum is not all APIs' introduction date.

MAUI 10 optimized UIKit handlers are the default. Inspect actual registration/
version; do not opt out to old handlers or inherit internal Items2 cells by default.
Classic registration/dequeue can retain the exact native control when a newer
binding is unavailable. Swift sample syntax is not a reason for a shim.

## Source and reuse

Create registrations once per cell type, outside the provider: registration
instance lifetime affects UIKit's reuse queues.

Keep unique stable IDs in snapshots and cheap key-to-presentation lookup outside
them. Verify identifier-object generic constraints in the installed binding.
Publish one accepted ordered update authority; use supported reconfigure/reload
for changed content under an unchanged ID.

ReconfigureItems preserves the cell and may not invoke PrepareForReuse. Bind must
reset all state. Do not capture a cell to apply a late image; update keyed data or
validate key/bind generation/epoch.

UIKit preparation is not display. A prepared cell can never display; an item can
leave/reenter display without immediate recycling. Keep playback/visibility
separate from bind/subscription ownership.

Retain compatible MAUI content/handlers, rebind context/logical ownership and
cache constrained measurements. Do not instantiate all templates or dequeue a
collection-managed cell as a measuring prototype.

## Geometry, chrome and effects

Constrain the cross axis; use metadata/fixed extents or estimated self-sizing with
width/font/content-sensitive cache. First-item sizing only fits uniform content.
Native section header/footer providers and boundary supplementary views preserve
group semantics; pinning and global versus section chrome remain distinct.

Apple's inspected sample proves native pinned headers/footers and orthogonal
sections. It does not supply application loading/teardown.
Waterfall reference CHT caches attributes/union rectangles but still scans union
blocks linearly and rebuilds all geometry: harden lookup/reflow for large windows.

For coverflow, separate geometry invalidation from offset-only transforms. Copy
mutable attributes, include transformed visible candidates, and define z-order,
snap, hit testing and reduced motion. Do not rebuild all N rectangles per frame.

## Images and Catalyst

Prefetch translates transient index paths to current keys and feeds bounded
repository demand. Use documented background thumbnail/preparation APIs, then
publish ready images. Prepared pixel data consumes memory; cache by bytes/size/
revision/crop, not an unlimited item count.

**Verified C# binding trap:** with Apple SDK 26.5.10318, the synchronous
`GetImageByPreparingForDisplay()` binding enforces UI-thread access even when
native guidance discusses background preparation. Initiate
`PrepareForDisplayAsync()` on the owning dispatcher and keep the bounded
preparation slot until its native completion, not only until creating the raw
UIImage. Publish with key/generation/epoch/ownership guards.

Preparation/reuse and visibility are separate lifetimes. In the mobile prototype,
image demand begins at `WillDisplayCell`, is released at `CellDisplayingEnded`,
and resumes when a still-bound cell reenters display. A bounded strong set owns
displayed cells until release; diagnostic weak references must not be the sole
ownership mechanism for decoded resources. Disconnect drains visible ownership
as well as callbacks. [Local proof R4](sources.md#r4)

Serialize source batches and prioritize a newer user/programmatic scroll over a
batch's captured anchor. Native frames can look correct while an earlier batch
completion is still queued; wait for settled updates as well as frames/keys.

Share UIKit with IOS || MACCATALYST. Catalyst is not AppKit: test resizable windows,
trackpad/wheel/keyboard, contextual actions, selection, accessibility and density.
Use the owning UI dispatcher; no background MAUI/native view measurement.

## Requested interactions

Use [drag/reorder](drag-drop-and-reordering.md) for interactive movement,
collection drag/drop delegates and diffable ReorderingHandlers. DidReorder and
placeholder CommitInsertion may already own native updates; adopt backing state,
not a duplicate move/insert.

[Swipe deletion](deletion-and-swipe-actions.md) through
UICollectionLayoutListConfiguration is a list-section mechanism (14+), not
UITableView swipe delegates for every custom masonry. Full-swipe activation is
explicit policy. [Refresh](refresh-and-reconciliation.md) uses UIRefreshControl;
[context actions](context-menus-and-actions.md) use native providers, with
multi-item/empty-space routing since 16. Verify Catalyst input and C# bindings.
[Sources I6-I10](sources.md#i6)

**Proof:** both requested builds; actual collection/layout types; delayed-image
reuse; supplementary/group changes; source/disconnect/resize; Release native
frame/hitch and managed memory evidence. Portable tests/screenshots are not enough.
