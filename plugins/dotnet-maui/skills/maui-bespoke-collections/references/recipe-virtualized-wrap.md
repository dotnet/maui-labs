# Recipe: virtualized wrap

Use for sequential variable-width items that wrap to lines, not masonry or
common-height justified images. Read [geometry](geometry-and-anchoring.md),
[source](source-and-update-contracts.md) and target references.

## Contract

Define VirtualizedTagView/WrapCollectionView for a specific item design. Specify
axis, min/max width, intrinsic/metadata sizing, line height/alignment, spacing,
line breaks, RTL, selection, supported edits and expected data scale.

For deliberately tiny/capped chip groups, an eager native wrap can be simpler;
state the limit rather than building an unnecessary virtualizer.

## Native choices

| Target | Path |
|---|---|
| UIKit/AppKit | Flow layout or specialized native collection layout |
| Android | Custom manager or reviewed compatible FlexboxManager/fork |
| WinUI stable 1.8 | Custom VirtualizingLayout; not experimental general FlowLayout |
| GTK4 large data | Custom virtualizing widget; FlowBox is eager |
| WPF | Real virtualizing wrap panel/generator; ordinary WrapPanel is eager |

Google Flexbox is archived. FlexboxLayoutManager recycles with feature differences;
FlexboxLayout does not. General WinUI FlowLayout was 2.0-experimental at research
time; LinedFlowLayout is a different fixed-height image-row contract.
[Sources B8, W5, G5, P2](sources.md)

## Geometry

Line starts depend on the prefix: dimensions alone do not provide item positions.
Use metadata or constrained estimates, cache line starts/extents/checkpoints and
locate viewport lines efficiently.

A width/font/edit change can invalidate the affected line and subsequent line
state. Specify suffix reflow and deep-seek cost; never claim O(1) arbitrary jumps
without prefix data. Do not instantiate every offscreen text view to compute exact
line breaks; use a documented thread-safe measuring facility only when available.

Preserve keyed anchor through edits/measurement correction. Validate zero/one/
partial lines, oversized items, spacing/insets, orientation, text scale and RTL.
Use one declared line/height policy, not a universal Flexbox implementation.

## Renderer, loading and evidence

Use cheap indexed projections and compatible native/MAUI child reuse. Keep text
formatting/source I/O out of bind/measure. Apply content versus size invalidation
separately and release stale bindings/demand.

Prove native host/panel, bounded actual children/handlers, line lookup/measurement
counts at scale, range updates, focus/bring-into-view, accessibility and resize.
Measure Release line-reflow and scrolling, not only initial layout.
