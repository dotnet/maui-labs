# Recipe: photo masonry

Use for unequal-height columns, not a row-aligned grid.
Read [geometry](geometry-and-anchoring.md), [source](source-and-update-contracts.md)
and only requested platform references. This is a generation recipe, not a shipped control.

## Contract

Name a PhotoMasonryView with the actual app data/source. Specify vertical/horizontal
axis, responsive column width/count, gaps/insets, shortest versus stable-column
placement, card chrome/caption limits, and supported edits.

Require photo pixel dimensions/thumbnail revision in metadata before downloading
the image. Choose typed native photo/caption cells or retained typed MAUI templates.
Loading/global/section chrome and selection are included only when requested.

## Geometry and updates

1. Derive usable column width from finite cross-axis viewport and gaps.
2. Derive image height from aspect ratio; add known chrome/constrained caption size.
3. Place by the explicit column rule with deterministic tie breaking.
4. Cache metadata/geometry, and use per-column intervals/indexed blocks for viewport lookup.
5. Advance all columns around full-span chrome.
6. On width/font/content changes, remeasure affected content/reflow the needed suffix.
7. Preserve keyed anchor during corrections/append/refresh.

For small C, scanning column heights is simple; a heap can help larger C.
An early height/removal can change the whole shortest-column suffix; do not promise
constant reflow. Stable assigned columns have different visual/edit semantics.

Do not inflate all cells to find extent. Bound loaded records/decoded thumbnails
separately from lightweight geometry. If deep seeks lack prefix metadata, disclose
the computation/approximation policy.

## Native starting points

| Target | Path |
|---|---|
| UIKit | Custom UICollectionViewLayout or bounded metadata-driven groups |
| Android | Staggered manager if gap policy fits; custom manager for stricter placement |
| WinUI | Custom VirtualizingLayout, not UniformGridLayout/LinedFlowLayout |
| AppKit | Custom NSCollectionViewLayout |
| GTK4 | Custom virtualizing native widget; GridView/FlowBox is not masonry |
| WPF | Custom virtualizing panel; ordinary/virtualized row-wrap is not masonry |

Use existing native geometry/data separation as prior art; source examples CHT/
WinUI Gallery still have linear lookup/broad invalidation to harden.
[Sources U6, W6, B7](sources.md)

## Data and evidence

Use the existing HTTP cursor or projected DB repository, bounded demand and byte
caches. Reserve image size before arrival, update current keyed content, and reject
late completions by binding generation/epoch. Do not globally re-layout an already
reserved image rectangle simply because the bitmap arrived.

Test extreme valid aspect ratios, caption/style/column changes, full-span groups,
partial data, same-key image revisions, stale thumbnails, append/reset/remove,
deep seek and disconnect. Measure actual column placement, candidate lookup,
native/MAUI reuse, memory and Release frame quality on each requested target.

## Narrow execution example

maui-labs `samples/BespokeCollections` implements vertical 2-4-column metadata
masonry with typed UIKit/RecyclerView cards, append/query reset, 500-record
prefix eviction, keyed relative anchors, bounded thumbnails and teardown.
It includes portable tests and a 22-assertion native Release proof on an iOS
simulator and Android emulator. It uses original generated images and a
deterministic HTTP data fixture, **not a WebView**; HTTP is a source choice,
not a renderer or recipe requirement.

The Android trace is diagnostic virtual-device evidence. iOS simulator hitch
capture was explicitly unsupported. Physical frame/memory qualification, groups,
chrome, selection, dynamic text, arbitrary edits, MAUI templates, and desktop
handlers are not certified by this slice. Standalone skill installation does not
install the sample or SDKs. [Example and evidence R4](sources.md#r4)
