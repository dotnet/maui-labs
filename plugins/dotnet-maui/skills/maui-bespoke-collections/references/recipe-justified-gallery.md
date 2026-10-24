# Recipe: justified gallery

Use for aspect-ratio images that fill rows around a common height. This is not
shortest-column masonry. Read [geometry](geometry-and-anchoring.md),
[paging](paging-http-database.md) and requested platform references.

## Contract

Define JustifiedPhotoView with image dimension/revision metadata, preferred row
height and min/max bounds, spacing, allowable crop/stretch, minimum card width,
and explicit final-row policy. Captions/selection/chrome must fit the chosen
row sizing contract rather than silently force arbitrary variable heights.

## Geometry

For a tentative row of aspect ratios a[i] and usable width W:

```text
row height ~= (W - total interitem gaps) / sum(a[i])
image widths ~= row height * a[i]
```

Choose row breaks against preferred/min/max height and rounding; handle one
panorama, narrow images, invalid dimensions and the partial final row.
Store line starts/extents/checkpoints for viewport lookup and width changes.
Chrome starts/ends a row deliberately; it is not a normal photo with a magic span.

Width/aspect/caption changes can reflow a suffix. Recompute from checkpoints and
preserve keyed anchor. Decode reserved thumbnails at output pixel size; arrival
under unchanged dimensions needs keyed content update, not global reflow.

## Native mapping

WinUI LinedFlowLayout is appropriate when its fixed-height/variable-width
stretch/crop rules match; otherwise use specialized VirtualizingLayout.
UIKit/AppKit use metadata-driven grouping/custom collection layout; Android a
specialized RecyclerView manager; GTK/WPF need actual native virtualizing geometry.
Do not substitute a uniform grid or eager image wrap and claim justified layout.
[Sources W2, U1](sources.md)

## Data and proof

Use indexed projections plus source-supported paging, stable keys and bounded
asset caches. Unknown-total cursor data cannot promise exact arbitrary jump
geometry before row prefix metadata exists.

Test row width conservation with explicit rounding tolerance, no forbidden crop,
height bounds, final-row policy, resize/font/chrome, prepend/refresh, late images
and source teardown. Verify requested native types/reuse and frame/memory/row-reflow
budgets. A similar-looking tiled screenshot is not geometric fidelity.
